using DevDash.Features.Configuration;
using Microsoft.Extensions.Logging;

namespace DevDash;

public sealed class DevDashConfiguration
{
    /* public */

    public DevDashConfiguration SetPorts(int telemetryPort = 5284, int mainPort = 5285)
    {
        TelemetryPort = telemetryPort;
        MainPort = mainPort;

        return this;
    }

    public DevDashConfiguration AddCompose(string pathToFile, ComposeType composeType)
    {
        ComposeFilePath = pathToFile;
        ComposeType = composeType;

        return this;
    }

    public DevDashConfiguration AddDotNetApplication(string id, string pathToFolder, string? launchProfile = null)
    {
        var lowerId = id.ToLower();

        DotNetApplications[lowerId] = new DotNetApplication(lowerId, pathToFolder, launchProfile);
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

    internal int TelemetryPort { get; private set; } = 5284;

    internal int MainPort { get; private set; } = 5285;
}