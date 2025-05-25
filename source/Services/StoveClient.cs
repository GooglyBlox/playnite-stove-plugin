using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace StoveLibrary.Services
{
    public class StoveClient : LibraryClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string StoveExecutablePath = @"%programdata%\Smilegate\STOVE\STOVE.exe";

        public override string Icon
        {
            get
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    if (assembly?.Location != null)
                    {
                        return Path.Combine(Path.GetDirectoryName(assembly.Location), @"icon.png");
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug($"[STOVE] Error getting icon path: {ex.Message}");
                }
                return null;
            }
        }

        public override bool IsInstalled
        {
            get
            {
                try
                {
                    return File.Exists(GetStoveExecutablePath());
                }
                catch (Exception ex)
                {
                    logger.Debug($"[STOVE] Error checking if installed: {ex.Message}");
                    return false;
                }
            }
        }

        private static string GetStoveExecutablePath()
        {
            try
            {
                return Environment.ExpandEnvironmentVariables(StoveExecutablePath);
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Debug($"[STOVE] Error expanding environment variables: {ex.Message}");
                return StoveExecutablePath;
            }
        }

        public override void Open()
        {
            try
            {
                var stoveExePath = GetStoveExecutablePath();

                if (string.IsNullOrEmpty(stoveExePath) || !File.Exists(stoveExePath))
                {
                    logger.Warn($"[STOVE] Client executable not found at {stoveExePath}, opening web store instead");
                    OpenWebStore();
                    return;
                }

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = stoveExePath,
                        UseShellExecute = true
                    };
                    
                    using (var process = Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            logger.Info("[STOVE] Client launched successfully");
                        }
                        else
                        {
                            logger.Warn("[STOVE] Process.Start returned null, falling back to web store");
                            OpenWebStore();
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"[STOVE] Failed to launch client at {stoveExePath}");
                    OpenWebStore();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] Critical error in Open method");
                OpenWebStore();
            }
        }

        private void OpenWebStore()
        {
            try
            {
                ProcessStartInfo webStartInfo = new ProcessStartInfo
                {
                    FileName = "https://store.onstove.com/en",
                    UseShellExecute = true
                };
                Process.Start(webStartInfo);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] Failed to open web store as fallback");
            }
        }

        public override void Shutdown()
        {
            try
            {
                Process[] stoveProcesses = null;
                
                try
                {
                    stoveProcesses = Process.GetProcessesByName("STOVE");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "[STOVE] Error getting STOVE processes");
                    return;
                }

                if (stoveProcesses == null || stoveProcesses.Length == 0)
                {
                    logger.Debug("[STOVE] No running STOVE processes found");
                    return;
                }

                foreach (var process in stoveProcesses)
                {
                    if (process == null)
                        continue;

                    try
                    {
                        if (!process.HasExited)
                        {
                            try
                            {
                                process.CloseMainWindow();
                                if (!process.WaitForExit(5000))
                                {
                                    if (!process.HasExited)
                                    {
                                        process.Kill();
                                        logger.Info("[STOVE] Client process forcefully terminated");
                                    }
                                }
                                else
                                {
                                    logger.Info("[STOVE] Client process closed gracefully");
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                // Process may have already exited
                                logger.Debug("[STOVE] Process already exited during shutdown");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"[STOVE] Failed to close process {process.Id}");
                    }
                    finally
                    {
                        try
                        {
                            process.Dispose();
                        }
                        catch (Exception ex)
                        {
                            logger.Debug($"[STOVE] Error disposing process: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] Error during client shutdown");
            }
        }
    }
}