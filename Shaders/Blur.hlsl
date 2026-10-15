Texture2D<float4> Source : register(t0);
float4 PSMain(VertexOutput input) : SV_Target
{
    uint w,h; Source.GetDimensions(w,h);
#if BLUR_HORIZONTAL
    float2 d = float2(1.0/w,0);
#else
    float2 d = float2(0,1.0/h);
#endif
    // Девятиточечный гауссов фильтр: билинейная выборка объединяет пары соседних весов.
    float3 c = Source.SampleLevel(LinearClamp,input.uv,0).rgb * .2270270270;
    c += (Source.SampleLevel(LinearClamp,input.uv+d*1.3846153846,0).rgb
         +Source.SampleLevel(LinearClamp,input.uv-d*1.3846153846,0).rgb) * .3162162162;
    c += (Source.SampleLevel(LinearClamp,input.uv+d*3.2307692308,0).rgb
         +Source.SampleLevel(LinearClamp,input.uv-d*3.2307692308,0).rgb) * .0702702703;
    return float4(c,1);
}
