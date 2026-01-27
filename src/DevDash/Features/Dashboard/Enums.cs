namespace DevDash.Features.Dashboard;

internal enum ApplicationType
{
    Compose,
    Generic,
    DotNet
}

internal enum RunStatus
{
    NeverStarted,
    StartRequested,
    Started,
    Stopped
}