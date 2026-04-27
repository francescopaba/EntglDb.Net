using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EntglDb.ConflictResolution;
using FluentAssertions;

namespace EntglDb.ConflictResolution.Tests;

public class MergerTests
{
    // ---------- helpers ----------

    private static string Json(object? obj) =>
        JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });

    private static JsonElement Elem(object? obj)
    {
        using var doc = JsonDocument.Parse(Json(obj));
        return doc.RootElement.Clone();
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static DocumentMerger With(ConflictStrategy strategy, ArrayMergeMode mode = ArrayMergeMode.ReplaceArray, Func<MergeConflict, int>? hlc = null) =>
        new DocumentMerger(new MergeOptions
        {
            ConflictStrategy = strategy,
            ArrayMergeMode = mode,
            HlcComparator = hlc
        });

    // ====================================================================
    // Category A — JsonPointer (4 tests)
    // ====================================================================

    [Fact]
    public void JsonPointer_Parse_EmptyPointer_ReturnsEmptyList()
    {
        var segments = JsonPointer.Parse("");
        segments.Should().BeEmpty();
    }

    [Fact]
    public void JsonPointer_Parse_NestedPath_SplitsCorrectly()
    {
        var segments = JsonPointer.Parse("/a/b/0");
        segments.Should().Equal("a", "b", "0");
    }

    [Fact]
    public void JsonPointer_Escape_TildeAndSlash_EscapedCorrectly()
    {
        // RFC 6901: '~' must be replaced first to '~0', then '/' to '~1'
        JsonPointer.Escape("a/b~c").Should().Be("a~1b~0c");
        JsonPointer.Unescape("a~1b~0c").Should().Be("a/b~c");
    }

    [Fact]
    public void JsonPointer_Evaluate_ArrayIndex_ReturnsElement()
    {
        var doc = Parse("{\"items\":[\"x\",\"y\",\"z\"]}");
        var result = JsonPointer.Evaluate(doc, "/items/1");
        result.HasValue.Should().BeTrue();
        result!.Value.GetString().Should().Be("y");
    }

    // ====================================================================
    // Category B — JsonDiff (4 tests)
    // ====================================================================

    [Fact]
    public void JsonDiff_IdenticalDocuments_ReturnsEmptyPatch()
    {
        var ops = JsonDiff.Compute(Elem(new { a = 1 }), Elem(new { a = 1 }));
        ops.Should().BeEmpty();
    }

    [Fact]
    public void JsonDiff_AddedField_EmitsAddOperation()
    {
        var ops = JsonDiff.Compute(Elem(new { a = 1 }), Elem(new { a = 1, b = 2 }));
        ops.Should().ContainSingle(o => o.Op == JsonPatchOperationType.Add && o.Path == "/b");
    }

    [Fact]
    public void JsonDiff_RemovedField_EmitsRemoveOperation()
    {
        var ops = JsonDiff.Compute(Elem(new { a = 1, b = 2 }), Elem(new { a = 1 }));
        ops.Should().ContainSingle(o => o.Op == JsonPatchOperationType.Remove && o.Path == "/b");
    }

    [Fact]
    public void JsonDiff_ChangedPrimitive_EmitsReplaceOperation()
    {
        var ops = JsonDiff.Compute(Elem(new { a = 1 }), Elem(new { a = 2 }));
        ops.Should().ContainSingle(o => o.Op == JsonPatchOperationType.Replace && o.Path == "/a");
    }

    // ====================================================================
    // Category C — JsonPatch applier (4 tests)
    // ====================================================================

    [Fact]
    public void JsonPatch_ApplyAdd_AddsFieldToObject()
    {
        var doc = Parse("{\"a\":1}");
        var ops = new[] { new JsonPatchOperation(JsonPatchOperationType.Add, "/b", Elem(2), null) };
        var result = JsonPatch.Apply(doc, ops);
        result.GetProperty("b").GetInt32().Should().Be(2);
    }

    [Fact]
    public void JsonPatch_ApplyRemove_MissingPath_Throws()
    {
        var doc = Parse("{\"a\":1}");
        var ops = new[] { new JsonPatchOperation(JsonPatchOperationType.Remove, "/missing", null, null) };
        Action act = () => JsonPatch.Apply(doc, ops);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void JsonPatch_ApplyReplace_OnNestedPath_UpdatesValue()
    {
        var doc = Parse("{\"a\":{\"b\":1}}");
        var ops = new[] { new JsonPatchOperation(JsonPatchOperationType.Replace, "/a/b", Elem(99), null) };
        var result = JsonPatch.Apply(doc, ops);
        result.GetProperty("a").GetProperty("b").GetInt32().Should().Be(99);
    }

    [Fact]
    public void JsonPatch_ApplyAdd_ArrayAppendWithDashIndex()
    {
        var doc = Parse("{\"items\":[1,2,3]}");
        var ops = new[] { new JsonPatchOperation(JsonPatchOperationType.Add, "/items/-", Elem(4), null) };
        var result = JsonPatch.Apply(doc, ops);
        var arr = result.GetProperty("items").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        arr.Should().Equal(1, 2, 3, 4);
    }

    // ====================================================================
    // Category D — Merge disjoint / non-conflict (4 tests)
    // ====================================================================

    [Fact]
    public void Merge_DisjointFields_NoConflicts()
    {
        var baseJson = Json(new { name = "Mario", surname = "Rossi", age = 30 });
        var a = Json(new { name = "Mario Bianchi", surname = "Rossi", age = 30 });
        var b = Json(new { name = "Mario", surname = "Rossi", age = 31 });

        var result = With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeFalse();
        var merged = Parse(result.MergedJson);
        merged.GetProperty("name").GetString().Should().Be("Mario Bianchi");
        merged.GetProperty("surname").GetString().Should().Be("Rossi");
        merged.GetProperty("age").GetInt32().Should().Be(31);
    }

    [Fact]
    public void Merge_AAndBIdentical_NoConflict_ReturnsSame()
    {
        var baseJson = Json(new { x = 1 });
        var a = Json(new { x = 2 });
        var b = Json(new { x = 2 });
        var result = With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeFalse();
        Parse(result.MergedJson).GetProperty("x").GetInt32().Should().Be(2);
    }

    [Fact]
    public void Merge_OnlyAChanged_ReturnsA()
    {
        var baseJson = Json(new { x = 1, y = 1 });
        var a = Json(new { x = 9, y = 1 });
        var b = Json(new { x = 1, y = 1 });
        var result = With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeFalse();
        Parse(result.MergedJson).GetProperty("x").GetInt32().Should().Be(9);
    }

    [Fact]
    public void Merge_OnlyBChanged_ReturnsB()
    {
        var baseJson = Json(new { x = 1, y = 1 });
        var a = Json(new { x = 1, y = 1 });
        var b = Json(new { x = 1, y = 9 });
        var result = With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeFalse();
        Parse(result.MergedJson).GetProperty("y").GetInt32().Should().Be(9);
    }

    // ====================================================================
    // Category E — Conflict strategies (8 tests)
    // ====================================================================

    [Fact]
    public void Merge_SameField_ThrowOnConflict_Throws_WithConflictList()
    {
        var baseJson = Json(new { name = "Mario" });
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });

        Action act = () => With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);

        act.Should().Throw<MergeConflictException>()
            .Where(ex => ex.Conflicts.Count == 1 && ex.Conflicts[0].Path == "/name");
    }

    [Fact]
    public void Merge_SameField_PreferA_ReturnsA()
    {
        var baseJson = Json(new { name = "Mario" });
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });
        var result = With(ConflictStrategy.PreferA).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeTrue();
        Parse(result.MergedJson).GetProperty("name").GetString().Should().Be("Luigi");
    }

    [Fact]
    public void Merge_SameField_PreferB_ReturnsB()
    {
        var baseJson = Json(new { name = "Mario" });
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });
        var result = With(ConflictStrategy.PreferB).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeTrue();
        Parse(result.MergedJson).GetProperty("name").GetString().Should().Be("Pino");
    }

    [Fact]
    public void Merge_SameField_PreferLatestHlc_NoComparator_Throws()
    {
        var baseJson = Json(new { name = "Mario" });
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });

        Action act = () => With(ConflictStrategy.PreferLatestHlc).Merge(baseJson, a, b);

        act.Should().Throw<InvalidOperationException>().WithMessage("*HlcComparator*");
    }

    [Fact]
    public void Merge_SameField_PreferLatestHlc_AWins()
    {
        var baseJson = Json(new { name = "Mario" });
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });
        var result = With(ConflictStrategy.PreferLatestHlc, hlc: _ => -1).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeTrue();
        Parse(result.MergedJson).GetProperty("name").GetString().Should().Be("Luigi");
    }

    [Fact]
    public void Merge_SameField_PreferLatestHlc_BWins()
    {
        var baseJson = Json(new { name = "Mario" });
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });
        var result = With(ConflictStrategy.PreferLatestHlc, hlc: _ => 1).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeTrue();
        Parse(result.MergedJson).GetProperty("name").GetString().Should().Be("Pino");
    }

    [Fact]
    public void Merge_SameField_Custom_InvokesDelegate()
    {
        var baseJson = Json(new { age = 30 });
        var a = Json(new { age = 31 });
        var b = Json(new { age = 32 });
        var custom = ConflictStrategy.Custom(_ => Elem(99));
        var result = With(custom).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeTrue();
        Parse(result.MergedJson).GetProperty("age").GetInt32().Should().Be(99);
    }

    [Fact]
    public void Merge_MultipleConflicts_ThrowOnConflict_ExceptionContainsAll()
    {
        var baseJson = Json(new { x = 1, y = 1 });
        var a = Json(new { x = 2, y = 3 });
        var b = Json(new { x = 9, y = 8 });

        Action act = () => With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);

        act.Should().Throw<MergeConflictException>()
            .Where(ex => ex.Conflicts.Count == 2);
    }

    // ====================================================================
    // Category F — Arrays (5 tests)
    // ====================================================================

    [Fact]
    public void Merge_Array_ReplaceMode_ConflictingArrays_UsesStrategy()
    {
        var baseJson = "{\"tags\":[\"a\"]}";
        var a = "{\"tags\":[\"a\",\"b\"]}";
        var b = "{\"tags\":[\"a\",\"c\"]}";

        var result = With(ConflictStrategy.PreferB, ArrayMergeMode.ReplaceArray).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeTrue();
        var merged = Parse(result.MergedJson);
        merged.GetProperty("tags").EnumerateArray().Select(e => e.GetString()).Should().Equal("a", "c");
    }

    [Fact]
    public void Merge_Array_AppendMode_DisjointAdditions_UnionsElements()
    {
        var baseJson = "{\"items\":[{\"id\":1,\"v\":\"x\"}]}";
        var a = "{\"items\":[{\"id\":1,\"v\":\"x\"},{\"id\":2,\"v\":\"y\"}]}";
        var b = "{\"items\":[{\"id\":1,\"v\":\"x\"},{\"id\":3,\"v\":\"z\"}]}";

        var result = With(ConflictStrategy.ThrowOnConflict, ArrayMergeMode.AppendArray).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeFalse();
        var ids = Parse(result.MergedJson).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetInt32()).ToArray();
        ids.Should().Contain(new[] { 1, 2, 3 });
        ids.Length.Should().Be(3);
    }

    [Fact]
    public void Merge_Array_AppendMode_DeletedInBothParties_Removes()
    {
        var baseJson = "{\"items\":[{\"id\":1},{\"id\":2}]}";
        var a = "{\"items\":[{\"id\":1}]}";
        var b = "{\"items\":[{\"id\":1}]}";

        var result = With(ConflictStrategy.ThrowOnConflict, ArrayMergeMode.AppendArray).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeFalse();
        var ids = Parse(result.MergedJson).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetInt32()).ToArray();
        ids.Should().Equal(1);
    }

    [Fact]
    public void Merge_Array_AppendMode_SameIdDifferentValues_EmitsArrayConflict()
    {
        var baseJson = "{\"items\":[{\"id\":1,\"v\":\"x\"}]}";
        var a = "{\"items\":[{\"id\":1,\"v\":\"y\"}]}";
        var b = "{\"items\":[{\"id\":1,\"v\":\"z\"}]}";

        var result = With(ConflictStrategy.PreferB, ArrayMergeMode.AppendArray).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeTrue();
        result.Conflicts.Should().Contain(c => c.Type == MergeConflictType.ArrayConflict);
    }

    [Fact]
    public void Merge_Array_Primitives_AppendMode_SetUnion_Deduped()
    {
        var baseJson = "{\"tags\":[\"x\"]}";
        var a = "{\"tags\":[\"x\",\"a\"]}";
        var b = "{\"tags\":[\"x\",\"b\"]}";

        var result = With(ConflictStrategy.ThrowOnConflict, ArrayMergeMode.AppendArray).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeFalse();
        var tags = Parse(result.MergedJson).GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToArray();
        tags.Should().Contain(new[] { "x", "a", "b" });
        tags.Length.Should().Be(3);
    }

    // ====================================================================
    // Category G — Types and edge cases (5 tests)
    // ====================================================================

    [Fact]
    public void Merge_TypeMismatch_StringVsNumber_EmitsTypeMismatch()
    {
        var baseJson = "{\"v\":\"hello\"}";
        var a = "{\"v\":\"hello\"}";
        var b = "{\"v\":42}";

        var result = With(ConflictStrategy.PreferB).Merge(baseJson, a, b);

        // A is unchanged from base, B changed to a different type — only B's change applies; no conflict here.
        // Force a conflict by also changing A.
        a = "{\"v\":\"world\"}";
        result = With(ConflictStrategy.PreferB).Merge(baseJson, a, b);
        result.HasConflicts.Should().BeTrue();
        result.Conflicts.Should().Contain(c => c.Type == MergeConflictType.TypeMismatch);
    }

    [Fact]
    public void Merge_NullBase_DegeneratesToTwoWay()
    {
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });

        var result = With(ConflictStrategy.PreferA).Merge(null, a, b);

        result.HasConflicts.Should().BeTrue();
        Parse(result.MergedJson).GetProperty("name").GetString().Should().Be("Luigi");
    }

    [Fact]
    public void Merge_EmptyStringBase_TreatedAsNull()
    {
        var a = Json(new { name = "Luigi" });
        var b = Json(new { name = "Pino" });

        var result = With(ConflictStrategy.PreferB).Merge("", a, b);

        result.HasConflicts.Should().BeTrue();
        Parse(result.MergedJson).GetProperty("name").GetString().Should().Be("Pino");
    }

    [Fact]
    public void Merge_NestedObjects_MergesDeeplyDisjoint()
    {
        var baseJson = "{\"user\":{\"address\":{\"city\":\"Rome\",\"zip\":\"00100\"},\"age\":30}}";
        var a = "{\"user\":{\"address\":{\"city\":\"Milan\",\"zip\":\"00100\"},\"age\":30}}";
        var b = "{\"user\":{\"address\":{\"city\":\"Rome\",\"zip\":\"20100\"},\"age\":31}}";

        var result = With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeFalse();
        var merged = Parse(result.MergedJson);
        merged.GetProperty("user").GetProperty("address").GetProperty("city").GetString().Should().Be("Milan");
        merged.GetProperty("user").GetProperty("address").GetProperty("zip").GetString().Should().Be("20100");
        merged.GetProperty("user").GetProperty("age").GetInt32().Should().Be(31);
    }

    [Fact]
    public void Merge_VersionAEqualsBase_UsesBWithoutConflict()
    {
        var baseJson = Json(new { x = 1, y = 1 });
        var a = Json(new { x = 1, y = 1 });
        var b = Json(new { x = 2, y = 2 });

        var result = With(ConflictStrategy.ThrowOnConflict).Merge(baseJson, a, b);

        result.HasConflicts.Should().BeFalse();
        var merged = Parse(result.MergedJson);
        merged.GetProperty("x").GetInt32().Should().Be(2);
        merged.GetProperty("y").GetInt32().Should().Be(2);
    }
}
