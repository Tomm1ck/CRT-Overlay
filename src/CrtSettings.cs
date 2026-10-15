using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrtOverlay;

[AttributeUsage(AttributeTargets.Property)]
internal sealed class OptionAttribute(string name, double min = 0, double max = 1, string? choices = null) : Attribute
{
    public string Name { get; } = name;
    public double Min { get; } = min;
    public double Max { get; } = max;
    public string? Choices { get; } = choices;
}

internal sealed class CrtSettings
{
    // Missing version in older presets means schema 1; saves always emit schema 2.
    public int SchemaVersion { get; set; } = 2;
    [Option("tube-profile", choices: "legacy|philips-a51eal155x")] public string TubeProfile { get; set; } = "legacy";
    [Option("tube-area", choices: "fit|stretch")] public string TubeArea { get; set; } = "fit";
    [Option("tube-scale", .25, 1)] public float TubeScale { get; set; } = 1;
    [Option("tube-content", choices: "fit|crop|stretch")] public string TubeContent { get; set; } = "fit";
    [Option("view-distance-mm", 300, 3000)] public float ViewDistanceMm { get; set; } = 1000;
    [Option("tube-white", choices: "reference|temperature")] public string TubeWhite { get; set; } = "reference";
    [Option("tube-luminance", choices: "relative|reference")] public string TubeLuminance { get; set; } = "relative";
    [Option("display-white-nits", 40, 1000)] public float DisplayWhiteNits { get; set; } = 120;
    [Option("monitor", 0, 31)] public int Monitor { get; set; }
    [Option("present", choices: "vsync|immediate|tearing")] public string Present { get; set; } = "vsync";
    [Option("sync-interval", 0, 4)] public uint SyncInterval { get; set; } = 1;
    [Option("fps", 0, 1000)] public int Fps { get; set; }
    [Option("test-seconds", 0, 3600)] public int TestSeconds { get; set; }
    [Option("debug-layer")] public bool DebugLayer { get; set; }
    [JsonIgnore, Option("performance-log")] public bool PerformanceLog { get; set; }
    [Option("curvature-x", 0, .4)] public float CurvatureX { get; set; } = .08f;
    [Option("curvature-y", 0, .4)] public float CurvatureY { get; set; } = .08f;
    [Option("corner-radius", 0, .25)] public float CornerRadius { get; set; } = .03f;
    [Option("overscan", 0, .15)] public float Overscan { get; set; }
    [Option("mask", choices: "none|aperture|shadow|slot")] public string Mask { get; set; } = "aperture";
    [Option("mask-strength")] public float MaskStrength { get; set; } = .35f;
    [Option("mask-scale", .5, 8)] public float MaskScale { get; set; } = 1f;
    [Option("soft-mask")] public float SoftMask { get; set; } = .65f;
    [Option("mask-compensation")] public bool MaskCompensation { get; set; } = true;
    [Option("scanline-strength")] public float ScanlineStrength { get; set; } = .5f;
    [Option("scanlines", 180, 2160)] public int Scanlines { get; set; } = 540;
    [Option("beam-min", .2, 2)] public float BeamMin { get; set; } = .5f;
    [Option("beam-max", .2, 3)] public float BeamMax { get; set; } = 1.4f;
    [Option("horizontal-blur", 0, 3)] public float HorizontalBlur { get; set; } = .35f;
    [Option("bloom", 0, 2)] public float Bloom { get; set; } = .2f;
    [Option("halation", 0, 1)] public float Halation { get; set; } = .1f;
    [Option("convergence-r", choices: "x,y")] public string ConvergenceR { get; set; } = "0.4,0.0";
    [Option("convergence-b", choices: "x,y")] public string ConvergenceB { get; set; } = "-0.4,0.0";
    [Option("focus", 0, 4)] public float Focus { get; set; } = .45f;
    [Option("noise", 0, .2)] public float Noise { get; set; } = .003f;
    [Option("flicker", 0, .2)] public float Flicker { get; set; } = .005f;
    [Option("flicker-hz", 40, 120)] public float FlickerHz { get; set; } = 60;
    [Option("line-interference", 0, .2)] public float LineInterference { get; set; }
    [Option("persistence")] public float Persistence { get; set; } = .35f;
    [Option("rolling-scan")] public bool RollingScan { get; set; }
    [Option("rolling-compensation")] public bool RollingCompensation { get; set; } = true;
    [Option("scan-hz", 24, 240)] public float ScanHz { get; set; } = 60;
    [Option("bfi", 0, .9)] public float Bfi { get; set; }
    [Option("interlace")] public bool Interlace { get; set; }
    [Option("phosphor", choices: "P22|P4|custom")] public string Phosphor { get; set; } = "P22";
    [Option("decay-r", .001, 200)] public float DecayR { get; set; } = 6;
    [Option("decay-g", .001, 200)] public float DecayG { get; set; } = 4;
    [Option("decay-b", .001, 200)] public float DecayB { get; set; } = 2;
    [Option("gamut", choices: "P22|SMPTE-C|EBU")] public string Gamut { get; set; } = "SMPTE-C";
    [Option("temperature", 4000, 12000)] public float Temperature { get; set; } = 6500;
    [Option("gamma", 1, 3.5)] public float Gamma { get; set; } = 2.4f;
    [Option("output-gamma", 1, 3.5)] public float OutputGamma { get; set; } = 2.2f;
    [Option("vignette")] public float Vignette { get; set; } = .2f;
    [Option("glass-reflection", 0, .3)] public float GlassReflection { get; set; } = .025f;
    [Option("brightness", .1, 3)] public float Brightness { get; set; } = 1;
    [Option("saturation", 0, 2)] public float Saturation { get; set; } = 1;
    [Option("cursor", choices: "capture|system")] public string Cursor { get; set; } = "capture";
    [Option("console", choices: "none|psx|megadrive|snes|mastersystem|gameboy")] public string ConsoleStyle { get; set; } = "none";
    [Option("retro-width", 0, 2048)] public int RetroWidth { get; set; }
    [Option("retro-height", 0, 2048)] public int RetroHeight { get; set; }
    [Option("retro-dither")] public float RetroDither { get; set; } = 1;
    [Option("retro-filter")] public float RetroFilter { get; set; } = .25f;
    [Option("composite")] public float Composite { get; set; }
    [JsonIgnore] public string? SavePath { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal static readonly (PropertyInfo Property, OptionAttribute Option)[] Options = typeof(CrtSettings)
        .GetProperties().Select(p => (Property: p, Option: p.GetCustomAttribute<OptionAttribute>()))
        .Where(x => x.Option != null).Select(x => (x.Property, x.Option!)).ToArray();

    public static CrtSettings Parse(string[] args)
    {
        var s = new CrtSettings();
        // Пресет задаёт основу, явные CLI-параметры всегда имеют приоритет.
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--preset")
            {
                if (++i >= args.Length) throw new ArgumentException(L.T("--preset требует имя или путь JSON."));
                s = Load(args[i]);
            }
        bool presentChanged = false, syncChanged = false;
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--preset") { i++; continue; }
            if (key == "--save-preset")
            {
                s.SavePath = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "preset.json";
                continue;
            }
            if (key == "--curvature")
            {
                if (++i >= args.Length) throw new ArgumentException(L.T("--curvature требует число."));
                s.CurvatureX = s.CurvatureY = Float(args[i]); continue;
            }
            var option = Options.FirstOrDefault(x => "--" + x.Option.Name == key);
            if (option.Property == null) throw new ArgumentException(L.F("Неизвестный параметр {0}. См. --help.", key));
            string value;
            if (option.Property.PropertyType == typeof(bool) && (i + 1 == args.Length || args[i + 1].StartsWith("--"))) value = "on";
            else if (++i < args.Length) value = args[i];
            else throw new ArgumentException(L.F("Нет значения для {0}.", key));
            Type t = option.Property.PropertyType;
            object parsed;
            if (t == typeof(string)) parsed = value;
            else if (t == typeof(bool)) parsed = ParseBool(value);
            else if (t == typeof(int)) parsed = int.Parse(value, CultureInfo.InvariantCulture);
            else if (t == typeof(uint)) parsed = uint.Parse(value, CultureInfo.InvariantCulture);
            else parsed = Float(value);
            option.Property.SetValue(s, parsed);
            presentChanged |= key == "--present"; syncChanged |= key == "--sync-interval";
        }
        if (presentChanged && !syncChanged) s.SyncInterval = s.Present.Equals("vsync", StringComparison.OrdinalIgnoreCase) ? 1u : 0u;
        s.Validate();
        if (s.SavePath != null) s.Save(s.SavePath);
        return s;
    }

    private static float Float(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static bool ParseBool(string text) => text.ToLowerInvariant() switch
    {
        "on" or "true" or "1" => true,
        "off" or "false" or "0" => false,
        _ => throw new ArgumentException(L.T("Логический параметр: on или off."))
    };
    internal static System.Numerics.Vector2 Pair(string value)
    {
        string[] a = value.Split(',');
        if (a.Length != 2) throw new ArgumentException(L.T("Сведение: две координаты x,y от -10 до 10 пикселей."));
        float x = Float(a[0]), y = Float(a[1]);
        if (!float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 10 || Math.Abs(y) > 10)
            throw new ArgumentException(L.T("Сведение: диапазон -10..10 пикселей."));
        return new(x, y);
    }
    public void Validate()
    {
        if (SchemaVersion is < 1 or > 2)
            throw new ArgumentException(L.F("Версия JSON-пресета {0} не поддерживается. Поддерживаются версии 1 и 2.", SchemaVersion));
        foreach (var (p, o) in Options)
        {
            object? value = p.GetValue(this);
            if (value == null) throw new ArgumentException(L.F("{0} не может быть null.", p.Name));
            if (p.PropertyType == typeof(string))
            {
                string v = (string)value;
                if (o.Choices == "x,y") { Pair(v); continue; }
                string? match = o.Choices!.Split('|').FirstOrDefault(x => x.Equals(v, StringComparison.OrdinalIgnoreCase));
                if (match == null) throw new ArgumentException($"--{o.Name}: {o.Choices}.");
                p.SetValue(this, match);
            }
            else if (p.PropertyType != typeof(bool))
            {
                double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (!double.IsFinite(n) || n < o.Min || n > o.Max)
                    throw new ArgumentException(L.F("--{0}: диапазон {1}..{2}.", o.Name, o.Min, o.Max));
            }
        }
        if (BeamMin > BeamMax) throw new ArgumentException(L.T("beam-min должен быть <= beam-max."));
        if (Fps is > 0 and < 30) throw new ArgumentException(L.T("--fps: 0 (автоматически) или 30..1000."));
        if (RetroWidth is > 0 and < 64 || RetroHeight is > 0 and < 64)
            throw new ArgumentException(L.T("retro-width/height: 0 (разрешение пресета) или 64..2048."));
        if ((Present == "vsync" && SyncInterval == 0) || (Present != "vsync" && SyncInterval != 0))
            throw new ArgumentException(L.T("vsync требует sync-interval 1..4; immediate/tearing — 0."));
    }
    public void Save(string path)
    {
        var saved = Clone(); saved.SchemaVersion = 2; saved.Validate();
        File.WriteAllText(path, JsonSerializer.Serialize(saved, Json));
    }
    public CrtSettings Clone() => (CrtSettings)MemberwiseClone();
    public static CrtSettings Load(string path)
    {
        if (Names.Skip(1).Contains(path, StringComparer.OrdinalIgnoreCase))
            return BuiltIn(Array.FindIndex(Names, x => x.Equals(path, StringComparison.OrdinalIgnoreCase)));
        string text = File.ReadAllText(path);
        // Read version before strict deserialization, so a future schema has a clear error
        // even when it also contains fields this build does not recognize.
        using var document = JsonDocument.Parse(text);
        int version = 1;
        if (document.RootElement.ValueKind == JsonValueKind.Object)
            foreach (var field in document.RootElement.EnumerateObject())
                if (field.Name.Equals(nameof(SchemaVersion), StringComparison.OrdinalIgnoreCase))
                {
                    if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out version) || version is < 1 or > 2)
                        throw new ArgumentException(L.F("Версия JSON-пресета {0} не поддерживается. Поддерживаются версии 1 и 2.", field.Value.ToString()));
                }
        var s = JsonSerializer.Deserialize<CrtSettings>(text, Json)
            ?? throw new ArgumentException(L.T("Пустой JSON-пресет."));
        s.SchemaVersion = version;
        s.Validate(); return s;
    }
    public static readonly string[] Names = ["Startup", "Soft", "Balanced", "Strong", "Rolling",
        "PSX", "PSX-Composite", "MegaDrive", "MegaDrive-Composite", "SNES", "MasterSystem", "GameBoy", "Philips-A51EAL155X"];
    public static CrtSettings BuiltIn(int index) => index switch
    {
        1 => new() { CurvatureX = .035f, CurvatureY = .035f, MaskStrength = .15f, ScanlineStrength = .25f, Noise = .001f, Bloom = .12f, Persistence = .15f },
        3 => new() { CurvatureX = .12f, CurvatureY = .12f, MaskStrength = .6f, ScanlineStrength = .7f, Bloom = .35f, Halation = .2f, Noise = .008f },
        4 => new() { RollingScan = true, RollingCompensation = true, Persistence = .7f, ScanHz = 60, Brightness = 1, Noise = .002f, Flicker = 0, Phosphor = "custom", DecayR = 9, DecayG = 7, DecayB = 5 },
        5 => ConsolePreset("psx", 0),
        6 => ConsolePreset("psx", .7f),
        7 => ConsolePreset("megadrive", 0),
        8 => ConsolePreset("megadrive", .85f),
        9 => ConsolePreset("snes", .2f),
        10 => ConsolePreset("mastersystem", .3f),
        11 => ConsolePreset("gameboy", 0),
        12 => new()
        {
            TubeProfile = "philips-a51eal155x",
            CurvatureX = 0, CurvatureY = 0,
            ConvergenceR = "0,0", ConvergenceB = "0,0",
            Noise = 0, Flicker = 0, GlassReflection = 0, Vignette = 0,
            // Effective user settings, not measured Philips decay constants.
            Phosphor = "custom", DecayR = 6, DecayG = 4, DecayB = 2
        },
        _ => new()
    };
    private static CrtSettings ConsolePreset(string style, float composite)
    {
        var s = new CrtSettings
        {
            ConsoleStyle = style, Composite = composite, Noise = .001f, Flicker = 0,
            Persistence = .12f, CurvatureX = .045f, CurvatureY = .045f,
            MaskStrength = .2f, ScanlineStrength = .4f, Bloom = .12f, Halation = .06f,
            Focus = .2f, HorizontalBlur = .15f, Gamma = 2.2f,
            ConvergenceR = "0.15,0.0", ConvergenceB = "-0.15,0.0",
            RetroDither = style == "psx" ? 1 : style == "megadrive" ? .65f : .3f
        };
        s.Scanlines = s.RetroResolution.Height;
        if (style == "gameboy")
        {
            // Game Boy — LCD, поэтому не добавляем маску/развёртку и геометрию трубки.
            s.CurvatureX = s.CurvatureY = s.ScanlineStrength = s.MaskStrength = 0;
            s.Mask = "none"; s.Bloom = s.Halation = s.Focus = s.HorizontalBlur = 0;
            s.ConvergenceR = s.ConvergenceB = "0,0"; s.Noise = s.GlassReflection = 0;
            s.Vignette = .08f; s.Persistence = .25f; s.Phosphor = "custom";
            s.DecayR = s.DecayG = s.DecayB = 70; s.RetroDither = .5f;
            s.Scanlines = 180; // Параметр не используется при scanline-strength=0.
        }
        return s;
    }
    [JsonIgnore] public (int Width, int Height) RetroResolution
    {
        get
        {
            (int w, int h) = ConsoleStyle switch
            {
                "megadrive" => (320, 224), "snes" => (256, 224),
                "mastersystem" => (256, 192), "gameboy" => (160, 144), _ => (320, 240)
            };
            return (RetroWidth == 0 ? w : RetroWidth, RetroHeight == 0 ? h : RetroHeight);
        }
    }
    public void KeepRuntime(CrtSettings source)
    {
        Monitor = source.Monitor; Present = source.Present; SyncInterval = source.SyncInterval;
        Fps = source.Fps; TestSeconds = source.TestSeconds; DebugLayer = source.DebugLayer; Cursor = source.Cursor;
        PerformanceLog = source.PerformanceLog;
    }
    public static string Help
    {
        get
        {
            var defaults = new CrtSettings();
            var b = new System.Text.StringBuilder(L.T("CRT Overlay\n--language ru|en  Переключить и запомнить язык интерфейса\n--editor  Открыть редактор пресетов (также у работающего экземпляра)\n--preset FILE|") + string.Join('|', Names.Skip(1)) + L.T("\n--save-preset [FILE]  Сохранить итоговые настройки, по умолчанию preset.json\n--curvature 0..0.4  Задать X и Y одновременно\n--stop  Штатно завершить работающий экземпляр\n--self-test  Проверить шейдеры на GPU без оверлея\n"));
            foreach (var (p, o) in Options)
                b.AppendLine($"--{o.Name,-22} {(p.PropertyType == typeof(bool) ? "on|off" : o.Choices ?? $"{o.Min.ToString(CultureInfo.InvariantCulture)}..{o.Max.ToString(CultureInfo.InvariantCulture)}"),-26} default={Convert.ToString(p.GetValue(defaults), CultureInfo.InvariantCulture)}");
            b.AppendLine(L.T("\nPhilips: отдельный профиль трубки; tube-area/content задают пропорции и размещение.\nОхват и шаг маски берутся из профиля; затухание, пятно и свечение остаются оценочными.\ntube-luminance reference: масштаб 79 / display-white-nits; brightness — отдельная поправка.\n"));
            return b + L.T("\n--fps 0: частота монитора автоматически, включая 60/144 Гц; вручную 30..1000.\nCtrl+Alt+Shift: Q — выход; F11 — эффект; F5 — HLSL; F10 — пресет; E — редактор.\nP4 — монохромный; custom использует decay-r/g/b в мс (tau при persistence=1).\nRolling интегрирует свет за кадр; rolling-compensation сохраняет среднюю яркость. BFI затемняет изображение.\nretro-width/height: 0 = по типу консоли, иначе 64..2048; сетка масштабируется на весь экран.\nretro-filter: усреднение входных пикселей; composite: приближённое смешение цветности.\nКонсольные режимы — постобработка, без эмуляции CPU/GPU, полигонов и игровых палитр.\n");
        }
    }
}
