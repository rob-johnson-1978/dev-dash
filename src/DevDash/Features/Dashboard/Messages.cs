using Akka.Actor;
using DevDash.Features.Configuration;
using DevDash.Infastructure;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard;

/* dashboard */

internal sealed record ConfigureDashboard;

internal sealed record StartRunnableApplications;

internal interface IShouldCheckIfNextGroupOfRunnableApplicationsCanBeStarted;

internal sealed record CheckIfNextGroupOfRunnableApplicationsCanBeStarted : IShouldCheckIfNextGroupOfRunnableApplicationsCanBeStarted;

internal sealed record RunnableApplicationStarted(string Id) : IShouldCheckIfNextGroupOfRunnableApplicationsCanBeStarted;

internal sealed record UpdateRunnableApplication(RunnableApplication Application);

internal sealed record GetRunnableApplications;

internal sealed record PublishDashboardUpdate;

internal sealed record PublishUpdateForAllRunnableApplications;

internal interface ICommmandRunnableApplicationsToChangeState
{
    string Id { get; }
}

internal sealed record StartRunnableApplication(string Id): ICommmandRunnableApplicationsToChangeState;

internal sealed record StopRunnableApplication(string Id) : ICommmandRunnableApplicationsToChangeState;

internal sealed record RestartRunnableApplication(string Id) : ICommmandRunnableApplicationsToChangeState;

internal sealed record ProcessExited;

internal sealed record ApplicationUrlDetected(string Url);

internal sealed record StartDashboard;

internal sealed record StopDashboard;

internal sealed record RestartDashboard;

/* dashboard - generic process */
internal sealed record RunGenericProcess(GenericProcessConfiguration Configuration);

/* dashboard - dotnet application */

internal sealed record RunDotNetApplication(DotNetApplication Application);

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

internal sealed record RunnableApplicationsStarting;

internal sealed record RunnableApplicationStatusPublished(string ApplicationId, RunStatus Status, ImmutableArray<string> Urls);

internal sealed record MessageAreaMessagePublished(string Message, string Status = "default");

internal sealed record ApplicationOutputLineEmitted(string Id, string Line);

internal sealed record ApplicationErrorOutputLineEmitted(string Id, string Line);