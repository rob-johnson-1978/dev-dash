namespace DevDash.Features.Configuration;

internal sealed record DotNetApplication
{
    public DotNetApplication(int startupOrder, string id, string pathToFolder, string? launchProfile)
    {
        StartupOrder = startupOrder;
        Id = id;
        LaunchProfile = launchProfile;
        WorkingDirectoryPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), pathToFolder));
    }

    public string Id { get; }

    public int StartupOrder { get; }

    public string? LaunchProfile { get; }

    public string WorkingDirectoryPath { get; }
}
