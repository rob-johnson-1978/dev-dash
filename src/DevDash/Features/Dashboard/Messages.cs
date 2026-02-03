using DevDash;
using DevDash.Infastructure;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard;

/* dashboard */

internal sealed record ConfigureDashboard;

internal sealed record StartRunnableProcesses;

internal interface IShouldCheckIfNextGroupOfRunnableProcessesCanBeStarted;

internal sealed record CheckIfNextGroupOfRunnableProcessesCanBeStarted : IShouldCheckIfNextGroupOfRunnableProcessesCanBeStarted;

internal sealed record RunnableProcessStarted(string Id) : IShouldCheckIfNextGroupOfRunnableProcessesCanBeStarted;

internal sealed record UpdateRunnableProcess(RunnableProcess Process);

internal sealed record GetRunnableProcesses;

internal sealed record PublishDashboardUpdate;

internal sealed record PublishUpdateForAllRunnableProcesses;

internal interface ICommmandRunnableProcessesToChangeState
{
    string Id { get; }
}

internal sealed record StartRunnableProcess(string Id): ICommmandRunnableProcessesToChangeState;

internal sealed record StopRunnableProcess(string Id) : ICommmandRunnableProcessesToChangeState;

internal sealed record RestartRunnableProcess(string Id) : ICommmandRunnableProcessesToChangeState;

internal sealed record ProcessExited;

internal sealed record ProcessUrlDetected(string Url);

internal sealed record StartDashboard;

internal sealed record StopDashboard;

internal sealed record RestartDashboard;

/* dashboard - generic process */
internal sealed record RunGenericProcess(string Id, ProcessConfiguration Configuration);

/* dashboard - compose */

internal sealed record RunCompose(string ComposeFilePath, ComposeType ComposeType, int CheckTimeoutInSeconds);

internal sealed record WaitForComposeStatusToBecomeAvailable(
    string WorkingDirectory,
    string FullComposePath,
    ComposeType ComposeType,
    int CheckTimeoutInSeconds
);

internal sealed record CheckComposeStatus;

internal sealed record ComposeStatusCheckTimedOut;

internal sealed record ComposeStarted;

internal sealed record ComposeStartFailed;

/* dashboard events */

internal interface IDashboardEventRaised
{
    string Json { get; }
    string Type { get; }
}

internal static class DashboardEventRaised
{
    public static DashboardEventRaised<T> Create<T>(T data)
        where T : class => new(data);
}


internal sealed record DashboardEventRaised<TEvent> : IDashboardEventRaised
    where TEvent : class
{
    public DashboardEventRaised(TEvent Body)
    {
        Json = Body.Serialize();
        Type = typeof(TEvent).Name;
    }

    public string Json { get; }

    public string Type { get; }
}

internal sealed record DashboardStatusPublished(RunStatus Status);

internal sealed record RunnableProcessesStarting;

internal sealed record RunnableProcessStatusPublished(string ProcessId, RunStatus Status, ImmutableArray<string> Urls);

internal sealed record MessageAreaMessagePublished(string Message, string Status = "default");

internal sealed record ProcessOutputLineEmitted(string Id, string Line);

internal sealed record ProcessErrorOutputLineEmitted(string Id, string Line);