# EntglDb.ConflictResolution

Standalone .NET library for **3-way JSON merge** built on **JSON Diff / JSON Patch (RFC 6902 / 6901)**.
It extends EntglDb's conflict resolution beyond whole-document *Last-Write-Wins* (LWW): when two nodes
edit the same document concurrently, changes that touch different fields are merged automatically, and
real conflicts (same field, different values) are dispatched to a configurable strategy.

- **Version:** 2.3.6
- **Target frameworks:** `netstandard2.1`, `net10.0`
- **Dependencies:** `System.Text.Json` only (bundled with .NET; an explicit package reference is added for the `netstandard2.1` target)
- **License:** MIT

---

## Installation

> **Status: ready to publish — not yet on NuGet.org.** `EntglDb.ConflictResolution.csproj` is fully
> configured for packaging (package id, version, description, MIT license, bundled README, tags,
> repository URL, symbol package). `dotnet pack` produces a valid `.nupkg` + `.snupkg` ready for
> `dotnet nuget push`; it simply hasn't been pushed to a public feed yet.
> See [Packaging & publishing](#packaging--publishing).

Pick one of the following.

### Option A — Project reference (recommended for this repository)

This is how the demo and the test project consume it.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\EntglDb.ConflictResolution\EntglDb.ConflictResolution.csproj" />
</ItemGroup>
```

```bash
dotnet add <your-project> reference src/EntglDb.ConflictResolution/EntglDb.ConflictResolution.csproj
```

### Option B — Build a local NuGet package

```bash
# From src/EntglDb.ConflictResolution/
dotnet pack -c Release
# → bin/Release/EntglDb.ConflictResolution.2.3.6.nupkg
```

Add the output folder as a local feed, then reference it:

```bash
dotnet nuget add source <path-to>/bin/Release --name entgldb-local
dotnet add package EntglDb.ConflictResolution --version 2.3.6
```

### Option C — Once published to NuGet.org

```bash
dotnet add package EntglDb.ConflictResolution
```

---

## Quick start

```csharp
using EntglDb.ConflictResolution;

var baseJson = """{ "name": "Mario", "surname": "Rossi", "age": 30 }""";
var versionA = """{ "name": "Mario Bianchi", "surname": "Rossi", "age": 30 }"""; // node A changed name
var versionB = """{ "name": "Mario", "surname": "Rossi", "age": 31 }""";         // node B changed age

var merger = new DocumentMerger();
MergeResult result = merger.Merge(baseJson, versionA, versionB);

// result.HasConflicts == false
// result.MergedJson  == {"age":31,"name":"Mario Bianchi","surname":"Rossi"}
```

Disjoint edits merge with no conflict. Object keys are emitted in ordinal-sorted order.

---

## How it works — 3-way merge

The merger compares each node version against a common **base** snapshot (the document at fork time):

```
Input:  baseJson   snapshot before the fork
        versionA   node A's version
        versionB   node B's version

For every JSON node, walked recursively:
  A == base && B == base  → keep base
  A == base               → take B   (only B changed)
  B == base               → take A   (only A changed)
  A == B                  → take A   (same change on both sides)
  A.Kind != B.Kind        → TypeMismatch conflict
  object                  → recurse per key (key removals detected via base)
  array                   → see "Array handling"
  scalar, both changed    → ValueChanged conflict
```

Conflicts are collected during the walk. After the walk, if the configured strategy is
`ThrowOnConflict`, a `MergeConflictException` is thrown; otherwise the strategy's chosen value is
already written into the merged output.

### 2-way mode (no base)

If `baseJson` is `null` / empty / whitespace, the merger runs in **2-way mode**:

- Objects still merge key-by-key (keys present on one side only are kept; **removals cannot be
  detected** without a base).
- Any scalar divergence is a `ValueChanged` conflict.
- Arrays: `AppendArray` mode merges by id; `ReplaceArray` mode reports an `ArrayConflict`.

---

## API reference

### `IDocumentMerger` / `DocumentMerger`

```csharp
public interface IDocumentMerger
{
    MergeResult Merge(string? baseJson, string versionA, string versionB);
    MergeResult Merge(string? baseJson, string versionA, string versionB, MergeOptions options);
}

public sealed class DocumentMerger : IDocumentMerger
{
    public MergeOptions Options { get; }
    public DocumentMerger();                       // uses MergeOptions.Default
    public DocumentMerger(MergeOptions options);
}
```

`versionA` and `versionB` must not be `null` (throws `ArgumentNullException`). Invalid JSON throws
`JsonException` from `System.Text.Json`.

### `MergeResult` / `MergeConflict`

```csharp
public readonly record struct MergeResult(
    bool HasConflicts,
    string MergedJson,
    IReadOnlyList<MergeConflict> Conflicts);

public readonly record struct MergeConflict(
    string Path,             // JSON Pointer (RFC 6901), e.g. "/address/city"
    JsonElement ValueA,
    JsonElement ValueB,
    MergeConflictType Type);

public enum MergeConflictType { ValueChanged, ArrayConflict, TypeMismatch }
```

### `MergeOptions`

| Property | Type | Default | Purpose |
|---|---|---|---|
| `ConflictStrategy` | `ConflictStrategy` | `ThrowOnConflict` | How real conflicts are resolved |
| `ArrayMergeMode` | `ArrayMergeMode` | `ReplaceArray` | Array merge behaviour |
| `ArrayIdSelector` | `Func<JsonElement, string?>?` | `null` | Identity key for array elements (`AppendArray`) |
| `HlcComparator` | `Func<MergeConflict, int>?` | `null` | Required by `PreferLatestHlc`; `> 0` ⇒ B newer |
| `WriteIndented` | `bool` | `false` | Pretty-print the merged JSON |

`MergeOptions.Default` is a shared instance with all defaults.

---

## Conflict strategies

Pass via `MergeOptions.ConflictStrategy`:

| Strategy | Behaviour |
|---|---|
| `ConflictStrategy.ThrowOnConflict` | **Default.** Walk completes, then throws `MergeConflictException` carrying all conflicts |
| `ConflictStrategy.PreferA` | Always keep node A's value |
| `ConflictStrategy.PreferB` | Always keep node B's value |
| `ConflictStrategy.PreferLatestHlc` | Per-field LWW — keeps the side with the newer HLC. **Requires** `MergeOptions.HlcComparator`, else throws `InvalidOperationException` |
| `ConflictStrategy.Custom(Func<MergeConflict, JsonElement>)` | Your callback decides the value for each conflicting field |

```csharp
var opts = new MergeOptions { ConflictStrategy = ConflictStrategy.PreferB };
var merged = new DocumentMerger(opts).Merge(baseJson, a, b);   // never throws; B wins ties

// Custom
var custom = new MergeOptions
{
    ConflictStrategy = ConflictStrategy.Custom(c => c.ValueA.GetRawText().Length >= c.ValueB.GetRawText().Length
        ? c.ValueA : c.ValueB)
};
```

---

## Array handling

Controlled by `MergeOptions.ArrayMergeMode`:

### `ReplaceArray` (default)

The array is treated as one atomic value.

- With base: if **only one** side changed the array → that side wins; if **both** changed → `ArrayConflict`.
- Without base: any divergence → `ArrayConflict`.

### `AppendArray`

Element-wise merge.

- **Arrays of primitives** → order-preserving union, de-duplicated by raw JSON text.
- **Arrays of objects** → indexed by identity:
  - `ArrayIdSelector` if provided; otherwise the default selector reads `id`, then `_id`.
  - Elements with no resolvable id fall back to a positional key (not stable across reorders).
  - Per element: present on both sides and equal → kept; changed on one side only (vs base) → that
    side wins; changed on both → `ArrayConflict`. Element present on one side: kept, unless the base
    shows the other side deleted it.

```csharp
var opts = new MergeOptions
{
    ArrayMergeMode  = ArrayMergeMode.AppendArray,
    ArrayIdSelector = el => el.TryGetProperty("sku", out var p) ? p.GetString() : null,
};
```

---

## Low-level JSON Diff / Patch API

The merge engine is built on a public RFC 6902 / 6901 layer you can use directly:

```csharp
using EntglDb.ConflictResolution;
using System.Text.Json;

using var baseDoc   = JsonDocument.Parse("""{ "a": 1, "b": 2 }""");
using var targetDoc = JsonDocument.Parse("""{ "a": 1, "b": 9, "c": 3 }""");

// Diff: base → target
IReadOnlyList<JsonPatchOperation> ops =
    JsonDiff.Compute(baseDoc.RootElement, targetDoc.RootElement);
// → Replace /b = 9 ; Add /c = 3

// Patch: apply ops to a document
JsonElement patched = JsonPatch.Apply(baseDoc.RootElement, ops);
```

```csharp
public enum JsonPatchOperationType { Add, Remove, Replace, Copy, Move, Test }

public readonly record struct JsonPatchOperation(
    JsonPatchOperationType Op,
    string Path,            // JSON Pointer
    JsonElement? Value,
    string? From);          // for Copy / Move
```

`JsonPatch.Apply` supports all six RFC 6902 operations and the `-` end-of-array token for `Add`.

---

## Integration with EntglDb

EntglDb's core contract is `EntglDb.Core.Sync.IConflictResolver`:

```csharp
ConflictResolutionResult Resolve(Document? local, OplogEntry remote);
```

The adapter that bridges this library to that contract is **`JsonDiffPatchResolver`**, currently
shipped in the demo project (`samples/EntglDb.ConflictResolution.Demo/JsonDiffPatchResolver.cs`) —
copy it into your own project or into `EntglDb.Net` to use it in production.

Because `IConflictResolver` only exposes the local document and the incoming remote oplog entry —
**there is no base snapshot** — `JsonDiffPatchResolver` invokes the merger in **2-way mode**
(`baseJson = null`). It defaults to `ConflictStrategy.PreferLatestHlc`, feeding an `HlcComparator`
derived from the documents' HLC timestamps — i.e. **per-field LWW**, an upgrade over EntglDb's
whole-document LWW resolver.

```csharp
using EntglDb.ConflictResolution;
using EntglDb.Core.Sync;

// Default: per-field LWW driven by HLC timestamps
IConflictResolver resolver = new JsonDiffPatchResolver();

// Or with explicit options
IConflictResolver custom = new JsonDiffPatchResolver(
    new MergeOptions
    {
        ConflictStrategy = ConflictStrategy.PreferB,
        ArrayMergeMode   = ArrayMergeMode.AppendArray,
    });
```

It also mirrors existing resolvers for the non-merge cases: no local document → install the remote
payload; remote tombstone → LWW on the delete.

---

## Build, test, demo

```bash
# Build the library
dotnet build src/EntglDb.ConflictResolution/EntglDb.ConflictResolution.csproj

# Run the test suite (34 tests — disjoint merges, conflict strategies, arrays, edge cases)
dotnet test tests/EntglDb.ConflictResolution.Tests/

# Run the interactive console demo (3 scenarios)
dotnet run --project samples/EntglDb.ConflictResolution.Demo/
```

The demo walks through a disjoint merge, the four conflict strategies on a same-field conflict, and
the `JsonDiffPatchResolver` bridge against EntglDb `Document` / `OplogEntry` types.

---

## Packaging & publishing

The project is **ready to be published on NuGet**. `EntglDb.ConflictResolution.csproj` is fully
configured for packaging:

| Property | Value |
|---|---|
| `PackageId` | `EntglDb.ConflictResolution` |
| `Version` | `2.3.6` |
| `Authors` | `francescopaba` |
| `Description` / `Copyright` | set |
| `PackageLicenseExpression` | `MIT` |
| `PackageReadmeFile` | `README.md` — this file, bundled into the package |
| `PackageTags` | `conflict-resolution;json-patch;json-diff;merge;entgldb;crdt` |
| `PackageProjectUrl` / `RepositoryUrl` | GitHub repository |
| `IncludeSymbols` / `SymbolPackageFormat` | `true` / `snupkg` |

Build and publish:

```bash
# From src/EntglDb.ConflictResolution/
dotnet pack -c Release
# → bin/Release/EntglDb.ConflictResolution.2.3.6.nupkg
# → bin/Release/EntglDb.ConflictResolution.2.3.6.snupkg

dotnet nuget push bin/Release/EntglDb.ConflictResolution.2.3.6.nupkg \
  --api-key <your-nuget-api-key> \
  --source https://api.nuget.org/v3/index.json
```

Bump `<Version>` in the `.csproj` for every release.

## License

MIT — see the repository `LICENSE` file.
