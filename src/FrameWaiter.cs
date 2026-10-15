using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CrtOverlay;

// Wait on the frame deadline, UI messages and exit without a busy-spin tail.
internal sealed class FrameWaiter : IDisposable
{
    private readonly EventWaitHandle stop;
    private readonly SafeWaitHandle timer;
    private bool usable;

    public FrameWaiter(EventWaitHandle stop)
    {
        this.stop = stop;
        // Auto-reset, high-resolution; only MODIFY_STATE and SYNCHRONIZE rights.
        timer = CreateWaitableTimerExW(0, null, 2, 0x00100002);
        bool highResolution = !timer.IsInvalid;
        if (!highResolution)
        {
            timer.Dispose();
            timer = CreateWaitableTimerExW(0, null, 0, 0x00100002);
        }
        usable = !timer.IsInvalid;
        Program.Log($"Frame pacing: {(highResolution ? "high-resolution timer" : usable ? "standard timer" : "message-wait fallback")}.");
    }

    public unsafe void WaitUntil(long deadline, OverlayWindow window)
    {
        long remaining = deadline - Stopwatch.GetTimestamp();
        if (remaining <= 0) return;
        if (usable)
        {
            // A negative due time is a relative delay in 100 ns units.
            long due = -Math.Max(1, (long)Math.Ceiling(remaining * (10_000_000.0 / Stopwatch.Frequency)));
            if (SetWaitableTimer(timer, in due, 0, 0, 0, false))
            {
                nint* handles = stackalloc nint[2] { timer.DangerousGetHandle(), stop.SafeWaitHandle.DangerousGetHandle() };
                uint result = Native.MsgWaitForMultipleObjectsEx(2, (nint)handles, uint.MaxValue, 0x04FF, 4);
                if (result != uint.MaxValue) return;
            }
            usable = false;
            Program.Log($"Frame timer wait failed (Win32={Marshal.GetLastWin32Error()}); using message waits.");
        }
        remaining = deadline - Stopwatch.GetTimestamp();
        if (remaining > 0)
            window.Wait((uint)Math.Clamp(Math.Ceiling(remaining * (1000.0 / Stopwatch.Frequency)), 1, 1000));
    }

    public void Dispose() => timer.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
