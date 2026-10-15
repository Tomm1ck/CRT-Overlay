using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CrtOverlay;

internal sealed unsafe class ScreenCapture : IDisposable
{
    private IDXGIOutputDuplication duplication = null!;
    private ID3D11Texture2D texture = null!;
    // Одна заимствованная обёртка вместо new COM-wrapper на каждом кадре.
    private readonly ID3D11Texture2D borrowed = new(0);
    private static readonly Guid TextureId = typeof(ID3D11Texture2D).GUID;
    public ID3D11ShaderResourceView View { get; private set; } = null!;
    public bool HasFrame { get; private set; }
    public double RefreshRateHz { get; private set; } = 60;
    public CursorOverlay Cursor { get; private set; } = null!;
    private uint width, height;

    public ScreenCapture(ID3D11Device device, IDXGIOutput1 output)
    {
        try
        {
            duplication = output.DuplicateOutput(device);
            var mode = duplication.Description.ModeDescription;
            // Используем рациональную частоту: 60000/1001 не округляется до целых 60.
            if (mode.RefreshRate.Denominator != 0 && mode.RefreshRate.Numerator != 0)
                RefreshRateHz = Math.Clamp((double)mode.RefreshRate.Numerator / mode.RefreshRate.Denominator, 24, 1000);
            width = mode.Width; height = mode.Height;
            texture = device.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource
            });
            View = device.CreateShaderResourceView(texture);
            var rect = output.Description.DesktopCoordinates;
            Cursor = new CursorOverlay(device, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        catch { Dispose(); throw; }
    }

    public bool Update(ID3D11DeviceContext context)
    {
        nint resource = 0, source = 0;
        OutduplFrameInfo info = default;
        nint self = duplication.NativePointer;
        // IDXGIOutputDuplication: IUnknown(3), IDXGIObject(4), GetDesc, AcquireNextFrame.
        // Стабильный ABI Windows SDK, слот 8. Обычный Vortice AcquireNextFrame создаёт
        // IDXGIResource на каждом успешном вызове; здесь кадры не создают managed-объекты.
        var acquire = (delegate* unmanaged[Stdcall]<nint, uint, OutduplFrameInfo*, nint*, int>)(*(nint**)self)[8];
        int hr = acquire(self, 0, &info, &resource);
        if (hr == unchecked((int)0x887A0027)) return false; // WAIT_TIMEOUT: сохраняем последнюю текстуру.
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            Cursor.ReadDuplication(duplication, in info, context);
            Guid iid = TextureId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(resource, in iid, out source));
            borrowed.NativePointer = source;
            var desc = borrowed.Description;
            if (desc.Width != width || desc.Height != height || desc.Format != Format.B8G8R8A8_UNorm)
                throw new COMException(L.T("Изменился формат/размер desktop surface."), unchecked((int)0x887A0026));
            // Единственная копия кадра — GPU→GPU. Нет staging, Map и CPU-readback.
            context.CopyResource(texture, borrowed);
            HasFrame = true;
            return true;
        }
        finally
        {
            borrowed.NativePointer = 0; // Dispose этой обёртки не должен освобождать заимствованный pointer.
            if (source != 0) Marshal.Release(source);
            if (resource != 0) Marshal.Release(resource);
            duplication.ReleaseFrame().CheckError(); // Всегда парно успешному AcquireNextFrame.
        }
    }

    public void Dispose()
    {
        Cursor?.Dispose();
        View?.Dispose();
        texture?.Dispose();
        duplication?.Dispose();
        borrowed.Dispose();
    }
}

