using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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

internal sealed record StartRunnableProcess(string Id) : ICommmandRunnableProcessesToChangeState;

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

/* dashboard events - context cache */
internal static class DashboardEventRaisedJsonContexts
{
    private static readonly ImmutableDictionary<Type, JsonSerializerContext> _contexts;

    static DashboardEventRaisedJsonContexts()
    {
        _contexts = new Dictionary<Type, JsonSerializerContext>
        {
            { typeof(DashboardStatusPublished), new DashboardStatusPublishedJsonContext(BuildSerializerOptions()) },
            { typeof(RunnableProcessesStarting), new RunnableProcessesStartingJsonContext(BuildSerializerOptions()) },
            { typeof(RunnableProcessStatusPublished), new RunnableProcessStatusPublishedJsonContext(BuildSerializerOptions()) },
            { typeof(MessageAreaMessagePublished), new MessageAreaMessagePublishedJsonContext(BuildSerializerOptions()) },
            { typeof(ProcessOutputLineEmitted), new ProcessOutputLineEmittedJsonContext(BuildSerializerOptions()) },
            { typeof(ProcessErrorOutputLineEmitted), new ProcessErrorOutputLineEmittedJsonContext(BuildSerializerOptions()) },
        }
        .ToImmutableDictionary();
    }

    internal static JsonSerializerContext GetContext<TEvent>()
        where TEvent : class => 
        _contexts.TryGetValue(typeof(TEvent), out var context) 
            ? context 
            : throw new InvalidOperationException($"Type {typeof(TEvent)} is not registered in the context dictionary");

    private static JsonSerializerOptions BuildSerializerOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

/* dashboard events - wrapper event */

internal sealed record DashboardEventRaised(string Json, string Type)
{
    internal static DashboardEventRaised Create<TEvent>(TEvent from)
        where TEvent : class
    {
        var context = DashboardEventRaisedJsonContexts.GetContext<TEvent>();

        var json = JsonSerializer.Serialize(from, typeof(TEvent), context);

        return new DashboardEventRaised(json, typeof(TEvent).Name);
    }
}

[JsonSerializable(typeof(DashboardEventRaised))]
internal partial class DashboardEventRaisedJsonContext : JsonSerializerContext { }

/* dashboard events - specific events */

internal sealed record DashboardStatusPublished(DashboardBehaviour CurrentBehaviour);

[JsonSerializable(typeof(DashboardStatusPublished))]
internal partial class DashboardStatusPublishedJsonContext : JsonSerializerContext { }

internal sealed record RunnableProcessesStarting;

[JsonSerializable(typeof(RunnableProcessesStarting))]
internal partial class RunnableProcessesStartingJsonContext : JsonSerializerContext { }

internal sealed record RunnableProcessStatusPublished(string ProcessId, RunnableProcessBehaviour CurrentBehaviour, ImmutableArray<string> Urls);

[JsonSerializable(typeof(RunnableProcessStatusPublished))]
internal partial class RunnableProcessStatusPublishedJsonContext : JsonSerializerContext { }

internal sealed record MessageAreaMessagePublished(string Message, string Status = "default");

[JsonSerializable(typeof(MessageAreaMessagePublished))]
internal partial class MessageAreaMessagePublishedJsonContext : JsonSerializerContext { }

internal sealed record ProcessOutputLineEmitted(string Id, string Line);

[JsonSerializable(typeof(ProcessOutputLineEmitted))]
internal partial class ProcessOutputLineEmittedJsonContext : JsonSerializerContext { }

internal sealed record ProcessErrorOutputLineEmitted(string Id, string Line);

[JsonSerializable(typeof(ProcessErrorOutputLineEmitted))]
internal partial class ProcessErrorOutputLineEmittedJsonContext : JsonSerializerContext { }