namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisorState
{
    public Dictionary<string, RunnableProcessWithActor> RunnableProcesses { get; } = [];
    public int CurrentGroupOfProcessesToBeStarted { get; set; }
}
