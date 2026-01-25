using System.Diagnostics;

namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardProcessRunnerState
{
    public string ApplicationId { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string[] Args { get; set; } = [];
    public Process? Process { get; set; }
    public Func<string, string?>? DetectRunnableApplicationStartedUrlViaStdOut { get; set; }
    public Func<string, bool>? DetectRunnableApplicationStartedViaStdOut { get; set; }
    public Action? DetectRunnableApplicationStartedAfterProcessStarted { get; set; }
    public HashSet<string> Urls { get; } = [];
    public RunStatus RunStatus { get; set; }    
    public string FullComposePath { get; set; } = string.Empty;
}
