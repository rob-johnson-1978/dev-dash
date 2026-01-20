namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisorState
{
    public Dictionary<string, RunnableApplicationWithActor> RunnableApplications { get; } = [];
    public int CurrentGroupOfApplicationsToBeStarted { get; set; }
}
