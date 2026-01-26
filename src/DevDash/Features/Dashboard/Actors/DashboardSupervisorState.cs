namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisorState
{
    public RunStatus RunStatus { get; set; } = RunStatus.NeverStarted;
    public Dictionary<string, RunnableApplicationWithActor> RunnableApplications { get; } = [];
    public int CurrentGroupOfApplicationsToBeStarted { get; set; }
}
