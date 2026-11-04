using System.Text.Json;

namespace PanelMirror;

internal enum SummonMode { Mirror, Move }

internal sealed class Binding
{
    public SummonMode Mode { get; set; } = SummonMode.Mirror;
    public bool SeparateReturnKey { get; set; }
    public Hotkey? SummonKey { get; set; }
    public Hotkey? ReturnKey { get; set; }
    internal Hotkey SummonShortcut(int slot) => SummonKey ?? Hotkey.Default(slot);
    internal Hotkey ReturnShortcut(int slot) => ReturnKey ?? Hotkey.Default(slot, true);
    public string ProcessName { get; set; } = "";
    public string WindowClass { get; set; } = "";
    public string Title { get; set; } = "";
    public int PhotoshopMajor { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Keyboard { get; set; }
    // Logical (96 DPI) client size; zero keeps automatic first-use sizing.
    public int MirrorWidth { get; set; }
    public int MirrorHeight { get; set; }
    public bool Configured => ProcessName.Length > 0;
}

internal sealed class Settings
{
    public string Language { get; set; } = "auto";
    public Binding[] Slots { get; set; } = Enumerable.Range(0, 5).Select(_ => new Binding()).ToArray();
    // The source-only demo harness sets this before constructing any forms.
    internal static string DataDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PanelMirror");
    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException(UiText.T("配置文件为空。"));
        if (settings.Language is not ("auto" or "zh-CN" or "en")) throw new InvalidDataException("Unsupported interface language.");
        if (settings.Slots is null || settings.Slots.Length != 5 || settings.Slots.Any(x => x is null))
            throw new InvalidDataException(UiText.T("配置必须包含五个槽位。"));
        foreach (var slot in settings.Slots)
        {
            if ((slot.SummonKey is not null && !slot.SummonKey.Valid) || (slot.ReturnKey is not null && !slot.ReturnKey.Valid) ||
                !Enum.IsDefined(slot.Mode) || slot.ProcessName is null || slot.WindowClass is null || slot.Title is null || slot.PhotoshopMajor < 0 ||
                slot.X < 0 || slot.Y < 0 || slot.Width < 0 || slot.Height < 0 ||
                slot.X > 100000 || slot.Y > 100000 || slot.Width > 100000 || slot.Height > 100000 ||
                (slot.Width == 0) != (slot.Height == 0))
                throw new InvalidDataException(UiText.T("槽位的窗口标识或裁剪数据无效。"));
            if (slot.MirrorWidth < 0 || slot.MirrorHeight < 0 || slot.MirrorWidth > 10000 || slot.MirrorHeight > 10000 ||
                (slot.MirrorWidth == 0) != (slot.MirrorHeight == 0))
                throw new InvalidDataException(UiText.T("镜像尺寸无效。"));
        }
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
