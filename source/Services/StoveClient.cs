using Playnite.SDK;
using Playnite.SDK.Plugins;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace StoveLibrary.Services
{
    public class StoveClient : LibraryClient
    {
        public override string Icon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"icon.png");
        public override bool IsInstalled => false; // STOVE doesn't have a standalone client we can detect yet

        public override void Open()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "https://store.onstove.com/en",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        public override void Shutdown()
        {
            // No specific shutdown process for STOVE
        }
    }
}