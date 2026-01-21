using System.Diagnostics;

namespace DevDash.Features.Dashboard.Actors;

internal sealed class DashboardProcessRunnerState
{
    public string ApplicationId { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string[] Args { get; set; } = [];
    public Process? Process { get; set; }
    public bool ManuallyStopped { get; set; }
    public Func<string, string?>? FindUrlInMessageViaStdOut { get; set; }
    public Func<string, bool>? DetectStartedViaStdOut { get; set; }
    public HashSet<string> Urls { get; } = [];
    public bool Running { get; set; }
    public bool RunRequested { get; set; }
}
