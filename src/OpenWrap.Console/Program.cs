using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpenWrap.Console
{
    internal class Program
    {
        static int Main(string[] args)
        {
            var rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWrap");
            var wrapsPath = Path.Combine(rootPath, "wraps");
            var cachePath = Path.Combine(wrapsPath, "_cache");
            return (int)new BootstrapRunner(rootPath, cachePath, wrapsPath, new[] { "openwrap", "openfilesystem", "sharpziplib" }, "http://wraps.openwrap.org/bootstrap", new ConsoleNotifier()).Run(args);
        }
    }
}