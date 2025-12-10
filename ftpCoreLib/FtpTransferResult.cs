namespace ftpCoreLib
{
    public sealed class FtpTransferResult
    {
        public int FileCount { get; internal set; }
        public TimeSpan Duration { get; internal set; }
        public Exception? Exception { get; internal set; }
        public bool Success => Exception == null;
    }
}
