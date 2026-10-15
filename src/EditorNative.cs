using System.Runtime.InteropServices;
using System.Text;

namespace CrtOverlay;

internal static class EditorNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct ScrollInfo { public uint Size, Mask; public int Min, Max; public uint Page; public int Pos, TrackPos; }
    [StructLayout(LayoutKind.Sequential)] internal struct CommonControls { public uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OpenFileName
    {
        public uint Size; public nint Owner, Instance;
        public string Filter; public nint CustomFilter; public uint MaxCustomFilter, FilterIndex;
        public nint File; public uint MaxFile; public nint FileTitle; public uint MaxFileTitle;
        public string? InitialDir, Title; public uint Flags; public ushort FileOffset, FileExtension;
        public string DefaultExtension; public nint CustomData, Hook; public string? Template;
        public nint Reserved; public uint Reserved2, FlagsEx;
    }
    [DllImport("user32.dll")] internal static extern int GetMessageW(out Native.Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool IsDialogMessageW(nint dialog, ref Native.Message message);
    [DllImport("user32.dll")] internal static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] internal static extern nint SendText(nint hwnd, uint msg, nint wParam, string text);
    [DllImport("user32.dll")] internal static extern bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetWindowTextW(nint hwnd, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextW(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool EnableWindow(nint hwnd, bool enabled);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] internal static extern int SetScrollInfo(nint hwnd, int bar, ref ScrollInfo info, bool redraw);
    [DllImport("user32.dll")] internal static extern bool GetScrollInfo(nint hwnd, int bar, ref ScrollInfo info);
    [DllImport("user32.dll")] internal static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll")] internal static extern nint GetLastActivePopup(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool EndDialog(nint hwnd, nint result);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] internal static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("comctl32.dll")] internal static extern bool InitCommonControlsEx(ref CommonControls controls);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetOpenFileNameW(ref OpenFileName name);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetSaveFileNameW(ref OpenFileName name);
    [DllImport("comdlg32.dll")] internal static extern uint CommDlgExtendedError();
}
