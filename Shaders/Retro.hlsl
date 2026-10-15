Texture2D<float4> Source : register(t0);

// Матрица PS1: смещение до отбрасывания трёх младших бит RGB.
// https://psx-spx.consoledev.net/dev/ps1/gpu/misc/
static const float PsxDither[16] = {
    -4, 0, -3, 1, 2, -2, 3, -1, -3, 1, -4, 0, 3, -1, 2, -2
};
static const float Bayer[16] = {
    0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5
};
float4 PSMain(VertexOutput input) : SV_Target
{
    // Проход действительно рисуется в маленький framebuffer; палитра/дизеринг
    // привязаны к его пикселям, а не к физическим субпикселям монитора.
    float3 center = Source.SampleLevel(LinearClamp, input.uv, 0).rgb;
    float2 d = .25 / Retro.yz;
    float3 average = (Source.SampleLevel(LinearClamp, input.uv + d, 0).rgb
        + Source.SampleLevel(LinearClamp, input.uv - d, 0).rgb
        + Source.SampleLevel(LinearClamp, input.uv + float2(d.x,-d.y), 0).rgb
        + Source.SampleLevel(LinearClamp, input.uv + float2(-d.x,d.y), 0).rgb) * .25;
    float3 c = saturate(ToSrgb(lerp(center, average, RetroSignal.y)));
    uint2 pixel = (uint2)input.position.xy;
    uint index = (pixel.y & 3u) * 4u + (pixel.x & 3u);
    float ordered = ((Bayer[index] + .5) / 16 - .5) * Retro.w;
    if (Retro.x == 1)
    {
        // Постобработка не знает тип примитива, поэтому дизеринг применяется ко
        // всему изображению. В настоящем PS1 его применение зависело от команды GPU.
        c = floor(clamp(c * 255 + PsxDither[index] * Retro.w, 0, 255) / 8) / 31;
    }
    else if (Retro.x == 5)
    {
        // Четыре стилизованных оттенка отражающего LCD Game Boy; это не P4-люминофор.
        float shade = floor(saturate(dot(c,float3(.299,.587,.114))) * 3 + .5 + ordered);
        shade = clamp(shade, 0, 3);
        c = shade < .5 ? float3(15,56,15) / 255 : shade < 1.5 ? float3(48,98,48) / 255
            : shade < 2.5 ? float3(139,172,15) / 255 : float3(155,188,15) / 255;
    }
    else
    {
        // MD: RGB333; Master System: RGB222; SNES: RGB555.
        // Это пространство возможных цветов, а не ограничение игровых CRAM-палитр.
        // Нелинейность конкретного аналогового DAC здесь не моделируется.
        float levels = Retro.x == 2 ? 7 : Retro.x == 4 ? 3 : 31;
        c = clamp(floor(c * levels + .5 + ordered), 0, levels) / levels;
    }
    return float4(ToLinear(c), 1);
}
