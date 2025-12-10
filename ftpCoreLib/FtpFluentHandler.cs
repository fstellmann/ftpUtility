using FluentFTP;
using System.Collections.Concurrent;
using System.Net;

namespace ftpCoreLib
{
    internal class FtpFluentHandler : IDisposable
    {
        private bool _disposed;
        private FtpTransferOptions options { get; set; }
        public FtpFluentHandler(FtpTransferOptions _options)
        {
            options = _options;
        }
        public int ExecuteFtp(CancellationToken cancellationToken, bool useFtps)
        {
            return options.Mode == FtpTransferMode.Download ? DownloadFtp(cancellationToken, useFtps) : UploadFtp(cancellationToken, useFtps);
        }

        public int DownloadFtp(CancellationToken cancellationToken, bool useFtps)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            FtpClient CreateClient()
            {
                var client = new FtpClient(options.Host)
                {
                    Credentials = new NetworkCredential(options.Username, options.Password)
                };

                if (options.Port > 0) client.Port = options.Port;

                if (useFtps)
                {
                    client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                    client.Config.ValidateAnyCertificate = true;
                }

                client.Connect();
                return client;
            }

            using var initialClient = CreateClient();
            var listOptions = options.Recursive ? FtpListOption.Recursive : FtpListOption.Auto;
            var listing = initialClient.GetListing(options.RemoteFolder, listOptions).Where(x => x.Type == FtpObjectType.File).ToArray();
            // var listing = initialClient.GetListing(options.RemoteFolder).Where(x => x.Type == FtpObjectType.File).ToArray();
            if (listing.Length == 0) return 0;

            var clients = new ConcurrentBag<FtpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(listing, parallelOptions, file =>
            {
                FtpClient client;
                if (!clients.TryTake(out client!)) client = CreateClient();

                try
                {
                    var relative = FtpHelper.GetRelativeRemotePath(options.RemoteFolder, file.FullName);
                    var localPath = Path.Combine(options.LocalFolder, relative.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    var existsMode = options.OverwriteTarget ? FtpLocalExists.Overwrite : FtpLocalExists.Skip;
                    var status = client.DownloadFile(localPath, file.FullName, existsMode, FtpVerify.None);
                    if (status == FtpStatus.Success) Interlocked.Increment(ref fileCount);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
                finally
                {
                    clients.Add(client);
                }
            });

            while (clients.TryTake(out var c))
            {
                c.Dispose();
            }

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);

            if (options.DeleteSource)
            {
                using var client = CreateClient();
                foreach (var file in listing)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    client.DeleteFile(file.FullName);
                }
            }

            return fileCount;
        }

        public int UploadFtp(CancellationToken cancellationToken, bool useFtps)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            //var files = Directory.GetFiles(options.LocalFolder);
            var files = Directory.GetFiles(options.LocalFolder, "*", options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            if (files.Length == 0) return 0;

            FtpClient CreateClient()
            {
                var client = new FtpClient(options.Host)
                {
                    Credentials = new NetworkCredential(options.Username, options.Password)
                };

                if (options.Port > 0) client.Port = options.Port;

                if (useFtps)
                {
                    client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                    client.Config.ValidateAnyCertificate = true;
                }

                client.Connect();
                return client;
            }

            using var initialClient = CreateClient();
            var clients = new ConcurrentBag<FtpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(files, parallelOptions, filePath =>
            {
                FtpClient client;
                if (!clients.TryTake(out client!)) client = CreateClient();

                try
                {
                    var relative = Path.GetRelativePath(options.LocalFolder, filePath).Replace(Path.DirectorySeparatorChar, '/');
                    var remotePath = FtpHelper.CombineRemotePath(options.RemoteFolder, relative);

                    var remoteDir = remotePath.Contains('/') ? remotePath[..remotePath.LastIndexOf('/')] : options.RemoteFolder;

                    if (!string.IsNullOrEmpty(remoteDir))
                        client.CreateDirectory(remoteDir, true);

                    var existsMode = options.OverwriteTarget ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip;
                    var status = client.UploadFile(filePath, remotePath, existsMode, false, FtpVerify.None);
                    if (status == FtpStatus.Success) Interlocked.Increment(ref fileCount);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
                finally
                {
                    clients.Add(client);
                }
            });

            while (clients.TryTake(out var c))
            {
                c.Dispose();
            }

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);

            if (options.DeleteSource)
            {
                foreach (var filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(filePath);
                }
            }

            return fileCount;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (!disposing) return;
        }
    }
}
