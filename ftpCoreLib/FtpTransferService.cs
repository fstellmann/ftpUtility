using System.Diagnostics;

namespace ftpCoreLib
{
    public sealed class FtpTransferService
    {
        public FtpTransferResult Execute(FtpTransferOptions options, CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();
            Directory.CreateDirectory(options.LocalFolder);

            var sw = Stopwatch.StartNew();
            var result = new FtpTransferResult();

            try
            {
                using FtpFluentHandler fluentHandler = new FtpFluentHandler(options);
                using FtpSshHandler sshHandler = new FtpSshHandler(options);
                int count = options.Protocol switch
                {
                    FtpProtocol.Ftp => fluentHandler.ExecuteFtp(cancellationToken, useFtps: false),
                    FtpProtocol.Ftps => fluentHandler.ExecuteFtp(cancellationToken, useFtps: true),
                    FtpProtocol.Sftp => sshHandler.ExecuteSftp(cancellationToken),
                    _ => throw new NotSupportedException($"Protocol {options.Protocol} is not supported.")
                };

                sw.Stop();
                result.FileCount = count;
                result.Duration = sw.Elapsed;
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.FileCount = 0;
                result.Duration = sw.Elapsed;
                result.Exception = ex;
                return result;
            }
        }
    }
}
