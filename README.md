# Skipify - Spotify Ad Skipper

A lightweight Windows service that automatically detects and skips Spotify advertisements by monitoring window titles and restarting the Spotify client when an ad is detected.

## Features

- Runs as a Windows background service
- Automatically starts with Windows
- Minimal resource usage with adaptive monitoring
- No Spotify Premium account required
- No Spotify API keys needed
- Works with the desktop Spotify client

## How It Works

This service monitors Spotify's window titles to detect advertisements. When an ad is playing, Spotify changes its window title to display the ad content or company name, which breaks the usual "Artist - Song" pattern. When the service detects this pattern change, it:

1. Closes the Spotify application
2. Waits a brief moment
3. Restarts Spotify automatically

The result is that ads are effectively skipped, and your music resumes with minimal interruption.

## Prerequisites

- Windows 10/11
- .NET 6.0 SDK or Runtime
- Spotify desktop client
- Administrative privileges (for service installation)

## Installation

### Option 1: Build from Source

1. Clone this repository:
   ```
   git clone https://github.com/yourusername/spotify-ad-skipper.git
   cd spotify-ad-skipper
   ```

2. Build the project:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
   ```

3. Install as a Windows service (run PowerShell as Administrator):
   ```powershell
   cd ./publish
   sc.exe create "SpotifyAdSkipper" binPath= "$(Get-Location)\SpotifyAdSkipper.exe" start= auto
   sc.exe start "SpotifyAdSkipper"
   ```

### Option 2: Download Pre-built Release

1. Download the latest release from the Releases page
2. Extract the zip file
3. Run PowerShell as Administrator and navigate to the extracted folder
4. Install the service:
   ```powershell
   sc.exe create "SpotifyAdSkipper" binPath= "$(Get-Location)\SpotifyAdSkipper.exe" start= auto
   sc.exe start "SpotifyAdSkipper"
   ```

## Usage

Once installed, the service runs automatically in the background. No additional configuration is required. The service:

- Starts automatically when Windows boots
- Monitors Spotify only when it's running
- Uses minimal resources when Spotify is closed
- Creates a debug log at `C:\SpotifyAdSkipper_debug.log`

## Troubleshooting

If you encounter issues:

1. Check the debug log at `C:\SpotifyAdSkipper_debug.log`
2. Check Windows Event Viewer > Application logs for "SpotifyAdSkipper" entries
3. Common issues:
   - **Spotify not closing properly**: Try increasing the delay in the `RestartSpotify()` method
   - **Spotify path not found**: Update the `SpotifyExePath` constant in the code to match your installation
   - **Service crashes**: Check the log for detailed error messages

### Restarting or Removing the Service

To restart the service:
```powershell
sc.exe stop "SpotifyAdSkipper"
sc.exe start "SpotifyAdSkipper"
```

To remove the service:
```powershell
sc.exe stop "SpotifyAdSkipper"
sc.exe delete "SpotifyAdSkipper"
```

## How Ad Detection Works

The service detects ads using several methods:

1. **Pattern detection**: Normal music follows the "Artist - Song" pattern (contains " - ")
2. **Keyword detection**: Looks for words like "Advertisement" or "Sponsored"
3. **Title analysis**: Identifies when the window title is just "Spotify" without song info

This multi-layered approach catches almost all advertisements, even when they don't explicitly identify themselves as ads.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Disclaimer

This project is for educational purposes only. The use of this software may be against Spotify's terms of service. Use at your own risk.
