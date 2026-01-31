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
        RunnableProcesses = await dashboardSupervisorRequiredActor
            .ActorRef
            .Ask<ImmutableArray<RunnableProcess>>(
                new GetRunnableProcesses(),
                TimeSpan.FromSeconds(5),
                HttpContext.RequestAborted
            );
    }

    internal ImmutableArray<RunnableProcess> RunnableProcesses { get; set; } = [];
}
