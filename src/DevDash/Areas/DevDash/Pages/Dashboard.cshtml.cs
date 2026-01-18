using Akka.Actor;
using Akka.Hosting;
using DevDash;
using DevDash.Features.Dashboard;
using DevDash.Features.Dashboard.Actors;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Immutable;

namespace DevDash.Areas.DevDash.Pages;

internal class DashboardModel(IRequiredActor<DashboardSupervisor> dashboardSupervisorRequiredActor) : PageModel
{
    public async Task OnGet()
    {
        RunnableApplications = await dashboardSupervisorRequiredActor
            .ActorRef
            .Ask<ImmutableArray<RunnableApplication>>(
                new GetRunnableApplications(),
                TimeSpan.FromSeconds(5),
                HttpContext.RequestAborted
            );
    }

    internal ImmutableArray<RunnableApplication> RunnableApplications { get; set; } = [];
}
