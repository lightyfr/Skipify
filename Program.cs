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

namespace Skipify
{
    public class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;

        public static void Main(string[] args)
        {
            try
            {
                // Hide console window
                var handle = GetConsoleWindow();
                ShowWindow(handle, SW_HIDE);

                // Register in startup
                RegisterInStartup();

                // For debugging directly
                if (args.Length > 0 && args[0] == "console")
                {
                    ShowWindow(handle, 1); // Show console in debug mode
                    Console.WriteLine("Running in console mode");
                    SpotifyMonitorService service = new SpotifyMonitorService(null);
                    service.StartConsoleMode().GetAwaiter().GetResult();
                    return;
                }

                // Run as background process
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                File.WriteAllText("C:\\Skipify_error.log", $"Startup error: {ex}");
            }
        }

        private static void RegisterInStartup()
        {
            try
            {
                // Get the path of the currently running executable
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                
                // Open the registry key for startup programs
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    // Check if already registered
                    if (key.GetValue("Skipify") == null)
                    {
                        // Add the application to startup
                        key.SetValue("Skipify", $"\"{exePath}\"");
                        File.AppendAllText("C:\\Skipify_debug.log", $"{DateTime.Now}: Added to startup: {exePath}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("C:\\Skipify_error.log", $"{DateTime.Now}: Failed to register startup: {ex.Message}\n");
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
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
        private string _lastSongBeforeRestart = string.Empty;
        private DateTime _lastAdDetection = DateTime.MinValue;
        private string _debugLogPath = "C:\\Skipify_debug.log";

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
            Console.WriteLine("Skipify running in console mode");
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
                LogDebug("Skipify service started");
                
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
                                            
                                            // Store the last recognized song title before restart
                                            _lastSongBeforeRestart = _previousTitle;
                                            LogDebug($"Storing last song title before restart: '{_lastSongBeforeRestart}'");
                                            
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
                    catch (Exception ex) when (!(ex is TaskCanceledException))
                    {
                        LogDebug($"Error monitoring Spotify: {ex.Message}");
                    }
                    
                    try
                    {
                        await Task.Delay(_checkIntervalMs, stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        LogDebug("Service stopping gracefully");
                        break;
                    }
                }
                
                LogDebug("Skipify service stopped normally");
            }
            catch (TaskCanceledException)
            {
                LogDebug("Skipify service stopped gracefully");
            }
            catch (Exception ex)
            {
                LogDebug($"Fatal error: {ex}");
                throw; // Rethrow fatal errors that aren't cancellation
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

        private async Task CheckAndSkipTrackIfNeeded()
        {
            try
            {
                // Get Spotify processes
                var spotifyProcesses = Process.GetProcessesByName(SpotifyProcessName);
                string currentTitle = string.Empty;
                
                // Get the current title from any Spotify window
                foreach (var process in spotifyProcesses)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        currentTitle = GetWindowTitle(process.MainWindowHandle);
                        if (!string.IsNullOrEmpty(currentTitle))
                        {
                            break;
                        }
                    }
                }
                
                LogDebug($"After restart title check: '{currentTitle}'");
                LogDebug($"Last song before restart: '{_lastSongBeforeRestart}'");
                
                // If the current song matches the one before restart, skip to next track
                if (!string.IsNullOrEmpty(currentTitle) && 
                    !string.IsNullOrEmpty(_lastSongBeforeRestart) && 
                    currentTitle == _lastSongBeforeRestart && 
                    !IsAdvertisement(currentTitle))
                {
                    LogDebug("Same song detected after restart - skipping to next track");
                    
                    // Press and release the Next Track media key
                    keybd_event(VK_MEDIA_NEXT_TRACK, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    keybd_event(VK_MEDIA_NEXT_TRACK, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    
                    LogDebug("Skipped to next track");
                }
                else
                {
                    LogDebug("Song after restart is different or an ad - no need to skip");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error checking or skipping track: {ex.Message}");
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
                    // Create process start info with improved background settings
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = spotifyPath,
                        UseShellExecute = true,            // Required for window styling
                        CreateNoWindow = false,            // Must be false when UseShellExecute is true
                        WindowStyle = ProcessWindowStyle.Hidden  // Try Hidden instead of Minimized
                    };
                    
                    var proc = Process.Start(psi);
                    started = true;
                    LogDebug("Spotify process started with hidden window style");
                    
                    // Store the process ID to find and hide its window later
                    int spotifyPid = proc?.Id ?? -1;
                    if (spotifyPid > 0)
                    {
                        LogDebug($"Started Spotify with PID: {spotifyPid}");
                    }
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
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            
                            var proc = Process.Start(psi);
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
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            
                            var proc = Process.Start(psi);
                            LogDebug("Spotify started via shell command in minimized mode");
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Failed to start via shell: {ex.Message}");
                        }
                    }
                }
                
                // More aggressive window hiding approach
                LogDebug("Waiting for Spotify to initialize");
                await Task.Delay(1000); // Shorter initial wait
                
                // Try to immediately hide any Spotify windows as soon as they appear
                // SW_HIDE = 0, SW_MINIMIZE = 6
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    // Try to hide by window name
                    IntPtr spotifyWindow = FindWindow(null, "Spotify");
                    if (spotifyWindow != IntPtr.Zero)
                    {
                        ShowWindow(spotifyWindow, 0); // Use SW_HIDE (0) instead of minimize
                        LogDebug($"Hidden Spotify window on attempt {attempt+1}");
                    }
                    
                    // Try to hide by process main window
                    foreach (var process in Process.GetProcessesByName(SpotifyProcessName))
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(process.MainWindowHandle, 0); // Use SW_HIDE (0)
                            LogDebug($"Hidden Spotify window handle {process.MainWindowHandle}");
                        }
                    }
                    
                    await Task.Delay(500); // Check every half second
                }
                
                // Continue with regular startup sequence
                await Task.Delay(3000); // Still give it time to fully initialize
                
                // Resume playback with media keys
                if (started)
                {
                    LogDebug("Sending media play command");
                    // Press and release the Play/Pause media key
                    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    
                    // Final check to hide any windows that might have appeared
                    IntPtr spotifyWindow = FindWindow(null, "Spotify");
                    if (spotifyWindow != IntPtr.Zero)
                    {
                        ShowWindow(spotifyWindow, 0); // SW_HIDE = 0
                        LogDebug("Hidden Spotify window after playback started");
                    }
                    
                    // Wait a moment for the track to start and the title to update
                    await Task.Delay(2000);
                    
                    // Check if we need to skip to the next track
                    await CheckAndSkipTrackIfNeeded();
                    
                    foreach (var process in Process.GetProcessesByName(SpotifyProcessName))
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(process.MainWindowHandle, 0); // SW_HIDE = 0
                            LogDebug($"Hidden Spotify window handle {process.MainWindowHandle} after playback");
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