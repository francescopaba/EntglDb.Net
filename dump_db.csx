#r "samples/EntglDb.Demo.Game/bin/Debug/net10.0/BLite.dll"
#r "samples/EntglDb.Demo.Game/bin/Debug/net10.0/BLite.Core.dll"
#r "samples/EntglDb.Demo.Game/bin/Debug/net10.0/BLite.Bson.dll"
#r "samples/EntglDb.Demo.Game/bin/Debug/net10.0/EntglDb.Demo.Game.dll"
#r "samples/EntglDb.Demo.Game/bin/Debug/net10.0/EntglDb.Persistence.BLite.dll"
#r "samples/EntglDb.Demo.Game/bin/Debug/net10.0/EntglDb.Core.dll"
#r "samples/EntglDb.Demo.Game/bin/Debug/net10.0/EntglDb.Persistence.dll"

using EntglDb.Demo.Game;
using EntglDb.Persistence.BLite.Entities;
using BLite.Core.Query;
using System.Text.Json;

var dataDir = "samples/EntglDb.Demo.Game/bin/Debug/net10.0/data";
var nodes = new[] { "hero-5124", "hero-5807" };

foreach (var nodeId in nodes)
{
    var dbPath = Path.Combine(dataDir, $"{nodeId}.blite");
    Console.WriteLine($"\n{'=',60}");
    Console.WriteLine($"  NODE: {nodeId}");
    Console.WriteLine($"{'=',60}");

    using var db = new GameDbContext(dbPath);

    // Heroes
    Console.WriteLine("\n--- HEROES ---");
    var heroes = db.Heroes.FindAll().ToList();
    foreach (var h in heroes)
        Console.WriteLine($"  [{h.Id}] {h.Name}  Lv:{h.Level}  HP:{h.Hp}/{h.MaxHp}  ATK:{h.Attack}  DEF:{h.Defense}  XP:{h.Xp}  Gold:{h.Gold}  Kills:{h.MonstersKilled}  Alive:{h.IsAlive}  NodeId:{h.NodeId}");

    // DocumentMetadatas (ALL)
    Console.WriteLine("\n--- DOC METADATA (all) ---");
    var metas = db.DocumentMetadatas.FindAll().ToList();
    foreach (var m in metas)
        Console.WriteLine($"  Key:{m.Key}  T:{m.HlcPhysicalTime}|{m.HlcLogicalCounter}|{m.HlcNodeId}  Deleted:{m.IsDeleted}");

    // Oplog entries (last 10 per node)
    Console.WriteLine("\n--- OPLOG (last 15) ---");
    var oplog = db.OplogEntries.AsQueryable()
        .OrderByDescending(e => e.TimestampPhysicalTime)
        .ThenByDescending(e => e.TimestampLogicalCounter)
        .Take(15)
        .ToList();
    foreach (var e in oplog)
    {
        var h = string.IsNullOrEmpty(e.Hash) ? "(none)" : e.Hash[..Math.Min(8, e.Hash.Length)];
        var p = string.IsNullOrEmpty(e.PreviousHash) ? "(none)" : e.PreviousHash[..Math.Min(8, e.PreviousHash.Length)];
        var payload = string.IsNullOrEmpty(e.PayloadJson) ? "" : e.PayloadJson;
        if (payload.Length > 120) payload = payload[..120] + "...";
        Console.WriteLine($"  {e.Operation,-6} {e.Collection}/{e.Key}  T:{e.TimestampPhysicalTime}|{e.TimestampLogicalCounter}|{e.TimestampNodeId}  Hash:{h}  Prev:{p}");
        if (e.Collection == "Heroes") Console.WriteLine($"         PAYLOAD: {payload}");
    }
}
