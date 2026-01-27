namespace DevDash;

public sealed record GenericProcessConfiguration
{
    public required int StartupOrder { get; init; }
    public required string Id { get; init; }
    public required string PathToFolder { get; init; }
    public required string FileName { get; init; }
    public required string[] Args { get; init; }
    public string? StartDetectionRegex { get; init; }
    public string? PreDefinedStartDetection { get; init; }
    public string? UrlDetectionRegex { get; init; }
    public string? PreDefinedUrlDetection { get; init; }
}
