namespace DevDash.Features.Configuration
{
    internal sealed record ComposeConfiguration
    {
        public ComposeConfiguration(int startupOrder, string filePath, ComposeType composeType)
        {
            StartupOrder = startupOrder;
            FilePath = filePath;
            ComposeType = composeType;
        }

        public int StartupOrder { get; }

        public string FilePath { get; }

        public ComposeType ComposeType { get; }
    }
}
