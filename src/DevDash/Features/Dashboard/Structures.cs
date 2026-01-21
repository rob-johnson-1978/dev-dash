using Akka.Actor;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard;

internal record RunnableApplication(string Id, bool Running, bool RunRequested, ImmutableArray<string> Urls);

internal sealed record RunnableApplicationWithActor(
    ApplicationType Type,
    int StartupOrder,
    string Id,
    bool Running,
    bool RunRequested,
    ImmutableArray<string> Urls,
    IActorRef ActorRef
) : RunnableApplication(Id, Running, RunRequested, Urls);
