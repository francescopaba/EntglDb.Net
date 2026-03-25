using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Network;
using EntglDb.Persistence.BLite;
using EntglDb.Sample.Shared;
using EntglDb.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => 
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "EntglDb ASP.NET Node", 
        Version = "v0.8.6",
        Description = "A decentralized peer-to-peer database node running on ASP.NET Core. Features P2P syncing, dynamic discovery, and vector-clock based consistency."
    });
});
builder.Services.AddControllers();

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<EntglDbHealth>("EntglDb");

// Register Configuration Provider
builder.Services.AddSingleton<AspNetPeerNodeConfigurationProvider>();

// Configure EntglDb
var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
Directory.CreateDirectory(dataPath);
var nodeName = builder.Configuration["EntglDb:NodeName"] ?? "AspNetSampleNode";
var databasePath = Path.Combine(dataPath, $"{nodeName}.blite");

builder.Services.AddEntglDbCore()
    .AddEntglDbBLite<SampleDbContext, SampleDocumentStore>(sp => new SampleDbContext(databasePath), databasePath + ".meta")
    .AddEntglDbNetwork<AspNetPeerNodeConfigurationProvider>() // transport only
    .AddEntglDbSync(useHostedService: true); // sync handlers + node orchestrator

var app = builder.Build();

app.UseStaticFiles(); // Serve wwwroot for custom CSS

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => 
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EntglDb API v1");
        c.InjectStylesheet("/css/swagger-custom.css");
        c.DocumentTitle = "EntglDb Node API";
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// API: Get Available Collections
app.MapGet("/api/collections", (SampleDbContext db) =>
{
    return Results.Ok(new[] { "Users", "TodoLists" });
})
.WithName("GetCollections");

// API: Get Connected Peers
app.MapGet("/api/peers", (IDiscoveryService discovery) =>
{
    var activePeers = discovery.GetActivePeers();
    return Results.Ok(activePeers);
})
.WithName("GetPeers");

// API: Get Telemetry
app.MapGet("/api/telemetry", async (SampleDbContext db, EntglDb.Network.Telemetry.INetworkTelemetryService telemetry) =>
{
    var counts = new Dictionary<string, int>
    {
        ["Users"] = await db.Users.FindAllAsync().CountAsync(),
        ["TodoLists"] = await db.TodoLists.FindAllAsync().CountAsync()
    };

    return Results.Ok(new
    {
        DocumentCounts = counts,
        NetworkStats = telemetry.GetSnapshot(),
        Timestamp = DateTime.UtcNow
    });
})
.WithName("GetTelemetry");

app.Run();

// Configuration Provider implementation
public class AspNetPeerNodeConfigurationProvider : IPeerNodeConfigurationProvider
{
    private readonly PeerNodeConfiguration _config;

    public AspNetPeerNodeConfigurationProvider(IConfiguration configuration)
    {
        var nodeName = configuration["EntglDb:NodeName"] ?? "AspNetCoreNode";
        var portObj = configuration["EntglDb:Port"];
        int port = int.TryParse(portObj, out int p) ? p : 4001;
        var authToken = configuration["EntglDb:AuthToken"] ?? "Test-Cluster-Key";

        _config = new PeerNodeConfiguration
        {
            NodeId = nodeName,
            TcpPort = port,
            AuthToken = authToken
        };
    }

    public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;

    public Task<PeerNodeConfiguration> GetConfiguration()
    {
        return Task.FromResult(_config);
    }
}

public class EntglDbHealth : IHealthCheck
{
    private readonly SampleDbContext _db;

    public EntglDbHealth(SampleDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await _db.Users.FindAllAsync().CountAsync();
            return HealthCheckResult.Healthy("EntglDb is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("EntglDb is unreachable", ex);
        }
    }
}
