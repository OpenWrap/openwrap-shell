using System;
using System.IO;

namespace OpenWrap
{
    internal class Program
    {
        static int Main(string[] args)
        {
            var rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openwrap");
            var wrapsPath = Path.Combine(rootPath, "wraps");
            var cachePath = Path.Combine(wrapsPath, "_cache");
            return (int)new BootstrapRunner("o.exe", rootPath, new[] { "openwrap"}, "http://wraps.openwrap.org/", new ConsoleNotifier()).Run(args);
        }
    }
}