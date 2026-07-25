#nullable enable
namespace BazaarPlusPlus.Infrastructure.Files;

internal enum ArtifactPublicationFailureKind
{
    PathEscapesRoot,
    SymbolicLinkOrReparsePoint,
    InvalidArtifactIdentity,
    InvalidDestinationType,
}

/// <summary>
/// A deterministic artifact-path or immutable-identity failure. Callers may safely classify these
/// failures without coupling retry policy to localized exception messages.
/// </summary>
internal sealed class ArtifactPublicationException : IOException
{
    internal ArtifactPublicationException(
        ArtifactPublicationFailureKind failureKind,
        string message
    )
        : base(message)
    {
        FailureKind = failureKind;
    }

    internal ArtifactPublicationFailureKind FailureKind { get; }
}
