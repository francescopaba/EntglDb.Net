# Getting Started (v2.1)

## Installation

EntglDb is available as a set of NuGet packages targeting `.NET Standard 2.1` and `.NET 10.0`.

> **Breaking Change (v2.0)**: EntglDb no longer supports `netstandard2.0`, `net6.0`, or `net8.0`. Upgrade to `.NET 10.0` for full feature support, or use `.NET Standard 2.1` compatible hosts.

### Core Packages

```bash
# Core abstractions and sync engine
dotnet add package EntglDb.Core

# P2P networking (TCP sync, UDP discovery, Protobuf protocol)
dotnet add package EntglDb.Network

# Sync orchestration (oplog, vector clocks, CDC)
dotnet add package EntglDb.Sync
```

### Persistence Providers

```bash
# BLite embedded document database (recommended for desktop/mobile/edge)
dotnet add package EntglDb.Persistence.BLite

# Entity Framework Core (SQL Server, PostgreSQL, MySQL, SQLite)
dotnet add package EntglDb.Persistence.EntityFramework
```

### Cloud & ASP.NET Core

```bash
# ASP.NET Core hosting with health checks, multi-cluster support
dotnet add package EntglDb.AspNet
```

## Requirements

- **.NET 10.0** (recommended) or any runtime supporting `.NET Standard 2.1`
- **BLite 3.7.0** (bundled with `EntglDb.Persistence.BLite`)
- **EF Core 10.0.5** (bundled with `EntglDb.Persistence.EntityFramework`)

## Package Overview

| Package | Purpose | Target Framework |
|---------|---------|-----------------|
| `EntglDb.Core` | Interfaces, models, conflict resolution | netstandard2.1; net10.0 |
| `EntglDb.Sync` | Oplog, Vector Clock, CDC orchestration | netstandard2.1; net10.0 |
| `EntglDb.Network` | TCP sync, UDP discovery, Protobuf wire format | netstandard2.1; net10.0 |
| `EntglDb.Persistence` | Abstract persistence interfaces | netstandard2.1; net10.0 |
| `EntglDb.Persistence.BLite` | BLite embedded document DB provider | net10.0 |
| `EntglDb.Persistence.EntityFramework` | EF Core provider (multi-database) | net10.0 |
| `EntglDb.AspNet` | ASP.NET Core hosting, health checks | net10.0 |

## Basic Usage

### 1. Define Your Database Context

```csharp
public class MyDbContext : EntglDocumentDbContext
{
    public DocumentCollection<string, Customer> Customers { get; private set; }
    public DocumentCollection<string, Order> Orders { get; private set; }

    public MyDbContext(string dbPath) : base(dbPath) { }
}
```

### 2. Create Your Document Store (the Sync Bridge)

This is where you tell EntglDb which collections to sync and how to map between your entities and the sync engine:

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
        WatchCollection("Customers", context.Customers, c => c.Id);
        WatchCollection("Orders", context.Orders, o => o.Id);
    }

    protected override async Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken ct)
    {
        switch (collection)
        {
            case "Customers":
                var customer = content.Deserialize<Customer>()!;
                customer.Id = key;
                var existing = _context.Customers.Find(c => c.Id == key).FirstOrDefault();
                if (existing != null) _context.Customers.Update(customer);
                else _context.Customers.Insert(customer);
                break;
        }
        await _context.SaveChangesAsync(ct);
    }

    // Implement GetEntityAsJsonAsync, RemoveEntityAsync, GetAllEntitiesAsJsonAsync...
}
```

### 3. Wire It Up with Dependency Injection

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddSingleton<IPeerNodeConfigurationProvider>(
    new StaticPeerNodeConfigurationProvider(new PeerNodeConfiguration
    {
        NodeId = "node-1",
        TcpPort = 8580,
        AuthToken = "my-cluster-secret"
    }));

builder.Services
    .AddEntglDbCore()
    .AddEntglDbBLite<MyDbContext, MyDocumentStore>(
        sp => new MyDbContext("mydata.blite"))
    .AddEntglDbNetwork<StaticPeerNodeConfigurationProvider>();

await builder.Build().RunAsync();
```

### 4. Use Your Database Normally

```csharp
public class MyService
{
    private readonly MyDbContext _db;

    public MyService(MyDbContext db) => _db = db;

    public async Task CreateCustomer(string name)
    {
        // Write directly — EntglDb handles sync automatically
        await _db.Customers.InsertAsync(
            new Customer { Id = Guid.NewGuid().ToString(), Name = name });
        await _db.SaveChangesAsync();
        // CDC detects the change, creates an OplogEntry, and gossips to peers
    }
}
```

## ASP.NET Core Deployment

### Single Cluster Mode (Recommended)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEntglDbEntityFramework(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("EntglDb"));
});

builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.NodeId = "server-01";
    options.TcpPort = 5001;
    options.RequireAuthentication = true;
    options.OAuth2Authority = "https://auth.example.com";
});

var app = builder.Build();
app.MapHealthChecks("/health");
await app.RunAsync();
```

### Multi-Cluster Mode

For multi-tenant scenarios or shared hosting:

```csharp
builder.Services.AddEntglDbAspNetMultiCluster(options =>
{
    options.BasePort = 5001;
    options.ClusterCount = 10;
    options.RequireAuthentication = true;
    options.NodeIdTemplate = "server-{ClusterId}";
});
```

See [Deployment Modes](deployment-modes.md) for detailed comparison.

## Version History Highlights

### v2.1 — ContentHash & Integrity
- **ContentHash**: Deterministic SHA-256 content hash on `DocumentMetadata` with canonical JSON normalization
- **P2P Sync Correctness**: CDC serialization fixes, LWW timestamp accuracy, deterministic metadata IDs

### v2.0 — Platform Maturity (Breaking)
- **Framework Targeting**: `netstandard2.1;net10.0` only (dropped `netstandard2.0`, `net6.0`, `net8.0`)
- **Dynamic Database Paths**: Per-collection table support in SQLite persistence
- **Full Async BLite**: Complete async read/write operations
- **Centralized EntglDbNodeService**: Auto-registration in Network package
- **Remote Peer Auto-Sync**: Automatic synchronization of remote peer configurations via `_system_remote_peers`
- **Dependencies**: Microsoft.Extensions 10.0.5, EF Core 10.0.5

### v1.0 — Middleware Identity
- **BLiteDocumentStore**: Abstract base class for BLite persistence
- **EfCoreDocumentStore**: Abstract base class for EF Core persistence
- **DocumentMetadataStore**: HLC timestamp tracking per document
- **VectorClockService**: Shared singleton keeping Vector Clock in sync between CDC and OplogStore
- **Positioning**: Redefined from "P2P database" to "sync middleware"

### v0.8 — Cloud Infrastructure
- **ASP.NET Core Hosting**: Single and Multi-cluster deployment modes
- **Entity Framework Core**: SQL Server, PostgreSQL, MySQL, SQLite support
- **OAuth2 JWT Authentication**: Secure cloud deployments
- **Health Checks**: Production monitoring and observability
- **Snapshots**: Fast reconnection with delta sync

### v0.7 — Efficient Networking
- **Brotli Compression**: Bandwidth-efficient synchronization
- **Protocol v4**: Enhanced framing and security negotiation

### v0.6 — Security & Conflict Resolution
- **ECDH + AES-256**: Secure peer-to-peer communication
- **Recursive Merge**: Field-level JSON merge with array ID detection

See [CHANGELOG](https://github.com/EntglDb/EntglDb.Net/blob/main/CHANGELOG.md) for complete version history.

## Next Steps

- [Architecture Overview](architecture.html) - Understand HLC, Gossip Protocol, and mesh networking
- [Persistence Providers](persistence-providers.html) - Choose the right database for your deployment
- [Deployment Modes](deployment-modes.html) - Single vs Multi-cluster strategies
- [Security Configuration](security.html) - Encryption and authentication
- [Conflict Resolution Strategies](conflict-resolution.html) - LWW vs Recursive Merge
- [Production Hardening](production-hardening.html) - Best practices and monitoring
- [API Reference](api-reference.html) - Complete API documentation
