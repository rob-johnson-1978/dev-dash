using System.Text.Json.Serialization;

namespace DevDash.Features.Dashboard;

internal enum ProcessType
{
    Compose,
    Generic
}

[JsonConverter(typeof(JsonStringEnumConverter<DashboardBehaviour>))]
internal enum DashboardBehaviour
{
    None,
    Configured,
    Starting,
    Started
}

[JsonConverter(typeof(JsonStringEnumConverter<RunnableProcessBehaviour>))]
internal enum RunnableProcessBehaviour
{
    None,
    StartRequested,
    Stopped,
    Started
}