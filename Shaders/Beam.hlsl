Texture2D<float4> Source : register(t0);
float ErfApprox(float x)
{
    // Приближение erf (A&S 7.1.26); интеграл гауссиана по площади пикселя снижает алиасинг.
    float s = sign(x); x = abs(x); float t = 1 / (1 + .3275911 * x);
    float e = (((((1.061405429*t - 1.453152027)*t) + 1.421413741)*t - .284496736)*t + .254829592)*t;
    return s * (1 - e * exp(-x*x));
}
float3 Bandwidth(float2 uv)
{
    float2 d = float2(Beam.w * Screen.z, 0);
    return Source.SampleLevel(LinearClamp,uv,0).rgb * .5
        + (Source.SampleLevel(LinearClamp,uv-d,0).rgb + Source.SampleLevel(LinearClamp,uv+d,0).rgb) * .25;
}
float3 PhosphorMask(float2 pixel)
{
    if (Mask.x < .5 || Mask.y == 0) return 1;
    float2 p = pixel / Mask.z;
    float stagger = Mask.x == 2 ? fmod(floor(p.y / 2), 2) * 1.5 : 0;
    float phase = fmod(p.x + stagger, 3);
    float3 hard = float3(phase < 1, phase >= 1 && phase < 2, phase >= 2);
    float aperture = 1;
    if (Mask.x == 2) aperture = .65 + .35 * pow(sin(3.14159265 * frac(p.y / 2)), 2);
    if (Mask.x == 3) aperture = .65 + .35 * smoothstep(.08,.3,abs(frac(p.y / 4)-.5));
    // Фильтруем маску к среднему при слишком мелкой триаде — ослабление муара на 1080p.
    float softness = Mask.w * saturate(1 / Mask.z);
    float meanAperture = Mask.x == 1 ? 1 : .825;
    float3 maskValue = lerp(hard * aperture, meanAperture / 3, softness);
    float3 result = lerp(1, maskValue, Mask.y);
    if (Light.w > .5) result /= lerp(1, meanAperture / 3, Mask.y);
    return result;
}
float4 PSMain(VertexOutput input) : SV_Target
{
    float row = input.uv.y * Signal.x - .5;
    float baseRow = floor(row);
    float footprint = max(Signal.x * Screen.w, .1);
    if (SurfaceA.w > .5) footprint = max(footprint, Signal.x * Screen.w / TubeArea.y);
    float3 beamColor = 0;
    [unroll] for (int i = -2; i <= 2; i++)
    {
        float center = baseRow + i;
        float3 c = Bandwidth(float2(input.uv.x, (center + .5) / Signal.x));
        float sigma = lerp(Beam.y, Beam.z, sqrt(saturate(Luma(c)))) * .4;
        float d = row - center;
        float weight = .5 * (ErfApprox((d + footprint*.5) / (sigma * 1.41421356))
                          - ErfApprox((d - footprint*.5) / (sigma * 1.41421356))) / footprint;
        beamColor += c * weight;
    }
    float3 c = lerp(Source.SampleLevel(LinearClamp,input.uv,0).rgb, beamColor, Beam.x);
    // Physical stripes are sampled after projection, where final pixel footprint is known.
    return float4(max(c,0) * (SurfaceA.w > .5 ? float3(1,1,1) : PhosphorMask(input.position.xy)), 1);
}
