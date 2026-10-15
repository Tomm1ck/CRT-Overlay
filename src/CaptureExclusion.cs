using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CrtOverlay;

internal static class CaptureExclusion
{
    private const uint ExcludeFromCapture = 0x11;

    // Called before either window is shown. Never fall back to WDA_MONITOR:
    // a black capture rectangle cannot provide the desktop pixels needed by the overlay.
    public static void Require(nint window, string role)
    {
        int compositionResult = Native.DwmIsCompositionEnabled(out bool composition);
        Program.Log($"Capture exclusion ({role}): DWM HRESULT=0x{compositionResult:X8}, composition={composition}, remoteSession={Native.GetSystemMetrics(0x1000) != 0}.");
        if (!Native.SetWindowDisplayAffinity(window, ExcludeFromCapture))
        {
            int error = Marshal.GetLastWin32Error();
            string systemMessage = new Win32Exception(error).Message;
            Program.Log($"SetWindowDisplayAffinity({role}, 0x11) failed: Win32={error}; {systemMessage}");
            throw new Win32Exception(error, L.F("Windows не разрешила исключение окна из захвата ({0}). Код {1}: {2}. Подробности — в crt.log.", role, error, systemMessage));
        }
        if (!Native.GetWindowDisplayAffinity(window, out uint affinity))
        {
            int error = Marshal.GetLastWin32Error();
            Program.Log($"GetWindowDisplayAffinity({role}) failed: Win32={error}.");
            throw new Win32Exception(error, L.T("Не удалось проверить исключение окна из захвата. Подробности — в crt.log."));
        }
        if (affinity != ExcludeFromCapture)
        {
            Program.Log($"Capture exclusion ({role}): expected 0x11, actual 0x{affinity:X}.");
            throw new InvalidOperationException(L.T("Windows не подтвердила WDA_EXCLUDEFROMCAPTURE."));
        }
        Program.Log($"Capture exclusion ({role}): confirmed 0x11.");
    }
}
