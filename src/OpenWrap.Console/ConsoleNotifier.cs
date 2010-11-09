using System;

namespace OpenWrap
{
    public class ConsoleNotifier : INotifier
    {
        int _downloadProgress;

        public void BootstraperIs(string entrypointFile, Version entrypointVersion)
        {
            Console.WriteLine("# OpenWrap v{0} ['{1}']", entrypointVersion, entrypointFile);
        }

        public BootstrapResult BootstrappingFailed(Exception exception)
        {
            Console.WriteLine("OpenWrap bootstrapping failed.");
            Console.WriteLine(exception.ToString());
            return BootstrapResult.BootstrapFailed;
        }

        public InstallAction InstallOptions()
        {
            Console.WriteLine("The OpenWrap shell is not installed on this machine. Do you want to:");
            Console.WriteLine("(i) install the shell and make it available on the path?");
            Console.WriteLine("(c) use the current executable location and make it available on the path?");
            Console.WriteLine("(n) do nothing?");
            var key = Console.ReadKey();
            Console.WriteLine();
            switch (key.KeyChar)
            {
                case 'i':
                case 'I':
                    return InstallAction.InstallToDefaultLocation;
                case 'c':
                case 'C':
                    return InstallAction.UseCurrentExecutableLocation;
            }
            return InstallAction.None;
        }

        public void Message(string message, params object[] messageParameters)
        {
            Console.WriteLine(message, messageParameters);
        }

        public BootstrapResult RunFailed(Exception e)
        {
            Console.WriteLine("OpenWrap could not be started.");
            Console.WriteLine(e.Message);
            var oldColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(e.ToString());
            }
            finally
            {
                Console.ForegroundColor = oldColor;
            }
            return BootstrapResult.RunFailed;
        }

        public void DownloadEnd()
        {
            _downloadProgress = 0;
            Console.WriteLine("]");
        }

        public void DownloadProgress(int progressPercentage)
        {
            var progress = progressPercentage / 10;

            if (_downloadProgress < progress && progress <= 10)
            {
                Console.Write(new string('.', progress - _downloadProgress));
                _downloadProgress = progress;
            }
        }

        public void DownloadStart(Uri downloadAddress)
        {
            _downloadProgress = 0;
            Console.Write("Downloading {0} [", downloadAddress);
        }
    }
}