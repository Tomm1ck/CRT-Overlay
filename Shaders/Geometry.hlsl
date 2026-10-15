Texture2D<float4> Source : register(t0);
float SurfaceSag(float2 xy)
{
    xy = abs(xy);
    return SurfaceA.x * pow(xy.x, SurfaceB.x) + SurfaceA.y * pow(xy.y, SurfaceB.y)
        + SurfaceA.z * pow(xy.x, SurfaceB.z) * pow(xy.y, SurfaceB.w);
}
float2 TubePosition(float2 projected)
{
    // Perspective from axial viewer onto a plane through the central apex:
    // projectedXY = surfaceXY * D / (D + sag(surfaceXY)).
    float2 plane = projected * Tube.xy * .5;
    float2 xy = plane;
    [unroll] for (int i = 0; i < 8; i++)
        xy = plane * (1 + SurfaceSag(clamp(xy, -Tube.xy*.6, Tube.xy*.6)) / Tube.w);
    return xy / (Tube.xy * .5);
}
float3 PhysicalStripes(float2 uv)
{
    if (Color.w > .5 || Mask.y == 0) return 1;
    // One phase period = distance between identical colours, NOT one subpixel.
    float phase = uv.x * Tube.x / Tube.z;
    float footprint = max(fwidth(phase), .0001);
    float duty = TubeLight.y / 3;
    float3 stripes = duty;
    // Rectangular RGB stripes with estimated black gaps. Box integration plus a
    // Nyquist fade removes unresolved harmonics instead of aliasing them into moire.
    [unroll] for (int k = 1; k <= 6; k++)
    {
        float frequency = k * footprint;
        float box = sin(3.14159265 * frequency) / (3.14159265 * frequency);
        float aa = 1 - smoothstep(.35, .5, frequency);
        float coefficient = 2 * sin(3.14159265 * k * duty) / (3.14159265 * k);
        stripes += coefficient * box * aa * cos(6.2831853 * k * (phase - float3(1.0/6, .5, 5.0/6)));
    }
    stripes = lerp(stripes, duty, Mask.w * saturate(footprint));
    float3 result = lerp(1, max(stripes,0), Mask.y);
    if (Light.w > .5) result /= lerp(1, duty, Mask.y);
    return result;
}
float3 FocusSample(float2 uv, float radius)
{
    float2 d = radius * Screen.zw;
    return Source.SampleLevel(LinearClamp,uv,0).rgb * .5
        + (Source.SampleLevel(LinearClamp,uv+float2(d.x,0),0).rgb
          +Source.SampleLevel(LinearClamp,uv-float2(d.x,0),0).rgb
          +Source.SampleLevel(LinearClamp,uv+float2(0,d.y),0).rgb
          +Source.SampleLevel(LinearClamp,uv-float2(0,d.y),0).rgb)*.125;
}
float4 PSMain(VertexOutput input) : SV_Target
{
    float2 p = input.uv * 2 - 1;
    if (SurfaceA.w > .5) p = TubePosition(p / TubeArea.xy);
    float2 warped = p * (1 + Geometry.xy * float2(p.y*p.y,p.x*p.x));
    float2 uv = warped * .5 + .5;
    float radius = Geometry.z;
    float2 q = abs(uv-.5) - (.5-radius);
    float distance = length(max(q,0)) + min(max(q.x,q.y),0) - radius;
    float coverage = 1 - smoothstep(-fwidth(distance),fwidth(distance),distance);
    float2 sampleUv = (uv-.5) * (1-Geometry.w) + .5;
    float r2 = dot(p,p)*.5;
    float focus = Signal.y * r2;
    float3 c;
    c.r = FocusSample(sampleUv + Convergence.xy*Screen.zw*r2,focus).r;
    c.g = FocusSample(sampleUv,focus).g;
    c.b = FocusSample(sampleUv + Convergence.zw*Screen.zw*r2,focus).b;
    if (SurfaceA.w > .5) c *= PhysicalStripes(uv);
    return float4(c * coverage, coverage);
}
