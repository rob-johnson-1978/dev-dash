using Akka.Actor;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard;

internal record RunnableApplication(string Id, RunStatus RunStatus, ImmutableArray<string> Urls);

internal sealed record RunnableApplicationWithActor(
    ApplicationType Type,
    int StartupOrder,
    string Id,
    RunStatus RunStatus,
    ImmutableArray<string> Urls,
    IActorRef ActorRef
) : RunnableApplication(Id, RunStatus, Urls);