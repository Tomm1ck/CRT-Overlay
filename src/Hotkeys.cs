using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CrtOverlay;

internal sealed class Hotkeys : IDisposable
{
    public const int Quit = 1, Toggle = 2, Reload = 3, Preset = 4, Editor = 5;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private readonly nint hwnd;
    private uint registered;
    public Hotkeys(nint hwnd)
    {
        this.hwnd = hwnd;
        try
        {
            Register(Quit, 0x51, "Q"); Register(Toggle, 0x7A, "F11");
            Register(Reload, 0x74, "F5"); Register(Preset, 0x79, "F10"); Register(Editor, 0x45, "E");
        }
        catch { Dispose(); throw; }
    }
    private void Register(int id, uint key, string keyName)
    {
        // Ctrl + Alt + Shift + MOD_NOREPEAT. All actions remain available in the tray;
        // Stop.bat also provides an exit independent of global hotkey registration.
        if (Native.RegisterHotKey(hwnd, id, 0x4007, key))
        {
            registered |= 1u << id;
            return;
        }
        int error = Marshal.GetLastWin32Error();
        string combination = "Ctrl+Alt+Shift+" + keyName;
        if (error == ErrorHotkeyAlreadyRegistered)
        {
            Program.Log(L.F("Сочетание {0} занято другой программой и отключено в этом сеансе. Действие доступно из трея; выход — также через Stop.bat.", combination));
            return;
        }
        throw new Win32Exception(error, L.F("Не удалось зарегистрировать {0}. Код {1}: {2}", combination, error, new Win32Exception(error).Message));
    }
    public void Dispose()
    {
        for (int id = Quit; id <= Editor; id++)
            if ((registered & (1u << id)) != 0) Native.UnregisterHotKey(hwnd, id);
        registered = 0;
    }
}
