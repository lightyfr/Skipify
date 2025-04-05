using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
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
            try
            {
                // For debugging directly
                if (args.Length > 0 && args[0] == "console")
                {
                    Console.WriteLine("Running in console mode");
                    SpotifyMonitorService service = new SpotifyMonitorService(null);
                    service.StartConsoleMode().GetAwaiter().GetResult();
                    return;
                }

                // Run as a service
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                File.WriteAllText("C:\\SpotifyAdSkipper_error.log", $"Startup error: {ex}");
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
        private int _checkIntervalMs = 1000;
        private bool _isRestarting = false;
        private const string SpotifyProcessName = "Spotify";
        private const string SpotifyExePath = @"C:\Users\{0}\AppData\Roaming\Spotify\Spotify.exe";
        private string _previousTitle = string.Empty;
        private DateTime _lastAdDetection = DateTime.MinValue;
        private string _debugLogPath = "C:\\SpotifyAdSkipper_debug.log";

        // Win32 API imports
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // Media key constants
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public SpotifyMonitorService(ILogger<SpotifyMonitorService> logger)
        {
            _logger = logger;
            LogDebug("SpotifyMonitorService constructor called");
        }

        private void LogDebug(string message)
        {
            try
            {
                string logMessage = $"{DateTime.Now}: {message}";
                File.AppendAllText(_debugLogPath, logMessage + Environment.NewLine);
                _logger?.LogInformation(message);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public async Task StartConsoleMode()
        {
            Console.WriteLine("Spotify Ad Skipper running in console mode");
            Console.WriteLine("Press Ctrl+C to exit");
            
            var tokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                tokenSource.Cancel();
            };
            
            await ExecuteAsync(tokenSource.Token);
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
                            // Get the main Spotify process
                            var spotifyProcesses = Process.GetProcessesByName(SpotifyProcessName);
                            
                            foreach (var process in spotifyProcesses)
                            {
                                if (process.MainWindowHandle != IntPtr.Zero)
                                {
                                    // Get the window title
                                    string title = GetWindowTitle(process.MainWindowHandle);
                                    
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
                LogDebug($"Fatal error: {ex}");
            }
        }

        private string GetWindowTitle(IntPtr windowHandle)
        {
            StringBuilder sb = new StringBuilder(256);
            if (GetWindowText(windowHandle, sb, 256) > 0)
            {
                return sb.ToString().Trim();
            }
            return string.Empty;
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
            try
            {
                var isRunning = Process.GetProcessesByName(SpotifyProcessName).Length > 0;
                return isRunning;
            }
            catch
            {
                return false;
            }
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
                bool started = false;
                
                if (File.Exists(spotifyPath))
                {
                    // Create process start info with proper background settings
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = spotifyPath,
                        UseShellExecute = true,            // Use shell execution which is required for proper window style 
                        WindowStyle = ProcessWindowStyle.Minimized  // Start minimized
                    };
                    
                    Process.Start(psi);
                    started = true;
                    LogDebug("Spotify started in minimized mode successfully");
                }
                
                if (!started)
                {
                    // Try alternative locations with consistent background launch settings
                    string[] altPaths = {
                        @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.225.922.0_x86__zpdnekdrzrea0\Spotify.exe",
                        @"C:\Program Files (x86)\Spotify\Spotify.exe"
                    };
                    
                    foreach (var path in altPaths)
                    {
                        if (File.Exists(path))
                        {
                            LogDebug($"Found alternative Spotify path: {path}");
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true,
                                WindowStyle = ProcessWindowStyle.Minimized
                            };
                            
                            Process.Start(psi);
                            LogDebug("Spotify started from alternative path in minimized mode");
                            started = true;
                            break;
                        }
                    }
                    
                    if (!started)
                    {
                        // Last resort - try the shell command with minimized settings
                        try 
                        {
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = "spotify",
                                UseShellExecute = true,
                                WindowStyle = ProcessWindowStyle.Minimized
                            };
                            
                            Process.Start(psi);
                            LogDebug("Spotify started via shell command in minimized mode");
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Failed to start via shell: {ex.Message}");
                        }
                    }
                }
                
                // Wait for Spotify to start up
                LogDebug("Waiting for Spotify to start");
                await Task.Delay(5000);
                
                // Resume playback with media keys
                if (started)
                {
                    LogDebug("Sending media play command");
                    // Press and release the Play/Pause media key
                    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    
                    // Try to hide Spotify window if it appears
                    IntPtr spotifyWindow = FindWindow(null, "Spotify");
                    if (spotifyWindow != IntPtr.Zero)
                    {
                        // SW_MINIMIZE = 6
                        ShowWindow(spotifyWindow, 6);
                        LogDebug("Minimized Spotify window");
                    }
                    
                    // Also look for any window with "Spotify" in the title
                    await Task.Delay(1000); // Short delay to allow window to appear
                    foreach (var process in Process.GetProcessesByName(SpotifyProcessName))
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(process.MainWindowHandle, 6); // SW_MINIMIZE = 6
                            LogDebug($"Minimized Spotify window with handle {process.MainWindowHandle}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to restart Spotify: {ex.Message}");
            }
            finally
            {
                _isRestarting = false;
            }
        }
    }
}