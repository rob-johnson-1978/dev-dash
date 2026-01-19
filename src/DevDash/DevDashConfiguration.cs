using DevDash.Features.Configuration;
using DevDash.Infastructure;
using Microsoft.Extensions.Logging;

namespace DevDash;

public sealed class DevDashConfiguration
{
    /* public */

    public DevDashConfiguration AddCompose(int startupOrder, string pathToFile, ComposeType composeType)
    {
        ComposeFilePath = pathToFile;
        ComposeType = composeType;

        AddToStartupOrdering(startupOrder, Constants.DockerComposeApplicationId);

        return this;
    }

    public DevDashConfiguration AddDotNetApplication(int startupOrder, string id, string pathToFolder, string? launchProfile = null)
    {
        var lowerId = id.ToLower();

        DotNetApplications[lowerId] = new DotNetApplication(lowerId, pathToFolder, launchProfile);

        AddToStartupOrdering(startupOrder, lowerId);

        return this;
    }

    public DevDashConfiguration SetBeforeStart(Func<Task> beforeStart)
    {
        BeforeStart = beforeStart;
        return this;
    }

    public DevDashConfiguration SetOnShutdown(Func<Task> onShutdown)
    {
        OnShutdown = onShutdown;
        return this;
    }

    public DevDashConfiguration SetLogLevel(LogLevel logLevel)
    {
        LogLevel = logLevel;

        return this;
    }

    /* internal */

    internal Dictionary<string, DotNetApplication> DotNetApplications { get; } = [];

    internal Func<Task>? BeforeStart { get; private set; }

    internal Func<Task>? OnShutdown { get; private set; }

    internal LogLevel LogLevel { get; private set; } = LogLevel.Debug;

    internal string ComposeFilePath { get; private set; } = string.Empty;

    internal ComposeType ComposeType { get; private set; }

    internal bool HasCompose => !string.IsNullOrWhiteSpace(ComposeFilePath);

    internal Dictionary<int, List<string>> StartupOrdering { get; } = [];

    private void AddToStartupOrdering(int startupOrder, string applicationId)
    {
        if (!StartupOrdering.TryGetValue(startupOrder, out List<string>? value))
        {
            value = [];
            StartupOrdering[startupOrder] = value;
        }

        value.Add(applicationId);
    }
}