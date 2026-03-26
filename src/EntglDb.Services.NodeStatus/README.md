# EntglDb.Services.NodeStatus

Diagnostic service for the **EntglDb** P2P mesh network. Allows any node to query the runtime status of remote peers — uptime, known peer addresses, and service version — over the shared TCP connection.

Wire types: **1000** (request) / **1001** (response).

## Installation

```bash
dotnet add package EntglDb.Network
dotnet add package EntglDb.Services.NodeStatus
```

## Quick Start

```csharp
builder.Services
    .AddEntglDbNetwork<MyConfigProvider>()
    .AddEntglDbNodeStatus();    // registers server handler + INodeStatusService client
```

Query a remote peer:

```csharp
var status = await nodeStatusService.QueryAsync("192.168.1.10:9000", ct);
Console.WriteLine($"Node {status.NodeId} up for {status.Uptime}, knows {status.KnownPeerAddresses.Count} peers");
```

## How It Works

`AddEntglDbNodeStatus()` registers two things:

- **`NodeStatusHandler`** (server side) — handles incoming `NodeStatusReq` messages (wire type 1000) and replies with a snapshot of the local node's state.
- **`INodeStatusService`** / `NodeStatusClient` (client side) — sends a `NodeStatusReq` to the target peer via `IPeerMessenger` and maps the response to `NodeStatusInfo`.

The connection is shared with all other EntglDb services (sync, file transfer, etc.) — no extra socket is opened.
