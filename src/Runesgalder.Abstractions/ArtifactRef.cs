namespace Runesgalder;

/// <summary>An immutable reference to a content-addressed artifact.</summary>
/// <param name="Id">The artifact identifier.</param>
/// <param name="MediaType">The artifact media type.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 digest.</param>
/// <param name="Size">The artifact size in bytes.</param>
public sealed record ArtifactRef(string Id, string MediaType, string Sha256, long Size);
