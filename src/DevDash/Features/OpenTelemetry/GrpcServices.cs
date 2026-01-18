using Akka.Actor;
using Akka.Hosting;
using DevDash.Features.OpenTelemetry.Actors;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OtelResource = OpenTelemetry.Proto.Resource.V1.Resource;

namespace DevDash.Features.OpenTelemetry;

// will need to update package when new versions are released
// https://github.com/open-telemetry/opentelemetry-proto/proto 

internal class DevDashTraceService(
    ILogger<DevDashTraceService> logger,
    IRequiredActor<TelemetrySupervisor> telemetrySupervisor) : TraceService.TraceServiceBase
{
    private const string ServiceNameAttribute = "service.name";

    public override Task<ExportTraceServiceResponse> Export(ExportTraceServiceRequest request, ServerCallContext context)
    {
        foreach (var resourceSpans in request.ResourceSpans)
        {
            var serviceName = ExtractServiceName(resourceSpans.Resource);

            if (string.IsNullOrEmpty(serviceName))
            {
                logger.LogWarning("Received spans without service.name attribute, skipping");
                continue;
            }

            var command = new StoreTraces(
                serviceName,
                resourceSpans.Resource,
                [.. resourceSpans.ScopeSpans]
            );

            telemetrySupervisor.ActorRef.Tell(command);
        }

        return Task.FromResult(new ExportTraceServiceResponse());
    }

    private static string? ExtractServiceName(OtelResource? resource)
    {
        if (resource == null) return null;

        var serviceNameAttr = resource.Attributes
            .FirstOrDefault(a => a.Key == ServiceNameAttribute);

        return serviceNameAttr?.Value?.StringValue;
    }
}

internal class DevDashMetricsService(
    ILogger<DevDashMetricsService> logger,
    IRequiredActor<TelemetrySupervisor> telemetrySupervisor) : MetricsService.MetricsServiceBase
{
    private const string ServiceNameAttribute = "service.name";

    public override Task<ExportMetricsServiceResponse> Export(ExportMetricsServiceRequest request, ServerCallContext context)
    {
        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            var serviceName = ExtractServiceName(resourceMetrics.Resource);

            if (string.IsNullOrEmpty(serviceName))
            {
                logger.LogWarning("Received metrics without service.name attribute, skipping");
                continue;
            }

            var command = new StoreMetrics(
                serviceName,
                resourceMetrics.Resource,
                [.. resourceMetrics.ScopeMetrics]
            );

            telemetrySupervisor.ActorRef.Tell(command);
        }

        return Task.FromResult(new ExportMetricsServiceResponse());
    }

    private static string? ExtractServiceName(OtelResource? resource)
    {
        if (resource == null) return null;

        var serviceNameAttr = resource.Attributes
            .FirstOrDefault(a => a.Key == ServiceNameAttribute);

        return serviceNameAttr?.Value?.StringValue;
    }
}

internal class DevDashLogsService(
    ILogger<DevDashLogsService> logger,
    IRequiredActor<TelemetrySupervisor> telemetrySupervisor) : LogsService.LogsServiceBase
{
    private const string ServiceNameAttribute = "service.name";

    public override Task<ExportLogsServiceResponse> Export(ExportLogsServiceRequest request, ServerCallContext context)
    {
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var serviceName = ExtractServiceName(resourceLogs.Resource);

            if (string.IsNullOrEmpty(serviceName))
            {
                logger.LogWarning("Received logs without service.name attribute, skipping");
                continue;
            }

            var command = new StoreLogs(
                serviceName,
                resourceLogs.Resource,
                [.. resourceLogs.ScopeLogs]
            );

            telemetrySupervisor.ActorRef.Tell(command);
        }

        return Task.FromResult(new ExportLogsServiceResponse());
    }

    private static string? ExtractServiceName(OtelResource? resource)
    {
        if (resource == null) return null;

        var serviceNameAttr = resource.Attributes
            .FirstOrDefault(a => a.Key == ServiceNameAttribute);

        return serviceNameAttr?.Value?.StringValue;
    }
}
