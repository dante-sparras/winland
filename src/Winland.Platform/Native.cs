using System.Runtime.InteropServices;

namespace Winland.Platform;

internal static partial class Native
{
    internal const int WhCbt = 5;
    internal const int HcbtCreateWnd = 3;
    internal const int WhKeyboardLl = 13;
    internal const int WhMouseLl = 14;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmLButtonUp = 0x0202;
    internal const int WmLButtonDblClk = 0x0203;
    internal const int WmMouseWheel = 0x020A;
    internal const uint WmNcHitTest = 0x0084;
    internal const uint WmWindowPosChanging = 0x0046;
    internal const uint GaRoot = 2;
    internal const uint SmtoBlock = 0x0001;
    internal const int WmKeyDown = 0x0100;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmSysKeyUp = 0x0105;
    internal const int LlkhfInjected = 0x10;
    internal const int GwlStyle = -16;
    internal const int WsCaption = 0x00C00000;
    internal const long WsPopup = 0x80000000;
    internal const long WsThickFrame = 0x00040000;
    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x00000080;
    internal const long WsExDlgModalFrame = 0x00000001;
    internal const long WsExTopmost = 0x00000008;
    internal const int WsExAppWindow = 0x00040000;
    internal const int WsExNoActivate = 0x08000000;
    internal const long WsExLayered = 0x00080000;
    internal const uint LwaAlpha = 0x2;
    internal const int SwHide = 0;
    internal const int SwShow = 5;
    internal const int SwShowNa = 8;
    internal const int SwRestore = 9;
    internal static readonly nint HwndTopMost = -1;
    internal static readonly nint HwndNoTopMost = -2;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoSendChanging = 0x0400;
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint SwpNoCopyBits = 0x0100;
    internal const uint SwpFrameChanged = 0x0020;
    internal const int WmClose = 0x0010;
    internal const uint WmSysCommand = 0x0112;
    internal const int ScRestore = 0xF120;
    internal const uint GwHwndFirst = 0;
    internal const uint GwHwndNext = 2;
    internal const uint GwChild = 5;
    internal const uint RdwInvalidate = 0x0001;
    internal const uint WmGetMinMaxInfo = 0x0024;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const int SmCxMinTrack = 34;
    internal const int SmCyMinTrack = 35;
    internal const int DwmwaTransitionsForcedDisabled = 3;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int DwmwaCloak = 13;
    internal const int DwmwaCloaked = 14;
    internal const int DwmcCloakedShell = 2;
    internal const int DwmwaUseImmersiveDarkMode = 20;
    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwaBorderColor = 34;
    internal const int DwmwaCaptionColor = 35;
    internal const int DwmwaVisibleFrameBorderThickness = 37;
    internal const int SpiGetWorkArea = 0x0030;
    internal const int SpiGetAnimation = 0x0048;
    internal const int SpiSetAnimation = 0x0049;
    internal const int SpiGetClientAreaAnimation = 0x1042;
    internal const int SpiSetClientAreaAnimation = 0x1043;
    internal const int SpiGetForegroundLockTimeout = 0x2000;
    internal const int SpiSetForegroundLockTimeout = 0x2001;
    internal const uint InputKeyboard = 1;
    internal const uint KeyeventfExtendedKey = 0x0001;
    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint WmNull = 0;
    internal const uint WmQuit = 0x0012;
    internal const uint WmTimer = 0x0113;
    internal const uint WmSetCursor = 0x0020;
    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmMouseMove = 0x0200;
    internal const uint WmRButtonDown = 0x0204;
    internal const uint WmRButtonUp = 0x0205;
    internal const int MaNoActivate = 3;
    internal const uint WmReleaseSuper = 0x8001;
    internal const uint WmInput = 0x00FF;
    internal const uint PmRemove = 0x0001;
    internal const uint QsAllInput = 0x04FF;
    internal const uint RidInput = 0x10000003;
    internal const uint RimTypeKeyboard = 1;
    internal const uint RidevInputSink = 0x00000100;
    internal const uint RidevRemove = 0x00000001;
    internal const ushort RiKeyBreak = 0x0001;
    internal const uint EventSystemForeground = 0x0003;
    internal const ushort VkEscape = 0x1B;
    internal const uint WmWtsSessionChange = 0x02B1;
    internal const int WtsSessionLock = 0x7;
    internal const int WtsSessionUnlock = 0x8;
    internal const int NotifyForThisSession = 0;
    internal const int ErrorClassAlreadyExists = 1410;
    internal const int RegDword = 4;
    internal const int KeyQueryValue = 0x0001;
    internal const int KeySetValue = 0x0002;
    internal static readonly nint HwndMessage = -3;
    internal static readonly nint HkeyCurrentUser = new(unchecked((int)0x80000001));
    internal const int VkLWin = 0x5B;
    internal const int VkRWin = 0x5C;
    internal const int VkShift = 0x10;
    internal const int VkControl = 0x11;
    internal const int VkMenu = 0x12;
    internal const ushort VkF11 = 0x7A;
    internal const int VkLShift = 0xA0;
    internal const int VkRShift = 0xA1;
    internal const int VkLControl = 0xA2;
    internal const int VkRControl = 0xA3;
    internal const int VkLMenu = 0xA4;
    internal const int VkRMenu = 0xA5;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint TokenQuery = 0x0008;
    internal const int TokenElevation = 20;
    internal const int ErrorAccessDenied = 5;

    internal delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(nint hhk);

    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern bool GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [DllImport("user32.dll")]
    internal static extern bool KillTimer(nint hWnd, nuint uIDEvent);

    internal delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClass
    {
        public uint Style;
        public nint LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassW")]
    internal static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    internal static extern nint CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string? lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyWindow(nint hwnd);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSRegisterSessionNotification(nint hwnd, int flags);

    [DllImport("wtsapi32.dll")]
    internal static extern bool WTSUnRegisterSessionNotification(nint hwnd);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int RegOpenKeyEx(nint key, string subKey, int options, int sam, out nint result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int RegCreateKeyEx(
        nint key,
        string subKey,
        int reserved,
        string? className,
        int options,
        int sam,
        nint security,
        out nint result,
        out int disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegSetValueEx(nint key, string name, int reserved, int type, ref int data, int size);

    [DllImport("advapi32.dll")]
    internal static extern int RegCloseKey(nint key);

    /// <summary>
    /// Sets DisableLockWorkstation using only SetValue rights.
    /// The .NET registry key asks for CreateSubKey as well, and this key grants SetValue alone.
    /// </summary>
    /// <param name="disabled">True stops Windows locking, on Super+L and on <see cref="LockWorkStation"/> alike. False lets it lock.</param>
    /// <param name="error">Win32 error, or 0 when the value is set.</param>
    /// <returns>True when the value is set.</returns>
    internal static bool TryDisableLockWorkstation(bool disabled, out int error)
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        var sam = KeyQueryValue | KeySetValue;
        error = RegOpenKeyEx(HkeyCurrentUser, path, 0, sam, out var key);
        if (error != 0)
        {
            error = RegCreateKeyEx(HkeyCurrentUser, path, 0, null, 0, sam, 0, out key, out _);
            if (error != 0)
            {
                return false;
            }
        }

        try
        {
            var data = disabled ? 1 : 0;
            error = RegSetValueEx(key, "DisableLockWorkstation", 0, RegDword, ref data, sizeof(int));
            return error == 0;
        }
        finally
        {
            _ = RegCloseKey(key);
        }
    }

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    internal static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool PeekMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    internal static extern uint MsgWaitForMultipleObjects(uint nCount, nint pHandles, bool fWaitAll, uint dwMilliseconds, uint dwWakeMask);

    /// <summary>Returns when input is already waiting, even input an earlier peek saw and left. A hook call is never left waiting for the timeout.</summary>
    internal const uint MwmoInputAvailable = 0x0004;

    [DllImport("user32.dll")]
    internal static extern uint MsgWaitForMultipleObjectsEx(uint nCount, nint pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterRawInputDevices(RawInputDevice[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    internal static extern uint GetRawInputData(nint hRawInput, uint uiCommand, nint pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    /// <summary>Keeps a number on a window under a name. It stays when the process that set it exits, and goes with the window.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetPropW", SetLastError = true)]
    internal static extern bool SetProp(nint hWnd, string name, nint value);

    /// <summary>The number kept on a window under a name. Zero when there is none.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPropW")]
    internal static extern nint GetProp(nint hWnd, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RemovePropW", SetLastError = true)]
    internal static extern nint RemoveProp(nint hWnd, string name);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    internal static extern unsafe int GetWindowText(nint hWnd, char* lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    internal static extern unsafe int GetClassName(nint hWnd, char* lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    internal static extern int SetWindowRgn(nint hWnd, nint hRgn, bool redraw);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll")]
    internal static extern nint GetParent(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint item);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);

    internal const uint SrcCopy = 0x00CC0020;
    internal const uint CaptureBlt = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    internal const uint UlwOpaque = 0x00000004;
    internal const uint UlwAlpha = 0x00000002;
    internal const byte AcSrcAlpha = 1;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UpdateLayeredWindow(nint hwnd, nint destDc, ref Point destination, ref Size size, nint sourceDc, ref Point source, uint key, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool IsHungAppWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindowAsync(nint hwnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    internal static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    internal static extern uint TimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    internal static extern uint TimeEndPeriod(uint uPeriod);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    internal static extern nint BeginDeferWindowPos(int nNumWindows);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint DeferWindowPos(nint hWinPosInfo, nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern bool EndDeferWindowPos(nint hWinPosInfo);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint SetFocus(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint hWnd, ref Point lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    internal static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, ref MinMaxInfo lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    internal static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    internal static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    internal static extern bool GlobalUnlock(nint hMem);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool QueryFullProcessImageName(nint process, int flags, char[] name, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern unsafe bool QueryFullProcessImageName(nint process, int flags, char* name, ref int size);

    /// <summary>A process's times as 100-nanosecond counts. The creation time tells two processes that had the same id apart.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    /// <summary>Lists top-level windows, including the immersive ones <c>EnumWindows</c> skips.</summary>
    [DllImport("win32u.dll")]
    internal static extern int NtUserBuildHwndList(nint desktop, nint window, bool children, bool removeImmersive, uint thread, uint max, nint list, ref uint count);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool GetTokenInformation(nint token, int informationClass, out int elevation, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    /// <summary>
    /// True when the shell's cloak is on this window: Winoma hid it (<see cref="ShellCloak"/>), or Windows has it on another virtual desktop.
    /// Either way it is not drawn, and it keeps the keyboard it had. The shell's hide ledger holds the ones Winoma hid,
    /// and the shell asks Windows about a window on another virtual desktop.
    /// </summary>
    internal static bool ShellCloaked(nint hwnd) =>
        hwnd != 0
        && DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloak, sizeof(int)) == 0
        && (cloak & DwmcCloakedShell) != 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowEx(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    internal static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool RedrawWindow(nint hwnd, nint update, nint region, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfo(int uiAction, int uiParam, ref Rect pvParam, int fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    internal static extern bool SystemParametersInfo(int uiAction, int uiParam, ref int pvParam, int fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    internal static extern bool SystemParametersInfo(int uiAction, int uiParam, ref AnimationInfo pvParam, int fWinIni);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    internal static extern bool SystemParametersInfoFlag(int uiAction, int uiParam, nint pvParam, int fWinIni);

    [DllImport("user32.dll")]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    internal static extern nint SetCursor(nint cursor);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(nint hwnd, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool LockWorkStation();

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetWindowRect(nint hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    internal delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    internal static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    internal static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);

    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MsLlHook
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHook
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Ki;

        // Present so INPUT matches the native union (40 bytes on x64). A shorter
        // struct makes SendInput reject the buffer and drop the Super key-up.
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public nint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public nint DwExtraInfo;
    }
}
