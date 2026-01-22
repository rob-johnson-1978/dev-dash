namespace DevDash.Features.Configuration
{
    internal sealed record ComposeConfiguration
    {
        public ComposeConfiguration(int startupOrder, string filePath, ComposeType composeType, int checkTimeoutInSeconds)
        {
            StartupOrder = startupOrder;
            FilePath = filePath;
            ComposeType = composeType;
            CheckTimeoutInSeconds = checkTimeoutInSeconds;
        }

        public int StartupOrder { get; }

        public string FilePath { get; }

        public ComposeType ComposeType { get; }

        public int CheckTimeoutInSeconds { get; }
    }
}
