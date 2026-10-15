using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CrtOverlay;

internal sealed unsafe class CursorOverlay : IDisposable
{
    private const int MaxSize = 512, Stride = MaxSize * 4, Capacity = Stride * MaxSize * 2;
    private readonly nint raw = (nint)NativeMemory.AllocZeroed(Capacity);
    private readonly nint pixels = (nint)NativeMemory.AllocZeroed(Stride * MaxSize);
    private readonly nint mask = (nint)NativeMemory.AllocZeroed(Capacity);
    private ID3D11Texture2D texture = null!;
    public ID3D11ShaderResourceView View { get; private set; } = null!;
    private readonly int left, top, screenWidth, screenHeight;
    private nint lastCursor;
    private uint shapeWidth, shapeHeight, shapeType;
    private int hotX, hotY, dupX, dupY;
    private bool dupVisible, dupShapeAvailable, usingDup;
    public bool Visible { get; private set; }
    public bool WantsHidden { get; private set; }
    public Vector4 Rectangle { get; private set; }
    public float Type => shapeType;

    public CursorOverlay(ID3D11Device device, int left, int top, int width, int height)
    {
        this.left = left; this.top = top; screenWidth = width; screenHeight = height;
        try
        {
            texture = device.CreateTexture2D(new Texture2DDescription
            {
                Width = MaxSize,
                Height = MaxSize,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource
            });
            View = device.CreateShaderResourceView(texture);
        }
        catch { Dispose(); throw; }
    }
    public void ReadDuplication(IDXGIOutputDuplication duplication, in OutduplFrameInfo frame, ID3D11DeviceContext context)
    {
        if (frame.LastMouseUpdateTime != 0)
        {
            dupVisible = frame.PointerPosition.Visible;
            dupX = frame.PointerPosition.Position.X; dupY = frame.PointerPosition.Position.Y;
        }
        if (frame.PointerShapeBufferSize == 0) return;
        if (frame.PointerShapeBufferSize > Capacity) { dupShapeAvailable = false; return; }
        duplication.GetFramePointerShape(Capacity, raw, out _, out var info).CheckError();
        uint h = info.Type == 1 ? info.Height / 2 : info.Height;
        if (info.Width > MaxSize || h > MaxSize || info.Width == 0 || h == 0) { dupShapeAvailable = false; return; }
        DecodeDuplication(raw, pixels, Stride, info);
        shapeWidth = info.Width; shapeHeight = h; shapeType = info.Type;
        hotX = info.HotSpot.X; hotY = info.HotSpot.Y;
        dupShapeAvailable = usingDup = true; lastCursor = 0;
        Upload(context);
    }
    internal static void DecodeDuplication(nint source, nint destination, int stride, OutduplPointerShapeInfo info)
    {
        byte* src = (byte*)source; byte* dst = (byte*)destination;
        uint height = info.Type == 1 ? info.Height / 2 : info.Height;
        for (uint y = 0; y < height; y++) for (uint x = 0; x < info.Width; x++)
            {
                uint* pixel = (uint*)(dst + y * stride + x * 4);
                if (info.Type == 1)
                {
                    uint bit = 0x80u >> (int)(x & 7);
                    uint andMask = (src[y * info.Pitch + x / 8] & bit) != 0 ? 0xff000000u : 0;
                    uint xorMask = (src[(y + height) * info.Pitch + x / 8] & bit) != 0 ? 0x00ffffffu : 0;
                    *pixel = andMask | xorMask;
                }
                else *pixel = *(uint*)(src + y * info.Pitch + x * 4);
            }
    }
    public void Update(ID3D11DeviceContext context, bool capture, bool hidden)
    {
        var info = new CursorNative.CursorInfo { Size = (uint)Marshal.SizeOf<CursorNative.CursorInfo>() };
        if (!capture || !CursorNative.GetCursorInfo(ref info)) { Visible = WantsHidden = false; return; }
        bool onMonitor = info.X >= left && info.X < left + screenWidth && info.Y >= top && info.Y < top + screenHeight;
        bool showing = (info.Flags & 1) != 0;
        if ((hidden || !dupVisible || !dupShapeAvailable) && (info.Cursor != lastCursor || usingDup))
        {
            if (!ReadWin32(info.Cursor, context)) { Visible = WantsHidden = false; return; }
            lastCursor = info.Cursor; usingDup = false;
        }
        WantsHidden = showing && onMonitor && shapeWidth > 0;
        Visible = WantsHidden && (hidden || dupVisible);
        // В DXGI Position уже означает верхний левый угол; GetCursorInfo возвращает hotspot.
        Rectangle = usingDup && !hidden ? new(dupX, dupY, shapeWidth, shapeHeight)
            : new(info.X - left - hotX, info.Y - top - hotY, shapeWidth, shapeHeight);
    }
    private bool ReadWin32(nint handle, ID3D11DeviceContext context)
    {
        if (handle == 0 || !CursorNative.GetIconInfo(handle, out var icon)) return false;
        nint dc = CursorNative.GetDC(0);
        try
        {
            nint bitmap = icon.Color != 0 ? icon.Color : icon.Mask;
            if (CursorNative.GetObjectW(bitmap, Marshal.SizeOf<CursorNative.Bitmap>(), out var bm) == 0) return false;
            int width = bm.Width, height = icon.Color != 0 ? bm.Height : bm.Height / 2;
            if (width < 1 || height < 1 || width > MaxSize || height > MaxSize) return false;
            if (!ReadBitmap(dc, icon.Mask, width, icon.Color != 0 ? height : height * 2, mask)) return false;
            bool alpha = false;
            if (icon.Color != 0)
            {
                if (!ReadBitmap(dc, icon.Color, width, height, raw)) return false;
                for (int i = 0; i < width * height; i++) alpha |= (((uint*)raw)[i] & 0xff000000) != 0;
            }
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    uint andMask = (((uint*)mask)[y * width + x] & 255) != 0 ? 0xff000000u : 0;
                    uint color = icon.Color != 0 ? ((uint*)raw)[y * width + x] : ((uint*)mask)[(y + height) * width + x] & 0xffffff;
                    if (alpha)
                    {
                        // Альфа-иконки GDI хранят premultiplied BGRA; шейдер ожидает straight alpha.
                        uint a = color >> 24;
                        if (a > 0 && a < 255)
                            color = (a << 24) | (Math.Min(255, ((color >> 16) & 255) * 255 / a) << 16)
                                | (Math.Min(255, ((color >> 8) & 255) * 255 / a) << 8) | Math.Min(255, (color & 255) * 255 / a);
                    }
                    else color = (color & 0xffffff) | andMask;
                    ((uint*)pixels)[y * MaxSize + x] = color;
                }
            shapeWidth = (uint)width; shapeHeight = (uint)height; shapeType = alpha ? 2u : icon.Color != 0 ? 4u : 1u;
            hotX = (int)icon.HotspotX; hotY = (int)icon.HotspotY; Upload(context); return true;
        }
        finally
        {
            if (icon.Color != 0) CursorNative.DeleteObject(icon.Color);
            if (icon.Mask != 0) CursorNative.DeleteObject(icon.Mask);
            CursorNative.ReleaseDC(0, dc);
        }
    }
    private static bool ReadBitmap(nint dc, nint bitmap, int width, int height, nint destination)
    {
        var header = new CursorNative.BitmapInfoHeader
        {
            Size = 40,
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            SizeImage = (uint)(width * height * 4)
        };
        return CursorNative.GetDIBits(dc, bitmap, 0, (uint)height, destination, ref header, 0) == height;
    }
    private void Upload(ID3D11DeviceContext context) => context.UpdateSubresource(texture, 0, null, pixels, Stride, 0);
    public void Dispose()
    {
        View?.Dispose(); texture?.Dispose(); NativeMemory.Free((void*)raw); NativeMemory.Free((void*)pixels); NativeMemory.Free((void*)mask);
    }
}

internal sealed class CursorVisibility : IDisposable
{
    private bool initialized, guardReady;
    public bool Hidden { get; private set; }
    public bool Available => initialized;
    public CursorVisibility(bool enabled)
    {
        if (enabled) initialized = CursorNative.MagInitialize();
    }
    public void SetHidden(bool hidden)
    {
        if (!initialized || hidden == Hidden) return;
        if (hidden && !guardReady)
        {
            string name = $"Local\\CrtOverlay.CursorGuard.{Environment.ProcessId}";
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, name);
            string executable = Environment.ProcessPath!;
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "crt.dll"));
            start.ArgumentList.Add("--cursor-guard"); start.ArgumentList.Add(Environment.ProcessId.ToString()); start.ArgumentList.Add(name);
            using var process = Process.Start(start);
            guardReady = process != null && ready.WaitOne(3000);
            if (!guardReady) { Program.Log(L.T("Не запущена страховка курсора; оставлен системный курсор.")); initialized = false; CursorNative.MagUninitialize(); return; }
            ready.Reset();
        }
        if (CursorNative.MagShowSystemCursor(!hidden)) Hidden = hidden;
        else if (hidden)
        {
            CursorNative.MagShowSystemCursor(true); CursorNative.MagUninitialize(); initialized = false; Hidden = false;
            Program.Log(L.T("MagShowSystemCursor недоступен; оставлен системный курсор."));
        }
    }
    public static int RunGuard(string[] args)
    {
        EventWaitHandle? ready = null;
        try
        {
            using var parent = Process.GetProcessById(int.Parse(args[1]));
            ready = EventWaitHandle.OpenExisting(args[2]); ready.Set();
            parent.WaitForExit();
        }
        catch (ArgumentException) { }
        catch (WaitHandleCannotBeOpenedException) { }
        finally
        {
            if (CursorNative.MagInitialize()) { CursorNative.MagShowSystemCursor(true); CursorNative.MagUninitialize(); }
            ready?.Set(); ready?.Dispose();
        }
        return 0;
    }
    public static void WaitForGuard(int pid)
    {
        try { using var ready = EventWaitHandle.OpenExisting($"Local\\CrtOverlay.CursorGuard.{pid}"); ready.WaitOne(5000); }
        catch (WaitHandleCannotBeOpenedException) { }
    }
    public void Dispose()
    {
        if (initialized) { CursorNative.MagShowSystemCursor(true); CursorNative.MagUninitialize(); Hidden = false; }
    }
}

internal static class CursorNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct CursorInfo { public uint Size, Flags; public nint Cursor; public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct IconInfo { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] internal struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important; }
    [DllImport("Magnification.dll")] internal static extern bool MagInitialize();
    [DllImport("Magnification.dll")] internal static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] internal static extern bool MagShowSystemCursor(bool show);
    [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] internal static extern bool GetIconInfo(nint cursor, out IconInfo info);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint bitmap);
    [DllImport("gdi32.dll")] internal static extern int GetObjectW(nint obj, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] internal static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, nint bits, ref BitmapInfoHeader info, uint usage);
}
