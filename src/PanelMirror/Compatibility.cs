namespace PanelMirror;

internal static class Compatibility
{
    internal const string Targets = "Photoshop 2019（20.x）及以上（兼容目标，尚未实机验证）";
    internal static string Display(string process, int major)
    {
        if (!process.Equals("Photoshop", StringComparison.OrdinalIgnoreCase)) return process;
        return major switch
        {
            20 => "Photoshop 2019 (20.x)",
            21 => "Photoshop 2020 (21.x)",
            26 => "Photoshop 2025 (26.x)",
            0 => "Photoshop（版本未识别）",
            >= 20 => $"Photoshop {major}.x（兼容目标，待验证）",
            _ => $"Photoshop {major}.x（低于最低兼容目标）"
        };
    }
}
