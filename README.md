# 🧘 Monk Mode

A "subtract-only" digital environment that creates visual isolation for deep focus work on Windows.

## Features

### 🔦 The Spotlight
- Full-screen black overlay that tracks your active window
- Three intensity levels:
  - **Flow** (50%): Light dimming for casual focus
  - **Deep Work** (90%): Dark environment for serious work
  - **Blackout** (100%): Complete visual isolation

### ⚔️ The Bouncer
- **Process Killer**: Automatically terminates distracting apps (Steam, Discord, etc.)
- **DNS Wall**: Blocks distracting websites via hosts file (requires Admin)
- Emergency cleanup ensures your system is restored even if the app crashes

### 🧠 The Coach (Optional)
- AI-powered session analysis using OpenAI
- Identifies distraction patterns
- Provides personalized, non-cliché productivity tips

## Requirements

- Windows 10 / 11
- .NET 8.0 Runtime
- Administrator privileges (for DNS blocking)
- OpenAI API key (optional, for AI coaching)

## Installation

1. Clone the repository
2. Open `MonkMode.sln` in Visual Studio 2022 or later
3. Build the solution
4. Run as Administrator

```bash
cd MonkMode
dotnet build
dotnet run
```

## Configuration

### AI Coach Setup (Optional)

Set your OpenAI API key as an environment variable:

```powershell
[Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "your-key-here", "User")
```

Or set it in Windows Settings > System > About > Advanced System Settings > Environment Variables.

### Default Blocklists

The app comes with sensible defaults:

**Blocked Apps:**
- Steam, Discord, Spotify, Slack

**Blocked Sites:**
- YouTube, Twitter, Reddit, Instagram, Facebook, TikTok, Twitch, Netflix

You can customize these in the app before starting a session.

## Usage

1. **Launch** Monk Mode (as Administrator for full features)
2. **Name** your task (e.g., "Write Q3 Report")
3. **Select** intensity level
4. **Optionally** configure blocked apps and sites
5. Click **ENTER THE VOID**
6. Work in focused isolation
7. Press **Ctrl+Shift+Q** or click Exit to end the session
8. Review your session and get coaching

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+Q` | Exit focus session |

## Safety Features

- Global exception handler ensures hosts file is always restored
- Taskbar is automatically restored on exit
- Process monitoring stops immediately on session end
- Backup of hosts file created before modification

## Architecture

```
MonkMode/
├── Views/
│   ├── MainWindow.xaml          # Dashboard
│   ├── OverlayWindow.xaml       # The spotlight effect
│   └── SessionSummaryDialog.xaml # Post-session review
├── Services/
│   ├── WindowTrackerService.cs  # Active window tracking
│   ├── SystemBlockerService.cs  # DNS & process blocking
│   ├── DatabaseService.cs       # SQLite persistence
│   ├── AiCoachService.cs        # OpenAI integration
│   └── NativeMethods.cs         # P/Invoke declarations
├── Models/
│   └── SessionLog.cs            # Data models
└── Themes/
    ├── Colors.xaml              # Color palette
    └── Controls.xaml            # Styled controls
```

## Data Storage

Session logs are stored in:
```
%LOCALAPPDATA%\MonkMode\monkmode.db
```

## Known Limitations

- **Multi-Monitor**: Currently supports primary monitor only. Secondary monitors are not blacked out (planned for v2).
- **Admin Required**: DNS blocking requires administrator privileges. The app works without admin but DNS blocking will be disabled.

## Troubleshooting

### Internet not working after crash
If the app crashes and your internet stops working, manually restore the hosts file:

```powershell
# Open as Administrator
notepad C:\Windows\System32\drivers\etc\hosts
```

Remove all lines between `# MONK MODE BLOCK START` and `# MONK MODE BLOCK END`.

Then flush DNS:
```powershell
ipconfig /flushdns
```

### Overlay not tracking windows
Ensure the app is running with proper permissions. Some elevated applications may not be trackable from a non-elevated process.

## License

MIT License - Use responsibly for your own productivity.

---

*"The mind is everything. What you think, you become."* - Buddha
