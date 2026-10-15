using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CrtOverlay;

// Русский текст служит ключом и резервным переводом. Английский каталог встроен
// в EXE; язык интерфейса хранится отдельно от графических JSON-пресетов.
internal static class L
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string,string> English = LoadEnglish();
    private static int english;
    public const uint ChangedMessage = 0x8060;
    public static event Action? Changed;
    public static string Language => Volatile.Read(ref english) == 0 ? "ru" : "en";
    private static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CrtOverlay","ui.json");
    private static Dictionary<string,string> LoadEnglish()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CrtOverlay.Locales.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
    public static void Initialize()
    {
        string language = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
        try
        {
            if (File.Exists(PreferencePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(PreferencePath));
                if (document.RootElement.TryGetProperty("Language",out var setting) && setting.ValueKind == JsonValueKind.String
                    && setting.GetString() is "ru" or "en") language = setting.GetString()!;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
        Volatile.Write(ref english,language == "en" ? 1 : 0); ApplyThreadCulture();
    }
    public static void ApplyThreadCulture() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Language == "ru" ? "ru-RU" : "en-US");
    public static void SetLanguage(string language)
    {
        language = language.ToLowerInvariant();
        if (language is not ("ru" or "en")) throw new ArgumentException(T("Язык должен быть ru или en."));
        lock (Gate)
        {
            Volatile.Write(ref english,language == "en" ? 1 : 0);
            string temporary = PreferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
                File.WriteAllText(temporary,JsonSerializer.Serialize(new { Language = language }));
                File.Move(temporary,PreferencePath,true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Program.Log("UI language was changed but could not be saved: " + ex.Message); }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        ApplyThreadCulture(); Changed?.Invoke();
    }
    public static string T(string russian) => Volatile.Read(ref english) != 0 && English.TryGetValue(russian,out string? value) ? value : russian;
    public static string F(string russian, params object[] args) => string.Format(CultureInfo.InvariantCulture,T(russian),args);
    public static string Preset(string name) => name switch
    {
        "Startup" => T("Исходный"), "Soft" => T("Мягкий"), "Balanced" => T("Сбалансированный"),
        "Strong" => T("Выраженный"), "Rolling" => T("Бегущая развёртка"),
        "Philips-A51EAL155X" => "Philips A51EAL155X", _ => name
    };
    public static string Choice(string value) => value switch
    {
        "legacy" => T("Прежний художественный режим"), "philips-a51eal155x" => "Philips A51EAL155X",
        "fit" => T("Вписать с сохранением пропорций"), "crop" => T("Заполнить с обрезкой"), "stretch" => T("Растянуть"),
        "reference" => T("По условиям даташита"), "temperature" => T("Пользовательская температура"), "relative" => T("Относительная яркость"),
        "none" => T("Нет"), "aperture" => T("Апертурная решётка"), "shadow" => T("Теневая маска"),
        "slot" => T("Щелевая маска"), "custom" => T("Пользовательский"), "psx" => "PlayStation / PSX",
        "megadrive" => "Sega Mega Drive", "mastersystem" => "Sega Master System", "snes" => "SNES", "gameboy" => "Game Boy", _ => value
    };
}
