using System.Collections.Immutable;

namespace DevDash.Features.OpenTelemetry;

/// <summary>
/// Simplified span storage record (extracted from protobuf Span)
/// </summary>
internal sealed record StoredSpan(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    ImmutableDictionary<string, string> Attributes,
    string? StatusCode,
    string? StatusMessage
);

/// <summary>
/// Simplified metric data point storage record
/// </summary>
internal sealed record StoredMetricDataPoint(
    string MetricName,
    string MetricType,
    DateTimeOffset Timestamp,
    double? Value,
    long? Count,
    ImmutableDictionary<string, string> Attributes
);

/// <summary>
/// Simplified log record storage
/// </summary>
internal sealed record StoredLogRecord(
    DateTimeOffset Timestamp,
    string SeverityText,
    int SeverityNumber,
    string Body,
    ImmutableDictionary<string, string> Attributes
);

/// <summary>
/// State held by ApplicationTelemetryActor - manages circular buffers for telemetry data
/// </summary>
internal sealed class ApplicationTelemetryState(string serviceName, bool isKnownApplication)
{
    private const int MaxSpans = 1000;
    private const int MaxMetrics = 1000;
    private const int MaxLogs = 1000;

    private readonly Queue<StoredSpan> _spans = new();
    private readonly Queue<StoredMetricDataPoint> _metrics = new();
    private readonly Queue<StoredLogRecord> _logs = new();

    public string ServiceName { get; } = serviceName;
    public bool IsKnownApplication { get; } = isKnownApplication;

    public void AddSpan(StoredSpan span)
    {
        _spans.Enqueue(span);
        while (_spans.Count > MaxSpans)
            _spans.Dequeue();
    }

    public void AddMetric(StoredMetricDataPoint metric)
    {
        _metrics.Enqueue(metric);
        while (_metrics.Count > MaxMetrics)
            _metrics.Dequeue();
    }

    public void AddLog(StoredLogRecord log)
    {
        _logs.Enqueue(log);
        while (_logs.Count > MaxLogs)
            _logs.Dequeue();
    }

    public ImmutableArray<StoredSpan> GetRecentSpans() => [.. _spans];
    public ImmutableArray<StoredMetricDataPoint> GetRecentMetrics() => [.. _metrics];
    public ImmutableArray<StoredLogRecord> GetRecentLogs() => [.. _logs];

    public int SpanCount => _spans.Count;
    public int MetricCount => _metrics.Count;
    public int LogCount => _logs.Count;
}
