using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace CrtOverlay;

internal sealed partial class CrtPipeline
{
    // Изолированная проверка GPU-математики. Staging/readback используется ТОЛЬКО
    // здесь, по явному --self-test; рабочий захват и рендер не читают пиксели на CPU.
    public static unsafe void SelfTest()
    {
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0],
            out var device, out ID3D11DeviceContext context).CheckError();
        using var ownedDevice = device; using var ownedContext = context;
        using var pipeline = new CrtPipeline(device, 16, 16, 1, new CrtSettings());
        using var staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = 16,
            Height = 16,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R16G16B16A16_Float,
            SampleDescription = new(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read
        });
        uint[] grey = Enumerable.Repeat(0xff808080u, 256).ToArray();
        using var desktop = device.CreateTexture2D(grey, Format.B8G8R8A8_UNorm, 16, 16, bindFlags: BindFlags.ShaderResource);
        using var desktopView = device.CreateShaderResourceView(desktop);
        uint[] cursorPixels = new uint[256];
        using var cursor = device.CreateTexture2D(cursorPixels, Format.B8G8R8A8_UNorm, 16, 16, bindFlags: BindFlags.ShaderResource);
        using var cursorView = device.CreateShaderResourceView(cursor);
        pipeline.settingsData = new SettingsData
        {
            Beam = new(0, .5f, 1.4f, 0),
            Color = new(2.2f, 1, 2.2f, 0),
            Light = new(0, 0, 1, 1),
            Decay = new(.02f, .03f, .04f, 60),
            Temporal = new(1, 0, 0, 0),
            Signal = new(16, 0, 60, 0),
            MatrixR = new(1, 0, 0, 0),
            MatrixG = new(0, 1, 0, 0),
            MatrixB = new(0, 0, 1, 0)
        };
        FrameData frameData = new() { Screen = new(16, 16, 1f / 16, 1f / 16), Timing = new(0, .01f, 0, .6f), Scan = new(.6f, 1, 1, 0) };
        void Constants()
        {
            context.UpdateSubresource(in pipeline.settingsData, pipeline.settingsBuffer);
            context.UpdateSubresource(in frameData, pipeline.frameBuffer);
            context.PSSetConstantBuffer(0, pipeline.settingsBuffer); context.PSSetConstantBuffer(1, pipeline.frameBuffer);
            context.PSSetSampler(0, pipeline.sampler); context.VSSetShader(pipeline.vertex);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        }
        Vector3 Pixel(Surface surface, int x = 8, int y = 8)
        {
            context.CopyResource(staging, surface.Texture);
            var map = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Half* data = (Half*)((byte*)map.DataPointer + y * map.RowPitch + x * 8); return new((float)data[0], (float)data[1], (float)data[2]); }
            finally { context.Unmap(staging, 0); }
        }
        void Check(string name, Vector3 actual, Vector3 expected, float tolerance = .002f)
        {
            if (!float.IsFinite(actual.X + actual.Y + actual.Z) || Vector3.Distance(actual, expected) > tolerance)
                throw new InvalidOperationException($"FAIL {name}: {actual}, expected {expected}");
            Console.WriteLine("PASS " + name);
        }
        static float Linear(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
        Vector3 LinearRgb(Vector3 c) => new(Linear(c.X), Linear(c.Y), Linear(c.Z));
        Constants();
        pipeline.Draw(context, pipeline.input, PassId.Input, desktopView, cursorView);
        Check("sRGB -> linear", Pixel(pipeline.input), new(Linear(128f / 255)));
        byte[] mono = [0x30, 0x50];
        fixed (byte* source = mono) fixed (uint* destination = cursorPixels)
            CursorOverlay.DecodeDuplication((nint)source, (nint)destination, 64, new OutduplPointerShapeInfo { Type = 1, Width = 4, Height = 2, Pitch = 1 });
        context.UpdateSubresource<uint>(cursorPixels, cursor, 0, 64);
        frameData.Pointer = new(0, 0, 4, 1); frameData.CursorData = new(1, 1, 0, 0); Constants();
        pipeline.Draw(context, pipeline.input, PassId.Input, desktopView, cursorView);
        Check("monochrome AND=0 XOR=0", Pixel(pipeline.input, 0, 0), Vector3.Zero);
        Check("monochrome AND=0 XOR=1", Pixel(pipeline.input, 1, 0), Vector3.One);
        Check("monochrome AND=1 XOR=0", Pixel(pipeline.input, 2, 0), new(Linear(128f / 255)));
        Check("monochrome AND=1 XOR=1", Pixel(pipeline.input, 3, 0), new(Linear(127f / 255)));
        cursorPixels[0] = 0xff123456; cursorPixels[1] = 0x00abcdef;
        context.UpdateSubresource<uint>(cursorPixels, cursor, 0, 64);
        frameData.CursorData.X = 4; Constants(); pipeline.Draw(context, pipeline.input, PassId.Input, desktopView, cursorView);
        Check("masked color XOR", Pixel(pipeline.input, 0, 0), LinearRgb(new((128 ^ 0x12) / 255f, (128 ^ 0x34) / 255f, (128 ^ 0x56) / 255f)));
        Check("masked color replace", Pixel(pipeline.input, 1, 0), LinearRgb(new(0xab / 255f, 0xcd / 255f, 0xef / 255f)));
        cursorPixels[0] = 0x80ff0000; context.UpdateSubresource<uint>(cursorPixels, cursor, 0, 64);
        frameData.CursorData.X = 2; Constants(); pipeline.Draw(context, pipeline.input, PassId.Input, desktopView, cursorView);
        Check("linear color alpha compositing", Pixel(pipeline.input, 0, 0), Vector3.Lerp(new(Linear(128f / 255)), new(1, 0, 0), 128f / 255));

        context.ClearRenderTargetView(pipeline.beam.Target, new Color4(0, 0, 0, 1));
        context.ClearRenderTargetView(pipeline.history[0].Target, new Color4(1, 1, 1, 1));
        frameData.Timing.Y = .01f; Constants();
        pipeline.Draw(context, pipeline.history[1], PassId.Persistence, pipeline.beam.View, pipeline.history[0].View);
        Vector3 once = Pixel(pipeline.history[1]);
        Check("independent RGB exponential decay", once, new(MathF.Exp(-.5f), MathF.Exp(-1f / 3), MathF.Exp(-.25f)));
        frameData.Timing.Y = .005f; Constants();
        pipeline.Draw(context, pipeline.history[1], PassId.Persistence, pipeline.beam.View, pipeline.history[0].View);
        pipeline.Draw(context, pipeline.history[0], PassId.Persistence, pipeline.beam.View, pipeline.history[1].View);
        Check("decay invariant to dt subdivision", Pixel(pipeline.history[0]), once);

        context.ClearRenderTargetView(pipeline.history[0].Target, new Color4(0, 0, 0, 0));
        context.ClearRenderTargetView(pipeline.beam.Target, new Color4(1, 1, 1, 1));
        pipeline.settingsData.Temporal.Y = 1; frameData.Timing.Y = .003f; frameData.Scan.X = .18f; Constants();
        pipeline.Draw(context, pipeline.history[1], PassId.Persistence, pipeline.beam.View, pipeline.history[0].View);
        Check("rolling unvisited row remains dark", Pixel(pipeline.history[1], 8, 0), Vector3.Zero);
        float age = (.6f - 8.5f / 16) / 60;
        Check("rolling visited row decay from actual scan time", Pixel(pipeline.history[1]), new(MathF.Exp(-age / .02f), MathF.Exp(-age / .03f), MathF.Exp(-age / .04f)));
        pipeline.settingsData.Temporal = new(0, 0, 0, 1); Constants();
        pipeline.Draw(context, pipeline.history[1], PassId.Persistence, pipeline.beam.View, pipeline.history[0].View);
        Check("interlace inactive field", Pixel(pipeline.history[1], 8, 0), Vector3.Zero);
        Check("interlace active field", Pixel(pipeline.history[1], 8, 1), Vector3.One);
        pipeline.settingsData.Geometry = new(.35f, .35f, .1f, 0); Constants();
        pipeline.Draw(context, pipeline.geometry, PassId.Geometry, pipeline.beam.View);
        Check("geometry black corners", Pixel(pipeline.geometry, 0, 0), Vector3.Zero);
        Check("geometry center retained", Pixel(pipeline.geometry), Vector3.One);
        frameData.Scan.Z = 0; Constants(); pipeline.Draw(context, pipeline.glow, PassId.Final, pipeline.geometry.View);
        Check("BFI black frame", Pixel(pipeline.glow), Vector3.Zero);
        context.ClearRenderTargetView(pipeline.input.Target, new Color4(0, 0, 0, 0));
        Half[] impulse = new Half[16 * 16 * 4]; impulse[(8 * 16 + 8) * 4] = (Half)4;
        context.UpdateSubresource<Half>(impulse, pipeline.input.Texture, 0, 16 * 8);
        pipeline.Draw(context, pipeline.beam, PassId.BlurX, pipeline.input.View);
        if (Pixel(pipeline.beam, 7, 8).X <= .1f || Pixel(pipeline.beam, 8, 0).X > .001f)
            throw new InvalidOperationException("FAIL Gaussian spatial spread");
        Console.WriteLine("PASS Gaussian spatial spread");
        var parsed = CrtSettings.Parse(["--present", "tearing", "--curvature", "0.12", "--fps", "240", "--rolling-scan", "off"]);
        if (parsed.SyncInterval != 0 || parsed.CurvatureY != .12f || parsed.Fps != 240 || parsed.RollingScan)
            throw new InvalidOperationException("FAIL CLI typed overrides");
        foreach (string[] invalid in new[] { new[] { "--noise", "NaN" }, new[] { "--beam-min", "2", "--beam-max", "1" }, new[] { "--convergence-r", "99,0" } })
        {
            bool rejected = false; try { CrtSettings.Parse(invalid); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("FAIL CLI validation");
        }
        Console.WriteLine("PASS CLI validation and overrides");
        context.ClearState(); context.Flush(); Console.WriteLine("All self-tests passed.");
    }
}
