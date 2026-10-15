cbuffer Settings : register(b0)
{
    float4 Geometry;       // curvature X/Y, corner radius, overscan
    float4 Beam;           // strength, min/max width, horizontal bandwidth
    float4 Mask;           // type, strength, triad scale, softness
    float4 Light;          // bloom, halation, brightness, mask compensation
    float4 Color;          // CRT gamma, saturation, output gamma, monochrome
    float4 Convergence;    // R.xy, B.xy в пикселях
    float4 Finish;         // vignette, reflection, noise, flicker
    float4 Temporal;       // persistence, rolling, BFI, interlace
    float4 Decay;          // RGB tau в секундах, scan Hz
    float4 Signal;         // logical scanlines, focus, flicker Hz, line interference
    float4 MatrixR;
    float4 MatrixG;
    float4 MatrixB;
    float4 Retro;          // mode, virtual width/height, dither strength
    float4 RetroSignal;    // composite chroma blend, input prefilter, reserved
    float4 Rolling;        // compensate average brightness, reserved
    float4 Tube;           // useful width/height mm, identical-colour pitch mm, axial view distance mm
    float4 TubeArea;       // fraction of output width/height (centred)
    float4 TubeContent;    // fraction of tube raster occupied by desktop (can exceed 1 for crop)
    float4 SurfaceA;       // A1,A2,A3, physical profile enabled
    float4 SurfaceB;       // B1,B2,B3,B4, outer faceplate equation (mm)
    float4 TubeLight;      // reference/output white ratio, estimated RGB fill, reserved
};
cbuffer Frame : register(b1)
{
    float4 Screen;         // width, height, 1/width, 1/height
    float4 Timing;         // seconds modulo 4096, real dt, frame index, scan phase
    float4 Pointer;        // x,y,width,height; верхний левый угол, без вычитания hotspot DXGI
    float4 CursorData;     // DXGI shape type, visible, reserved
    float4 Scan;           // dt * scanHz, field parity, BFI gate, reserved
};
SamplerState LinearClamp : register(s0);
struct VertexOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
float3 ToLinear(float3 c) { return lerp(c / 12.92, pow(max((c + .055) / 1.055, 0), 2.4), step(.04045, c)); }
float3 ToSrgb(float3 c) { return lerp(12.92 * c, 1.055 * pow(max(c, 0), 1.0 / 2.4) - .055, step(.0031308, c)); }
float Luma(float3 c) { return dot(c, float3(.2126, .7152, .0722)); }
float Hash(float2 p)
{
    // Хеш на uint не зависит от точности sin больших временных аргументов.
    uint n = (uint)p.x * 1973u + (uint)p.y * 9277u + (uint)Timing.z * 26699u + 911u;
    n = (n ^ (n >> 16)) * 2246822519u; n = (n ^ (n >> 13)) * 3266489917u;
    return (n & 0x00ffffffu) / 16777216.0;
}
