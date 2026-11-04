namespace PanelMirror;

internal static class Compatibility
{
    internal static string Targets => UiText.T("Photoshop 2019（20.x）及以上（兼容目标，尚未实机验证）");
    internal static string Display(string process, int major)
    {
        if (!process.Equals("Photoshop", StringComparison.OrdinalIgnoreCase)) return process;
        return major switch
        {
            20 => "Photoshop 2019 (20.x)",
            21 => "Photoshop 2020 (21.x)",
            26 => "Photoshop 2025 (26.x)",
            0 => UiText.T("Photoshop（版本未识别）"),
            >= 20 => UiText.F("Photoshop {0}.x（兼容目标，待验证）", major),
            _ => UiText.F("Photoshop {0}.x（低于最低兼容目标）", major)
        };
    }
}
