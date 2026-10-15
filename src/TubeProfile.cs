using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrtOverlay;

// An embedded, versioned component description, separate from user image presets.
// Loaded once, only when a physical profile is selected; never read in Render.
internal sealed class TubeProfile
{
    public required int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Component { get; init; }
    public string? Chassis { get; init; }
    public required Dictionary<string, Source> Sources { get; init; }
    public required Dictionary<string, Parameter> Parameters { get; init; }
    public required string[] Assumptions { get; init; }
    internal sealed class Source
    {
        public required string Title { get; init; }
        public required string Url { get; init; }
        public required string Verified { get; init; }
        public required string Hosting { get; init; }
    }
    internal sealed class Parameter
    {
        public float[]? Value { get; init; }
        public required string Units { get; init; }
        public required string Origin { get; init; }
        public string? Source { get; init; }
        public required int[] Pages { get; init; }
        public required string Conditions { get; init; }
        public string? Fallback { get; init; }
    }
    private static readonly Lazy<TubeProfile> Philips = new(() => Load("philips-a51eal155x"));
    public static TubeProfile? Resolve(string id) => id switch
    {
        "legacy" => null,
        "philips-a51eal155x" => Philips.Value,
        _ => throw new ArgumentException("Unknown tube profile: " + id)
    };
    public float[] Values(string key) => Parameters[key].Value
        ?? throw new InvalidDataException("Unknown physical parameter: " + key);
    private static TubeProfile Load(string id)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CrtOverlay.Profiles." + id + ".json")
            ?? throw new InvalidDataException("Missing tube profile: " + id);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.GetProperty("SchemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported tube profile schema (expected 1).");
        var profile = document.Deserialize<TubeProfile>(new JsonSerializerOptions
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })!;
        if (profile.Id != id) throw new InvalidDataException("Tube profile ID mismatch.");
        foreach (var (name, p) in profile.Parameters)
        {
            if (p.Origin is not ("documented" or "measured" or "estimated" or "unknown")
                || (p.Value == null) != (p.Origin == "unknown")
                || (p.Value == null && string.IsNullOrWhiteSpace(p.Fallback))
                || p.Value?.Any(v => !float.IsFinite(v)) == true
                || (p.Source != null && !profile.Sources.ContainsKey(p.Source)))
                throw new InvalidDataException("Invalid tube parameter: " + name);
        }
        profile.Require("usefulSize", "mm", 2, positive: true);
        profile.Require("stripePitch", "mm", 1, positive: true);
        foreach (string name in new[] { "red", "green", "blue", "white" })
        {
            profile.Require(name, "CIE xy", 2, positive: true);
            var xy = profile.Values(name);
            if (xy[0] + xy[1] > 1.001f) throw new InvalidDataException("Invalid chromaticity: " + name);
        }
        profile.Require("surfaceA", "mm-based coefficients", 3);
        profile.Require("surfaceB", "exponents", 4, positive: true);
        profile.Require("stripeFill", "fraction", 1, positive: true);
        profile.Require("glassTransmission", "fraction", 1, positive: true);
        profile.Require("referenceLuminance", "cd/m2", 1, positive: true);
        if (profile.Values("stripeFill")[0] > 1 || profile.Values("glassTransmission")[0] > 1)
            throw new InvalidDataException("Invalid tube fraction.");
        return profile;
    }
    private void Require(string key, string units, int count, bool positive = false)
    {
        if (!Parameters.TryGetValue(key, out var p) || p.Units != units || p.Value?.Length != count
            || (positive && p.Value.Any(v => v <= 0)))
            throw new InvalidDataException("Invalid tube parameter shape/units: " + key);
    }
}
