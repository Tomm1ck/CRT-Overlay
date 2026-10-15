using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CrtOverlay;

internal sealed partial class CrtPipeline : IDisposable
{
    // Каждая строка точно совпадает с float4 в HLSL cbuffer; нет bool/неявного padding.
    [StructLayout(LayoutKind.Sequential)]
    private struct SettingsData
    {
        public Vector4 Geometry, Beam, Mask, Light, Color, Convergence, Finish, Temporal, Decay, Signal;
        public Vector4 MatrixR, MatrixG, MatrixB;
        public Vector4 Retro, RetroSignal, Rolling;
        public Vector4 Tube, TubeArea, TubeContent, SurfaceA, SurfaceB, TubeLight;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FrameData
    {
        public Vector4 Screen, Timing, Pointer, CursorData, Scan;
    }
    private enum PassId { Input, Retro, Colorize, Beam, Persistence, RollingDisplay, Threshold, Downsample, BlurX, BlurY, Glow, Geometry, Final }
    private sealed class Surface : IDisposable
    {
        public readonly uint Width, Height;
        public ID3D11Texture2D Texture = null!;
        public ID3D11RenderTargetView Target = null!;
        public ID3D11ShaderResourceView View = null!;
        public Surface(ID3D11Device device, uint width, uint height)
        {
            Width = width; Height = height;
            try
            {
                Texture = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    ArraySize = 1,
                    MipLevels = 1,
                    Format = Format.R16G16B16A16_Float,
                    SampleDescription = new(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
                });
                Target = device.CreateRenderTargetView(Texture); View = device.CreateShaderResourceView(Texture);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { View?.Dispose(); Target?.Dispose(); Texture?.Dispose(); }
    }
    private readonly ID3D11Device device;
    private readonly uint width, height;
    private readonly int rotation;
    private readonly List<Surface> owned = [];
    private readonly Surface[] history = new Surface[2], bloom = new Surface[5], scratch = new Surface[5];
    private Surface input = null!, colorized = null!, beam = null!, glow = null!, geometry = null!, exposure = null!;
    private Surface? retro;
    private ID3D11Buffer settingsBuffer = null!, frameBuffer = null!;
    private ID3D11SamplerState sampler = null!;
    private ID3D11VertexShader vertex = null!;
    private ID3D11PixelShader[] pixels = [];
    private CrtSettings settings;
    private SettingsData settingsData;
    private (string Gamut, float Temperature, TubeProfile? Tube, bool ReferenceWhite)? colorMatrixKey;
    private bool settingsDirty = true, resetHistory = true;
    private int historyIndex;
    private long lastTick, startTick, frame;

    public CrtPipeline(ID3D11Device device, uint width, uint height, int rotation, CrtSettings settings)
    {
        this.device = device; this.width = width; this.height = height; this.rotation = rotation; this.settings = settings;
        try
        {
            input = NewSurface(width, height); beam = NewSurface(width, height);
            colorized = NewSurface(width, height); exposure = NewSurface(width, height);
            glow = NewSurface(width, height); geometry = NewSurface(width, height);
            history[0] = NewSurface(width, height); history[1] = NewSurface(width, height);
            for (int i = 0; i < 5; i++)
            {
                uint w = Math.Max(1, width >> (i + 1)), h = Math.Max(1, height >> (i + 1));
                bloom[i] = NewSurface(w, h); scratch[i] = NewSurface(w, h);
            }
            settingsBuffer = device.CreateBuffer((uint)Marshal.SizeOf<SettingsData>(), BindFlags.ConstantBuffer);
            frameBuffer = device.CreateBuffer((uint)Marshal.SizeOf<FrameData>(), BindFlags.ConstantBuffer);
            sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
            ApplySettings(settings); ReloadShaders();
            startTick = lastTick = Stopwatch.GetTimestamp();
        }
        catch { Dispose(); throw; }
    }
    private Surface NewSurface(uint w, uint h) { var surface = new Surface(device, w, h); owned.Add(surface); return surface; }

    public void ApplySettings(CrtSettings value)
    {
        // Пересоздание маленького framebuffer происходит только при смене пресета/размера.
        // Сначала создаём замену, чтобы не потерять старую текстуру при ошибке устройства.
        var size = value.RetroResolution;
        if (value.ConsoleStyle != "none" && (retro == null || retro.Width != size.Width || retro.Height != size.Height))
        {
            var replacement = new Surface(device, (uint)size.Width, (uint)size.Height);
            retro?.Dispose(); retro = replacement;
        }
        var r = CrtSettings.Pair(value.ConvergenceR); var b = CrtSettings.Pair(value.ConvergenceB);
        // P22/P4: эффективные, растянутые для 144 Гц экспоненциальные времена,
        // а НЕ паспортные измерения конкретной трубки. Для измеренных tau — custom.
        // Красный хвост P22 медленнее синего; реальное затухание не строго экспоненциально.
        Vector3 decay = value.Phosphor == "P4" ? new(.024f) : value.Phosphor == "custom"
            ? new(value.DecayR * .001f, value.DecayG * .001f, value.DecayB * .001f) : new(.006f, .004f, .002f);
        var tube = TubeProfile.Resolve(value.TubeProfile);
        var data = new SettingsData
        {
            Geometry = new(value.CurvatureX, value.CurvatureY, value.CornerRadius, value.Overscan),
            Beam = new(value.ScanlineStrength, value.BeamMin, value.BeamMax, value.HorizontalBlur),
            Mask = new(value.Phosphor == "P4" ? 0 : value.Mask switch { "aperture" => 1, "shadow" => 2, "slot" => 3, _ => 0 }, value.MaskStrength, value.MaskScale, value.SoftMask),
            Light = new(value.Bloom, value.Halation, value.Brightness, value.MaskCompensation ? 1 : 0),
            Color = new(value.Gamma, value.Saturation, value.OutputGamma, value.Phosphor == "P4" ? 1 : 0),
            Convergence = new(r.X, r.Y, b.X, b.Y),
            Finish = new(value.Vignette, value.GlassReflection, value.Noise, value.Flicker),
            Temporal = new(value.Persistence, value.RollingScan ? 1 : 0, value.Bfi, value.Interlace ? 1 : 0),
            Decay = new(decay, value.ScanHz),
            Signal = new(value.Scanlines, value.Focus, value.FlickerHz, value.LineInterference),
            Retro = new(value.ConsoleStyle switch { "psx" => 1, "megadrive" => 2, "snes" => 3, "mastersystem" => 4, "gameboy" => 5, _ => 0 }, size.Width, size.Height, value.RetroDither),
            RetroSignal = new(value.Composite, value.RetroFilter, 0, 0),
            Rolling = new(value.RollingCompensation ? 1 : 0, 0, 0, 0)
        };
        if (tube != null)
        {
            var dimensions = tube.Values("usefulSize");
            float aspect = dimensions[0] / dimensions[1], screenAspect = (float)width / height;
            var area = value.TubeArea == "fit"
                ? new Vector2(Math.Min(1, aspect / screenAspect), Math.Min(1, screenAspect / aspect))
                : Vector2.One;
            area *= value.TubeScale;
            // Raster is expressed in tube XY. Mapping desktop aspect is explicit and precedes beam/history.
            var content = value.TubeContent switch
            {
                "fit" => new Vector2(Math.Min(1, screenAspect / aspect), Math.Min(1, aspect / screenAspect)),
                "crop" => new Vector2(Math.Max(1, screenAspect / aspect), Math.Max(1, aspect / screenAspect)),
                _ => Vector2.One
            };
            var a = tube.Values("surfaceA"); var exponents = tube.Values("surfaceB");
            data.Tube = new(dimensions[0], dimensions[1], tube.Values("stripePitch")[0], value.ViewDistanceMm);
            data.TubeArea = new(area, 0, 0);
            data.TubeContent = new(content, 0, 0);
            data.SurfaceA = new(a[0], a[1], a[2], 1);
            data.SurfaceB = new(exponents[0], exponents[1], exponents[2], exponents[3]);
            data.TubeLight = new(value.TubeLuminance == "reference" ? tube.Values("referenceLuminance")[0] / value.DisplayWhiteNits : 1,
                tube.Values("stripeFill")[0], 0, 0);
        }
        var matrixKey = (value.Gamut, value.Temperature, tube, value.TubeWhite == "reference");
        if (!colorMatrixKey.HasValue || colorMatrixKey.Value != matrixKey)
            ColorMatrix(value.Gamut, value.Temperature, tube, value.TubeWhite == "reference",
                out data.MatrixR, out data.MatrixG, out data.MatrixB);
        else
        {
            data.MatrixR = settingsData.MatrixR; data.MatrixG = settingsData.MatrixG; data.MatrixB = settingsData.MatrixB;
        }
        // Only inputs to Colorize, Beam and Persistence invalidate their stored light.
        // Geometry, glow and output adjustments operate after history and keep it intact.
        resetHistory |= HistoryChanged(in settingsData, in data);
        settings = value; settingsData = data; colorMatrixKey = matrixKey; settingsDirty = true;
    }
    private static bool HistoryChanged(in SettingsData previous, in SettingsData next) =>
        previous.Beam != next.Beam
        || previous.Color.X != next.Color.X || previous.Color.Y != next.Color.Y || previous.Color.W != next.Color.W
        || previous.MatrixR != next.MatrixR || previous.MatrixG != next.MatrixG || previous.MatrixB != next.MatrixB
        || previous.Finish.Z != next.Finish.Z || previous.Finish.W != next.Finish.W
        || previous.Signal.X != next.Signal.X || previous.Signal.Z != next.Signal.Z || previous.Signal.W != next.Signal.W
        || previous.Temporal.X != next.Temporal.X || previous.Temporal.Y != next.Temporal.Y || previous.Temporal.W != next.Temporal.W
        || previous.Decay != next.Decay || previous.Rolling != next.Rolling
        || previous.Retro != next.Retro || previous.RetroSignal != next.RetroSignal
        || previous.SurfaceA.W != next.SurfaceA.W
        || (next.SurfaceA.W > .5f
            ? previous.TubeContent != next.TubeContent || previous.TubeArea.Y != next.TubeArea.Y
            : previous.Mask != next.Mask || previous.Light.W != next.Light.W);
    public void ResetHistory() => resetHistory = true;
    private static Vector3 Xyz(float x, float y) => new(x / y, 1, (1 - x - y) / y);
    private static Matrix4x4 Primaries(Vector3 r, Vector3 g, Vector3 b, Vector3 white)
    {
        var p = new Matrix4x4(r.X, g.X, b.X, 0, r.Y, g.Y, b.Y, 0, r.Z, g.Z, b.Z, 0, 0, 0, 0, 1);
        Matrix4x4.Invert(p, out var inverse);
        var s = Vector3.Transform(white, Matrix4x4.Transpose(inverse));
        return p * Matrix4x4.CreateScale(s);
    }
    private static void ColorMatrix(string gamut, float kelvin, TubeProfile? tube, bool referenceWhite, out Vector4 r, out Vector4 g, out Vector4 b)
    {
        // P22 — семейство, а не единая матрица. Использованы xy конкретных P22R/G/B PTL:
        // https://www.phosphor-technology.com/crt-phosphors/
        // SMPTE-C/EBU используют стандартные первичные координаты.
        Vector3 red, green, blue;
        if (tube != null)
        {
            var rp = tube.Values("red"); var gp = tube.Values("green"); var bp = tube.Values("blue");
            red = Xyz(rp[0], rp[1]); green = Xyz(gp[0], gp[1]); blue = Xyz(bp[0], bp[1]);
        }
        else if (gamut == "P22") { red = Xyz(.647f, .343f); green = Xyz(.310f, .594f); blue = Xyz(.148f, .062f); }
        else if (gamut == "EBU") { red = Xyz(.64f, .33f); green = Xyz(.29f, .60f); blue = Xyz(.15f, .06f); }
        else { red = Xyz(.630f, .340f); green = Xyz(.310f, .595f); blue = Xyz(.155f, .070f); }
        // Аппроксимация дневного белого по CCT; D65 и 9300 K доступны одним параметром.
        float t = kelvin;
        float x = t <= 7000 ? -.046078f * 1e9f / (t * t * t) + 2.9678f * 1e6f / (t * t) + .09911f * 1e3f / t + .244063f
                         : -2.0064f * 1e9f / (t * t * t) + 1.9018f * 1e6f / (t * t) + .24748f * 1e3f / t + .237040f;
        float y = -3 * x * x + 2.87f * x - .275f;
        if (tube != null && referenceWhite) { var white = tube.Values("white"); x = white[0]; y = white[1]; }
        var source = Primaries(red, green, blue, Xyz(x, y));
        var srgb = Primaries(Xyz(.64f, .33f), Xyz(.30f, .60f), Xyz(.15f, .06f), Xyz(.3127f, .3290f));
        Matrix4x4.Invert(srgb, out var inverseSrgb);
        var m = inverseSrgb * source;
        r = new(m.M11, m.M12, m.M13, 0); g = new(m.M21, m.M22, m.M23, 0); b = new(m.M31, m.M32, m.M33, 0);
    }
    private static string ReadShader(string name)
    {
#if DEBUG
        string path = Path.Combine(Environment.CurrentDirectory, "Shaders", name);
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "Shaders", name);
        if (File.Exists(path)) return File.ReadAllText(path);
#endif
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CrtOverlay.Shaders." + name)
            ?? throw new FileNotFoundException(L.T("Шейдер не найден: ") + name);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public void ReloadShaders()
    {
        string common = ReadShader("Common.hlsl");
        var replacement = new ID3D11PixelShader[Enum.GetValues<PassId>().Length];
        ID3D11VertexShader? newVertex = null;
        try
        {
            var vs = Compiler.Compile(common + ReadShader("Fullscreen.hlsl"), "VSMain", "Fullscreen.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3);
            newVertex = device.CreateVertexShader(vs.Span);
            for (int i = 0; i < replacement.Length; i++)
            {
                var id = (PassId)i;
                string file = id switch { PassId.Threshold => "Downsample", PassId.BlurX or PassId.BlurY => "Blur", PassId.RollingDisplay => "Persistence", _ => id.ToString() };
                string source = $"#define OUTPUT_ROTATION {rotation}\n#define ROLLING_EXPOSURE {(id == PassId.RollingDisplay ? 1 : 0)}\n#define BLOOM_THRESHOLD {(id == PassId.Threshold ? 1 : 0)}\n#define BLUR_HORIZONTAL {(id == PassId.BlurX ? 1 : 0)}\n" + common + ReadShader(file + ".hlsl");
                var ps = Compiler.Compile(source, "PSMain", file + ".hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3);
                replacement[i] = device.CreatePixelShader(ps.Span);
            }
        }
        catch { newVertex?.Dispose(); foreach (var shader in replacement) shader?.Dispose(); throw; }
        vertex?.Dispose(); foreach (var shader in pixels) shader.Dispose();
        vertex = newVertex; pixels = replacement; resetHistory = true;
    }
    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, ScreenCapture capture)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - startTick) / (double)Stopwatch.Frequency;
        float dt = (float)((now - lastTick) / (double)Stopwatch.Frequency); lastTick = now;
        double cycles = elapsed * settings.ScanHz;
        double duty = 1 - settings.Bfi;
        float gate = settings.Bfi == 0 || Math.Floor((frame + 1) * duty) > Math.Floor(frame * duty) ? 1 : 0;
        var data = new FrameData
        {
            Screen = new(width, height, 1f / width, 1f / height),
            Timing = new((float)(elapsed % 4096), dt, frame++ % 65536, (float)(cycles % 1)),
            Pointer = capture.Cursor.Rectangle,
            CursorData = new(capture.Cursor.Type, capture.Cursor.Visible ? 1 : 0, 0, 0),
            Scan = new(dt * settings.ScanHz, (float)(Math.Floor(cycles) % 2), gate, 0)
        };
        if (settingsDirty) { context.UpdateSubresource(in settingsData, settingsBuffer); settingsDirty = false; }
        context.UpdateSubresource(in data, frameBuffer);
        context.PSSetConstantBuffer(0, settingsBuffer); context.PSSetConstantBuffer(1, frameBuffer);
        context.PSSetSampler(0, sampler); context.VSSetShader(vertex);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        if (resetHistory)
        {
            context.ClearRenderTargetView(history[0].Target, new Color4(0, 0, 0, 0));
            context.ClearRenderTargetView(history[1].Target, new Color4(0, 0, 0, 0));
            resetHistory = false;
        }
        Draw(context, input, PassId.Input, capture.View, capture.Cursor.View);
        ID3D11ShaderResourceView colorSource = input.View;
        if (settings.ConsoleStyle != "none")
        {
            Draw(context, retro!, PassId.Retro, input.View);
            colorSource = retro!.View;
        }
        Draw(context, colorized, PassId.Colorize, colorSource);
        Draw(context, beam, PassId.Beam, colorized.View);
        int next = 1 - historyIndex;
        // История хранит мгновенную яркость в конце кадра, exposure — интеграл света.
        // Их нельзя смешивать: иначе усреднение повторно попадает в обратную связь.
        if (settings.RollingScan)
            Draw(context, exposure, PassId.RollingDisplay, beam.View, history[historyIndex].View);
        Draw(context, history[next], PassId.Persistence, beam.View, history[historyIndex].View); historyIndex = next;
        var visible = settings.RollingScan ? exposure.View : history[next].View;
        // Both effects off: bypass the entire pyramid and the full-size mix.
        // Keep the temporal passes above running even on an unchanged desktop.
        if (settings.Bloom > 0 || settings.Halation > 0)
        {
            for (int level = 0; level < 5; level++)
            {
                Draw(context, bloom[level], level == 0 ? PassId.Threshold : PassId.Downsample, level == 0 ? visible : bloom[level - 1].View);
                Draw(context, scratch[level], PassId.BlurX, bloom[level].View);
                Draw(context, bloom[level], PassId.BlurY, scratch[level].View);
            }
            for (uint i = 0; i < 5; i++) context.PSSetShaderResource(i + 1, bloom[i].View);
            Draw(context, glow, PassId.Glow, visible, keepExtras: true);
            visible = glow.View;
        }
        Draw(context, geometry, PassId.Geometry, visible);
        context.OMSetRenderTargets(target); context.RSSetViewport(0, 0, width, height);
        context.PSSetShader(pixels[(int)PassId.Final]); context.PSSetShaderResource(0, geometry.View);
        context.Draw(3, 0); context.PSUnsetShaderResources(0, 6);
    }
    private void Draw(ID3D11DeviceContext c, Surface target, PassId pass, ID3D11ShaderResourceView source, ID3D11ShaderResourceView? aux = null, bool keepExtras = false)
    {
        c.OMSetRenderTargets(target.Target); c.RSSetViewport(0, 0, target.Width, target.Height);
        c.PSSetShader(pixels[(int)pass]); c.PSSetShaderResource(0, source);
        if (!keepExtras)
        {
            if (aux == null) c.PSUnsetShaderResource(1); else c.PSSetShaderResource(1, aux);
        }
        c.Draw(3, 0); c.PSUnsetShaderResources(0, 6);
    }
    public void Dispose()
    {
        foreach (var p in pixels) p?.Dispose(); vertex?.Dispose(); sampler?.Dispose();
        frameBuffer?.Dispose(); settingsBuffer?.Dispose(); retro?.Dispose(); foreach (var surface in owned) surface.Dispose();
    }
}

