using System.Reflection;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace CrtOverlay;

internal sealed class AppIcons : IDisposable
{
    public nint Large { get; private set; }
    public nint Small { get; private set; }
    public nint Paused { get; private set; }
    public AppIcons()
    {
        try { Large = Load("crt.ico", 32); Small = Load("crt.ico", 20); Paused = Load("crt-paused.ico", 20); }
        catch { Dispose(); throw; }
    }
    private static unsafe nint Load(string name, int size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CrtOverlay.Assets." + name)!;
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); reader.ReadUInt16(); int count = reader.ReadUInt16();
        int bestDistance = int.MaxValue, length = 0, offset = 0;
        for (int i = 0; i < count; i++)
        {
            int width = reader.ReadByte(); if (width == 0) width = 256;
            reader.ReadBytes(7); int bytes = reader.ReadInt32(), start = reader.ReadInt32();
            int distance = Math.Abs(width - size);
            if (distance < bestDistance) { bestDistance = distance; length = bytes; offset = start; }
        }
        stream.Position = offset; byte[] image = reader.ReadBytes(length);
        fixed (byte* p = image)
        {
            nint icon = CreateIconFromResourceEx(p, (uint)length, true, 0x00030000, size, size, 0);
            if (icon == 0) throw new Win32Exception();
            return icon;
        }
    }
    public void Dispose()
    {
        if (Large != 0) DestroyIcon(Large); if (Small != 0) DestroyIcon(Small); if (Paused != 0) DestroyIcon(Paused);
        Large = Small = Paused = 0;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern unsafe nint CreateIconFromResourceEx(byte* bits, uint size, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
