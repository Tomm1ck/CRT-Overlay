Texture2D<float4> Source : register(t0);
float3 EncodedAt(int2 p, int2 size)
{
    return ToSrgb(Source.Load(int3(clamp(p,0,size-1),0)).rgb);
}
float3 ToYiq(float3 c)
{
    return float3(dot(c,float3(.299,.587,.114)), dot(c,float3(.596,-.274,-.322)), dot(c,float3(.211,-.523,.312)));
}
float3 FromYiq(float3 c)
{
    return float3(dot(c,float3(1,.956,.621)), dot(c,float3(1,-.272,-.647)), dot(c,float3(1,-1.106,1.703)));
}
float4 PSMain(VertexOutput input) : SV_Target
{
    uint w,h; Source.GetDimensions(w,h);
    float2 sourceUv = input.uv;
    if (SurfaceA.w > .5)
    {
        sourceUv = (sourceUv - .5) / TubeContent.xy + .5;
        if (any(sourceUv < 0) || any(sourceUv > 1)) return float4(0,0,0,1);
    }
    int2 size = int2(w,h), p = clamp(int2(sourceUv * float2(w,h)),0,size-1);
    // Load сохраняет резкую пиксельную сетку при увеличении виртуального framebuffer.
    float3 c = Source.Load(int3(p,0)).rgb;
    // Continuous desktop scaling avoids nearest-neighbour aliasing on fitted/cropped areas.
    // Retro keeps its intentionally discrete framebuffer grid.
    if (SurfaceA.w > .5 && Retro.x < .5) c = Source.SampleLevel(LinearClamp, sourceUv, 0).rgb;
    if (Retro.x > .5 && RetroSignal.x > 0)
    {
        // Приближение малой полосы цветности композитного видеосигнала.
        // Усреднение соседних цветных точек также смешивает шахматный дизеринг.
        // Это не полный NTSC/PAL-декодер: dot crawl и конкретный RF-тракт не эмулируются.
        float3 yiq = ToYiq(ToSrgb(c));
        float3 filtered = (ToYiq(EncodedAt(p+int2(-2,0),size))
            + 4*ToYiq(EncodedAt(p+int2(-1,0),size)) + 6*yiq
            + 4*ToYiq(EncodedAt(p+int2(1,0),size))
            + ToYiq(EncodedAt(p+int2(2,0),size))) / 16;
        yiq = lerp(yiq, filtered, float3(.15,1,1) * RetroSignal.x);
        c = ToLinear(saturate(FromYiq(yiq)));
    }
    c = pow(max(c,0), Color.x / 2.2);
    c = max(float3(dot(MatrixR.xyz,c), dot(MatrixG.xyz,c), dot(MatrixB.xyz,c)), 0);
    if (Color.w > .5) c = Luma(c).xxx;
    c = lerp(Luma(c).xxx, c, Color.y);
    float noise = (Hash(input.position.xy) - .5) * Finish.z;
    float interference = sin(input.uv.y * Screen.y * .11 + Timing.x * 73) * Signal.w;
    c = max(0, c + noise + interference);
    c *= 1 + Finish.w * sin(6.2831853 * Timing.x * Signal.z);
    return float4(c,1);
}
