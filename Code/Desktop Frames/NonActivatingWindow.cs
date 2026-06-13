using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Desktop_Frames;

public class NonActivatingWindow : Window
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_RESTORE = 0xF120;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const double ResizeBorderThickness = 8.0;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private bool _focusPreventionEnabled = true;
    private bool _isDesktopChild = false;
    private IntPtr _originalParent = IntPtr.Zero;

    // --- Idle Fade-Out Fields ---
    private System.Windows.Threading.DispatcherTimer _idleTimer;
    private bool _isIdleFaded = false;
    // ----------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        hwndSource.AddHook(WndProc);
        SetWindowLong(new WindowInteropHelper(this).Handle, GWL_EXSTYLE, GetWindowLong(new WindowInteropHelper(this).Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        SetupIdleTimer();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {

        const int WM_ENTERSIZEMOVE = 0x0231; // Resizing starts
        const int WM_EXITSIZEMOVE = 0x0232;  // Resizing ends

        if (msg == WM_ENTERSIZEMOVE)
        {
            Framemanager.OnResizingStarted(this);
        }
        else if (msg == WM_EXITSIZEMOVE)
        {
            Framemanager.OnResizingEnded(this);
        }

        if (msg == WM_NCHITTEST && ResizeMode != ResizeMode.NoResize)
        {
            IntPtr hitTest = HitTestResizeBorder(lParam);
            if (hitTest != IntPtr.Zero)
            {
                handled = true;
                return hitTest;
            }
        }

        // Handle existing focus prevention

        if (_focusPreventionEnabled && msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        // Block Aero Snap maximize/restore commands
        if (msg == WM_SYSCOMMAND)
        {
            int command = wParam.ToInt32() & 0xFFF0;
            if (command == SC_MAXIMIZE || command == SC_RESTORE)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr HitTestResizeBorder(IntPtr lParam)
    {
        int x = unchecked((short)((long)lParam & 0xFFFF));
        int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
        Point position = PointFromScreen(new Point(x, y));

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return IntPtr.Zero;

        bool left = position.X >= 0 && position.X <= ResizeBorderThickness;
        bool right = position.X <= width && position.X >= width - ResizeBorderThickness;
        bool top = position.Y >= 0 && position.Y <= ResizeBorderThickness;
        bool bottom = position.Y <= height && position.Y >= height - ResizeBorderThickness;

        if (top && left) return new IntPtr(HTTOPLEFT);
        if (top && right) return new IntPtr(HTTOPRIGHT);
        if (bottom && left) return new IntPtr(HTBOTTOMLEFT);
        if (bottom && right) return new IntPtr(HTBOTTOMRIGHT);
        if (left) return new IntPtr(HTLEFT);
        if (right) return new IntPtr(HTRIGHT);
        if (top) return new IntPtr(HTTOP);
        if (bottom) return new IntPtr(HTBOTTOM);

        return IntPtr.Zero;
    }

    public void EnableFocusPrevention(bool enable)
    {
        _focusPreventionEnabled = enable;
        if (enable)
        {
            SetWindowLong(new WindowInteropHelper(this).Handle, GWL_EXSTYLE, GetWindowLong(new WindowInteropHelper(this).Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        }
        else
        {
            SetWindowLong(new WindowInteropHelper(this).Handle, GWL_EXSTYLE, GetWindowLong(new WindowInteropHelper(this).Handle, GWL_EXSTYLE) & ~WS_EX_NOACTIVATE);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private const int SW_SHOWNOACTIVATE = 4;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SMTO_NORMAL = 0x0000;
    private const uint WM_SPAWN_WORKERW = 0x052C;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public void ShowWithoutActivation()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }

    public void PinForShowDesktop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        GetWindowRect(hwnd, out RECT rect);

        if (!_isDesktopChild)
        {
            _originalParent = GetParent(hwnd);
        }
        else
        {
            SetParent(hwnd, IntPtr.Zero);
            _isDesktopChild = false;
            _originalParent = IntPtr.Zero;
        }

        int style = GetWindowLong(hwnd, GWL_STYLE);
        SetWindowLong(hwnd, GWL_STYLE, (style & ~WS_CHILD) | WS_POPUP);

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_APPWINDOW & ~0x00000008) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        Topmost = false;
        SetWindowPos(hwnd, HWND_NOTOPMOST, rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top), SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    public void ReleaseShowDesktopPin()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        GetWindowRect(hwnd, out RECT rect);

        if (_isDesktopChild)
        {
            SetParent(hwnd, _originalParent);
            _isDesktopChild = false;
            _originalParent = IntPtr.Zero;
        }

        int style = GetWindowLong(hwnd, GWL_STYLE);
        SetWindowLong(hwnd, GWL_STYLE, (style & ~WS_CHILD) | WS_POPUP);

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_TOOLWINDOW & ~0x00000008) | WS_EX_NOACTIVATE);

        Topmost = false;
        SetWindowPos(hwnd, HWND_NOTOPMOST, rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top), SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void BeginKeyboardInteractiveEdit(UIElement targetElement)
    {
        EnableFocusPrevention(false);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        IntPtr foregroundHwnd = GetForegroundWindow();

        if (foregroundHwnd != hwnd && foregroundHwnd != IntPtr.Zero)
        {
            uint foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
            uint currentThread = GetCurrentThreadId();

            if (foregroundThread != currentThread)
            {
                AttachThreadInput(currentThread, foregroundThread, true);
                SetForegroundWindow(hwnd);
                AttachThreadInput(currentThread, foregroundThread, false);
            }
            else
            {
                SetForegroundWindow(hwnd);
            }
        }
        else
        {
            SetForegroundWindow(hwnd);
        }

        // Deferred focus to ensure the OS has actually switched foreground windows
        this.Dispatcher.BeginInvoke(new Action(() =>
        {
            targetElement.Focus();
            if (targetElement is System.Windows.Controls.TextBox tb) tb.SelectAll();
            else if (targetElement is System.Windows.Controls.ComboBox cb)
            {
                var innerTextBox = (System.Windows.Controls.TextBox)cb.Template.FindName("PART_EditableTextBox", cb);
                innerTextBox?.Focus();
                innerTextBox?.SelectAll();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void EndKeyboardInteractiveEdit()
    {
        EnableFocusPrevention(true);
        System.Windows.Input.Keyboard.ClearFocus();
    }

    // =========================================================
    // IDLE FADE-OUT ENGINE
    // =========================================================

    public void SetupIdleTimer()
    {
        if (_idleTimer == null)
        {
            _idleTimer = new System.Windows.Threading.DispatcherTimer();
            _idleTimer.Tick += (s, ev) => ExecuteIdleFadeOut();

            this.MouseEnter += (s, ev) => ResetIdleTimer(true);
            this.MouseLeave += (s, ev) => ResetIdleTimer(false);
            this.MouseMove += (s, ev) => ResetIdleTimer(true);
        }

        RefreshIdleSettings();
    }

    public void RefreshIdleSettings()
    {
        if (_idleTimer == null) return;

        if (SettingsManager.FramesFadeOutFx)
        {
            _idleTimer.Interval = TimeSpan.FromSeconds(SettingsManager.FadeOutTime);
            _idleTimer.Start();
        }
        else
        {
            _idleTimer.Stop();
            RestoreOpacity();
        }
    }

    private void ResetIdleTimer(bool isMouseInside)
    {
        if (!SettingsManager.FramesFadeOutFx) return;

        _idleTimer.Stop();

        if (isMouseInside)
        {
            RestoreOpacity();
        }
        else
        {
            _idleTimer.Start();
        }
    }

    private void ExecuteIdleFadeOut()
    {
        _idleTimer.Stop();

        // --- BUG FIX: Prevent fading if the mouse is currently resting on the frame ---
        if (this.IsMouseOver)
        {
            _idleTimer.Start(); // Restart the countdown and check again later
            return;
        }

        if (!SettingsManager.FramesFadeOutFx || _isIdleFaded || this.Visibility != Visibility.Visible || this.Opacity == 0.0) return;

        _isIdleFaded = true;

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = SettingsManager.FadeOutFxTargetAlpha,
            Duration = TimeSpan.FromMilliseconds(400)
        };

        this.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    public void TriggerWakeUpIdleReset()
    {
        _isIdleFaded = false; // Reset the state since the global manager is forcing opacity to 1.0

        if (SettingsManager.FramesFadeOutFx && _idleTimer != null)
        {
            _idleTimer.Stop();
            _idleTimer.Start(); // Restart the countdown automatically
        }
    }

    private void RestoreOpacity()
    {
        if (!_isIdleFaded)
        {
            if (SettingsManager.FramesFadeOutFx) _idleTimer.Start();
            return;
        }

        _isIdleFaded = false;

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200)
        };

        this.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        if (SettingsManager.FramesFadeOutFx) _idleTimer.Start();
    }

}
