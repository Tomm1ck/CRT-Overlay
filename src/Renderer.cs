using System.Reflection;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DirectComposition;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace CrtOverlay;

internal sealed class Renderer : IDisposable
{
    private IDXGIFactory2 factory = null!;
    private IDXGIAdapter1 adapter = null!;
    private IDXGIOutput1 output = null!;
    private ID3D11Device device = null!;
    private ID3D11DeviceContext context = null!;
    private IDXGISwapChain1 swapChain = null!;
    private ID3D11RenderTargetView target = null!;
    private IDCompositionDevice composition = null!;
    private IDCompositionTarget compositionTarget = null!;
    private IDCompositionVisual visual = null!;
    private CrtPipeline pipeline = null!;
    private ScreenCapture capture = null!;
    private PerformanceLog? performance;
    private CrtSettings settings;
    private int rotation;
    private uint width, height;
    private PresentFlags presentFlags;
    public bool HasFrame => capture.HasFrame;
    public double RefreshRateHz => capture.RefreshRateHz;
    public bool WantsCursorHidden => capture.Cursor.WantsHidden;

    private Renderer(CrtSettings settings) => this.settings = settings;

    public static Renderer Create(OverlayWindow window, CrtSettings settings)
    {
        var renderer = new Renderer(settings);
        try { renderer.Initialize(window); return renderer; }
        catch { renderer.Dispose(); throw; }
    }

    private void Initialize(OverlayWindow window)
    {
        factory = CreateDXGIFactory1<IDXGIFactory2>();
        SelectOutput(settings.Monitor);
        var description = output.Description;
        var rect = description.DesktopCoordinates;
        width = (uint)(rect.Right - rect.Left); height = (uint)(rect.Bottom - rect.Top);
        rotation = (int)description.Rotation;
        window.Place(rect.Left, rect.Top, (int)width, (int)height);
        Program.Log($"Монитор {settings.Monitor}: {description.DeviceName}, {width}x{height}, rotation={rotation}.");
        using (var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>())
        {
            if (output6 != null && output6.Description1.ColorSpace != ColorSpaceType.RgbFullG22NoneP709)
            {
                string warning = L.T("HDR/расширенное цветовое пространство: отключите HDR в настройках Windows. Цвета SDR-захвата могут быть неверными.");
                Program.Log(warning);
                Native.MessageBoxW(window.Handle, warning, "CRT Overlay", 0x30);
            }
        }
        var flags = DeviceCreationFlags.BgraSupport;
        if (settings.DebugLayer) flags |= DeviceCreationFlags.Debug;
        var deviceResult = D3D11CreateDevice(adapter, DriverType.Unknown, flags, [FeatureLevel.Level_11_0],
            out device, out context);
        if (deviceResult.Code == unchecked((int)0x887A002D))
            throw new InvalidOperationException(L.T("D3D11 Debug Layer отсутствует. Уберите --debug-layer или установите дополнительный компонент Windows «Graphics Tools»."));
        deviceResult.CheckError();
        capture = new ScreenCapture(device, output);

        var swapFlags = SwapChainFlags.None;
        if (settings.Present == "tearing")
        {
            using var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>();
            if (factory5 == null || !factory5.PresentAllowTearing)
                throw new NotSupportedException(L.T("Драйвер не поддерживает ALLOW_TEARING. Используйте --present vsync или immediate."));
            swapFlags |= SwapChainFlags.AllowTearing;
            presentFlags = PresentFlags.AllowTearing;
        }
        swapChain = factory.CreateSwapChainForComposition(device, new SwapChainDescription1
        {
            Width = width,
            Height = height,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Premultiplied,
            Flags = swapFlags
        });
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice1>())
            dxgiDevice.MaximumFrameLatency = 1;
        using (var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0))
            target = device.CreateRenderTargetView(backBuffer);
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
        composition.CreateTargetForHwnd(window.Handle, true, out compositionTarget).CheckError();
        visual = composition.CreateVisual();
        visual.SetContent(swapChain).CheckError();
        compositionTarget.SetRoot(visual).CheckError();
        composition.Commit().CheckError();
        pipeline = new CrtPipeline(device, width, height, rotation, settings);
        if (settings.PerformanceLog) performance = new PerformanceLog(device);
        Program.Log($"D3D11 + DirectComposition готовы; {settings.Present}, sync={settings.SyncInterval}, fps limit={settings.Fps}.");
    }

    private void SelectOutput(int wanted)
    {
        int index = 0;
        for (uint a = 0; ; a++)
        {
            var adapterResult = factory.EnumAdapters1(a, out var candidateAdapter);
            if (adapterResult.Code == unchecked((int)0x887A0002)) break; // NOT_FOUND.
            adapterResult.CheckError();
            bool selected = false;
            try
            {
                for (uint o = 0; ; o++)
                {
                    var outputResult = candidateAdapter.EnumOutputs(o, out var candidateOutput);
                    if (outputResult.Code == unchecked((int)0x887A0002)) break;
                    outputResult.CheckError();
                    using (candidateOutput)
                    {
                        if (!candidateOutput.Description.AttachedToDesktop) continue;
                        if (index++ != wanted) continue;
                        output = candidateOutput.QueryInterface<IDXGIOutput1>();
                        adapter = candidateAdapter;
                        selected = true;
                        return;
                    }
                }
            }
            finally { if (!selected) candidateAdapter.Dispose(); }
        }
        // В том числе отключённый монитор: не показываем старое изображение, ждём возвращения.
        throw new COMException(L.F("Активный DXGI-монитор {0} отсутствует (найдено {1}).", wanted, index), unchecked((int)0x887A0022));
    }

    public void ReloadShaders() => pipeline.ReloadShaders();
    public void ResetHistory() => pipeline.ResetHistory();
    public void ApplySettings(CrtSettings value) { settings = value; pipeline.ApplySettings(value); }

    public bool Render(bool cursorHidden, bool editorOpen = false)
    {
        long started = performance != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        bool fresh = capture.Update(context);
        if (!capture.HasFrame) return fresh;
        capture.Cursor.Update(context, settings.Cursor == "capture" && !editorOpen, cursorHidden);
        performance?.Begin(context);
        pipeline.Render(context, target, capture);
        performance?.End(context);
        swapChain.Present(settings.SyncInterval, presentFlags).CheckError();
        performance?.Record(System.Diagnostics.Stopwatch.GetTimestamp() - started, fresh);
        return fresh;
    }
    public static bool IsRecoverable(Exception ex) => unchecked((uint)ex.HResult) is
        0x887A0026 or // ACCESS_LOST.
        0x887A0005 or // DEVICE_REMOVED.
        0x887A0006 or // DEVICE_HUNG.
        0x887A0007 or // DEVICE_RESET.
        0x887A0020 or // DRIVER_INTERNAL_ERROR.
        0x887A0022 or // NOT_CURRENTLY_AVAILABLE.
        0x887A0028 or // SESSION_DISCONNECTED.
        0x80070005;   // E_ACCESSDENIED: secure desktop/UAC.

    public void Dispose()
    {
        // Сначала разрываем ссылки DComp и context, затем освобождаем COM в обратном порядке.
        if (visual != null) visual.SetContent(null);
        if (compositionTarget != null) compositionTarget.SetRoot(null);
        composition?.Commit();
        context?.ClearState();
        context?.Flush();
        performance?.Dispose();
        pipeline?.Dispose();
        visual?.Dispose(); compositionTarget?.Dispose(); composition?.Dispose();
        target?.Dispose(); swapChain?.Dispose(); capture?.Dispose();
        context?.Dispose(); device?.Dispose(); output?.Dispose(); adapter?.Dispose(); factory?.Dispose();
    }
}

