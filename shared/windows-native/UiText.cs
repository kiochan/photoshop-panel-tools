using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization.Json;

// Shared by the .NET 8 tool and C# 5 / .NET Framework prototype.
// Translation keys are complete source strings; window identities are never translated.
internal static class UiText
{
    private static readonly Dictionary<string, string> english = LoadEnglish();
    private static bool chinese = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    internal static void SetLanguage(string language)
    {
        if (language != "auto" && language != "zh-CN" && language != "en")
            throw new ArgumentException("Unsupported interface language.", "language");
        chinese = language == "zh-CN" || (language == "auto" && CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
    }

    internal static string T(string source)
    {
        return !chinese && english.ContainsKey(source) ? english[source] : source;
    }

    internal static string F(string source, params object[] values)
    { return String.Format(CultureInfo.CurrentCulture, T(source), values); }

    private static Dictionary<string, string> LoadEnglish()
    {
        using (var stream = typeof(UiText).Assembly.GetManifestResourceStream("PanelTools.Strings.en.json"))
        {
            if (stream == null) throw new InvalidOperationException("Missing embedded interface translations.");
            var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            var result = serializer.ReadObject(stream) as Dictionary<string, string>;
            if (result == null) throw new InvalidOperationException("Invalid embedded interface translations.");
            return result;
        }
    }
}
