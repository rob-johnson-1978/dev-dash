namespace DevDash.Features.Configuration;

internal sealed record DotNetApplication
{
    public DotNetApplication(
        int startupOrder, 
        string id, 
        string pathToFolder, 
        string? startDetectionPattern = null,
        string? launchProfile = null)
    {
        StartupOrder = startupOrder;
        Id = id;
        StartDetectionPattern = startDetectionPattern;
        LaunchProfile = launchProfile;
        WorkingDirectoryPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), pathToFolder));
    }

    public string Id { get; }

    public int StartupOrder { get; }

    public string? LaunchProfile { get; }

    public string? StartDetectionPattern { get; }

    public string WorkingDirectoryPath { get; }
}
