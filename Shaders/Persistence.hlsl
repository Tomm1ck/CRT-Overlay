Texture2D<float4> Excitation : register(t0);
Texture2D<float4> History : register(t1);
float4 PSMain(VertexOutput input) : SV_Target
{
    float3 current = Excitation.SampleLevel(LinearClamp,input.uv,0).rgb;
    float3 previous = History.SampleLevel(LinearClamp,input.uv,0).rgb;
    float3 tau = max(Decay.xyz * Temporal.x, .0000001);
    float3 fading = previous * exp(-Timing.y / tau);
    float rowParity = fmod(floor(input.uv.y * Signal.x), 2);
    if (Temporal.y > .5)
    {
        // Интегрируем экспоненциальные импульсы за весь интервал кадра.
        // Выборка только в конце кадра давала неподвижный градиент при 60 Гц/60 Гц.
        // Теперь история и вывод раздельные: в истории мгновенная энергия,
        // на экране средний свет за dt. При полном периоде он не зависит от фазы.
        tau = max(tau, .00001); // Ограничение пиков для FP16 при почти нулевом persistence.
        float dt = max(Timing.y, .000001);
        float ageCycles = frac(Timing.w - input.uv.y);
        float periodCycles = 1;
        if (Temporal.w > .5)
        {
            float eventParity = fmod(Scan.y + (input.uv.y > Timing.w ? 1 : 0), 2);
            if (rowParity != eventParity) ageCycles += 1;
            periodCycles = 2;
        }
        float age = ageCycles / Decay.w;
        float period = periodCycles / Decay.w;
        // Считаем события в (t-dt,t]; начальная граница исключена, чтобы не удвоить импульс.
        float count = max(0, ceil((dt - age) / period));
        float3 tail = exp(-age / tau) * (1 - exp(-count * period / tau)) / (1 - exp(-period / tau));
        float3 pulse = current * (Rolling.x > .5 ? period / tau : float3(1,1,1));
        float3 decayFactor = exp(-dt / tau);
#if ROLLING_EXPOSURE
        // ∫ A exp(-t/tau) dt = A tau (1-exp(-t/tau)). Для нескольких
        // импульсов сумма хвостов — геометрическая прогрессия, без цикла по времени.
        float3 energy = previous * tau * (1 - decayFactor) + pulse * tau * max(0,count - tail);
        return float4(max(0,energy / dt),1);
#else
        return float4(previous * decayFactor + pulse * tail,1);
#endif
    }
    if (Temporal.w > .5 && rowParity != Scan.y) current = 0;
    return float4(Temporal.x == 0 ? current : max(current, fading), 1);
}
