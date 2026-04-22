using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EntglDb.ConflictResolution;

public enum JsonPatchOperationType
{
    Add,
    Remove,
    Replace,
    Copy,
    Move,
    Test
}

public readonly record struct JsonPatchOperation(
    JsonPatchOperationType Op,
    string Path,
    JsonElement? Value,
    string? From);

internal static class JsonPointer
{
    public static string Escape(string token)
    {
        if (string.IsNullOrEmpty(token)) return token;
        return token.Replace("~", "~0").Replace("/", "~1");
    }

    public static string Unescape(string token)
    {
        if (string.IsNullOrEmpty(token)) return token;
        return token.Replace("~1", "/").Replace("~0", "~");
    }

    public static IReadOnlyList<string> Parse(string pointer)
    {
        if (string.IsNullOrEmpty(pointer)) return Array.Empty<string>();
        if (pointer[0] != '/')
            throw new ArgumentException($"Invalid JSON Pointer '{pointer}': must start with '/' or be empty.", nameof(pointer));
        var raw = pointer.Substring(1).Split('/');
        var result = new string[raw.Length];
        for (int i = 0; i < raw.Length; i++) result[i] = Unescape(raw[i]);
        return result;
    }

    public static string Combine(string parent, string token) => parent + "/" + Escape(token);

    public static JsonElement? Evaluate(JsonElement root, string pointer)
    {
        var segments = Parse(pointer);
        var current = root;
        foreach (var seg in segments)
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(seg, out var child)) return null;
                current = child;
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(seg, out var idx)) return null;
                if (idx < 0 || idx >= current.GetArrayLength()) return null;
                current = current[idx];
            }
            else
            {
                return null;
            }
        }
        return current;
    }
}

public static class JsonDiff
{
    public static IReadOnlyList<JsonPatchOperation> Compute(JsonElement @base, JsonElement target)
    {
        var ops = new List<JsonPatchOperation>();
        ComputeRecursive(@base, target, string.Empty, ops);
        return ops;
    }

    private static void ComputeRecursive(JsonElement a, JsonElement b, string path, List<JsonPatchOperation> ops)
    {
        if (a.ValueKind != b.ValueKind)
        {
            ops.Add(new JsonPatchOperation(JsonPatchOperationType.Replace, path, CloneValue(b), null));
            return;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                DiffObjects(a, b, path, ops);
                break;
            case JsonValueKind.Array:
                DiffArrays(a, b, path, ops);
                break;
            default:
                if (!RawEquals(a, b))
                    ops.Add(new JsonPatchOperation(JsonPatchOperationType.Replace, path, CloneValue(b), null));
                break;
        }
    }

    private static void DiffObjects(JsonElement a, JsonElement b, string path, List<JsonPatchOperation> ops)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in a.EnumerateObject()) keys.Add(p.Name);
        foreach (var p in b.EnumerateObject()) keys.Add(p.Name);

        foreach (var key in keys)
        {
            var childPath = JsonPointer.Combine(path, key);
            var inA = a.TryGetProperty(key, out var aVal);
            var inB = b.TryGetProperty(key, out var bVal);

            if (inA && inB)
                ComputeRecursive(aVal, bVal, childPath, ops);
            else if (inB)
                ops.Add(new JsonPatchOperation(JsonPatchOperationType.Add, childPath, CloneValue(bVal), null));
            else
                ops.Add(new JsonPatchOperation(JsonPatchOperationType.Remove, childPath, null, null));
        }
    }

    private static void DiffArrays(JsonElement a, JsonElement b, string path, List<JsonPatchOperation> ops)
    {
        var aLen = a.GetArrayLength();
        var bLen = b.GetArrayLength();
        var minLen = Math.Min(aLen, bLen);

        for (int i = 0; i < minLen; i++)
            ComputeRecursive(a[i], b[i], JsonPointer.Combine(path, i.ToString()), ops);

        for (int i = minLen; i < bLen; i++)
            ops.Add(new JsonPatchOperation(JsonPatchOperationType.Add, JsonPointer.Combine(path, i.ToString()), CloneValue(b[i]), null));

        // Remove in descending order so indices stay valid during apply.
        for (int i = aLen - 1; i >= minLen; i--)
            ops.Add(new JsonPatchOperation(JsonPatchOperationType.Remove, JsonPointer.Combine(path, i.ToString()), null, null));
    }

    internal static bool RawEquals(JsonElement a, JsonElement b) => a.GetRawText() == b.GetRawText();

    private static JsonElement CloneValue(JsonElement element)
    {
        using var doc = JsonDocument.Parse(element.GetRawText());
        return doc.RootElement.Clone();
    }
}

public static class JsonPatch
{
    public static JsonElement Apply(JsonElement document, IReadOnlyList<JsonPatchOperation> operations)
    {
        var node = JsonNode.Parse(document.GetRawText());
        foreach (var op in operations)
            node = ApplyOne(node, op);

        var json = node?.ToJsonString() ?? "null";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonNode? ApplyOne(JsonNode? root, JsonPatchOperation op)
    {
        var segments = JsonPointer.Parse(op.Path);
        return op.Op switch
        {
            JsonPatchOperationType.Add => ApplyAdd(root, segments, op.Value),
            JsonPatchOperationType.Remove => ApplyRemove(root, segments),
            JsonPatchOperationType.Replace => ApplyReplace(root, segments, op.Value),
            JsonPatchOperationType.Copy => ApplyCopy(root, op.From ?? throw new InvalidOperationException("Copy operation requires 'From'."), segments),
            JsonPatchOperationType.Move => ApplyMove(root, op.From ?? throw new InvalidOperationException("Move operation requires 'From'."), segments),
            JsonPatchOperationType.Test => ApplyTest(root, segments, op.Value),
            _ => throw new NotSupportedException($"Unsupported operation: {op.Op}")
        };
    }

    private static JsonNode? Get(JsonNode? root, IReadOnlyList<string> segments)
    {
        var current = root;
        foreach (var seg in segments)
        {
            switch (current)
            {
                case JsonObject obj:
                    if (!obj.TryGetPropertyValue(seg, out current)) return null;
                    break;
                case JsonArray arr:
                    if (!int.TryParse(seg, out var idx) || idx < 0 || idx >= arr.Count) return null;
                    current = arr[idx];
                    break;
                default:
                    return null;
            }
        }
        return current;
    }

    private static JsonNode? ApplyAdd(JsonNode? root, IReadOnlyList<string> segments, JsonElement? value)
    {
        if (segments.Count == 0) return ValueToNode(value);

        var parentSegs = segments.Take(segments.Count - 1).ToList();
        var parent = Get(root, parentSegs)
            ?? throw new InvalidOperationException($"Add path parent not found: '/{string.Join("/", segments)}'.");
        var lastSeg = segments[segments.Count - 1];

        switch (parent)
        {
            case JsonObject obj:
                obj[lastSeg] = ValueToNode(value);
                break;
            case JsonArray arr:
                var valNode = ValueToNode(value);
                if (lastSeg == "-")
                {
                    arr.Add(valNode);
                }
                else if (int.TryParse(lastSeg, out var idx))
                {
                    if (idx < 0 || idx > arr.Count)
                        throw new InvalidOperationException($"Add array index out of range: {idx}.");
                    arr.Insert(idx, valNode);
                }
                else
                {
                    throw new InvalidOperationException($"Invalid array index token: '{lastSeg}'.");
                }
                break;
            default:
                throw new InvalidOperationException("Add parent must be object or array.");
        }
        return root;
    }

    private static JsonNode? ApplyRemove(JsonNode? root, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0) return null;

        var parentSegs = segments.Take(segments.Count - 1).ToList();
        var parent = Get(root, parentSegs)
            ?? throw new InvalidOperationException($"Remove path parent not found: '/{string.Join("/", segments)}'.");
        var lastSeg = segments[segments.Count - 1];

        switch (parent)
        {
            case JsonObject obj:
                if (!obj.ContainsKey(lastSeg))
                    throw new InvalidOperationException($"Remove path not found: '/{string.Join("/", segments)}'.");
                obj.Remove(lastSeg);
                break;
            case JsonArray arr:
                if (!int.TryParse(lastSeg, out var idx) || idx < 0 || idx >= arr.Count)
                    throw new InvalidOperationException($"Remove array index invalid or out of range: '{lastSeg}'.");
                arr.RemoveAt(idx);
                break;
            default:
                throw new InvalidOperationException("Remove parent must be object or array.");
        }
        return root;
    }

    private static JsonNode? ApplyReplace(JsonNode? root, IReadOnlyList<string> segments, JsonElement? value)
    {
        if (segments.Count == 0) return ValueToNode(value);

        var parentSegs = segments.Take(segments.Count - 1).ToList();
        var parent = Get(root, parentSegs)
            ?? throw new InvalidOperationException($"Replace path parent not found: '/{string.Join("/", segments)}'.");
        var lastSeg = segments[segments.Count - 1];

        switch (parent)
        {
            case JsonObject obj:
                if (!obj.ContainsKey(lastSeg))
                    throw new InvalidOperationException($"Replace path not found: '/{string.Join("/", segments)}'.");
                obj[lastSeg] = ValueToNode(value);
                break;
            case JsonArray arr:
                if (!int.TryParse(lastSeg, out var idx) || idx < 0 || idx >= arr.Count)
                    throw new InvalidOperationException($"Replace array index invalid or out of range: '{lastSeg}'.");
                arr[idx] = ValueToNode(value);
                break;
            default:
                throw new InvalidOperationException("Replace parent must be object or array.");
        }
        return root;
    }

    private static JsonNode? ApplyCopy(JsonNode? root, string from, IReadOnlyList<string> toSegments)
    {
        var fromSegs = JsonPointer.Parse(from);
        var src = Get(root, fromSegs)
            ?? throw new InvalidOperationException($"Copy source path not found: '{from}'.");
        var cloned = CloneNode(src);
        return ApplyAddNode(root, toSegments, cloned);
    }

    private static JsonNode? ApplyMove(JsonNode? root, string from, IReadOnlyList<string> toSegments)
    {
        var fromSegs = JsonPointer.Parse(from);
        var src = Get(root, fromSegs)
            ?? throw new InvalidOperationException($"Move source path not found: '{from}'.");
        var cloned = CloneNode(src);
        root = ApplyRemove(root, fromSegs);
        return ApplyAddNode(root, toSegments, cloned);
    }

    private static JsonNode? ApplyTest(JsonNode? root, IReadOnlyList<string> segments, JsonElement? expected)
    {
        var actual = Get(root, segments);
        var actualJson = actual?.ToJsonString() ?? "null";
        var expectedJson = expected?.GetRawText() ?? "null";
        if (actualJson != expectedJson)
            throw new InvalidOperationException($"Test operation failed at '/{string.Join("/", segments)}': expected {expectedJson}, got {actualJson}.");
        return root;
    }

    private static JsonNode? ApplyAddNode(JsonNode? root, IReadOnlyList<string> segments, JsonNode? value)
    {
        if (segments.Count == 0) return value;

        var parentSegs = segments.Take(segments.Count - 1).ToList();
        var parent = Get(root, parentSegs)
            ?? throw new InvalidOperationException($"Add path parent not found: '/{string.Join("/", segments)}'.");
        var lastSeg = segments[segments.Count - 1];

        switch (parent)
        {
            case JsonObject obj:
                obj[lastSeg] = value;
                break;
            case JsonArray arr:
                if (lastSeg == "-")
                {
                    arr.Add(value);
                }
                else if (int.TryParse(lastSeg, out var idx))
                {
                    if (idx < 0 || idx > arr.Count)
                        throw new InvalidOperationException($"Add array index out of range: {idx}.");
                    arr.Insert(idx, value);
                }
                else
                {
                    throw new InvalidOperationException($"Invalid array index token: '{lastSeg}'.");
                }
                break;
            default:
                throw new InvalidOperationException("Add parent must be object or array.");
        }
        return root;
    }

    private static JsonNode? ValueToNode(JsonElement? value)
    {
        if (!value.HasValue) return null;
        return JsonNode.Parse(value.Value.GetRawText());
    }

    private static JsonNode? CloneNode(JsonNode? node)
    {
        return node == null ? null : JsonNode.Parse(node.ToJsonString());
    }
}
