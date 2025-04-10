# Skipify - Spotify Ad Skipper

A lightweight Windows service that automatically detects and skips Spotify advertisements by monitoring window titles and restarting the Spotify client when an ad is detected.

## Features

- Runs as a Windows background service
- Automatically starts with Windows
- Minimal resource usage with adaptive monitoring
- No Spotify Premium account required
- No Spotify API keys needed
- Works with the desktop Spotify client
- Smart ad detection using multiple methods
- Intelligent track resumption after ad skipping
- Hidden operation mode for minimal user interference
- Automatic error recovery and logging

## Technical Features

### Smart Ad Detection
- Pattern Analysis: Detects breaks in the standard "Artist - Song" format
- Keyword Detection: Identifies explicit advertisement markers
- Title Analysis: Monitors for suspicious window title changes
- Cooldown System: Prevents excessive restarts with 30-second intervals

### Intelligent Playback Management
- Track Memory: Remembers the last playing song before ad interruption
- Smart Resume: Automatically skips repeated tracks after restart
- Media Key Integration: Uses system media keys for reliable playback control
- Background Operation: Runs completely hidden from user view

### Robust Error Handling
- Multiple Spotify Path Detection: Supports various installation locations
- Graceful Process Management: Ensures clean Spotify termination and restart
- Debug Logging: Maintains detailed logs at `C:\Skipify_debug.log`
- Auto-Recovery: Handles unexpected states and process failures

### System Integration
- Windows Service Integration: Runs as a native Windows service
- Startup Registration: Automatically registers in Windows startup
- Resource Efficient: Adaptive monitoring intervals
- Silent Operation: Minimizes visual interference

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
   git clone https://github.com/lightyfr/skipify.git
   cd skipify
   ```

2. Build the project:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
   ```

3. Run EXE:
   ```powershell
   cd .\bin\Release\net6.0\win-x64\publish\
   .\Skipify.exe
   ```

### Option 2: Download Pre-built Release

1. Download the latest release from the Releases page
2. Extract the zip file
3. Run the Exe

## Usage

Once installed, the service runs automatically in the background, and should start on device startup. No additional configuration is required. The service:

- Starts automatically when Windows boots
- Monitors Spotify only when it's running
- Uses minimal resources when Spotify is closed
- Creates a debug log at `C:\Skipify_debug.log`

## Troubleshooting

If you encounter issues:

1. Check the debug log at `C:\Skipify_debug.log`
2. Check Windows Event Viewer > Application logs for "Skipify" entries
3. Common issues:
   - **Spotify not closing properly**: Try increasing the delay in the `RestartSpotify()` method
   - **Spotify path not found**: Update the `SpotifyExePath` constant in the code to match your installation
   - **Service crashes**: Check the log for detailed error messages


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
