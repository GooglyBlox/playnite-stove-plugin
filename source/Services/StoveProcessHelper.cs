using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace StoveLibrary.Services
{
    internal class StoveProcessInfo
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string ImagePath { get; set; }
    }

    internal static class StoveProcessHelper
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static List<StoveProcessInfo> TakeSnapshot()
        {
            var result = new List<StoveProcessInfo>();
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error enumerating processes");
                return result;
            }

            foreach (var process in processes)
            {
                try
                {
                    int pid = process.Id;
                    if (pid <= 4)
                        continue;

                    var imagePath = GetImagePath(pid);

                    string name = null;
                    try
                    {
                        name = process.ProcessName;
                    }
                    catch
                    {
                        if (!string.IsNullOrEmpty(imagePath))
                            name = Path.GetFileNameWithoutExtension(imagePath);
                    }

                    if (string.IsNullOrEmpty(imagePath) && string.IsNullOrEmpty(name))
                        continue;

                    result.Add(new StoveProcessInfo { Pid = pid, ProcessName = name, ImagePath = imagePath });
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }

            return result;
        }

        private static string GetImagePath(int pid)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
                return null;

            try
            {
                var size = 1024;
                var builder = new StringBuilder(size);
                if (QueryFullProcessImageName(handle, 0, builder, ref size))
                    return builder.ToString(0, size);
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static bool IsImageInDirectory(string imagePath, string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(installDirectory))
                return false;

            string processDirectory;
            try
            {
                processDirectory = Path.GetDirectoryName(imagePath);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(processDirectory))
                return false;

            var dir = installDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (dir.Length == 0)
                return false;

            return string.Equals(processDirectory, dir, StringComparison.OrdinalIgnoreCase) ||
                processDirectory.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static List<StoveProcessInfo> FindByDirectory(IEnumerable<StoveProcessInfo> snapshot, string installDirectory)
        {
            return snapshot.Where(p => IsImageInDirectory(p.ImagePath, installDirectory)).ToList();
        }

        public static List<StoveProcessInfo> FindByExactName(IEnumerable<StoveProcessInfo> snapshot, string executableName)
        {
            if (string.IsNullOrEmpty(executableName))
                return new List<StoveProcessInfo>();
            return snapshot.Where(p => string.Equals(p.ProcessName, executableName, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
