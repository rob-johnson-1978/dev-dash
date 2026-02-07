using Akka.Actor;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard;

internal record RunnableProcess(string Id, RunnableProcessBehaviour CurrentBehaviour, ImmutableArray<string> Urls);

internal sealed record RunnableProcessWithActor(
    ProcessType Type,
    int StartupOrder,
    string Id,
    RunnableProcessBehaviour CurrentBehaviour,
    ImmutableArray<string> Urls,
    IActorRef ActorRef
) : RunnableProcess(Id, CurrentBehaviour, Urls);