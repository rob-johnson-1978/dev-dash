namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardSupervisorState
{
    public Dictionary<string, RunnableApplicationWithActor> RunnableApplications { get; } = [];
}
