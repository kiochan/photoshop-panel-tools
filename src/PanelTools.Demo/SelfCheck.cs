using System.Text.Json;
using System.Text.RegularExpressions;
using PanelMirror;

namespace PanelTools.Demo;

// Headless checks use only the embedded catalog and a fresh isolated settings
// directory. They do not construct forms, enumerate windows or install hooks.
internal static class SelfCheck
{
    internal static void Run()
    {
        using var stream = typeof(Settings).Assembly.GetManifestResourceStream("PanelTools.Strings.en.json")
            ?? throw new InvalidOperationException("Translation resource is missing from the main assembly.");
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Translation catalog is empty.");
        UiText.SetLanguage("en");
        foreach (var pair in catalog)
        {
            Require(UiText.T(pair.Key) == pair.Value, "English lookup mismatch");
            int max = Regex.Matches(pair.Key, @"\{(\d+)(?::[^}]+)?\}")
                .Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max();
            var values = Enumerable.Repeat((object)42, max + 1).ToArray();
            UiText.F(pair.Key, values); // .NET formatting, including X8 and reordered parameters.
        }
        UiText.SetLanguage("zh-CN");
        Require(UiText.T("取消") == "取消", "Chinese lookup mismatch");
        UiText.SetLanguage("en");
        Require(UiText.T("取消") == "Cancel", "English locale switch failed");

        Settings.DataDirectory = Path.Combine(AppContext.BaseDirectory, "self-check", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Settings.DataDirectory);
        string file = Path.Combine(Settings.DataDirectory, "settings.json");
        File.WriteAllText(file, "{\"Slots\":[{},{},{},{},{}]}");
        var legacy = Settings.Load();
        Require(legacy.Language == "auto" && legacy.Slots.All(b => b.MirrorWidth == 0 && b.MirrorHeight == 0), "Legacy defaults changed");
        legacy.Language = "en";
        legacy.Slots[0].MirrorWidth = 320; legacy.Slots[0].MirrorHeight = 480;
        legacy.Save();
        var roundTrip = Settings.Load();
        Require(roundTrip.Language == "en" && roundTrip.Slots[0].MirrorWidth == 320 && roundTrip.Slots[0].MirrorHeight == 480,
            "Language/size persistence failed");
        legacy.Language = "unsupported"; legacy.Save();
        bool rejected = false;
        try { Settings.Load(); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Invalid settings language accepted");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-check-result.txt"),
            $"PASS: {catalog.Count} embedded English lookups and format templates; Chinese switching; legacy settings; language/size persistence; invalid language rejection.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
