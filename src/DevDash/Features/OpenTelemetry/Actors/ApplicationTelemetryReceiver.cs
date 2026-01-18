using Akka.Actor;
using Akka.Event;
using Google.Protobuf;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;
using System.Collections.Immutable;

namespace DevDash.Features.OpenTelemetry.Actors;

/// <summary>
/// Stores telemetry data for a single application identified by service.name.
/// Maintains circular buffers of recent spans, metrics, and logs.
/// </summary>
internal sealed class ApplicationTelemetryReceiver(string serviceName, bool isKnownApplication) : UntypedActor
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly ApplicationTelemetryState _state = new(serviceName, isKnownApplication);

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case StoreTraces command:
                HandleStoreTraces(command);
                break;

            case StoreMetrics command:
                HandleStoreMetrics(command);
                break;

            case StoreLogs command:
                HandleStoreLogs(command);
                break;

            default:
                Unhandled(message);
                break;
        }
    }

    private void HandleStoreTraces(StoreTraces command)
    {
        int count = 0;

        foreach (var scopeSpans in command.ScopeSpans)
        {
            foreach (var span in scopeSpans.Spans)
            {
                var storedSpan = ConvertSpan(span);
                _state.AddSpan(storedSpan);
                count++;
            }
        }

        _logger.Debug("Stored {0} spans for {1}", count, _state.ServiceName);

        // Publish event for UI
        Context.System.EventStream.Publish(
            new TelemetryReceivedEvent(_state.ServiceName, TelemetryType.Traces, count)
        );
    }

    private void HandleStoreMetrics(StoreMetrics command)
    {
        int count = 0;

        foreach (var scopeMetrics in command.ScopeMetrics)
        {
            foreach (var metric in scopeMetrics.Metrics)
            {
                var dataPoints = ExtractMetricDataPoints(metric);
                foreach (var dp in dataPoints)
                {
                    _state.AddMetric(dp);
                    count++;
                }
            }
        }

        _logger.Debug("Stored {0} metric data points for {1}", count, _state.ServiceName);

        Context.System.EventStream.Publish(
            new TelemetryReceivedEvent(_state.ServiceName, TelemetryType.Metrics, count)
        );
    }

    private void HandleStoreLogs(StoreLogs command)
    {
        int count = 0;

        foreach (var scopeLogs in command.ScopeLogs)
        {
            foreach (var logRecord in scopeLogs.LogRecords)
            {
                var storedLog = ConvertLogRecord(logRecord);
                _state.AddLog(storedLog);
                count++;
            }
        }

        _logger.Debug("Stored {0} log records for {1}", count, _state.ServiceName);

        Context.System.EventStream.Publish(
            new TelemetryReceivedEvent(_state.ServiceName, TelemetryType.Logs, count)
        );
    }

    // Helper conversion methods

    private static StoredSpan ConvertSpan(Span span) => new(
        TraceId: BytesToHex(span.TraceId),
        SpanId: BytesToHex(span.SpanId),
        ParentSpanId: span.ParentSpanId.IsEmpty ? null : BytesToHex(span.ParentSpanId),
        Name: span.Name,
        Kind: span.Kind.ToString(),
        StartTime: DateTimeOffset.FromUnixTimeMilliseconds((long)(span.StartTimeUnixNano / 1_000_000)),
        EndTime: DateTimeOffset.FromUnixTimeMilliseconds((long)(span.EndTimeUnixNano / 1_000_000)),
        Attributes: ExtractAttributes(span.Attributes),
        StatusCode: span.Status?.Code.ToString(),
        StatusMessage: span.Status?.Message
    );

    private static IEnumerable<StoredMetricDataPoint> ExtractMetricDataPoints(Metric metric)
    {
        if (metric.Gauge != null)
        {
            foreach (var dp in metric.Gauge.DataPoints)
            {
                yield return new StoredMetricDataPoint(
                    metric.Name,
                    "Gauge",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000)),
                    dp.AsDouble,
                    null,
                    ExtractAttributes(dp.Attributes)
                );
            }
        }
        else if (metric.Sum != null)
        {
            foreach (var dp in metric.Sum.DataPoints)
            {
                yield return new StoredMetricDataPoint(
                    metric.Name,
                    "Sum",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000)),
                    dp.AsDouble,
                    null,
                    ExtractAttributes(dp.Attributes)
                );
            }
        }
        else if (metric.Histogram != null)
        {
            foreach (var dp in metric.Histogram.DataPoints)
            {
                yield return new StoredMetricDataPoint(
                    metric.Name,
                    "Histogram",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000)),
                    null,
                    (long)dp.Count,
                    ExtractAttributes(dp.Attributes)
                );
            }
        }
        else if (metric.ExponentialHistogram != null)
        {
            foreach (var dp in metric.ExponentialHistogram.DataPoints)
            {
                yield return new StoredMetricDataPoint(
                    metric.Name,
                    "ExponentialHistogram",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000)),
                    null,
                    (long)dp.Count,
                    ExtractAttributes(dp.Attributes)
                );
            }
        }
        else if (metric.Summary != null)
        {
            foreach (var dp in metric.Summary.DataPoints)
            {
                yield return new StoredMetricDataPoint(
                    metric.Name,
                    "Summary",
                    DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000)),
                    dp.Sum,
                    (long)dp.Count,
                    ExtractAttributes(dp.Attributes)
                );
            }
        }
    }

    private static StoredLogRecord ConvertLogRecord(LogRecord log) => new(
        Timestamp: DateTimeOffset.FromUnixTimeMilliseconds((long)(log.TimeUnixNano / 1_000_000)),
        SeverityText: log.SeverityText,
        SeverityNumber: (int)log.SeverityNumber,
        Body: log.Body?.StringValue ?? "",
        Attributes: ExtractAttributes(log.Attributes)
    );

    private static string BytesToHex(ByteString bytes) =>
        Convert.ToHexStringLower(bytes.ToByteArray());

    private static ImmutableDictionary<string, string> ExtractAttributes(Google.Protobuf.Collections.RepeatedField<KeyValue> attributes)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>();

        foreach (var attr in attributes)
        {
            var value = attr.Value?.ValueCase switch
            {
                AnyValue.ValueOneofCase.StringValue => attr.Value.StringValue,
                AnyValue.ValueOneofCase.IntValue => attr.Value.IntValue.ToString(),
                AnyValue.ValueOneofCase.DoubleValue => attr.Value.DoubleValue.ToString(),
                AnyValue.ValueOneofCase.BoolValue => attr.Value.BoolValue.ToString(),
                _ => attr.Value?.ToString() ?? ""
            };

            builder[attr.Key] = value;
        }

        return builder.ToImmutable();
    }
}