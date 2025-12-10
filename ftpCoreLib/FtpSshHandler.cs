using Renci.SshNet;
using System.Collections.Concurrent;

namespace ftpCoreLib
{
    internal class FtpSshHandler : IDisposable
    {
        private bool _disposed;
        private FtpTransferOptions options { get; set; }
        public FtpSshHandler(FtpTransferOptions _options)
        {
            options = _options;
        }
        internal int ExecuteSftp(CancellationToken cancellationToken)
        {
            return options.Mode == FtpTransferMode.Download ? DownloadSftp(cancellationToken) : UploadSftp(cancellationToken);
        }

        private SftpClient CreateSftpClient()
        {
            int port = options.Port > 0 ? options.Port : 22;
            return new SftpClient(options.Host, port, options.Username, options.Password);
        }

        private int DownloadSftp(CancellationToken cancellationToken)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            using var initialClient = CreateSftpClient();
            initialClient.Connect();

            var listing = initialClient.ListDirectory(options.RemoteFolder).Where(x => !x.IsDirectory && !x.IsSymbolicLink).ToArray();
            if (listing.Length == 0) return 0;

            var clients = new ConcurrentBag<SftpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(listing, parallelOptions, file =>
            {
                SftpClient client;
                if (!clients.TryTake(out client!))
                {
                    client = CreateSftpClient();
                    client.Connect();
                }

                try
                {
                    var localPath = Path.Combine(options.LocalFolder, file.Name);
                    if (File.Exists(localPath))
                    {
                        if (options.OverwriteTarget) File.Delete(localPath);
                        else
                        {
                            return;
                        }
                    }

                    using var fs = File.Open(localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    client.DownloadFile(file.FullName, fs);
                    Interlocked.Increment(ref fileCount);
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
                if (c.IsConnected) c.Disconnect();
                c.Dispose();
            }

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);

            if (options.DeleteSource)
            {
                using var client = CreateSftpClient();
                client.Connect();
                foreach (var file in listing)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    client.DeleteFile(file.FullName);
                }
                client.Disconnect();
            }

            return fileCount;
        }

        private int UploadSftp(CancellationToken cancellationToken)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            var files = Directory.GetFiles(options.LocalFolder);
            if (files.Length == 0) return 0;

            using var initialClient = CreateSftpClient();
            initialClient.Connect();

            var clients = new ConcurrentBag<SftpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(files, parallelOptions, filePath =>
            {
                SftpClient client;
                if (!clients.TryTake(out client!))
                {
                    client = CreateSftpClient();
                    client.Connect();
                }

                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var remotePath = FtpHelper.CombineRemotePath(options.RemoteFolder, fileName);

                    using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (client.Exists(remotePath))
                    {
                        if (!options.OverwriteTarget) return;
                        client.DeleteFile(remotePath);
                    }

                    client.UploadFile(fs, remotePath);
                    Interlocked.Increment(ref fileCount);
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
                if (c.IsConnected) c.Disconnect();
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
