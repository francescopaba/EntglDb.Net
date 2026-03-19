---
layout: default
---

# EntglDb Documentation

Welcome to the EntglDb documentation for **version 2.1**.

## What's New in v2.1

Version 2.1 introduces **ContentHash** on `DocumentMetadata` with canonical JSON normalization, major framework targeting changes, and solidifies EntglDb as a **P2P synchronization middleware and platform**.

- **ContentHash**: Each document now carries a deterministic SHA-256 content hash for integrity verification
- **Framework Targeting**: Targets `netstandard2.1` and `net10.0` (dropped `netstandard2.0`, `net6.0`, `net8.0`)
- **P2P Sync Correctness**: Improved CDC serialization, LWW timestamp handling, and deterministic metadata IDs
- **Dynamic Database Paths**: Per-collection table support for scalable SQLite persistence
- **Full Async BLite**: Async read/write operations throughout the BLite persistence layer
- **Platform Services**: EntglDb is now a P2P platform on which additional services can be built

See the [CHANGELOG](https://github.com/EntglDb/EntglDb.Net/blob/main/CHANGELOG.md) for complete version history.

## Documentation

### Getting Started
*   [**Getting Started**](getting-started.html) - Installation, setup, and your first EntglDb application

### Core Concepts
*   [Architecture](architecture.html) - Understanding HLC, Gossip Protocol, P2P mesh networking, and the platform model
*   [API Reference](api-reference.html) - Complete API documentation and examples
*   [Querying](querying.html) - Data querying patterns and LINQ support

### Persistence & Storage
*   [Persistence Providers](persistence-providers.html) - BLite and EF Core provider comparison and configuration
*   [Deployment Modes](deployment-modes.html) - Single vs Multi-cluster deployment strategies

### Networking & Security
*   [Security](security.html) - Encryption, authentication, and secure networking
*   [Conflict Resolution](conflict-resolution.html) - LWW and Recursive Merge strategies
*   [Network Telemetry](network-telemetry.html) - Monitoring and diagnostics
*   [Dynamic Reconfiguration](dynamic-reconfiguration.html) - Runtime configuration and leader election
*   [Remote Peer Configuration](remote-peer-configuration.html) - Managing remote peers

### Deployment & Operations
*   [Deployment (LAN)](deployment-lan.html) - Platform-specific deployment instructions
*   [Production Hardening](production-hardening.html) - Configuration, monitoring, and best practices

## Cross-Platform Ecosystem

EntglDb is designed as a technology-agnostic protocol. While the .NET implementation is the most mature, the same principles apply across all stacks:

| Language | Repository | Status |
|----------|-----------|--------|
| **C# / .NET** | [EntglDb.Net](https://github.com/EntglDb/EntglDb.Net) | ✅ Stable (v2.1) |
| **Kotlin / Android** | [EntglDb.Kotlin](https://github.com/EntglDb/EntglDb.Kotlin) | ⚠ Alpha |
| **Node.js / TypeScript** | [EntglDb.NodeJs](https://github.com/EntglDb/EntglDb.NodeJs) | 🚧 In Development |
| **React Native** | [EntglDb.ReactNative](https://github.com/EntglDb/EntglDb.ReactNative) | 🚧 In Development |

All implementations follow the same [canonical specifications](https://github.com/EntglDb/EntglDb/tree/main/spec).

## Previous Versions

- [v1.x Documentation](v1/getting-started.html)
- [v0.9.x Documentation](v0.9/getting-started.html)
- [v0.8.x Documentation](v0.8/getting-started.html)

## Downloads

*   [**Download EntglStudio**](https://github.com/EntglDb/EntglDb.Net/releases) - Standalone tool for managing EntglDb data.

## Links

*   [GitHub Repository](https://github.com/EntglDb/EntglDb.Net)
*   [NuGet Packages](https://www.nuget.org/packages?q=EntglDb)
*   [Changelog](https://github.com/EntglDb/EntglDb.Net/blob/main/CHANGELOG.md)
