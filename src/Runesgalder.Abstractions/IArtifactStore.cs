namespace Runesgalder;

/// <summary>Stores immutable artifacts produced by a run.</summary>
public interface IArtifactStore
{
    /// <summary>Stores content and returns its content-addressed reference.</summary>
    /// <param name="content">The artifact bytes.</param>
    /// <param name="mediaType">The artifact media type.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A reference to the stored artifact.</returns>
    ValueTask<ArtifactRef> PutAsync(
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>Reads an artifact.</summary>
    /// <param name="artifact">The artifact reference.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The artifact bytes.</returns>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ArtifactRef artifact,
        CancellationToken cancellationToken = default);
}
