using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace DevDash.Features.Dashboard;

internal sealed class Configuration
{
    private Dictionary<string, ProcessConfiguration>? _processes;
    private int? _consoleOutputMaxLines;

    [YamlMember(Alias = "processes", ApplyNamingConventions = false)]
    internal Dictionary<string, ProcessConfiguration> Processes
    {
        get => _processes ?? [];
        init => _processes = value;
    }

    [YamlMember(Alias = "compose", ApplyNamingConventions = false)]
    internal ComposeConfiguration? Compose { get; set; }

    [YamlMember(Alias = "console_output_max_lines", ApplyNamingConventions = false)]
    internal int ConsoleOutputMaxLines
    {
        get => _consoleOutputMaxLines ?? 100;
        set => _consoleOutputMaxLines = value;
    }

    internal int ConsoleOutputLineRemovalBatchSize => Math.Max(1, ConsoleOutputMaxLines / 10);
}

internal sealed record ProcessConfiguration
{
    private int? _startupOrder;
    private string? _pathToFolder;
    private string? _instructions;
    private UrlDetection[]? _urlDetections;

    [YamlMember(Alias = "startup_order", ApplyNamingConventions = false)]
    internal int StartupOrder
    {
        get => _startupOrder ?? 0;
        init => _startupOrder = value;
    }

    [YamlMember(Alias = "path_to_folder", ApplyNamingConventions = false)]
    internal string PathToFolder
    {
        get => _pathToFolder ?? throw new InvalidOperationException("'path_to_folder' in a process cannot be empty");
        init => _pathToFolder = value;
    }

    [YamlMember(Alias = "instructions", ApplyNamingConventions = false)]
    internal string Instructions
    {
        get => _instructions ?? throw new InvalidOperationException("'instructions' in a process cannot be empty");
        init => _instructions = value;
    }

    [YamlMember(Alias = "start_detection_regex", ApplyNamingConventions = false)]
    internal string? StartDetectionRegex { get; init; }

    [YamlMember(Alias = "pre_defined_start_detection", ApplyNamingConventions = false)]
    internal string? PreDefinedStartDetection { get; init; }

    [YamlMember(Alias = "url_detections", ApplyNamingConventions = false)]
    internal UrlDetection[] UrlDetections
    {
        get => _urlDetections ?? [];
        init => _urlDetections = value;
    }

    [YamlMember(Alias = "pre_defined_url_detections", ApplyNamingConventions = false)]
    internal string? PreDefinedUrlDetections { get; init; }
}

internal sealed record UrlDetection
{
    private string? _pattern;
    private bool? _portOnly;
    private bool? _httpsWhenPortOnly;

    [YamlMember(Alias = "pattern", ApplyNamingConventions = false)]
    internal string Pattern
    {
        get => _pattern ?? throw new InvalidOperationException("'pattern' in an url_detection cannot be empty");
        init => _pattern = value;
    }

    [YamlMember(Alias = "port_only", ApplyNamingConventions = false)]
    internal bool PortOnly
    {
        get => _portOnly ?? false;
        init => _portOnly = value;
    }

    [YamlMember(Alias = "https_when_port_only", ApplyNamingConventions = false)]
    internal bool HttpsWhenPortOnly
    {
        get => _httpsWhenPortOnly ?? false;
        init => _httpsWhenPortOnly = value;
    }
}

internal sealed record UrlDetectionWithRegex(Regex Regex, bool PortOnly, bool HttpsWhenPortOnly);

internal sealed record ComposeConfiguration
{
    private string? _path;
    private ComposeType? _type;
    private int? _checkTimeoutSeconds;

    [YamlMember(Alias = "path", ApplyNamingConventions = false)]
    internal string Path
    {
        get => _path ?? string.Empty;
        init => _path = value;
    }

    [YamlMember(Alias = "type", ApplyNamingConventions = false)]
    internal ComposeType Type
    {
        get => _type ?? ComposeType.Docker;
        init => _type = value;
    }

    [YamlMember(Alias = "check_timeout_seconds", ApplyNamingConventions = false)]
    internal int CheckTimeoutSeconds
    {
        get => _checkTimeoutSeconds ?? 60;
        init => _checkTimeoutSeconds = value;
    }
}

internal enum ComposeType
{
    Docker,
    Podman
}