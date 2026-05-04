using System;
using System.Text.Json;
using EntglDb.ConflictResolution;
using EntglDb.ConflictResolution.Demo;
using EntglDb.Core;

PrintBanner("EntglDb.ConflictResolution — Console Demo");
Console.WriteLine();

RunScenario1_DisjointMerge();
RunScenario2_ConflictStrategies();
RunScenario3_EntglDbIntegration();

PrintBanner("Demo complete");

static void RunScenario1_DisjointMerge()
{
    PrintBanner("Scenario 1 — Disjoint merge (no conflicts)");

    var baseJson = "{\"name\":\"Mario\",\"surname\":\"Rossi\",\"age\":30}";
    var versionA = "{\"name\":\"Mario Bianchi\",\"surname\":\"Rossi\",\"age\":30}";
    var versionB = "{\"name\":\"Mario\",\"surname\":\"Rossi\",\"age\":31}";

    PrintJson("Base", baseJson);
    PrintJson("Node A (changed name)", versionA);
    PrintJson("Node B (changed age)", versionB);

    var merger = new DocumentMerger(new MergeOptions { WriteIndented = true });
    var result = merger.Merge(baseJson, versionA, versionB);

    Console.WriteLine($"HasConflicts: {result.HasConflicts}");
    Console.WriteLine($"Conflicts:    {result.Conflicts.Count}");
    PrintJson("Merged", result.MergedJson);
    Console.WriteLine();
}

static void RunScenario2_ConflictStrategies()
{
    PrintBanner("Scenario 2 — Same-field conflict, four strategies");

    var baseJson = "{\"name\":\"Mario\"}";
    var versionA = "{\"name\":\"Luigi\"}";
    var versionB = "{\"name\":\"Pino\"}";

    PrintJson("Base", baseJson);
    PrintJson("Node A", versionA);
    PrintJson("Node B", versionB);
    Console.WriteLine();

    RunStrategy("PreferA", new MergeOptions { ConflictStrategy = ConflictStrategy.PreferA }, baseJson, versionA, versionB);
    RunStrategy("PreferB", new MergeOptions { ConflictStrategy = ConflictStrategy.PreferB }, baseJson, versionA, versionB);
    RunStrategy(
        "PreferLatestHlc (B newer)",
        new MergeOptions { ConflictStrategy = ConflictStrategy.PreferLatestHlc, HlcComparator = _ => 1 },
        baseJson, versionA, versionB);
    RunStrategy(
        "Custom (always 'Bowser')",
        new MergeOptions { ConflictStrategy = ConflictStrategy.Custom(_ => Elem("\"Bowser\"")) },
        baseJson, versionA, versionB);

    try
    {
        var thrower = new DocumentMerger(new MergeOptions { ConflictStrategy = ConflictStrategy.ThrowOnConflict });
        thrower.Merge(baseJson, versionA, versionB);
    }
    catch (MergeConflictException ex)
    {
        Console.WriteLine($"  ThrowOnConflict     -> exception: {ex.Conflicts.Count} conflict(s) at {ex.Conflicts[0].Path}");
    }
    Console.WriteLine();
}

static void RunScenario3_EntglDbIntegration()
{
    PrintBanner("Scenario 3 — EntglDb bridge (JsonDiffPatchResolver)");

    var localContent = JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Mario\",\"city\":\"Rome\"}");
    var localTs = new HlcTimestamp(100, 0, "node-A");
    var local = new Document("users", "u1", localContent, localTs, false);

    var remoteTs = new HlcTimestamp(200, 0, "node-B");
    var remote = new OplogEntry(
        collection: "users",
        key: "u1",
        operation: OperationType.Put,
        payload: "{\"name\":\"Mario\",\"city\":\"Milan\",\"age\":31}",
        timestamp: remoteTs,
        previousHash: string.Empty);

    Console.WriteLine($"Local  ({localTs}): {JsonSerializer.Serialize(localContent)}");
    Console.WriteLine($"Remote ({remoteTs}): {remote.Payload}");
    Console.WriteLine();

    var resolver = new JsonDiffPatchResolver();
    var result = resolver.Resolve(local, remote);

    Console.WriteLine($"ShouldApply: {result.ShouldApply}");
    if (result.MergedDocument != null)
    {
        PrintJson("Merged document", result.MergedDocument.Content.GetRawText());
        Console.WriteLine($"UpdatedAt: {result.MergedDocument.UpdatedAt}");
    }
    Console.WriteLine();
}

static void RunStrategy(string label, MergeOptions options, string baseJson, string a, string b)
{
    options = new MergeOptions
    {
        ConflictStrategy = options.ConflictStrategy,
        ArrayMergeMode = options.ArrayMergeMode,
        ArrayIdSelector = options.ArrayIdSelector,
        HlcComparator = options.HlcComparator,
        WriteIndented = false
    };
    var merger = new DocumentMerger(options);
    var result = merger.Merge(baseJson, a, b);
    Console.WriteLine($"  {label,-22} -> {result.MergedJson}   (conflicts: {result.Conflicts.Count})");
}

static void PrintBanner(string title)
{
    var line = new string('=', Math.Max(title.Length + 4, 60));
    Console.WriteLine(line);
    Console.WriteLine($"  {title}");
    Console.WriteLine(line);
}

static void PrintJson(string label, string json)
{
    using var doc = JsonDocument.Parse(json);
    var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"--- {label} ---");
    Console.WriteLine(pretty);
}

static JsonElement Elem(string raw)
{
    using var doc = JsonDocument.Parse(raw);
    return doc.RootElement.Clone();
}
