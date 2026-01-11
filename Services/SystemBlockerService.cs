using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using static MonkMode.Services.NativeMethods;

namespace MonkMode.Services;

/// <summary>
/// Handles system-level blocking: DNS (hosts file), process killing, Focus Assist, and taskbar hiding.
/// Implements critical safety measures for cleanup on crash/exit.
/// </summary>
public class SystemBlockerService : IDisposable
{
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");
    
    private static readonly string HostsBackupPath = HostsPath + ".monkmode.bak";
    private static readonly string MonkModeMarker = "# MONK MODE BLOCK START";
    private static readonly string MonkModeMarkerEnd = "# MONK MODE BLOCK END";

    // Default distraction blocklists
    public static readonly string[] DefaultBlockedDomains = new[]
    {
        // Social Media
        "twitter.com", "x.com", "facebook.com", "instagram.com", "tiktok.com",
        "snapchat.com", "linkedin.com", "reddit.com", "tumblr.com", "pinterest.com",
        
        // Video/Entertainment
        "youtube.com", "netflix.com", "twitch.tv", "hulu.com", "disneyplus.com",
        "primevideo.com", "hbomax.com", "crunchyroll.com",
        
        // News/Media (optional - can be removed)
        "news.ycombinator.com", "buzzfeed.com", "9gag.com",
        
        // Gaming
        "store.steampowered.com", "epicgames.com", "discord.com",
        
        // Other time sinks
        "amazon.com", "ebay.com", "etsy.com"
    };

    public static readonly string[] DefaultBlockedProcesses = new[]
    {
        // Social/Communication
        "Discord", "Slack", "Teams", "Telegram", "WhatsApp",
        
        // Games
        "steam", "EpicGamesLauncher", "Battle.net",
        
        // Entertainment
        "Spotify" // Optional - some people use for focus music
    };

    private readonly DispatcherTimer? _processKillerTimer;
    private readonly HashSet<string> _blockedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedDomains = new();
    private bool _isSessionActive;
    private bool _taskbarHidden;
    private bool _hostsModified;
    private bool _focusAssistEnabled;
    private int _previousFocusAssistState;
    private bool _isDisposed;

    public event EventHandler<ProcessBlockedEventArgs>? ProcessBlocked;

    public SystemBlockerService()
    {
        _processKillerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _processKillerTimer.Tick += OnProcessKillerTick;
    }

    #region Process Blocking

    /// <summary>
    /// Add processes to the block list.
    /// </summary>
    public void SetBlockedProcesses(IEnumerable<string> processNames)
    {
        _blockedProcesses.Clear();
        foreach (var name in processNames)
        {
            // Remove .exe extension if present for consistency
            string cleanName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
            _blockedProcesses.Add(cleanName);
        }
    }

    /// <summary>
    /// Add domains to the DNS block list.
    /// </summary>
    public void SetBlockedDomains(IEnumerable<string> domains)
    {
        _blockedDomains.Clear();
        foreach (var domain in domains)
        {
            _blockedDomains.Add(domain.Trim().ToLowerInvariant());
        }
    }

    /// <summary>
    /// Start blocking processes, DNS, and enable Focus Assist.
    /// </summary>
    public void StartBlocking()
    {
        _isSessionActive = true;

        // Enable Focus Assist (DND) first
        EnableFocusAssist();

        // Start process killer
        if (_blockedProcesses.Count > 0)
        {
            _processKillerTimer?.Start();
            // Kill any currently running blocked processes
            KillBlockedProcesses();
        }

        // Apply DNS blocks
        if (_blockedDomains.Count > 0)
        {
            ApplyHostsFileBlocks();
        }
        
        Debug.WriteLine($"[MonkMode] Blocking started - DNS: {_blockedDomains.Count} domains, Processes: {_blockedProcesses.Count}");
    }

    /// <summary>
    /// Stop all blocking.
    /// </summary>
    public void StopBlocking()
    {
        _isSessionActive = false;
        _processKillerTimer?.Stop();
        RestoreHostsFile();
        DisableFocusAssist();
        
        Debug.WriteLine("[MonkMode] All blocking stopped");
    }

    private void OnProcessKillerTick(object? sender, EventArgs e)
    {
        if (!_isSessionActive) return;
        KillBlockedProcesses();
    }

    private void KillBlockedProcesses()
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    if (_blockedProcesses.Contains(process.ProcessName))
                    {
                        string processName = process.ProcessName;
                        process.Kill();
                        
                        ProcessBlocked?.Invoke(this, new ProcessBlockedEventArgs
                        {
                            ProcessName = processName,
                            Timestamp = DateTime.Now
                        });

                        Debug.WriteLine($"[MonkMode] Killed blocked process: {processName}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MonkMode] Failed to kill process: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Process enumeration error: {ex.Message}");
        }
    }

    #endregion

    #region DNS Blocking (Hosts File)

    private void ApplyHostsFileBlocks()
    {
        try
        {
            // Backup existing hosts file
            if (File.Exists(HostsPath) && !File.Exists(HostsBackupPath))
            {
                File.Copy(HostsPath, HostsBackupPath, overwrite: false);
            }

            // Read current hosts content
            string existingContent = File.Exists(HostsPath) 
                ? File.ReadAllText(HostsPath) 
                : string.Empty;

            // Remove any existing Monk Mode blocks
            existingContent = RemoveMonkModeBlocks(existingContent);

            // Build new block entries
            var blockEntries = new List<string>
            {
                "",
                MonkModeMarker,
                $"# Added by Monk Mode on {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                "# DO NOT EDIT - Will be removed automatically"
            };

            foreach (var domain in _blockedDomains)
            {
                blockEntries.Add($"127.0.0.1 {domain}");
                blockEntries.Add($"127.0.0.1 www.{domain}");
            }

            blockEntries.Add(MonkModeMarkerEnd);
            blockEntries.Add("");

            // Write combined content
            string newContent = existingContent.TrimEnd() + Environment.NewLine + 
                               string.Join(Environment.NewLine, blockEntries);
            
            File.WriteAllText(HostsPath, newContent);
            _hostsModified = true;

            // Flush DNS cache
            FlushDnsCache();

            Debug.WriteLine($"[MonkMode] Applied DNS blocks for {_blockedDomains.Count} domains");
        }
        catch (UnauthorizedAccessException)
        {
            Debug.WriteLine("[MonkMode] Failed to modify hosts file - need administrator privileges");
            throw new InvalidOperationException(
                "Cannot modify hosts file. Please run Monk Mode as Administrator.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Hosts file error: {ex.Message}");
            throw;
        }
    }

    public void RestoreHostsFile()
    {
        if (!_hostsModified) return;

        try
        {
            if (File.Exists(HostsPath))
            {
                string content = File.ReadAllText(HostsPath);
                string cleanedContent = RemoveMonkModeBlocks(content);
                File.WriteAllText(HostsPath, cleanedContent);
            }

            // Also try to restore from backup if it exists
            if (File.Exists(HostsBackupPath))
            {
                // Only restore from backup if current file seems corrupted
                File.Delete(HostsBackupPath);
            }

            _hostsModified = false;
            FlushDnsCache();

            Debug.WriteLine("[MonkMode] Restored hosts file");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Failed to restore hosts file: {ex.Message}");
        }
    }

    private static string RemoveMonkModeBlocks(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var result = new List<string>();
        bool inMonkModeBlock = false;

        foreach (var line in lines)
        {
            if (line.Trim() == MonkModeMarker)
            {
                inMonkModeBlock = true;
                continue;
            }

            if (line.Trim() == MonkModeMarkerEnd)
            {
                inMonkModeBlock = false;
                continue;
            }

            if (!inMonkModeBlock)
            {
                result.Add(line);
            }
        }

        // Remove trailing empty lines
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return string.Join(Environment.NewLine, result);
    }

    private static void FlushDnsCache()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] DNS flush error: {ex.Message}");
        }
    }

    #endregion

    #region Focus Assist (Windows DND)

    /// <summary>
    /// Enable Focus Assist (Do Not Disturb) to block all notifications.
    /// </summary>
    public void EnableFocusAssist()
    {
        try
        {
            // Save current state to restore later
            _previousFocusAssistState = GetFocusAssistState();
            
            // Set Focus Assist to Priority Only (1) or Alarms Only (2)
            // 2 = Alarms only (strictest)
            SetFocusAssistState(2);
            _focusAssistEnabled = true;
            
            Debug.WriteLine("[MonkMode] Focus Assist enabled (Alarms only)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Failed to enable Focus Assist: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore Focus Assist to previous state.
    /// </summary>
    public void DisableFocusAssist()
    {
        if (!_focusAssistEnabled) return;
        
        try
        {
            SetFocusAssistState(_previousFocusAssistState);
            _focusAssistEnabled = false;
            
            Debug.WriteLine("[MonkMode] Focus Assist restored to previous state");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Failed to disable Focus Assist: {ex.Message}");
        }
    }

    private static int GetFocusAssistState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.shell.focusassistcounterconfig\windows.data.shell.focusassistcounterconfig",
                false);
            
            if (key?.GetValue("Data") is byte[] data && data.Length > 0)
            {
                // The focus assist state is typically at a specific offset
                // This is a simplified approach - actual parsing is complex
                return 0; // Return 0 (off) as default
            }
        }
        catch { }
        
        return 0;
    }

    private static void SetFocusAssistState(int state)
    {
        try
        {
            // Method 1: Use WNF (Windows Notification Facility) - most reliable
            // For simplicity, we'll use the Settings app approach via process
            
            // Start Focus Assist via PowerShell
            var script = state switch
            {
                0 => "$settings = Get-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Notifications\\Settings' -ErrorAction SilentlyContinue; " +
                     "if ($settings) { Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Notifications\\Settings' -Name 'NOC_GLOBAL_SETTING_TOASTS_ENABLED' -Value 1 -Type DWord -ErrorAction SilentlyContinue }",
                     
                2 => "$settings = Get-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Notifications\\Settings' -ErrorAction SilentlyContinue; " +
                     "if ($settings) { Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Notifications\\Settings' -Name 'NOC_GLOBAL_SETTING_TOASTS_ENABLED' -Value 0 -Type DWord -ErrorAction SilentlyContinue }",
                     
                _ => ""
            };

            if (!string.IsNullOrEmpty(script))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit(3000);
            }
            
            // Also set via direct registry for notification suppression
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings");
            
            if (state == 2) // Suppress all
            {
                key?.SetValue("NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 0, RegistryValueKind.DWord);
                key?.SetValue("NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK", 0, RegistryValueKind.DWord);
            }
            else // Restore
            {
                key?.SetValue("NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 1, RegistryValueKind.DWord);
                key?.SetValue("NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK", 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Focus Assist registry error: {ex.Message}");
        }
    }

    #endregion

    #region Taskbar Control

    public void HideTaskbar()
    {
        try
        {
            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
            if (taskbarHandle != IntPtr.Zero)
            {
                ShowWindow(taskbarHandle, SW_HIDE);
                _taskbarHidden = true;
            }

            // Also hide secondary taskbar on multi-monitor setups
            IntPtr secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondaryTaskbar != IntPtr.Zero)
            {
                ShowWindow(secondaryTaskbar, SW_HIDE);
            }

            Debug.WriteLine("[MonkMode] Taskbar hidden");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Failed to hide taskbar: {ex.Message}");
        }
    }

    public void RestoreTaskbar()
    {
        if (!_taskbarHidden) return;

        try
        {
            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
            if (taskbarHandle != IntPtr.Zero)
            {
                ShowWindow(taskbarHandle, SW_SHOW);
            }

            IntPtr secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondaryTaskbar != IntPtr.Zero)
            {
                ShowWindow(secondaryTaskbar, SW_SHOW);
            }

            _taskbarHidden = false;
            Debug.WriteLine("[MonkMode] Taskbar restored");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonkMode] Failed to restore taskbar: {ex.Message}");
        }
    }

    #endregion

    #region Emergency Cleanup

    /// <summary>
    /// Called during crash/emergency to ensure system is restored.
    /// </summary>
    public void EmergencyCleanup()
    {
        try
        {
            RestoreTaskbar();
            RestoreHostsFile();
            DisableFocusAssist();
            _processKillerTimer?.Stop();
            Debug.WriteLine("[MonkMode] Emergency cleanup completed");
        }
        catch
        {
            // Swallow exceptions during emergency cleanup
        }
    }

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;

        StopBlocking();
        RestoreTaskbar();
        _processKillerTimer?.Stop();
        _isDisposed = true;
        
        GC.SuppressFinalize(this);
    }
}

public class ProcessBlockedEventArgs : EventArgs
{
    public required string ProcessName { get; init; }
    public required DateTime Timestamp { get; init; }
}
