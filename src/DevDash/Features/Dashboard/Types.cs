namespace DevDash.Features.Dashboard;

internal sealed class AnsiStyles
{
    public bool Bold { get; private set; }
    public bool Dim { get; private set; }
    public bool Italic { get; private set; }
    public bool Underline { get; private set; }
    public bool Strikethrough { get; private set; }
    public string? ForegroundColor { get; private set; }
    public string? BackgroundColor { get; private set; }

    public void Reset()
    {
        Bold = false;
        Dim = false;
        Italic = false;
        Underline = false;
        Strikethrough = false;
        ForegroundColor = null;
        BackgroundColor = null;
    }

    public void ApplyCode(int code)
    {
        switch (code)
        {
            // Reset
            case 0:
                Reset();
                break;

            // Text styles
            case 1:
                Bold = true;
                break;
            case 2:
                Dim = true;
                break;
            case 3:
                Italic = true;
                break;
            case 4:
                Underline = true;
                break;
            case 9:
                Strikethrough = true;
                break;

            // Reset text styles
            case 22:
                Bold = false;
                Dim = false;
                break;
            case 23:
                Italic = false;
                break;
            case 24:
                Underline = false;
                break;
            case 29:
                Strikethrough = false;
                break;

            // Standard foreground colors (30-37)
            case 30:
                ForegroundColor = "ansi-black";
                break;
            case 31:
                ForegroundColor = "ansi-red";
                break;
            case 32:
                ForegroundColor = "ansi-green";
                break;
            case 33:
                ForegroundColor = "ansi-yellow";
                break;
            case 34:
                ForegroundColor = "ansi-blue";
                break;
            case 35:
                ForegroundColor = "ansi-magenta";
                break;
            case 36:
                ForegroundColor = "ansi-cyan";
                break;
            case 37:
                ForegroundColor = "ansi-white";
                break;

            // Default foreground color
            case 39:
                ForegroundColor = null;
                break;

            // Standard background colors (40-47)
            case 40:
                BackgroundColor = "ansi-bg-black";
                break;
            case 41:
                BackgroundColor = "ansi-bg-red";
                break;
            case 42:
                BackgroundColor = "ansi-bg-green";
                break;
            case 43:
                BackgroundColor = "ansi-bg-yellow";
                break;
            case 44:
                BackgroundColor = "ansi-bg-blue";
                break;
            case 45:
                BackgroundColor = "ansi-bg-magenta";
                break;
            case 46:
                BackgroundColor = "ansi-bg-cyan";
                break;
            case 47:
                BackgroundColor = "ansi-bg-white";
                break;

            // Default background color
            case 49:
                BackgroundColor = null;
                break;

            // Bright foreground colors (90-97)
            case 90:
                ForegroundColor = "ansi-bright-black";
                break;
            case 91:
                ForegroundColor = "ansi-bright-red";
                break;
            case 92:
                ForegroundColor = "ansi-bright-green";
                break;
            case 93:
                ForegroundColor = "ansi-bright-yellow";
                break;
            case 94:
                ForegroundColor = "ansi-bright-blue";
                break;
            case 95:
                ForegroundColor = "ansi-bright-magenta";
                break;
            case 96:
                ForegroundColor = "ansi-bright-cyan";
                break;
            case 97:
                ForegroundColor = "ansi-bright-white";
                break;

            // Bright background colors (100-107)
            case 100:
                BackgroundColor = "ansi-bg-bright-black";
                break;
            case 101:
                BackgroundColor = "ansi-bg-bright-red";
                break;
            case 102:
                BackgroundColor = "ansi-bg-bright-green";
                break;
            case 103:
                BackgroundColor = "ansi-bg-bright-yellow";
                break;
            case 104:
                BackgroundColor = "ansi-bg-bright-blue";
                break;
            case 105:
                BackgroundColor = "ansi-bg-bright-magenta";
                break;
            case 106:
                BackgroundColor = "ansi-bg-bright-cyan";
                break;
            case 107:
                BackgroundColor = "ansi-bg-bright-white";
                break;
        }
    }

    public string ToCssClasses()
    {
        var classes = new List<string>(4);

        if (Bold) classes.Add("ansi-bold");
        if (Dim) classes.Add("ansi-dim");
        if (Italic) classes.Add("ansi-italic");
        if (Underline) classes.Add("ansi-underline");
        if (Strikethrough) classes.Add("ansi-strikethrough");
        if (ForegroundColor != null) classes.Add(ForegroundColor);
        if (BackgroundColor != null) classes.Add(BackgroundColor);

        return string.Join(" ", classes);
    }
}