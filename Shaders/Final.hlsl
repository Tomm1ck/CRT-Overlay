Texture2D<float4> Source : register(t0);
float4 PSMain(VertexOutput input) : SV_Target
{
    float4 src = Source.SampleLevel(LinearClamp,input.uv,0);
    float2 p = input.uv * 2 - 1;
    if (SurfaceA.w > .5) p /= TubeArea.xy;
    float vignette = 1 - Finish.x * smoothstep(0,1.8,dot(p,p));
    // Широкие гауссовы пятна вместо внешней текстуры окружающей комнаты.
    float reflection = exp(-dot((p-float2(-.55,-.6))*float2(1.1,3.2),(p-float2(-.55,-.6))*float2(1.1,3.2))) * .35;
    float3 radiance = (src.rgb * vignette + reflection * Finish.y * src.a) * Light.z;
    if (SurfaceA.w > .5) radiance *= TubeLight.x;
    radiance *= Scan.z; // BFI задаётся по реально представленным кадрам, не по фазе 60 Гц.
    float3 outputColor = ToSrgb(pow(max(radiance,0), 2.2 / Color.z));
    outputColor += (Hash(input.position.yx + 71) - .5) / 255.0 * src.a * Scan.z;
    return float4(saturate(outputColor),1);
}
