namespace DevDash.Features.Dashboard;

internal enum ApplicationType
{
    Compose,
    DotNet
}

internal enum RunStatus
{
    NeverStarted,
    StartRequested,
    Started,
    Stopped
}