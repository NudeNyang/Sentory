namespace Sentory.Core;

public sealed record NormalizedUrl(
    string Original,
    string Value,
    string Domain);
