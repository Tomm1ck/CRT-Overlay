VertexOutput VSMain(uint id : SV_VertexID)
{
    VertexOutput o;
    float2 p = float2((id << 1) & 2, id & 2);
    o.position = float4(p.x * 2 - 1, 1 - p.y * 2, 0, 1); o.uv = p;
    return o;
}
