---
layout: default
title: Querying
---

# Querying

EntglDb exposes local data via the **DocumentStore** pattern. Collections are queried through your store's persistence layer (BLite or EF Core), with changes monitored via CDC and propagated to peers automatically.

## The DocumentStore Pattern

All data access goes through a class that extends `BLiteDocumentStore<T>` (or `EfCoreDocumentStore<T>`). Collections tracked with `WatchCollection<T>()` are synced; unregistered collections are local-only.

```csharp
public class AppDocumentStore : BLiteDocumentStore<AppDbContext>
{
    public AppDocumentStore(AppDbContext ctx, IEntglDbSyncManager sync)
        : base(ctx, sync)
    {
        WatchCollection<TodoItem>("todos");   // synced
        WatchCollection<Note>("notes");       // synced
        // Collections NOT called with WatchCollection are local-only
    }
}
```

## Basic CRUD

```csharp
// Inject your store via DI
public class TodoService
{
    private readonly AppDocumentStore _store;
    public TodoService(AppDocumentStore store) => _store = store;

    // Insert / update (upsert by document Id)
    public Task SaveAsync(TodoItem item)
        => _store.UpsertAsync("todos", item.Id, item);

    // Read single by key
    public Task<TodoItem?> GetAsync(string id)
        => _store.GetAsync<TodoItem>("todos", id);

    // Read all
    public Task<IEnumerable<TodoItem>> GetAllAsync()
        => _store.QueryAsync<TodoItem>("todos");

    // Delete
    public Task DeleteAsync(string id)
        => _store.DeleteAsync("todos", id);
}
```

## LINQ-Style Querying (BLite Provider)

When using `EntglDb.Persistence.BLite`, collections are backed by BLite embedded storage and expose a rich query API via the `Find` method.

```csharp
var collection = _store.GetCollection<TodoItem>("todos");

// Equality
var item = await collection.Find(t => t.Id == "abc123");

// Comparisons
var overdue = await collection.Find(t => t.DueDate < DateTime.UtcNow);

// Logical operators (AND / OR)
var urgent = await collection.Find(t => t.Priority == "High" && !t.IsCompleted);

// Contains (string)
var search = await collection.Find(t => t.Title.Contains("meeting"));

// Multiple results (returns IEnumerable<T>)
var completed = await collection.FindMany(t => t.IsCompleted);
```

### Supported Operators

| Operator | Description | Example |
|---|---|---|
| `==` | Equal | `t.Status == "active"` |
| `!=` | Not equal | `t.Status != "deleted"` |
| `>` | Greater than | `t.Score > 100` |
| `<` | Less than | `t.DueDate < DateTime.UtcNow` |
| `>=` | Greater than or equal | `t.Age >= 18` |
| `<=` | Less than or equal | `t.Priority <= 3` |
| `&&` | AND | `t.IsActive && t.IsVerified` |
| `\|\|` | OR | `t.Role == "Admin" \|\| t.Role == "Owner"` |
| `.Contains()` | String contains | `t.Name.Contains("foo")` |
| `.StartsWith()` | String prefix | `t.Code.StartsWith("INV-")` |

## Serialization & JSON Property Names

EntglDb respects `[JsonPropertyName]` attributes when translating LINQ expressions to storage queries.

```csharp
public class TodoItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("is_completed")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("due_date")]
    public DateTime? DueDate { get; set; }
}

// The expression below correctly queries json_extract(data, '$.is_completed')
var completed = await collection.FindMany(t => t.IsCompleted);
```

## Pagination & Ordering

```csharp
// Order and paginate
var page = await collection.FindManySorted(
    predicate: t => t.IsCompleted == false,
    orderBy: t => t.DueDate,
    ascending: true,
    skip: 0,
    take: 20);
```

## EF Core Provider Querying

When using `EntglDb.Persistence.EntityFramework`, collections are backed by your configured relational database (PostgreSQL, SQL Server, SQLite). Use standard EF Core LINQ queries via the `DbContext`:

```csharp
public class AppDocumentStore : EfCoreDocumentStore<AppDbContext>
{
    // WatchCollection registers CDC hooks; querying uses DbContext directly
}

// In your service:
await using var ctx = _contextFactory.CreateDbContext();
var todos = await ctx.Set<TodoItem>()
    .Where(t => !t.IsCompleted && t.DueDate < DateTime.UtcNow.AddDays(1))
    .OrderBy(t => t.DueDate)
    .ToListAsync();
```

## Accessing DocumentMetadata

Every document has associated metadata (sync timestamps, vector clock, ContentHash) accessible via:

```csharp
var meta = await _store.GetMetadataAsync("todos", item.Id);

Console.WriteLine($"Last modified: {meta.UpdatedAt}");
Console.WriteLine($"ContentHash:   {meta.ContentHash}");  // SHA-256, v2.1+
Console.WriteLine($"Modified by:   {meta.NodeId}");
```

The `ContentHash` (added in v2.1) is a SHA-256 hash of the document's canonical JSON representation, enabling fast integrity verification without full document comparison.

## Sync-Aware Queries

To query changes since a specific vector clock position (useful for building change feeds):

```csharp
var changes = await _store.GetOplogEntriesAfterAsync("todos", sinceSequence: 1000);
foreach (var entry in changes)
{
    Console.WriteLine($"{entry.Operation} {entry.DocumentId} @ seq {entry.Sequence}");
}
```

## Snapshot Queries

The snapshot service generates point-in-time snapshots of all watched collections. To force a snapshot and retrieve it:

```csharp
// Inject ISnapshotService
await _snapshotService.CreateSnapshotAsync();

var snapshot = await _snapshotService.GetLatestSnapshotAsync("todos");
// snapshot.Documents is IReadOnlyList<T>
```

