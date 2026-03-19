# EntglDb Persistence Providers

EntglDb supports multiple persistence backends through a clean abstraction layer. Choose the provider that best fits your deployment scenario.

## Overview

| Provider | Package | Best For | Target Framework |
|----------|---------|----------|-----------------|
| **BLite** | `EntglDb.Persistence.BLite` | Embedded/desktop/mobile/edge | net10.0 |
| **EF Core** | `EntglDb.Persistence.EntityFramework` | Cloud/enterprise, multi-database | net10.0 |

Both providers share the same abstract interfaces (`IDocumentStore`, `IOplogStore`, `ISnapshotMetadataStore`, `IDocumentMetadataStore`), making it straightforward to switch between them or implement a custom provider.

## BLite (Embedded Document Database)

**Package:** `EntglDb.Persistence.BLite`

BLite is a high-performance embedded BSON document database with zero external dependencies. It is the recommended provider for embedded, desktop, mobile, and edge scenarios.

### Characteristics

- ✅ **Zero configuration**: Works out of the box, single file database
- ✅ **High performance**: Native BSON engine, no ORM overhead
- ✅ **Full async**: Async read/write operations (v1.1+)
- ✅ **Per-collection tables**: Dynamic database paths for better isolation (v2.0+)
- ✅ **Snapshots**: Fast reconnection with `SnapshotMetadata`
- ✅ **ACID**: Write-Ahead Logging (WAL) for consistency
- ✅ **Vector search**: HNSW index support
- ✅ **Cross-platform**: Windows, Linux, macOS

### When to Use

- Desktop applications (Avalonia, WPF, WinForms)
- Mobile applications (.NET MAUI)
- Edge computing and IoT
- Development and testing
- Offline-first scenarios where portability matters

### Configuration

```csharp
// Register BLite persistence with DI
builder.Services
    .AddEntglDbCore()
    .AddEntglDbBLite<MyDbContext, MyDocumentStore>(
        sp => new MyDbContext("mydata.blite"));
```

### DocumentStore Implementation

Extend `BLiteDocumentStore<T>` to create your sync bridge:

```csharp
public class MyDocumentStore : BLiteDocumentStore<MyDbContext>
{
    public MyDocumentStore(
        MyDbContext context,
        IPeerNodeConfigurationProvider configProvider,
        IVectorClockService vectorClockService,
        ILogger<MyDocumentStore>? logger = null)
        : base(context, configProvider, vectorClockService, logger: logger)
    {
        WatchCollection("Users", context.Users, u => u.Id);
    }

    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken ct)
    {
        // Map incoming JSON to your entity and upsert
    }

    // Implement remaining abstract methods...
}
```

## EF Core (Entity Framework Core)

**Package:** `EntglDb.Persistence.EntityFramework`

The EF Core provider enables EntglDb to work with any database supported by Entity Framework Core, including SQL Server, PostgreSQL, MySQL, and SQLite.

### Characteristics

- ✅ **Multi-database support**: SQL Server, PostgreSQL, MySQL, SQLite
- ✅ **EF Core benefits**: Migrations, LINQ, change tracking
- ✅ **Type-safe**: Strongly-typed entities
- ✅ **Production-grade**: Connection pooling, retry logic
- ⚠️ **JSON queries**: Complex JSON queries evaluated in-memory

### When to Use

- Cloud deployments (Azure SQL, AWS RDS, etc.)
- Enterprise applications with existing SQL infrastructure
- Multi-tenant SaaS scenarios
- Teams already familiar with EF Core
- When database portability is important

### Configuration

#### SQL Server
```csharp
builder.Services.AddEntglDbEntityFramework(options =>
{
    options.UseSqlServer(
        "Server=localhost;Database=EntglDb;Integrated Security=true");
});
```

#### PostgreSQL
```csharp
builder.Services.AddDbContext<EntglDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddEntglDbEntityFramework();
```

#### MySQL
```csharp
var serverVersion = ServerVersion.AutoDetect(connectionString);
builder.Services.AddEntglDbEntityFrameworkMySql(connectionString, serverVersion);
```

#### SQLite (via EF Core)
```csharp
builder.Services.AddEntglDbEntityFrameworkSqlite("Data Source=entgldb.db");
```

### Migrations

```bash
dotnet ef migrations add InitialCreate --context EntglDbContext
dotnet ef database update --context EntglDbContext
```

### DocumentStore Implementation

Extend `EfCoreDocumentStore<T>` for EF Core:

```csharp
public class MyDocumentStore : EfCoreDocumentStore<MyEfDbContext>
{
    public MyDocumentStore(
        MyEfDbContext context,
        IPeerNodeConfigurationProvider configProvider,
        IVectorClockService vectorClockService,
        ILogger<MyDocumentStore>? logger = null)
        : base(context, configProvider, vectorClockService, logger: logger)
    {
        WatchCollection("Products", /* ... */);
    }
    // Implement abstract methods...
}
```

## Feature Comparison

| Feature | BLite | EF Core |
|---------|-------|---------|
| **Storage Format** | BSON file-based | Varies (SQL Server, PostgreSQL, etc.) |
| **Setup Complexity** | Zero config | Connection string + migrations |
| **Performance** | Excellent (native) | Good (ORM overhead) |
| **JSON Storage** | Native BSON | TEXT/NVARCHAR/JSONB |
| **Async Operations** | Full async (v1.1+) | Full async |
| **ContentHash** | ✅ (v2.1+) | ✅ (v2.1+) |
| **Snapshot Support** | ✅ | ✅ |
| **Per-Collection Tables** | ✅ (v2.0+) | Standard EF models |
| **Vector Search (HNSW)** | ✅ | ❌ |
| **Horizontal Scaling** | No | Yes (database-dependent) |
| **Connection Pooling** | N/A | Built-in |
| **Cloud Deployment** | Possible (file storage) | Recommended |

## Custom Persistence Providers

You can implement a custom persistence provider by implementing the core interfaces:

```csharp
public interface IDocumentStore
{
    Task PutAsync(string collection, string key, JsonElement content, CancellationToken ct);
    Task<JsonElement?> GetAsync(string collection, string key, CancellationToken ct);
    Task DeleteAsync(string collection, string key, CancellationToken ct);
    // ...
}

public interface IOplogStore
{
    Task AppendAsync(OplogEntry entry, CancellationToken ct);
    Task<IEnumerable<OplogEntry>> GetEntriesAfterAsync(HlcTimestamp after, CancellationToken ct);
    // ...
}
```

## Recommendations

### Development & Testing
- **Use BLite**: Fast, zero config, disposable, no server required

### Embedded / Edge / Mobile
- **Use BLite**: Best performance, single file, cross-platform, vector search

### Cloud / Enterprise
- **Use EF Core**: SQL Server or PostgreSQL for production workloads with managed database services

### Multi-Tenant SaaS
- **Use EF Core + ASP.NET Core Multi-Cluster**: Isolated databases per tenant with shared hosting

## Troubleshooting

### BLite: File locking issues
- Ensure only one process accesses the BLite file at a time
- Use per-collection tables (v2.0+) for better isolation

### EF Core: "Query evaluated in-memory"
- Expected for complex JSON queries
- Use simple equality/comparison operators for best performance
- Consider adding database indexes on frequently queried properties

### EF Core: Connection pool exhausted
- Increase `Maximum Pool Size` in connection string
- Ensure DbContext instances are properly disposed
- For PostgreSQL, consider PgBouncer for connection pooling
