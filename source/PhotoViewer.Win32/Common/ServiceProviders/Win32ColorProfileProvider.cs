using Avalonia.Controls;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace PhotoViewer.Win32.Common.ServiceProviders;

public partial class Win32ColorProfileProvider : PhDisposable, IWindowColorProfileProvider
{
    private Window? _window;
    private nint _windowHandle;
    private nint _currentMonitor;

    const int WM_COLORSPACECHANGED = 0x0320;
    const int WM_DISPLAYCHANGE = 0x007E;

    public event TEventHandler<IWindowColorProfileProvider, ColorProfileChangedEventArgs>? Changed;

    public string ProfilePath { get; private set; } = string.Empty;

    public bool IsHdr { get; private set; } = false;

    public bool IsInitialized { get; private set; } = false;

    protected override void OnDisposing()
    {
        base.OnDisposing();

        if (TopLevel.GetTopLevel(_window) is TopLevel top)
        {
            Win32Properties.RemoveWndProcHookCallback(top, WndProcHook);
        }

        if (_window != null)
        {
            _window.PositionChanged -= OnWindowMoved;
        }

        ProfilePath = string.Empty;
        IsHdr = false;
        IsInitialized = false;
    }

    public void Initialize(Window window)
    {
        if (IsInitialized) return;

        IsInitialized = true;
        _window = window;
        _windowHandle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        if (_windowHandle == IntPtr.Zero) return;

        _currentMonitor = GetMonitorFromWindow(_windowHandle);

        UpdateColorProfile();

        _window.PositionChanged += OnWindowMoved;
        if (TopLevel.GetTopLevel(window) is TopLevel top)
        {
            Win32Properties.AddWndProcHookCallback(top, WndProcHook);
        }
    }

    public async Task<byte[]?> ReadColorProfileDataAsync()
    {
        if (string.IsNullOrEmpty(ProfilePath)) return null;

        var data = await File.ReadAllBytesAsync(ProfilePath);
        return data;
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_COLORSPACECHANGED || msg == WM_DISPLAYCHANGE)
        {
            UpdateColorProfile();

            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnWindowMoved(object? sender, PixelPointEventArgs e)
    {
        if (_windowHandle == IntPtr.Zero) return;

        var monitor = GetMonitorFromWindow(_windowHandle);
        if (monitor == _currentMonitor) return;
        _currentMonitor = monitor;

        UpdateColorProfile();
    }

    private void UpdateColorProfile()
    {
        ProfilePath = GetColorProfilePath(_currentMonitor);
        IsHdr = IsHdrEnabled(_currentMonitor);

        Changed?.Invoke(this, new ColorProfileChangedEventArgs(ProfilePath, IsHdr));
    }

    public static nint GetMonitorFromWindow(nint windowHandle)
    {
        var monitor = PInvoke.MonitorFromWindow(new HWND(windowHandle),
            MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

        return monitor;
    }

    public static unsafe string GetColorProfilePath(nint hMonitor)
    {
        try
        {
            var mi = new MONITORINFOEXW();
            mi.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo((HMONITOR)hMonitor, (MONITORINFO*)(void*)&mi)) return string.Empty;

            var deviceName = new string(mi.szDevice.AsSpan());
            var dc = PInvoke.CreateDCW(string.Empty, deviceName);
            if (dc.IsNull) return string.Empty;

            try
            {
                uint size = 0;
                _ = PInvoke.GetICMProfile(dc, ref size, null);

                var buffer = new char[size];
                _ = PInvoke.GetICMProfile(dc, ref size, buffer);

                var profilePath = new string(buffer, 0, (int)size - 1);
                if (!File.Exists(profilePath)) return string.Empty;

                return profilePath;
            }
            finally
            {
                PInvoke.DeleteDC(dc);
            }
        }
        catch { }

        return string.Empty;
    }

    public static unsafe bool IsHdrEnabled(nint hMonitor)
    {
        var monitorInfo = new MONITORINFOEXW();
        monitorInfo.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo((HMONITOR)hMonitor, (MONITORINFO*)(void*)&monitorInfo)) return false;

        var inputDeviceName = new string(monitorInfo.szDevice.AsSpan());

        uint pathCount, modeCount;
        if (PInvoke.GetDisplayConfigBufferSizes(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            &pathCount, &modeCount) != 0)
            return false;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            if (PInvoke.QueryDisplayConfig(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                &pathCount, pPaths, &modeCount, pModes, null) != 0)
                return false;

            for (uint i = 0; i < pathCount; i++)
            {
                var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME),
                        adapterId = paths[i].sourceInfo.adapterId,
                        id = paths[i].sourceInfo.id
                    }
                };

                if (PInvoke.DisplayConfigGetDeviceInfo(ref sourceName.header) != 0)
                    continue;

                var viewGdiDeviceName = new string(sourceName.viewGdiDeviceName.AsSpan());

                if (!viewGdiDeviceName.Equals(inputDeviceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var advancedColorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO),
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id
                    }
                };

                if (PInvoke.DisplayConfigGetDeviceInfo(ref advancedColorInfo.header) == 0)
                {
                    var isEnabled = advancedColorInfo.Anonymous.Anonymous.advancedColorEnabled;

                    return isEnabled;
                }
            }
        }

        return false;
    }

}
