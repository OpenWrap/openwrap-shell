using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OpenWrap
{
    public class ConsoleNotifier : INotifier
    {
        int _downloadProgress;

        public void BootstraperIs(string entrypointFile, SemanticVersion entrypointVersion)
        {
            var version = FileVersionInfo.GetVersionInfo(typeof(ConsoleNotifier).Assembly.Location);
            var entrypointName = AssemblyName.GetAssemblyName(entrypointFile).Name;
            Console.WriteLine("# {0} v{1} (shell v{2})", entrypointName, entrypointVersion, version.FileVersion);
            //Console.WriteLine("# OpenWrap Shell {0}", version.FileVersion);
            //Console.WriteLine("# " + version.LegalCopyright);
            //Console.WriteLine("# Using {0} ({1})", entrypointFile, entrypointVersion);
            //Console.WriteLine();
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

            char key;
            try
            {
                key = Console.ReadKey().KeyChar;
            }
            catch (InvalidOperationException)
            {
                var input = Console.ReadLine();
                key = string.IsNullOrEmpty(input) ? '\0' : input[0];
            }
            Console.WriteLine();
            switch (key)
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
            Console.WriteLine("# OpenWrap Shell could not start.");
            Console.WriteLine();
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