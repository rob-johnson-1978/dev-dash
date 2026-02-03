using System.Text.Json.Serialization;

namespace DevDash.Features.Dashboard;

internal enum ProcessType
{
    Compose,
    Generic
}

[JsonConverter(typeof(JsonStringEnumConverter<RunStatus>))]
internal enum RunStatus
{
    NeverStarted,
    StartRequested,
    Started,
    Stopped
}