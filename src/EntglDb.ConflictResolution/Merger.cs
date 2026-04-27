using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EntglDb.ConflictResolution;

public interface IDocumentMerger
{
    MergeResult Merge(string? baseJson, string versionA, string versionB);
    MergeResult Merge(string? baseJson, string versionA, string versionB, MergeOptions options);
}

public readonly record struct MergeResult(
    bool HasConflicts,
    string MergedJson,
    IReadOnlyList<MergeConflict> Conflicts);

public readonly record struct MergeConflict(
    string Path,
    JsonElement ValueA,
    JsonElement ValueB,
    MergeConflictType Type);

public enum MergeConflictType
{
    ValueChanged,
    ArrayConflict,
    TypeMismatch
}

public enum ArrayMergeMode
{
    ReplaceArray,
    AppendArray
}

public sealed class MergeConflictException : Exception
{
    public IReadOnlyList<MergeConflict> Conflicts { get; }

    public MergeConflictException(IReadOnlyList<MergeConflict> conflicts)
        : base($"Merge produced {conflicts.Count} unresolved conflict(s).")
    {
        Conflicts = conflicts;
    }

    public MergeConflictException(string message, IReadOnlyList<MergeConflict> conflicts)
        : base(message)
    {
        Conflicts = conflicts;
    }
}

public abstract class ConflictStrategy
{
    private static readonly ConflictStrategy _throw = new ThrowStrategy();
    private static readonly ConflictStrategy _preferA = new PreferAStrategy();
    private static readonly ConflictStrategy _preferB = new PreferBStrategy();
    private static readonly ConflictStrategy _preferLatestHlc = new PreferLatestHlcStrategy();

    public static ConflictStrategy ThrowOnConflict => _throw;
    public static ConflictStrategy PreferA => _preferA;
    public static ConflictStrategy PreferB => _preferB;
    public static ConflictStrategy PreferLatestHlc => _preferLatestHlc;

    public static ConflictStrategy Custom(Func<MergeConflict, JsonElement> resolver)
    {
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));
        return new CustomStrategy(resolver);
    }

    internal abstract bool DefersToException { get; }

    internal abstract JsonElement Resolve(MergeConflict conflict, MergeOptions options);

    private sealed class ThrowStrategy : ConflictStrategy
    {
        internal override bool DefersToException => true;
        internal override JsonElement Resolve(MergeConflict conflict, MergeOptions options) => conflict.ValueA;
    }

    private sealed class PreferAStrategy : ConflictStrategy
    {
        internal override bool DefersToException => false;
        internal override JsonElement Resolve(MergeConflict conflict, MergeOptions options) => conflict.ValueA;
    }

    private sealed class PreferBStrategy : ConflictStrategy
    {
        internal override bool DefersToException => false;
        internal override JsonElement Resolve(MergeConflict conflict, MergeOptions options) => conflict.ValueB;
    }

    private sealed class PreferLatestHlcStrategy : ConflictStrategy
    {
        internal override bool DefersToException => false;
        internal override JsonElement Resolve(MergeConflict conflict, MergeOptions options)
        {
            if (options.HlcComparator == null)
                throw new InvalidOperationException(
                    "PreferLatestHlc strategy requires MergeOptions.HlcComparator to be set. " +
                    "Provide a Func<MergeConflict, int> returning > 0 if B is newer, < 0 if A is newer, 0 if equal.");

            var cmp = options.HlcComparator(conflict);
            return cmp >= 0 ? conflict.ValueB : conflict.ValueA;
        }
    }

    private sealed class CustomStrategy : ConflictStrategy
    {
        private readonly Func<MergeConflict, JsonElement> _resolver;
        public CustomStrategy(Func<MergeConflict, JsonElement> resolver) { _resolver = resolver; }
        internal override bool DefersToException => false;
        internal override JsonElement Resolve(MergeConflict conflict, MergeOptions options) => _resolver(conflict);
    }
}

public sealed class MergeOptions
{
    public ConflictStrategy ConflictStrategy { get; init; } = ConflictStrategy.ThrowOnConflict;
    public ArrayMergeMode ArrayMergeMode { get; init; } = ArrayMergeMode.ReplaceArray;
    public Func<JsonElement, string?>? ArrayIdSelector { get; init; }
    public Func<MergeConflict, int>? HlcComparator { get; init; }
    public bool WriteIndented { get; init; }

    public static MergeOptions Default { get; } = new();
}

public sealed class DocumentMerger : IDocumentMerger
{
    public MergeOptions Options { get; }

    public DocumentMerger() : this(MergeOptions.Default) { }

    public DocumentMerger(MergeOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MergeResult Merge(string? baseJson, string versionA, string versionB) =>
        Merge(baseJson, versionA, versionB, Options);

    public MergeResult Merge(string? baseJson, string versionA, string versionB, MergeOptions options)
    {
        if (versionA == null) throw new ArgumentNullException(nameof(versionA));
        if (versionB == null) throw new ArgumentNullException(nameof(versionB));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var hasBase = !string.IsNullOrWhiteSpace(baseJson);
        using var aDoc = JsonDocument.Parse(versionA);
        using var bDoc = JsonDocument.Parse(versionB);
        var a = aDoc.RootElement;
        var b = bDoc.RootElement;

        var conflicts = new List<MergeConflict>();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = options.WriteIndented }))
        {
            if (hasBase)
            {
                using var baseDoc = JsonDocument.Parse(baseJson!);
                MergeNode(writer, baseDoc.RootElement, a, b, string.Empty, conflicts, options);
            }
            else
            {
                MergeNode(writer, default, a, b, string.Empty, conflicts, options, baseAvailable: false);
            }
        }

        if (conflicts.Count > 0 && options.ConflictStrategy.DefersToException)
            throw new MergeConflictException(conflicts);

        var mergedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        return new MergeResult(conflicts.Count > 0, mergedJson, conflicts);
    }

    private static void MergeNode(
        Utf8JsonWriter writer,
        JsonElement baseEl,
        JsonElement a,
        JsonElement b,
        string path,
        List<MergeConflict> conflicts,
        MergeOptions options,
        bool baseAvailable = true)
    {
        if (!baseAvailable)
        {
            MergeNoBase(writer, a, b, path, conflicts, options);
            return;
        }

        var aEqBase = JsonDiff.RawEquals(a, baseEl);
        var bEqBase = JsonDiff.RawEquals(b, baseEl);

        if (aEqBase && bEqBase) { WriteValue(writer, baseEl); return; }
        if (aEqBase) { WriteValue(writer, b); return; }
        if (bEqBase) { WriteValue(writer, a); return; }
        if (JsonDiff.RawEquals(a, b)) { WriteValue(writer, a); return; }

        if (a.ValueKind != b.ValueKind)
        {
            EmitConflict(writer, MergeConflictType.TypeMismatch, path, a, b, conflicts, options);
            return;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                MergeObjects(writer, baseEl, a, b, path, conflicts, options);
                break;
            case JsonValueKind.Array:
                MergeArrays(writer, baseEl, a, b, path, conflicts, options);
                break;
            default:
                EmitConflict(writer, MergeConflictType.ValueChanged, path, a, b, conflicts, options);
                break;
        }
    }

    private static void MergeNoBase(
        Utf8JsonWriter writer,
        JsonElement a,
        JsonElement b,
        string path,
        List<MergeConflict> conflicts,
        MergeOptions options)
    {
        if (JsonDiff.RawEquals(a, b)) { WriteValue(writer, a); return; }

        if (a.ValueKind != b.ValueKind)
        {
            EmitConflict(writer, MergeConflictType.TypeMismatch, path, a, b, conflicts, options);
            return;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                MergeObjectsNoBase(writer, a, b, path, conflicts, options);
                break;
            case JsonValueKind.Array:
                if (options.ArrayMergeMode == ArrayMergeMode.AppendArray)
                {
                    MergeArrays(writer, default, a, b, path, conflicts, options, baseAvailable: false);
                }
                else
                {
                    EmitConflict(writer, MergeConflictType.ArrayConflict, path, a, b, conflicts, options);
                }
                break;
            default:
                EmitConflict(writer, MergeConflictType.ValueChanged, path, a, b, conflicts, options);
                break;
        }
    }

    private static void MergeObjects(
        Utf8JsonWriter writer,
        JsonElement baseEl,
        JsonElement a,
        JsonElement b,
        string path,
        List<MergeConflict> conflicts,
        MergeOptions options)
    {
        writer.WriteStartObject();

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in baseEl.EnumerateObject()) keys.Add(p.Name);
        foreach (var p in a.EnumerateObject()) keys.Add(p.Name);
        foreach (var p in b.EnumerateObject()) keys.Add(p.Name);

        foreach (var key in keys)
        {
            var inBase = baseEl.TryGetProperty(key, out var baseVal);
            var inA = a.TryGetProperty(key, out var aVal);
            var inB = b.TryGetProperty(key, out var bVal);

            var childPath = JsonPointer.Combine(path, key);

            if (inA && inB)
            {
                writer.WritePropertyName(key);
                if (inBase)
                    MergeNode(writer, baseVal, aVal, bVal, childPath, conflicts, options);
                else
                    MergeNoBase(writer, aVal, bVal, childPath, conflicts, options);
            }
            else if (inA)
            {
                if (inBase && JsonDiff.RawEquals(aVal, baseVal))
                {
                    // Removed by B (was in base, unchanged in A) → skip.
                }
                else
                {
                    writer.WritePropertyName(key);
                    WriteValue(writer, aVal);
                }
            }
            else if (inB)
            {
                if (inBase && JsonDiff.RawEquals(bVal, baseVal))
                {
                    // Removed by A → skip.
                }
                else
                {
                    writer.WritePropertyName(key);
                    WriteValue(writer, bVal);
                }
            }
            // else: in base only, removed by both → skip.
        }

        writer.WriteEndObject();
    }

    private static void MergeObjectsNoBase(
        Utf8JsonWriter writer,
        JsonElement a,
        JsonElement b,
        string path,
        List<MergeConflict> conflicts,
        MergeOptions options)
    {
        writer.WriteStartObject();

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in a.EnumerateObject()) keys.Add(p.Name);
        foreach (var p in b.EnumerateObject()) keys.Add(p.Name);

        foreach (var key in keys)
        {
            var inA = a.TryGetProperty(key, out var aVal);
            var inB = b.TryGetProperty(key, out var bVal);
            var childPath = JsonPointer.Combine(path, key);

            if (inA && inB)
            {
                writer.WritePropertyName(key);
                MergeNoBase(writer, aVal, bVal, childPath, conflicts, options);
            }
            else if (inA)
            {
                writer.WritePropertyName(key);
                WriteValue(writer, aVal);
            }
            else
            {
                writer.WritePropertyName(key);
                WriteValue(writer, bVal);
            }
        }

        writer.WriteEndObject();
    }

    private static void MergeArrays(
        Utf8JsonWriter writer,
        JsonElement baseEl,
        JsonElement a,
        JsonElement b,
        string path,
        List<MergeConflict> conflicts,
        MergeOptions options,
        bool baseAvailable = true)
    {
        if (options.ArrayMergeMode == ArrayMergeMode.AppendArray)
        {
            ArrayMerger.MergeAppend(writer, baseEl, a, b, path, conflicts, options, baseAvailable);
            return;
        }

        // ReplaceArray semantics: pick whichever side actually changed; if both changed → conflict.
        if (baseAvailable)
        {
            var aChanged = !JsonDiff.RawEquals(a, baseEl);
            var bChanged = !JsonDiff.RawEquals(b, baseEl);
            if (aChanged && bChanged)
                EmitConflict(writer, MergeConflictType.ArrayConflict, path, a, b, conflicts, options);
            else if (aChanged)
                WriteValue(writer, a);
            else
                WriteValue(writer, b);
        }
        else
        {
            EmitConflict(writer, MergeConflictType.ArrayConflict, path, a, b, conflicts, options);
        }
    }

    private static void EmitConflict(
        Utf8JsonWriter writer,
        MergeConflictType type,
        string path,
        JsonElement aVal,
        JsonElement bVal,
        List<MergeConflict> conflicts,
        MergeOptions options)
    {
        var aClone = Clone(aVal);
        var bClone = Clone(bVal);
        var conflict = new MergeConflict(path, aClone, bClone, type);
        conflicts.Add(conflict);

        if (options.ConflictStrategy.DefersToException)
        {
            // Provisional value; exception thrown after walk completes.
            WriteValue(writer, aClone);
            return;
        }

        var resolved = options.ConflictStrategy.Resolve(conflict, options);
        WriteValue(writer, resolved);
    }

    internal static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        value.WriteTo(writer);
    }

    internal static JsonElement Clone(JsonElement element)
    {
        using var doc = JsonDocument.Parse(element.GetRawText());
        return doc.RootElement.Clone();
    }
}

internal static class ArrayMerger
{
    public static void MergeAppend(
        Utf8JsonWriter writer,
        JsonElement baseEl,
        JsonElement a,
        JsonElement b,
        string path,
        List<MergeConflict> conflicts,
        MergeOptions options,
        bool baseAvailable)
    {
        if (LooksLikePrimitiveArray(a) || LooksLikePrimitiveArray(b))
        {
            UnionPrimitives(writer, a, b);
            return;
        }

        var idSelector = options.ArrayIdSelector ?? DefaultIdSelector;

        var baseIds = baseAvailable ? IndexById(baseEl, idSelector) : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var aIds = IndexById(a, idSelector);
        var bIds = IndexById(b, idSelector);

        var mergedKeys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in aIds.Keys)
            if (seen.Add(id)) mergedKeys.Add(id);
        foreach (var id in bIds.Keys)
            if (seen.Add(id)) mergedKeys.Add(id);

        writer.WriteStartArray();
        foreach (var id in mergedKeys)
        {
            var inA = aIds.TryGetValue(id, out var aVal);
            var inB = bIds.TryGetValue(id, out var bVal);
            var inBase = baseIds.TryGetValue(id, out var baseVal);

            var elemPath = JsonPointer.Combine(path, id);

            if (inA && inB)
            {
                if (JsonDiff.RawEquals(aVal, bVal))
                {
                    DocumentMerger.WriteValue(writer, aVal);
                }
                else if (inBase)
                {
                    if (JsonDiff.RawEquals(aVal, baseVal)) DocumentMerger.WriteValue(writer, bVal);
                    else if (JsonDiff.RawEquals(bVal, baseVal)) DocumentMerger.WriteValue(writer, aVal);
                    else EmitArrayItemConflict(writer, elemPath, aVal, bVal, conflicts, options);
                }
                else
                {
                    EmitArrayItemConflict(writer, elemPath, aVal, bVal, conflicts, options);
                }
            }
            else if (inA)
            {
                if (inBase && JsonDiff.RawEquals(aVal, baseVal))
                {
                    // Removed by B → skip.
                }
                else
                {
                    DocumentMerger.WriteValue(writer, aVal);
                }
            }
            else if (inB)
            {
                if (inBase && JsonDiff.RawEquals(bVal, baseVal))
                {
                    // Removed by A → skip.
                }
                else
                {
                    DocumentMerger.WriteValue(writer, bVal);
                }
            }
            // Removed by both → skip.
        }
        writer.WriteEndArray();
    }

    private static void EmitArrayItemConflict(
        Utf8JsonWriter writer,
        string path,
        JsonElement aVal,
        JsonElement bVal,
        List<MergeConflict> conflicts,
        MergeOptions options)
    {
        var aClone = DocumentMerger.Clone(aVal);
        var bClone = DocumentMerger.Clone(bVal);
        var conflict = new MergeConflict(path, aClone, bClone, MergeConflictType.ArrayConflict);
        conflicts.Add(conflict);

        if (options.ConflictStrategy.DefersToException)
        {
            DocumentMerger.WriteValue(writer, aClone);
            return;
        }

        var resolved = options.ConflictStrategy.Resolve(conflict, options);
        DocumentMerger.WriteValue(writer, resolved);
    }

    private static bool LooksLikePrimitiveArray(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                return false;
        }
        return true;
    }

    private static void UnionPrimitives(Utf8JsonWriter writer, JsonElement a, JsonElement b)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<JsonElement>();

        foreach (var item in a.EnumerateArray())
            if (seen.Add(item.GetRawText())) ordered.Add(item);
        foreach (var item in b.EnumerateArray())
            if (seen.Add(item.GetRawText())) ordered.Add(item);

        writer.WriteStartArray();
        foreach (var item in ordered) DocumentMerger.WriteValue(writer, item);
        writer.WriteEndArray();
    }

    private static Dictionary<string, JsonElement> IndexById(JsonElement arr, Func<JsonElement, string?> idSelector)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (arr.ValueKind != JsonValueKind.Array) return dict;

        var idx = 0;
        foreach (var item in arr.EnumerateArray())
        {
            var id = idSelector(item) ?? $"#idx-{idx}-{item.GetRawText()}";
            dict[id] = item;
            idx++;
        }
        return dict;
    }

    private static string? DefaultIdSelector(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty("id", out var idProp)) return RawString(idProp);
        if (element.TryGetProperty("_id", out var underId)) return RawString(underId);
        return null;
    }

    private static string RawString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText();
}
