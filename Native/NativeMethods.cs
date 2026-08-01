using System.Runtime.InteropServices;
using System.Text;

namespace Borderus.Native;

internal static class NativeMethods
{
    private static readonly object ConsoleLayoutLock = new();
    private static readonly Dictionary<nint, nint> ConsoleLayouts = new();
    private static nint _lastKnownLayout;

    internal const int GwlExStyle = -20;
    internal const int GwlHwndParent = -8;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExLayered = 0x00080000L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpNoCopyBits = 0x0100;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint GwOwner = 4;
    internal const uint GwHwndPrev = 3;
    internal const uint GwHwndNext = 2;
    internal const int DwmaExtendedFrameBounds = 9;
    internal const int DwmaCloaked = 14;
    internal const uint WineventOutOfContext = 0x0000;
    internal const uint WineventSkipOwnProcess = 0x0002;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventSystemMoveSizeStart = 0x000A;
    internal const uint EventSystemMoveSizeEnd = 0x000B;
    internal const uint EventObjectDestroy = 0x8001;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    internal const uint EventObjectFocus = 0x8005;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const int ObjidWindow = 0;
    internal const int ObjidCaret = -8;
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyUp = 0x0105;
    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint SpiGetKeyboardDelay = 0x0016;
    internal const uint SpiSetKeyboardDelay = 0x0017;
    internal const uint SpifUpdateIniFile = 0x0001;
    internal const uint SpifSendChange = 0x0002;
    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    internal const uint UlwAlpha = 0x00000002;
    internal const byte AcSrcOver = 0x00;
    internal const byte AcSrcAlpha = 0x01;
    internal const uint DibRgbColors = 0;
    internal const uint BiRgb = 0;
    private const uint MonitorDefaultToNearest = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    internal delegate void WinEventProc(nint hook, uint eventType, nint hWnd, int objectId, int childId, uint eventThread, uint eventTime);
    internal delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern nint SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint action, uint parameter, ref int value, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hWnd, uint command);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder className, int count);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);

    internal static int GetKeyboardLanguageId(nint foregroundWindow)
    {
        if (IsStandaloneConsoleWindow(foregroundWindow))
        {
            lock (ConsoleLayoutLock)
                return GetLanguageId(GetConsoleLayout(foregroundWindow));
        }

        nint layout = GetWindowKeyboardLayout(foregroundWindow);
        if (layout != 0)
        {
            lock (ConsoleLayoutLock) _lastKnownLayout = layout;
        }
        return GetLanguageId(layout);
    }

    internal static bool IsStandaloneConsoleWindow(nint window)
    {
        var className = new StringBuilder(32);
        return GetClassName(window, className, className.Capacity) > 0 &&
            className.ToString() == "ConsoleWindowClass";
    }

    internal static void CycleStandaloneConsoleKeyboardLayout(nint window, bool reverse)
    {
        if (!IsStandaloneConsoleWindow(window)) return;

        nint[] layouts = GetInstalledKeyboardLayouts();
        if (layouts.Length < 2) return;

        lock (ConsoleLayoutLock)
        {
            nint current = GetConsoleLayout(window);
            int currentIndex = Array.IndexOf(layouts, current);
            if (currentIndex < 0)
            {
                int language = GetLanguageId(current);
                currentIndex = Array.FindIndex(layouts, layout => GetLanguageId(layout) == language);
            }

            int nextIndex = currentIndex < 0
                ? (reverse ? layouts.Length - 1 : 0)
                : (currentIndex + (reverse ? -1 : 1) + layouts.Length) % layouts.Length;
            ConsoleLayouts[window] = layouts[nextIndex];
        }
    }

    private static nint GetWindowKeyboardLayout(nint foregroundWindow)
    {

        uint foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
        if (foregroundThread == 0) return 0;

        uint inputThread = foregroundThread;
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (GetGUIThreadInfo(foregroundThread, ref info) && info.FocusWindow != 0)
        {
            uint focusThread = GetWindowThreadProcessId(info.FocusWindow, out _);
            if (focusThread != 0) inputThread = focusThread;
        }

        nint layout = GetKeyboardLayout(inputThread);
        if (layout == 0 && inputThread != foregroundThread)
            layout = GetKeyboardLayout(foregroundThread);
        return layout;
    }

    private static nint GetConsoleLayout(nint window)
    {
        if (ConsoleLayouts.TryGetValue(window, out nint layout)) return layout;

        layout = _lastKnownLayout;
        if (layout == 0) layout = GetKeyboardLayout(0);
        if (layout == 0)
        {
            nint[] layouts = GetInstalledKeyboardLayouts();
            if (layouts.Length > 0) layout = layouts[0];
        }
        ConsoleLayouts[window] = layout;
        return layout;
    }

    private static nint[] GetInstalledKeyboardLayouts()
    {
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0) return [];

        var layouts = new nint[count];
        int actualCount = GetKeyboardLayoutList(layouts.Length, layouts);
        if (actualCount <= 0) return [];
        if (actualCount != layouts.Length) Array.Resize(ref layouts, actualCount);
        return layouts;
    }

    private static int GetLanguageId(nint layout) => (int)(layout.ToInt64() & 0xffff);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hWnd, ref NativePoint point);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint tokenHandle, int tokenInformationClass,
        out TokenElevationInfo tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint eventHookModule,
        WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(nint hWnd, nint destinationDc, ref NativePoint destination,
        ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo bitmapInfo, uint usage,
        out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint value);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hWnd, int attribute, out Rect rect, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hWnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hWnd, int attribute, ref int value, int size);

    internal static void SetDarkTitleBar(nint hWnd, bool enabled)
    {
        int value = enabled ? 1 : 0;
        if (DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(hWnd, 19, ref value, sizeof(int));
    }

    internal static bool TryGetFrameBounds(nint hWnd, out Rect rect)
    {
        return DwmGetWindowAttribute(hWnd, DwmaExtendedFrameBounds, out rect, Marshal.SizeOf<Rect>()) == 0
               && rect.Width > 0 && rect.Height > 0;
    }

    internal static bool IsCloaked(nint hWnd) =>
        DwmGetWindowAttribute(hWnd, DwmaCloaked, out int value, sizeof(int)) == 0 && value != 0;

    internal static bool IsFullscreenWindow(nint hWnd)
    {
        if (!GetWindowRect(hWnd, out Rect windowRect)) return false;

        nint monitor = MonitorFromWindow(hWnd, MonitorDefaultToNearest);
        if (monitor == 0) return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        return windowRect.Left <= info.Monitor.Left && windowRect.Top <= info.Monitor.Top
            && windowRect.Right >= info.Monitor.Right && windowRect.Bottom >= info.Monitor.Bottom;
    }

    internal static bool IsProcessElevated(nint hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint processId);
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return false;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out nint token)) return false;
            try
            {
                return GetTokenInformation(token, TokenElevation, out TokenElevationInfo elevation,
                    Marshal.SizeOf<TokenElevationInfo>(), out _) && elevation.IsElevated != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BlendFunction
    {
        public byte Operation;
        public byte Flags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public nint ActiveWindow;
        public nint FocusWindow;
        public nint CaptureWindow;
        public nint MenuOwner;
        public nint MoveSizeWindow;
        public nint CaretWindow;
        public Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelKeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevationInfo
    {
        public int IsElevated;
    }

}
