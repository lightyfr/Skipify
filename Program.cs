using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SpotifyAdSkipper
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Create a log file for debugging
            File.WriteAllText("C:\\SpotifyAdSkipper_debug.log", $"Service starting at {DateTime.Now}\n");
            
            try
            {
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                File.AppendAllText("C:\\SpotifyAdSkipper_debug.log", $"FATAL ERROR: {ex}\n");
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "Spotify Ad Skipper";
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<SpotifyMonitorService>();
                });
    }

    public class SpotifyMonitorService : BackgroundService
    {
        private readonly ILogger<SpotifyMonitorService> _logger;
        private int _checkIntervalMs = 1000; // Check every second
        private bool _isRestarting = false;
        private const string SpotifyProcessName = "Spotify";
        private const string SpotifyExePath = @"C:\Users\{0}\AppData\Roaming\Spotify\Spotify.exe";
        private string _previousTitle = string.Empty;
        private DateTime _lastAdDetection = DateTime.MinValue;
        
        // Debug file path
        private string _debugLogPath = "C:\\SpotifyAdSkipper_debug.log";

        // Win32 API imports for window title detection
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        // Callback for EnumWindows
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        public SpotifyMonitorService(ILogger<SpotifyMonitorService> logger)
        {
            _logger = logger;
            LogDebug("SpotifyMonitorService constructor called");
        }

        private void LogDebug(string message)
        {
            try
            {
                File.AppendAllText(_debugLogPath, $"{DateTime.Now}: {message}\n");
                _logger.LogInformation(message);
            }
            catch
            {
                // Ignore logging errors to prevent cascading failures
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                LogDebug("SpotifyAdSkipper service started");
                
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (IsSpotifyRunning() && !_isRestarting)
                        {
                            // Get all Spotify window titles
                            var spotifyWindows = GetSpotifyWindowTitles();
                            
                            if (spotifyWindows.Count > 0)
                            {
                                foreach (var title in spotifyWindows)
                                {
                                    // Only log if title changed
                                    if (title != _previousTitle)
                                    {
                                        LogDebug($"Spotify window title: '{title}'");
                                        _previousTitle = title;
                                    }
                                    
                                    // Check for ad indicators in the title
                                    if (IsAdvertisement(title))
                                    {
                                        // Make sure we don't restart too frequently
                                        if ((DateTime.Now - _lastAdDetection).TotalSeconds > 30)
                                        {
                                            LogDebug($"Advertisement detected in title: '{title}'");
                                            _lastAdDetection = DateTime.Now;
                                            await RestartSpotify();
                                            break;
                                        }
                                        else
                                        {
                                            LogDebug("Ad detected but waiting cooldown period");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Only log occasionally to reduce spam
                                if (DateTime.Now.Second % 30 == 0)
                                {
                                    LogDebug("Spotify is running but no windows found");
                                }
                            }
                        }
                        else if (!IsSpotifyRunning())
                        {
                            // Only log occasionally to reduce spam
                            if (DateTime.Now.Second % 30 == 0)
                            {
                                LogDebug("Spotify is not running");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error monitoring Spotify: {ex.Message}");
                    }
                    
                    await Task.Delay(_checkIntervalMs, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"FATAL ERROR: {ex}");
            }
        }

        private List<string> GetSpotifyWindowTitles()
        {
            var titles = new List<string>();
            
            EnumWindows((hWnd, lParam) =>
            {
                int processId;
                GetWindowThreadProcessId(hWnd, out processId);
                
                try
                {
                    Process process = Process.GetProcessById(processId);
                    
                    if (process.ProcessName.Equals(SpotifyProcessName, StringComparison.OrdinalIgnoreCase) && IsWindowVisible(hWnd))
                    {
                        StringBuilder sb = new StringBuilder(256);
                        
                        if (GetWindowText(hWnd, sb, 256) > 0)
                        {
                            string title = sb.ToString().Trim();
                            if (!string.IsNullOrEmpty(title))
                            {
                                titles.Add(title);
                            }
                        }
                    }
                }
                catch
                {
                    // Process might have exited, ignore
                }
                
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return titles;
        }

        private bool IsAdvertisement(string title)
        {
            // Ignore empty titles or special cases
            if (string.IsNullOrEmpty(title) || title == "Spotify Free" || title == "Spotify Premium")
                return false;
                
            // Known ad indicators in title
            bool hasExplicitAdKeywords = 
                title.Contains("Advertisement", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Sponsored", StringComparison.OrdinalIgnoreCase) ||
                title == "Spotify";
            
            if (hasExplicitAdKeywords)
            {
                LogDebug($"Ad detected by keywords: '{title}'");
                return true;
            }
            
            // Regular music follows "Artist - Song" pattern
            // This is the most reliable indicator for regular playback vs ads
            bool isRegularSongPattern = title.Contains(" - ", StringComparison.OrdinalIgnoreCase);
            
            if (!isRegularSongPattern)
            {
                // Not following regular music pattern, and not a known special title
                // Very likely an ad showing a brand name or promotional content
                LogDebug($"Potential ad detected: '{title}' doesn't match 'Artist - Song' pattern");
                return true;
            }
            
            // If we get here, it's showing a regular track with artist and song name
            return false;
        }

        private bool IsSpotifyRunning()
        {
            var isRunning = Process.GetProcessesByName(SpotifyProcessName).Length > 0;
            return isRunning;
        }

        private async Task RestartSpotify()
        {
            try
            {
                _isRestarting = true;
                LogDebug("Restarting Spotify...");
                
                // Kill Spotify process(es)
                foreach (var process in Process.GetProcessesByName(SpotifyProcessName))
                {
                    LogDebug($"Killing Spotify process with PID {process.Id}");
                    process.Kill();
                }
                
                // Wait for processes to fully terminate
                LogDebug("Waiting for processes to terminate");
                await Task.Delay(2000);
                
                // Start Spotify again
                string username = Environment.UserName;
                string spotifyPath = string.Format(SpotifyExePath, username);
                
                LogDebug($"Starting Spotify from: {spotifyPath}");
                if (File.Exists(spotifyPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = spotifyPath,
                        UseShellExecute = true
                    });
                    
                    LogDebug("Spotify started successfully");
                }
                else
                {
                    LogDebug($"ERROR: Spotify executable not found at {spotifyPath}");
                    
                    // Try alternative locations
                    string[] altPaths = {
                        @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.225.922.0_x86__zpdnekdrzrea0\Spotify.exe",
                        @"C:\Program Files (x86)\Spotify\Spotify.exe"
                    };
                    
                    foreach (var path in altPaths)
                    {
                        if (File.Exists(path))
                        {
                            LogDebug($"Found alternative Spotify path: {path}");
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true
                            });
                            
                            LogDebug("Spotify started from alternative path");
                            break;
                        }
                    }
                }
                
                // Wait for Spotify to start up before monitoring again
                await Task.Delay(5000);
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to restart Spotify: {ex}");
            }
            finally
            {
                _isRestarting = false;
            }
        }
    }
}