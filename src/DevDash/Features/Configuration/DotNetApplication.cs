namespace DevDash.Features.Configuration;

internal sealed record DotNetApplication
{
    public DotNetApplication(string id, string pathToFolder, string? launchProfile)
    {
        Id = id;
        LaunchProfile = launchProfile;
        WorkingDirectoryPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), pathToFolder));
    }

    public string Id { get; }

    public string? LaunchProfile { get; }

    public string WorkingDirectoryPath { get; }
}
