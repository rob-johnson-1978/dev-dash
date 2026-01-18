using Akka.Actor;
using Akka.Event;

namespace DevDash.Features.OpenTelemetry.Actors;

/// <summary>
/// Top-level supervisor for telemetry storage actors.
/// Routes incoming telemetry to per-application child actors based on service.name.
/// </summary>
internal sealed class TelemetrySupervisor(DevDashConfiguration configuration) : UntypedActor
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly Dictionary<string, IActorRef> _applicationActors = [];

    // Set of known application IDs from configuration (already lowercased)
    private readonly HashSet<string> _knownApplicationIds =
        configuration.DotNetApplications.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case StoreTraces command:
                ForwardToApplicationActor(command.ServiceName, command);
                break;

            case StoreMetrics command:
                ForwardToApplicationActor(command.ServiceName, command);
                break;

            case StoreLogs command:
                ForwardToApplicationActor(command.ServiceName, command);
                break;

            default:
                Unhandled(message);
                break;
        }
    }

    private void ForwardToApplicationActor(string serviceName, object message)
    {
        var normalizedName = serviceName.ToLower();

        if (!_applicationActors.TryGetValue(normalizedName, out var actorRef))
        {
            // Create new child actor for this service
            var isKnown = _knownApplicationIds.Contains(normalizedName);

            var props = Props.Create(() => new ApplicationTelemetryReceiver(serviceName, isKnown));
            actorRef = Context.ActorOf(props, $"telemetry-{normalizedName}");
            _applicationActors[normalizedName] = actorRef;

            _logger.Info("Created telemetry actor for service: {0} (known: {1})", serviceName, isKnown);

            // Publish event for UI
            Context.System.EventStream.Publish(new NewTelemetryServiceDiscovered(serviceName, isKnown));
        }

        actorRef.Tell(message);
    }
}
