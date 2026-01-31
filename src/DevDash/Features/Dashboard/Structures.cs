using Akka.Actor;
using System.Collections.Immutable;

namespace DevDash.Features.Dashboard;

internal sealed record ComposeConfiguration(string FilePath, ComposeType ComposeType, int CheckTimeoutInSeconds);

internal record RunnableProcess(string Id, RunStatus RunStatus, ImmutableArray<string> Urls);

internal sealed record RunnableProcessWithActor(
    ProcessType Type,
    int StartupOrder,
    string Id,
    RunStatus RunStatus,
    ImmutableArray<string> Urls,
    IActorRef ActorRef
) : RunnableProcess(Id, RunStatus, Urls);