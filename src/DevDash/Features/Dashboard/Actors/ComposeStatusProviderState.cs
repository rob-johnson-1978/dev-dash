namespace DevDash.Features.Dashboard.Actors;

internal sealed class ComposeStatusProviderState
{
    public string WorkingDirectory { get; set; } = string.Empty;
    public string FullComposePath { get; set; } = string.Empty;
    public ComposeType ComposeType { get; set; }
}
