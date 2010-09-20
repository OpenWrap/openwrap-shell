using System;

namespace OpenWrap.Console
{
    public interface INotifier
    {
        BootstrapResult BootstrappingFailed(Exception exception);
        BootstrapResult RunFailed(Exception e);
        void BootstraperIs(string entrypointFile, Version entrypointVersion);
        void Message(string message, params object[] messageParameters);
        void DownloadStart(Uri downloadAddress);
        void DownloadEnd();
        void DownloadProgress(int progressPercentage);
        InstallAction InstallOptions();
    }
}