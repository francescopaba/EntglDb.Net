# Architecture & Concepts

## Design Philosophy

EntglDb is a **peer-to-peer data synchronization middleware** designed for **Local Area Networks (LAN)** and **Local-First** scenarios. It is not a database — it is a **sync layer** that plugs into your existing data store and enables automatic replication across nodes in a mesh network.

Since v2.0, EntglDb also positions itself as a **P2P platform**: the mesh networking, discovery, and secure transport infrastructure can be leveraged to build additional distributed services beyond data synchronization.

**Target Deployments**:
- **LAN/Edge**: Trusted environments (offices, retail stores, factories, homes)
- **Cloud**: ASP.NET Core hosting with OAuth2 and EF Core for public-facing deployments
- **Hybrid**: LAN nodes with cloud gateway for cross-site replication

**Cross-Platform**: Windows, Linux, macOS (.NET 10.0+, with .NET Standard 2.1 support for broader compatibility)

## Core Architecture

```
+---------------------------------------------------+
|                  Your Application                 |
|  db.Users.InsertAsync(user)                       |
|  db.Users.Find(u => u.Age > 18)                   |
+---------------------------------------------------+
               | uses your DbContext directly
+---------------------------------------------------+
|         Your Database (BLite / EF Core)           |
|  +---------------------------------------------+  |
|  |  Users    |  Orders    |  Products    | ...  |  |
|  +---------------------------------------------+  |
|        | CDC (Change Data Capture)                |
|  +---------------------------------------------+  |
|  |  EntglDb Sync Engine                        |  |
|  |  - Oplog (append-only hash-chained journal) |  |
|  |  - Vector Clock (causal ordering)           |  |
|  |  - DocumentMetadata (ContentHash, HLC)      |  |
|  |  - Conflict Resolution (LWW / Recursive)    |  |
|  +---------------------------------------------+  |
+---------------------------------------------------+
              | P2P Network (TCP + UDP Discovery)
+---------------------------------------------------+
|           Other Nodes (same setup)                |
|  Node-A <-----> Node-B <-----> Node-C             |
+---------------------------------------------------+
```

### HLC (Hybrid Logical Clock)
To resolve conflicts without a central authority, EntglDb uses **Hybrid Logical Clocks**. HLC combines physical wall-clock time with logical counters to determine the "happened-before" relationship between events, even if system clocks are slightly skewed.

Each HLC timestamp is a struct with `PhysicalTime` (long), `LogicalCounter` (int), and `NodeId` (string). Ordering follows: PhysicalTime → LogicalCounter → NodeId (lexicographical).

## Synchronization

### Anti-Entropy
When two nodes connect, they exchange their latest HLC timestamps via Vector Clocks.
- If Node A is ahead of Node B, Node B "pulls" the missing operations from Node A.
- If Node B is ahead, Node A "pushes" its operations.
- If both have changes, a bidirectional sync occurs with conflict resolution.

### Gossip Protocol
Nodes discover each other via UDP Broadcast (LAN) and then form TCP connections to gossip updates every ~2 seconds. Updates propagate exponentially through the network (Epidemic Algorithm).

**Interest-Aware Gossip**: Nodes advertise which collections they sync. The orchestrator prioritizes peers sharing common interests, reducing unnecessary traffic.

### Snapshots & Fast Recovery
Each node maintains a **Snapshot** of the last known state (Hash & Timestamp) for every peer.
- When re-connecting, nodes compare their snapshot state.
- If the chain hash matches, they only exchange the delta.
- **Boundary Convergence**: Self-healing mechanism that prevents infinite loops when history is pruned differently across nodes.

### ContentHash Integrity (v2.1)
Each `DocumentMetadata` entry now includes a deterministic **ContentHash** (SHA-256) computed from canonical JSON normalization. This enables fast integrity verification without comparing full document content.

## Persistence Layer

EntglDb provides two production-ready persistence providers:

| Provider | Package | Best For |
|----------|---------|----------|
| **BLite** | `EntglDb.Persistence.BLite` | Embedded/desktop/mobile/edge — high performance, zero config |
| **EF Core** | `EntglDb.Persistence.EntityFramework` | Cloud/enterprise — SQL Server, PostgreSQL, MySQL, SQLite |

The persistence layer is fully abstracted through interfaces (`IDocumentStore`, `IOplogStore`, `ISnapshotMetadataStore`, `IDocumentMetadataStore`), allowing custom implementations.

## Platform Services

Beyond data synchronization, EntglDb's P2P infrastructure provides:

- **Automatic peer discovery** (UDP broadcast)
- **Secure transport** (ECDH + AES-256 + HMAC)
- **Leader election** (Bully Algorithm)
- **Remote peer management** (auto-synced via `_system_remote_peers` collection)
- **Health monitoring** (ASP.NET Core integration)
- **Network telemetry** (message counts, latency, compression ratios)

These building blocks enable developers to build additional distributed services on top of the EntglDb mesh.

## Security Model

| Deployment | Security Level | Notes |
|-----------|---------------|-------|
| **P2P Mesh (LAN)** | Shared AuthToken + optional ECDH/AES-256 | Designed for trusted networks |
| **ASP.NET Core (Cloud)** | HTTPS + OAuth2 JWT + standard web security | Full internet-grade security |

- **Transport**: Raw TCP with optional Noise Protocol handshake (ECDH key exchange, AES-256 encryption, HMAC authentication)
- **Authentication**: Shared cluster key for LAN; OAuth2 JWT for cloud
- **Authorization**: Once authenticated, a node has full read/write access to watched collections

**Recommendation**: 
- Use LAN mesh in trusted private networks (LAN, VPN, localhost)
- Use ASP.NET Core mode for internet-facing deployments with HTTPS and OAuth2
- See [Security](security.md) and [Production Hardening](production-hardening.md) for detailed guidance
