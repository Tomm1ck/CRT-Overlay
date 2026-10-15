Texture2D<float4> Source : register(t0);
float4 PSMain(VertexOutput input) : SV_Target
{
    uint w,h; Source.GetDimensions(w,h); float2 d = .5 / float2(w,h);
    float3 c = (Source.SampleLevel(LinearClamp,input.uv+float2(-d.x,-d.y),0).rgb
              +Source.SampleLevel(LinearClamp,input.uv+float2(d.x,-d.y),0).rgb
              +Source.SampleLevel(LinearClamp,input.uv+float2(-d.x,d.y),0).rgb
              +Source.SampleLevel(LinearClamp,input.uv+d,0).rgb) * .25;
#if BLOOM_THRESHOLD
    // Мягкое выделение ярких областей без резкого порога на движущихся границах.
    float l = Luma(c); c *= smoothstep(.15,.8,l);
#endif
    return float4(c,1);
}
