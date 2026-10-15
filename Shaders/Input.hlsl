Texture2D<float4> Desktop : register(t0);
Texture2D<float4> CursorShape : register(t1);
float4 PSMain(VertexOutput input) : SV_Target
{
    float2 uv = input.uv;
#if OUTPUT_ROTATION == 2
    uv = float2(uv.y, 1 - uv.x);
#elif OUTPUT_ROTATION == 3
    uv = 1 - uv;
#elif OUTPUT_ROTATION == 4
    uv = float2(1 - uv.y, uv.x);
#endif
    uint w, h; Desktop.GetDimensions(w, h);
    int2 p = clamp(int2(uv * float2(w,h)), 0, int2(w,h)-1);
    float3 encoded = Desktop.Load(int3(p,0)).rgb;
    float3 c = ToLinear(encoded);
    int2 cp = int2(floor(input.position.xy - Pointer.xy));
    if (CursorData.y > .5 && all(cp >= 0) && all(cp < int2(Pointer.zw)))
    {
        float4 mouse = CursorShape.Load(int3(cp,0));
        if (CursorData.x == 2) c = lerp(c, ToLinear(mouse.rgb), mouse.a);
        else
        {
            // AND/XOR определены над исходными 8-битными значениями, до sRGB-декодирования.
            uint3 desktopBits = (uint3)round(saturate(encoded) * 255);
            uint3 cursorBits = (uint3)round(mouse.rgb * 255);
            uint andBits = mouse.a > .5 ? 255u : 0u;
            encoded = float3((desktopBits & andBits) ^ cursorBits) / 255.0;
            c = ToLinear(encoded);
        }
    }
    // Цвет консоли обрабатывается следующим проходом вместе с уже наложенным курсором.
    return float4(c, 1);
}
