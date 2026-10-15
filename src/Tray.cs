using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CrtOverlay;

internal sealed class Tray : IDisposable
{
    private const string ClassName = "CrtOverlay.Tray";
    private const uint Callback = 0x8001;
    private readonly Native.WndProc callback;
    private readonly OverlayWindow overlay;
    private readonly CursorVisibility cursor;
    private readonly AppIcons icons;
    private readonly nint instance;
    private readonly uint taskbarCreated;
    private nint hwnd;
    private NotifyIcon data;
    private bool enabled = true;
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value; data.Icon = value ? icons.Small : icons.Paused;
            data.Tip = value ? L.T("CRT Overlay — эффект включён; двойной щелчок: редактор") : L.T("CRT Overlay — эффект выключен; двойной щелчок: редактор");
            if (hwnd != 0) Shell_NotifyIconW(1, ref data);
        }
    }
    public int PresetIndex { get; set; }

    public Tray(OverlayWindow overlay, CursorVisibility cursor, AppIcons icons)
    {
        this.overlay = overlay; this.cursor = cursor; this.icons = icons; callback = WindowProc; instance = Native.GetModuleHandleW(null);
        var wc = new Native.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
            Instance = instance,
            ClassName = ClassName,
            Procedure = Marshal.GetFunctionPointerForDelegate(callback)
        };
        if (Native.RegisterClassExW(ref wc) == 0) throw new Win32Exception();
        try
        {
            hwnd = Native.CreateWindowExW(0x80, ClassName, "CRT Overlay tray", 0, 0, 0, 0, 0, 0, 0, instance, 0);
            if (hwnd == 0) throw new Win32Exception();
            taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            data = new NotifyIcon
            {
                Size = (uint)Marshal.SizeOf<NotifyIcon>(),
                Window = hwnd,
                Id = 1,
                Flags = 7,
                Callback = Callback,
                Icon = icons.Small,
                Tip = L.T("CRT Overlay — эффект включён; двойной щелчок: редактор"),
                Info = "",
                InfoTitle = ""
            };
            if (!Shell_NotifyIconW(0, ref data)) throw new Win32Exception(L.T("Не удалось добавить значок в трей."));
            L.Changed += LanguageChanged;
        }
        catch { Dispose(); throw; }
    }
    private nint WindowProc(nint window, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            if (msg == L.ChangedMessage)
            {
                L.ApplyThreadCulture();
                data.Tip = Enabled ? L.T("CRT Overlay — эффект включён; двойной щелчок: редактор") : L.T("CRT Overlay — эффект выключен; двойной щелчок: редактор");
                Shell_NotifyIconW(1,ref data); return 0;
            }
            if (msg == 0x0010) { EndMenu(); Native.PostQuitMessage(0); return 0; }
            if (taskbarCreated != 0 && msg == taskbarCreated) { Shell_NotifyIconW(0, ref data); return 0; }
            if (msg == Callback)
            {
                uint mouse = (uint)((long)lParam & 0xffff);
                if (mouse == 0x0203) overlay.EditorRequested = true;
                if (mouse == 0x0205 || mouse == 0x007B) Menu();
                return 0;
            }
        }
        catch (Exception ex) { Program.Log(L.T("Ошибка трея: ") + ex.Message); }
        return Native.DefWindowProcW(window, msg, wParam, lParam);
    }
    private void Menu()
    {
        // TrackPopupMenu запускает модальный цикл Win32. Пока render-loop приостановлен,
        // показываем живой рабочий стол и системный курсор, а не замороженный CRT-кадр.
        cursor.SetHidden(false);
        overlay.SetVisible(false);
        nint menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            AppendMenuW(menu, Enabled ? 8u : 0u, 1, L.T("Эффект включён"));
            AppendMenuW(menu, 0, 4, L.T("Редактор пресетов…\tCtrl+Alt+Shift+E"));
            AppendMenuW(menu, 0x800, 0, null);
            for (int i = 0; i < CrtSettings.Names.Length; i++)
                AppendMenuW(menu, PresetIndex == i ? 8u : 0u, (nuint)(10 + i), L.T("Пресет: ") + L.Preset(CrtSettings.Names[i]));
            AppendMenuW(menu, 0x800, 0, null);
            AppendMenuW(menu, L.Language == "ru" ? 8u : 0u, 100, "Язык / Language: Русский");
            AppendMenuW(menu, L.Language == "en" ? 8u : 0u, 101, "Язык / Language: English");
            AppendMenuW(menu, 0x800, 0, null);
            AppendMenuW(menu, 0, 2, L.T("Перезагрузить шейдеры"));
            AppendMenuW(menu, 0, 3, L.T("Выход"));
            GetCursorPos(out var point); SetForegroundWindow(hwnd);
            uint selected = TrackPopupMenuEx(menu, 0x100 | 2, point.X, point.Y, hwnd, 0);
            PostMessageW(hwnd, 0, 0, 0);
            if (selected == 1) overlay.ToggleRequested = true;
            if (selected == 2) overlay.ReloadRequested = true;
            if (selected == 3) Native.PostQuitMessage(0);
            if (selected == 4) overlay.EditorRequested = true;
            if (selected is 100 or 101) L.SetLanguage(selected == 101 ? "en" : "ru");
            if (selected >= 10 && selected < 10 + CrtSettings.Names.Length) overlay.PresetRequested = (int)selected - 10;
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose()
    {
        L.Changed -= LanguageChanged;
        if (hwnd != 0) { Shell_NotifyIconW(2, ref data); Native.DestroyWindow(hwnd); hwnd = 0; }
        Native.UnregisterClassW(ClassName, instance); GC.KeepAlive(callback);
    }
    private void LanguageChanged() { if (hwnd != 0) PostMessageW(hwnd,L.ChangedMessage,0,0); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIcon
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint command, ref NotifyIcon data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern nint LoadIconW(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
}
