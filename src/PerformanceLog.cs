using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace CrtOverlay;

// Optional diagnostics. GPU results are polled without flushes or waits;
// only one in sixteen frames is sampled, with a bounded ring of query sets.
internal sealed class PerformanceLog : IDisposable
{
    private sealed class Sample : IDisposable
    {
        public ID3D11Query? Disjoint, Start, End;
        public bool Pending;
        public void Dispose()
        {
            End?.Dispose(); Start?.Dispose(); Disjoint?.Dispose();
            End = Start = Disjoint = null;
        }
    }

    private readonly Sample[] samples = [new(), new(), new(), new()];
    private bool gpuAvailable;
    private int active = -1, next;
    private long frame, frames, captures, lastReport = Stopwatch.GetTimestamp(), renderTicks, maxRenderTicks, gpuSamples;
    private double gpuMilliseconds, maxGpuMilliseconds;
    private long? lastCpu = CpuTime();

    public PerformanceLog(ID3D11Device device)
    {
        try
        {
            foreach (var sample in samples)
            {
                sample.Disjoint = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
                sample.Start = device.CreateQuery(new QueryDescription(QueryType.Timestamp));
                sample.End = device.CreateQuery(new QueryDescription(QueryType.Timestamp));
            }
            gpuAvailable = true;
        }
        catch (Exception ex)
        {
            foreach (var sample in samples) sample.Dispose();
            Program.Log("Performance GPU queries unavailable: " + ex.Message);
        }
        Program.Log("Performance logging enabled: 10 s intervals; render-wall-ms includes capture/CPU submission/Present waits; gpu-effects-ms samples only the CRT passes; cpu-process is normalized across logical processors.");
    }

    public void Begin(ID3D11DeviceContext context)
    {
        if (!gpuAvailable) return;
        foreach (var sample in samples)
        {
            if (!sample.Pending) continue;
            if (!Read(context, sample.Disjoint!, out QueryDataTimestampDisjoint disjoint)
                || !Read(context, sample.Start!, out ulong start) || !Read(context, sample.End!, out ulong end)) continue;
            sample.Pending = false;
            if (disjoint.Disjoint || disjoint.Frequency == 0 || end < start) continue;
            double ms = (end - start) * (1000.0 / disjoint.Frequency);
            gpuMilliseconds += ms; maxGpuMilliseconds = Math.Max(maxGpuMilliseconds, ms); gpuSamples++;
        }
        if (!gpuAvailable || (frame++ & 15) != 0) return;
        var current = samples[next];
        if (current.Pending) return; // A slow GPU must never stall rendering for diagnostics.
        active = next; next = (next + 1) % samples.Length;
        context.Begin(current.Disjoint!); context.End(current.Start!);
    }

    public void End(ID3D11DeviceContext context)
    {
        if (active < 0) return;
        var current = samples[active];
        context.End(current.End!); context.End(current.Disjoint!);
        current.Pending = true; active = -1;
    }

    private unsafe bool Read<T>(ID3D11DeviceContext context, ID3D11Query query, out T data) where T : unmanaged
    {
        T value = default;
        var result = context.GetData(query, (nint)(&value), (uint)sizeof(T), AsyncGetDataFlags.DoNotFlush);
        data = value;
        if (result.Failure)
        {
            if (gpuAvailable) Program.Log($"Performance GPU queries disabled: HRESULT=0x{result.Code:X8}.");
            gpuAvailable = false;
        }
        return result.Code == 0; // S_FALSE means not ready, not an error.
    }

    public void Record(long elapsedTicks, bool fresh)
    {
        frames++; if (fresh) captures++;
        renderTicks += elapsedTicks; maxRenderTicks = Math.Max(maxRenderTicks, elapsedTicks);
        long now = Stopwatch.GetTimestamp();
        double seconds = (now - lastReport) / (double)Stopwatch.Frequency;
        if (seconds < 10) return;
        long? cpu = CpuTime();
        string cpuText = cpu.HasValue && lastCpu.HasValue
            ? ((cpu.Value - lastCpu.Value) * 100.0 / (seconds * TimeSpan.TicksPerSecond * Environment.ProcessorCount)).ToString("F2", CultureInfo.InvariantCulture) + "%" : "n/a";
        string gpuText = gpuAvailable && gpuSamples > 0
            ? FormattableString.Invariant($"{gpuMilliseconds / gpuSamples:F3}, gpu-max-ms={maxGpuMilliseconds:F3}, gpu-samples={gpuSamples}") : "n/a";
        Program.Log(FormattableString.Invariant($"Performance: interval-s={seconds:F2}, present-fps={frames / seconds:F2}, capture-fps={captures / seconds:F2}, render-wall-ms={renderTicks * 1000.0 / Stopwatch.Frequency / frames:F3}, render-wall-max-ms={maxRenderTicks * 1000.0 / Stopwatch.Frequency:F3}, gpu-effects-ms={gpuText}, cpu-process={cpuText}."));
        lastReport = now; lastCpu = cpu;
        frames = captures = renderTicks = maxRenderTicks = gpuSamples = 0;
        gpuMilliseconds = maxGpuMilliseconds = 0;
    }

    private static long? CpuTime() => GetProcessTimes(new nint(-1), out _, out _, out long kernel, out long user) ? kernel + user : null;
    public void Dispose() { foreach (var sample in samples) sample.Dispose(); }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);
}
