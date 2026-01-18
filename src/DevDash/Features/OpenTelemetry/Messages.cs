using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using System.Collections.Immutable;

namespace DevDash.Features.OpenTelemetry;

/* Commands - sent to TelemetrySupervisor from gRPC services */

internal sealed record StoreTraces(string ServiceName, Resource Resource, ImmutableArray<ScopeSpans> ScopeSpans);

internal sealed record StoreMetrics(string ServiceName, Resource Resource, ImmutableArray<ScopeMetrics> ScopeMetrics);

internal sealed record StoreLogs(string ServiceName, Resource Resource, ImmutableArray<ScopeLogs> ScopeLogs);

/* Events - published to EventStream for UI integration */

internal interface ITelemetryEventRaised
{
    string ServiceName { get; }
}

internal sealed record TelemetryReceivedEvent(string ServiceName, TelemetryType Type, int Count) : ITelemetryEventRaised;

internal sealed record NewTelemetryServiceDiscovered(string ServiceName, bool IsKnownApplication) : ITelemetryEventRaised;
