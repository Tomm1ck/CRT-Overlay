param([string]$ShaderDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Shaders'))
$ErrorActionPreference = 'Stop'
# Только компиляция HLSL. Не создаёт окно, D3D-устройство и не запускает оверлей.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class CrtShaderBuild
{
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3DCompile(byte[] source, UIntPtr length,
        [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags, uint effectFlags, out IntPtr code, out IntPtr errors);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetPointer(IntPtr blob);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate UIntPtr GetSize(IntPtr blob);
    public static void Compile(string source, string name, string entry, string target)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        IntPtr code, errors;
        int result = D3DCompile(bytes, (UIntPtr)bytes.Length, name, IntPtr.Zero, IntPtr.Zero,
            entry, target, 1u << 15, 0, out code, out errors);
        try
        {
            if (result >= 0) return;
            string message = "HLSL compilation failed: " + name;
            if (errors != IntPtr.Zero)
            {
                IntPtr vtable = Marshal.ReadIntPtr(errors);
                var pointer = (GetPointer)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size), typeof(GetPointer));
                var size = (GetSize)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size), typeof(GetSize));
                message += "\n" + Marshal.PtrToStringAnsi(pointer(errors), checked((int)size(errors).ToUInt64()));
            }
            throw new InvalidOperationException(message);
        }
        finally
        {
            if (errors != IntPtr.Zero) Marshal.Release(errors);
            if (code != IntPtr.Zero) Marshal.Release(code);
        }
    }
}
'@
try {
    $common = [IO.File]::ReadAllText((Join-Path $ShaderDirectory 'Common.hlsl'))
    $vertex = [IO.File]::ReadAllText((Join-Path $ShaderDirectory 'Fullscreen.hlsl'))
    [CrtShaderBuild]::Compile($common + $vertex, 'Fullscreen.hlsl', 'VSMain', 'vs_5_0')
    $passes = @('Input', 'Retro', 'Colorize', 'Beam', 'Persistence', 'RollingDisplay', 'Threshold', 'Downsample', 'BlurX', 'BlurY', 'Glow', 'Geometry', 'Final')
    foreach ($pass in $passes) {
        $file = switch ($pass) {
            'Threshold' { 'Downsample' }
            'RollingDisplay' { 'Persistence' }
            'BlurX' { 'Blur' }
            'BlurY' { 'Blur' }
            default { $pass }
        }
        $rotations = if ($pass -eq 'Input') { @(1,2,3,4) } else { @(1) }
        foreach ($rotation in $rotations) {
            $prefix = "#define OUTPUT_ROTATION $rotation`n#define ROLLING_EXPOSURE $([int]($pass -eq 'RollingDisplay'))`n#define BLOOM_THRESHOLD $([int]($pass -eq 'Threshold'))`n#define BLUR_HORIZONTAL $([int]($pass -eq 'BlurX'))`n"
            $body = [IO.File]::ReadAllText((Join-Path $ShaderDirectory ($file + '.hlsl')))
            [CrtShaderBuild]::Compile($prefix + $common + $body, $file + '.hlsl', 'PSMain', 'ps_5_0')
        }
    }
    Write-Host 'HLSL compilation succeeded.'
} catch {
    [Console]::Error.WriteLine($_.Exception.ToString())
    exit 1
}
