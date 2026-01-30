using System.Text.RegularExpressions;

namespace DevDash;

public sealed record ProcessConfiguration
{
    public required int StartupOrder { get; init; }
    public required string Id { get; init; }
    public required string PathToFolder { get; init; }
    public required string Instructions { get; init; }
    public string? StartDetectionRegex { get; init; }
    public string? PreDefinedStartDetection { get; init; }
    public UrlDetection[] UrlDetections { get; init; } = [];
    public string? PreDefinedUrlDetections { get; init; }
}

public sealed record UrlDetection(string RegexPattern, bool IsPortOnly, bool IsHttpsWhenPortOnly);

internal sealed record UrlDetectionWithRegex(Regex Regex, bool IsPortOnly, bool IsHttpsWhenPortOnly);
