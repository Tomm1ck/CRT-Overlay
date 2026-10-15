using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CrtOverlay;

internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "CrtOverlay.Main";
    private readonly Native.WndProc callback;
    private readonly nint instance;
    private bool visible;
    public nint Handle { get; private set; }
    public bool RebuildRequested { get; set; }
    public bool ToggleRequested { get; set; }
    public bool ReloadRequested { get; set; }
    public bool EditorRequested { get; set; }
    public int PresetRequested { get; set; } = -2;

    public OverlayWindow(AppIcons icons)
    {
        callback = WindowProc;
        instance = Native.GetModuleHandleW(null);
        var wc = new Native.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
            Instance = instance,
            ClassName = ClassName,
            Procedure = Marshal.GetFunctionPointerForDelegate(callback), Icon = icons.Large, SmallIcon = icons.Small
        };
        if (Native.RegisterClassExW(ref wc) == 0) throw new Win32Exception();
        try
        {
            // TOPMOST | TOOLWINDOW | LAYERED | TRANSPARENT | NOACTIVATE.
            // LAYERED+TRANSPARENT пропускает мышь и в окна других потоков.
            Handle = Native.CreateWindowExW(0x080800A8, ClassName, "CRT Overlay",
                0x80000000, 0, 0, 1, 1, 0, 0, instance, 0);
            if (Handle == 0) throw new Win32Exception();
            // Initialize the layered window's redirection before asking DWM to exclude it.
            // The HWND is still hidden: no capture or presentation can occur here.
            if (!Native.SetLayeredWindowAttributes(Handle, 0, 255, 2)) throw new Win32Exception();
            CaptureExclusion.Require(Handle, "overlay");
        }
        catch { Dispose(); throw; }
    }

    public void Place(int x, int y, int width, int height)
    {
        if (!Native.SetWindowPos(Handle, new nint(-1), x, y, width, height, 0x0010))
            throw new Win32Exception();
    }

    public void SetVisible(bool value)
    {
        if (value == visible) return;
        // SetWindowPos не зависит от STARTUPINFO.wShowWindow, в отличие от первого ShowWindow.
        // NOMOVE | NOSIZE | NOACTIVATE | SHOWWINDOW/HIDEWINDOW.
        if (!Native.SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0, (uint)(0x13 | (value ? 0x40 : 0x80))))
            throw new Win32Exception();
        visible = value;
    }

    public bool Pump()
    {
        while (Native.PeekMessageW(out Native.Message msg, 0, 0, 0, 1))
        {
            if (msg.Id == 0x0012) return false;
            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }
        return true;
    }

    public void Wait(uint milliseconds) => Native.MsgWaitForMultipleObjectsEx(0, 0, milliseconds, 0x04FF, 4);

    private nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case 0x0084: return -1; // HTTRANSPARENT; основной click-through обеспечивают стили.
            case 0x0021: return 3;  // MA_NOACTIVATE.
            case 0x0014: return 1;  // Не затирать кадр GDI-фоном.
            case 0x007E:           // WM_DISPLAYCHANGE.
            case 0x02E0: RebuildRequested = true; return 0; // WM_DPICHANGED.
            case 0x0312:
                if (wParam == Hotkeys.Quit) Native.PostQuitMessage(0);
                if (wParam == Hotkeys.Toggle) ToggleRequested = true;
                if (wParam == Hotkeys.Reload) ReloadRequested = true;
                if (wParam == Hotkeys.Preset) PresetRequested = -1;
                if (wParam == Hotkeys.Editor) EditorRequested = true;
                return 0;
            case 0x8004: EditorRequested = true; return 0;
            case 0x8005: L.SetLanguage(wParam == 1 ? "en" : "ru"); return 0;
            case 0x0010: Native.PostQuitMessage(0); return 0;
            case 0x0002: Native.PostQuitMessage(0); return 0;
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle != 0) { Native.DestroyWindow(Handle); Handle = 0; }
        Native.UnregisterClassW(ClassName, instance);
        GC.KeepAlive(callback);
    }
}

internal static class Native
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Hwnd;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassExW(ref WindowClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool UnregisterClassW(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool PeekMessageW(out Message msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message msg);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(ref Message msg);
    [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("dwmapi.dll")] internal static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint MsgWaitForMultipleObjectsEx(uint count, nint handles, uint milliseconds, uint wakeMask, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AllocConsole();
    [DllImport("kernel32.dll")] internal static extern nint GetStdHandle(int kind);
    [DllImport("kernel32.dll")] internal static extern bool SetStdHandle(int kind, nint handle);
    [DllImport("kernel32.dll")] internal static extern uint GetFileType(nint handle);
    [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint period);
}
