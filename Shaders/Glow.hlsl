Texture2D<float4> Source : register(t0);
Texture2D<float4> Bloom0 : register(t1);
Texture2D<float4> Bloom1 : register(t2);
Texture2D<float4> Bloom2 : register(t3);
Texture2D<float4> Bloom3 : register(t4);
Texture2D<float4> Bloom4 : register(t5);
float4 PSMain(VertexOutput input) : SV_Target
{
    float2 uv = input.uv;
    float3 c = Source.SampleLevel(LinearClamp,uv,0).rgb;
    float3 wide = Bloom4.SampleLevel(LinearClamp,uv,0).rgb;
    float3 bloom = Bloom0.SampleLevel(LinearClamp,uv,0).rgb * .4
        + Bloom1.SampleLevel(LinearClamp,uv,0).rgb * .25
        + Bloom2.SampleLevel(LinearClamp,uv,0).rgb * .18
        + Bloom3.SampleLevel(LinearClamp,uv,0).rgb * .12 + wide * .05;
    // Широкий слабый ореол с чуть тёплым оттенком — приближение рассеяния в стекле.
    return float4(c + Light.x * bloom + Light.y * wide * float3(1.05,1,.95),1);
}
