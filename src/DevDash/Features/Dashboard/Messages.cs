using Akka.Actor;
using DevDash.Features.Configuration;
using DevDash.Infastructure;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard;

/* dashboard */

internal sealed record ConfigureDashboard;

internal sealed record StartRunnableApplications;

internal sealed record UpdateRunnableApplication(RunnableApplication Application);

internal record RunnableApplication(string Id, bool Running, ImmutableArray<string> Urls);

internal sealed record RunnableApplicationWithActor(string Id, bool Running, ImmutableArray<string> Urls, IActorRef ActorRef) 
    : RunnableApplication(Id, Running, Urls);

internal sealed record GetRunnableApplications;

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


/* dashboard - dotnet application */

internal sealed record RunDotNetApplication(DotNetApplication Application);

/* dashboard - compose */

internal sealed record RunCompose(string ComposeFilePath, ComposeType ComposeType);

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

internal sealed record RunnableApplicationsStarted;

internal sealed record RunnableApplicationUpdated(RunnableApplication Application, bool IsBackgroundUpdate = false);

internal sealed record ApplicationOutputLineEmitted(string Id, string Line);

internal sealed record ApplicationErrorOutputLineEmitted(string Id, string Line);