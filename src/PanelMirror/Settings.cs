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
    public bool Configured => ProcessName.Length > 0;
}

internal sealed class Settings
{
    public Binding[] Slots { get; set; } = Enumerable.Range(0, 5).Select(_ => new Binding()).ToArray();
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PanelMirror", "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("配置文件为空。");
        if (settings.Slots is null || settings.Slots.Length != 5 || settings.Slots.Any(x => x is null))
            throw new InvalidDataException("配置必须包含五个槽位。");
        foreach (var slot in settings.Slots)
        {
            if ((slot.SummonKey is not null && !slot.SummonKey.Valid) || (slot.ReturnKey is not null && !slot.ReturnKey.Valid) ||
                !Enum.IsDefined(slot.Mode) || slot.ProcessName is null || slot.WindowClass is null || slot.Title is null || slot.PhotoshopMajor < 0 ||
                slot.X < 0 || slot.Y < 0 || slot.Width < 0 || slot.Height < 0 ||
                slot.X > 100000 || slot.Y > 100000 || slot.Width > 100000 || slot.Height > 100000 ||
                (slot.Width == 0) != (slot.Height == 0))
                throw new InvalidDataException("槽位的窗口标识或裁剪数据无效。");
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
