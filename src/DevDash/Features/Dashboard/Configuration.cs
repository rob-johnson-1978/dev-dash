using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace DevDash.Features.Dashboard;

internal sealed class Configuration
{
    [YamlMember(Alias = "processes", ApplyNamingConventions = false)]
    internal required Dictionary<string, ProcessConfiguration> Processes { get; init; } = [];

    [YamlMember(Alias = "compose", ApplyNamingConventions = false)]
    internal ComposeConfiguration? Compose { get; init; }

    [YamlMember(Alias = "console_output_max_lines", ApplyNamingConventions = false)]
    internal required int ConsoleOutputMaxLines { get; init; } = 100;

    internal int ConsoleOutputLineRemovalBatchSize => Math.Max(1, ConsoleOutputMaxLines / 10);
}

internal sealed record ProcessConfiguration
{
    [YamlMember(Alias = "startup_order", ApplyNamingConventions = false)]
    internal required int StartupOrder { get; init; }

    [YamlMember(Alias = "path_to_folder", ApplyNamingConventions = false)]
    internal required string PathToFolder { get; init; }

    [YamlMember(Alias = "instructions", ApplyNamingConventions = false)]
    internal required string Instructions { get; init; }

    [YamlMember(Alias = "start_detection_regex", ApplyNamingConventions = false)]
    internal string? StartDetectionRegex { get; init; }

    [YamlMember(Alias = "pre_defined_start_detection", ApplyNamingConventions = false)]
    internal string? PreDefinedStartDetection { get; init; }

    [YamlMember(Alias = "url_detections", ApplyNamingConventions = false)]
    internal UrlDetection[] UrlDetections { get; init; } = [];

    [YamlMember(Alias = "pre_defined_url_detections", ApplyNamingConventions = false)]
    internal string? PreDefinedUrlDetections { get; init; }
}

internal sealed record UrlDetection
{
    [YamlMember(Alias = "pattern", ApplyNamingConventions = false)]
    internal required string Pattern { get; init; }

    [YamlMember(Alias = "port_only", ApplyNamingConventions = false)]
    internal bool PortOnly { get; init; }

    [YamlMember(Alias = "https_when_port_only", ApplyNamingConventions = false)]
    internal bool HttpsWhenPortOnly { get; init; }
}

internal sealed record UrlDetectionWithRegex(Regex Regex, bool PortOnly, bool HttpsWhenPortOnly);

internal sealed record ComposeConfiguration
{
    [YamlMember(Alias = "path", ApplyNamingConventions = false)]
    internal required string Path { get; init; }

    [YamlMember(Alias = "type", ApplyNamingConventions = false)]
    internal required ComposeType Type { get; init; }

    [YamlMember(Alias = "check_timeout_seconds", ApplyNamingConventions = false)]
    internal required int CheckTimeoutSeconds { get; init; } = 60;
}

internal enum ComposeType
{
    Docker,
    Podman
}