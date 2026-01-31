namespace DevDash.Features.Dashboard;

internal enum ProcessType
{
    Compose,
    Generic
}

internal enum RunStatus
{
    NeverStarted,
    StartRequested,
    Started,
    Stopped
}