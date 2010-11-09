using System;
using OpenWrap.Preloading;

namespace OpenWrap
{
    public interface INotifier : INotifyDownload
    {
        BootstrapResult BootstrappingFailed(Exception exception);
        BootstrapResult RunFailed(Exception e);
        void BootstraperIs(string entrypointFile, Version entrypointVersion);
        void Message(string message, params object[] messageParameters);
        InstallAction InstallOptions();
    }
}