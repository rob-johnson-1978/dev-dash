namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisorState
{
    public RunStatus RunStatus { get; set; } = RunStatus.NeverStarted;
    public Dictionary<string, RunnableProcessWithActor> RunnableProcesses { get; } = [];
    public int CurrentGroupOfProcessesToBeStarted { get; set; }
}
