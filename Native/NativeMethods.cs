using System.Runtime.InteropServices;
using System.Text;

namespace Borderus.Native;

internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const int GwlHwndParent = -8;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoOwnerZOrder = 0x0200;
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
    internal const uint LlkhfInjected = 0x00000010;
    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint SpiGetKeyboardDelay = 0x0016;
    internal const uint SpiSetKeyboardDelay = 0x0017;
    internal const uint SpifUpdateIniFile = 0x0001;
    internal const uint SpifSendChange = 0x0002;
    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    internal delegate void WinEventProc(nint hook, uint eventType, nint hWnd, int objectId, int childId, uint eventThread, uint eventTime);
    internal delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

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

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetKeyboardLayout(uint threadId);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

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
