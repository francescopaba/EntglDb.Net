using System;
using System.Text.Json;
using EntglDb.ConflictResolution;
using EntglDb.Core;
using EntglDb.Core.Sync;

namespace EntglDb.ConflictResolution.Demo;

/// <summary>
/// Bridge that adapts the standalone <see cref="IDocumentMerger"/> from the
/// <c>EntglDb.ConflictResolution</c> library into EntglDb's <see cref="IConflictResolver"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// EntglDb's <see cref="IConflictResolver.Resolve(Document?, OplogEntry)"/> exposes only the local
/// document and the incoming remote oplog entry — there is no "base" snapshot. Therefore this resolver
/// invokes the merger in 2-way mode (<c>baseJson = null</c>): every divergence between local and remote
/// is treated as a conflict, dispatched to the configured <see cref="ConflictStrategy"/>.
/// </para>
/// <para>
/// With <see cref="ConflictStrategy.PreferLatestHlc"/> (default), this becomes per-field LWW driven by
/// the document's <see cref="HlcTimestamp"/>, an upgrade over EntglDb's whole-document LWW resolver.
/// </para>
/// </remarks>
public sealed class JsonDiffPatchResolver : IConflictResolver
{
    private readonly IDocumentMerger _merger;
    private readonly MergeOptions _baseOptions;

    public JsonDiffPatchResolver()
        : this(new MergeOptions { ConflictStrategy = ConflictStrategy.PreferLatestHlc })
    {
    }

    public JsonDiffPatchResolver(MergeOptions options)
    {
        _baseOptions = options ?? throw new ArgumentNullException(nameof(options));
        _merger = new DocumentMerger();
    }

    public ConflictResolutionResult Resolve(Document? local, OplogEntry remote)
    {
        if (remote == null) throw new ArgumentNullException(nameof(remote));

        // Case 1: no local document → install the remote payload as new document.
        if (local == null)
        {
            var content = string.IsNullOrEmpty(remote.Payload)
                ? default
                : JsonSerializer.Deserialize<JsonElement>(remote.Payload!);
            var newDoc = new Document(
                remote.Collection,
                remote.Key,
                content,
                remote.Timestamp,
                remote.Operation == OperationType.Delete);
            return ConflictResolutionResult.Apply(newDoc);
        }

        // Case 2: remote tombstone → LWW on delete (mirror behavior of existing resolvers).
        if (remote.Operation == OperationType.Delete)
        {
            if (remote.Timestamp.CompareTo(local.UpdatedAt) > 0)
            {
                var deleted = new Document(local.Collection, local.Key, local.Content, remote.Timestamp, true);
                return ConflictResolutionResult.Apply(deleted);
            }
            return ConflictResolutionResult.Ignore();
        }

        // Case 3: real merge. Capture HLCs once and feed them to the strategy via a comparator.
        var localTs = local.UpdatedAt;
        var remoteTs = remote.Timestamp;

        var options = new MergeOptions
        {
            ConflictStrategy = _baseOptions.ConflictStrategy,
            ArrayMergeMode = _baseOptions.ArrayMergeMode,
            ArrayIdSelector = _baseOptions.ArrayIdSelector,
            HlcComparator = _ => remoteTs.CompareTo(localTs),
            WriteIndented = _baseOptions.WriteIndented
        };

        var localJson = local.Content.ValueKind == JsonValueKind.Undefined
            ? "null"
            : local.Content.GetRawText();
        var remoteJson = string.IsNullOrEmpty(remote.Payload) ? "null" : remote.Payload!;

        var result = _merger.Merge(baseJson: null, versionA: localJson, versionB: remoteJson, options);

        using var doc = JsonDocument.Parse(result.MergedJson);
        var mergedContent = doc.RootElement.Clone();
        var mergedTs = remoteTs.CompareTo(localTs) > 0 ? remoteTs : localTs;

        var mergedDocument = new Document(local.Collection, local.Key, mergedContent, mergedTs, false);
        return ConflictResolutionResult.Apply(mergedDocument);
    }
}
