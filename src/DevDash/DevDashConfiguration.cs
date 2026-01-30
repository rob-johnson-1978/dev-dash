using DevDash.Features.Configuration;
using DevDash.Infastructure;
using Microsoft.Extensions.Logging;

namespace DevDash;

public sealed class DevDashConfiguration
{
    /* public */

    public DevDashConfiguration AddCompose(int startupOrder, string pathToFile, ComposeType composeType, int checkTimeoutInSeconds = 60)
    {
        ComposeConfiguration = new ComposeConfiguration(startupOrder, pathToFile, composeType, checkTimeoutInSeconds);

        return this;
    }

    public DevDashConfiguration AddProcess(ProcessConfiguration configuration)
    {
        GenericProcesses[configuration.Id.ToLower()] = configuration;
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

    public DevDashConfiguration SetConsoleOutputMaxLines(int maxLines) 
    {
        ConsoleOutputMaxLines = maxLines;
        return this;
    }

    /* internal */

    internal Dictionary<string, ProcessConfiguration> GenericProcesses { get; } = [];

    internal Dictionary<string, DotNetApplication> DotNetApplications { get; } = [];

    internal Func<Task>? BeforeStart { get; private set; }

    internal Func<Task>? OnShutdown { get; private set; }

    internal LogLevel LogLevel { get; private set; } = LogLevel.Debug;

    internal ComposeConfiguration? ComposeConfiguration { get; private set; }  
    
    internal int ConsoleOutputMaxLines { get; private set; } = 100;

    internal int ConsoleOutputLineRemovalBatchSize => Math.Max(1, ConsoleOutputMaxLines / 10);
}