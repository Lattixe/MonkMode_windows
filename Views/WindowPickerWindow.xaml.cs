using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MonkMode.Services;
using static MonkMode.Services.NativeMethods;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace MonkMode.Views;

public partial class WindowPickerWindow : Window
{
    private readonly ObservableCollection<WindowInfo> _windows = new();
    private readonly Dictionary<IntPtr, WindowBorderOverlay> _previewOverlays = new();

    // P/Invoke for window enumeration - using DllImport for delegate compatibility
    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);
    
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    
    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    
    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    
    public string TaskName { get; set; } = "Focus";
    public int DurationMinutes { get; set; } = 25;
    
    /// <summary>
    /// Gets the selected windows after dialog closes (when used as ShowDialog)
    /// </summary>
    public List<WindowInfo> SelectedWindowsList { get; private set; } = new();
    
    /// <summary>
    /// Windows already in the workspace (will be pre-checked and disabled)
    /// </summary>
    public List<WindowInfo> ExistingWorkspaceWindows { get; set; } = new();
    
    /// <summary>
    /// When true, shows live previews of windows as they're selected (for Add Windows mode)
    /// </summary>
    public bool LivePreviewMode { get; set; } = false;
    
    // Flag to prevent selection changes during initial setup
    private bool _isInitializing = false;
    
    public event EventHandler<WorkspaceSessionRequest>? SessionRequested;

    public WindowPickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Close all preview overlays when picker closes
        CloseAllPreviewOverlays();
    }

    private void CloseAllPreviewOverlays()
    {
        foreach (var overlay in _previewOverlays.Values)
        {
            try
            {
                overlay.StopTracking();
                overlay.Close();
            }
            catch { }
        }
        _previewOverlays.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _isInitializing = true;
            
            // Update UI for mode
            if (LivePreviewMode)
            {
                TaskInfoText.Text = "editing";
                StartButton.Content = "Done";
                
                // Position picker on the right side so windows can be seen
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = SystemParameters.PrimaryScreenWidth - Width - 40;
                Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
            }
            else
            {
                TaskInfoText.Text = $"{DurationMinutes}m";
            }
            
            LoadOpenWindows();
            WindowListBox.ItemsSource = _windows;
            
            // Pre-select existing workspace windows and mark them
            if (ExistingWorkspaceWindows.Count > 0)
            {
                Debug.WriteLine($"[WindowPicker] Pre-selecting {ExistingWorkspaceWindows.Count} existing workspace windows");
                
                foreach (var window in _windows)
                {
                    if (ExistingWorkspaceWindows.Any(w => w.Handle == window.Handle))
                    {
                        window.IsInWorkspace = true;
                        WindowListBox.SelectedItems.Add(window);
                        Debug.WriteLine($"[WindowPicker] Pre-selected: {window.Title}");
                    }
                }
            }
            
            _isInitializing = false;
            
            // Update selection count after initialization
            UpdateSelectionCount();
            
            // Don't touch window visibility on open - they should already be visible in the workspace
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowPicker] Error on load: {ex.Message}");
            _isInitializing = false;
        }
    }
    
    private void UpdateSelectionCount()
    {
        int count = WindowListBox.SelectedItems.Count;
        SelectionCount.Text = count == 1 ? "1 selected" : $"{count} selected";
        StartButton.IsEnabled = count > 0;
    }

    private void LoadOpenWindows()
    {
        _windows.Clear();
        
        var currentProcessId = (uint)Process.GetCurrentProcess().Id;
        var collectedWindows = new List<WindowInfo>();
        
        Debug.WriteLine("[WindowPicker] Starting window enumeration...");

        // Keep a reference to the callback to prevent garbage collection
        EnumWindowsCallback callback = (hWnd, lParam) =>
        {
            try
            {
                // Skip if not visible
                if (!IsWindowVisible(hWnd))
                    return true;

                // Get window title length first
                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                // Get window title
                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString().Trim();

                if (string.IsNullOrWhiteSpace(title))
                    return true;

                // Get process ID
                GetWindowThreadProcessId(hWnd, out uint processId);
                
                // Skip our own process
                if (processId == currentProcessId)
                    return true;

                // Check if this is an "alt-tab" style window (visible in taskbar)
                // A window is shown in alt-tab if:
                // - It has no owner AND is not a tool window, OR
                // - It has WS_EX_APPWINDOW style
                IntPtr owner = GetWindow(hWnd, GW_OWNER);
                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
                bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;

                // Skip tool windows unless they explicitly want to be app windows
                if (isToolWindow && !isAppWindow)
                    return true;

                // Skip windows that have an owner (unless they are app windows)
                if (owner != IntPtr.Zero && !isAppWindow)
                    return true;

                // Get process name
                string processName = "Unknown";
                try
                {
                    using var proc = Process.GetProcessById((int)processId);
                    processName = proc.ProcessName;
                }
                catch { }

                // Skip system processes that we don't want to show
                string[] skipProcesses = { 
                    "TextInputHost", "ShellExperienceHost", 
                    "SearchHost", "StartMenuExperienceHost", "LockApp", 
                    "MonkMode", "dwm", "winlogon", "csrss", "svchost"
                };
                if (skipProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
                    return true;

                Debug.WriteLine($"[WindowPicker] Found: {title} ({processName})");

                collectedWindows.Add(new WindowInfo
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = processName,
                    ProcessId = (int)processId,
                    Index = collectedWindows.Count + 1
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowPicker] Error processing window: {ex.Message}");
            }

            return true; // Continue enumeration
        };

        // Enumerate windows
        EnumWindows(callback, IntPtr.Zero);

        // Add all collected windows to the observable collection
        foreach (var win in collectedWindows)
        {
            win.Index = _windows.Count + 1;
            _windows.Add(win);
        }

        Debug.WriteLine($"[WindowPicker] Found {_windows.Count} windows total");
    }

    private void WindowListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Skip during initialization - we don't want to manipulate windows
        if (_isInitializing) return;
        
        try
        {
            // Handle removed items (deselected)
            foreach (var item in e.RemovedItems.Cast<WindowInfo>())
            {
                if (LivePreviewMode)
                {
                    // In live preview mode, hide deselected windows
                    HideWindowPreview(item.Handle);
                }
                else
                {
                    RemovePreviewOverlay(item.Handle);
                }
            }

            // Handle added items (selected)
            foreach (var item in e.AddedItems.Cast<WindowInfo>())
            {
                if (LivePreviewMode)
                {
                    // In live preview mode, show selected windows
                    ShowWindowPreview(item.Handle);
                }
                else
                {
                    CreatePreviewOverlay(item);
                }
            }

            // Update selection count
            UpdateSelectionCount();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowPicker] Selection error: {ex.Message}");
        }
    }
    
    private void ShowWindowPreview(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        
        try
        {
            Debug.WriteLine($"[WindowPicker] Showing window preview: {handle}");
            
            // Restore if minimized (don't change size/position - just make visible)
            if (IsIconic(handle))
            {
                ShowWindow(handle, 9); // SW_RESTORE
            }
            else
            {
                // Just make sure it's visible
                ShowWindow(handle, 5); // SW_SHOW
            }
            
            // Bring to front
            BringWindowToTop(handle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowPicker] Error showing preview: {ex.Message}");
        }
    }
    
    private void HideWindowPreview(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        
        try
        {
            Debug.WriteLine($"[WindowPicker] Hiding window preview: {handle}");
            // Minimize the window to hide it
            ShowWindow(handle, 6); // SW_MINIMIZE
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowPicker] Error hiding preview: {ex.Message}");
        }
    }
    
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private void CreatePreviewOverlay(WindowInfo window)
    {
        if (window.Handle == IntPtr.Zero)
        {
            Debug.WriteLine($"[WindowPicker] Cannot create overlay - handle is zero for: {window.Title}");
            return;
        }

        // Don't create duplicate
        if (_previewOverlays.ContainsKey(window.Handle))
        {
            Debug.WriteLine($"[WindowPicker] Overlay already exists for: {window.Title}");
            return;
        }

        try
        {
            Debug.WriteLine($"[WindowPicker] Creating preview overlay for: {window.Title} (Handle: {window.Handle})");
            
            var overlay = new WindowBorderOverlay();
            overlay.Show();
            overlay.TrackWindow(window.Handle);
            
            _previewOverlays[window.Handle] = overlay;
            Debug.WriteLine($"[WindowPicker] Overlay created successfully, total overlays: {_previewOverlays.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowPicker] Error creating preview overlay: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void RemovePreviewOverlay(IntPtr handle)
    {
        if (_previewOverlays.TryGetValue(handle, out var overlay))
        {
            try
            {
                Debug.WriteLine($"[WindowPicker] Removing preview overlay");
                overlay.StopTracking();
                overlay.Close();
            }
            catch { }
            
            _previewOverlays.Remove(handle);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && StartButton.IsEnabled)
        {
            StartSession();
            e.Handled = true;
        }
        // Number keys 1-9 for quick toggle selection
        else if (e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int index = e.Key - Key.D1;
            ToggleItemSelection(index);
            e.Handled = true;
        }
        else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
        {
            int index = e.Key - Key.NumPad1;
            ToggleItemSelection(index);
            e.Handled = true;
        }
    }

    private void ToggleItemSelection(int index)
    {
        if (index < 0 || index >= _windows.Count)
            return;

        var item = _windows[index];
        if (WindowListBox.SelectedItems.Contains(item))
            WindowListBox.SelectedItems.Remove(item);
        else
            WindowListBox.SelectedItems.Add(item);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        StartSession();
    }

    private void StartSession()
    {
        try
        {
            var selectedWindows = WindowListBox.SelectedItems
                .Cast<WindowInfo>()
                .ToList();
            
            if (selectedWindows.Count == 0)
                return;

            Debug.WriteLine($"[WindowPicker] Starting session with {selectedWindows.Count} windows");

            // Close all preview overlays BEFORE starting the workspace
            CloseAllPreviewOverlays();

            // Store selected windows for dialog result access
            SelectedWindowsList = selectedWindows;

            // Fire event for event-based usage (used by LauncherWindow)
            if (SessionRequested != null)
            {
                SessionRequested.Invoke(this, new WorkspaceSessionRequest
                {
                    TaskName = TaskName,
                    DurationMinutes = DurationMinutes,
                    SelectedWindows = selectedWindows
                });
                Close();
            }
            else
            {
                // Set dialog result for ShowDialog usage (used by FocusWorkspaceWindow's Add Windows)
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowPicker] Start error: {ex.Message}");
            System.Windows.MessageBox.Show($"Error starting session: {ex.Message}", "Error");
        }
    }
}

public class WindowInfo : INotifyPropertyChanged
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public int Index { get; set; } // 1-based index for display
    public string IndexDisplay => Index <= 9 ? Index.ToString() : "";
    
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }
    
    /// <summary>
    /// True if this window is already in the workspace (pre-selected, shown differently)
    /// </summary>
    public bool IsInWorkspace { get; set; } = false;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public override string ToString() => Title;
}

public class WorkspaceSessionRequest : EventArgs
{
    public required string TaskName { get; init; }
    public required int DurationMinutes { get; init; }
    public required List<WindowInfo> SelectedWindows { get; init; }
}
