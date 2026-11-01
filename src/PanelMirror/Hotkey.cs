namespace PanelMirror;

internal sealed class Hotkey
{
    public int Key { get; set; }
    // RegisterHotKey flags: Alt=1, Ctrl=2, Shift=4. Win is deliberately excluded.
    public uint Modifiers { get; set; }
    internal bool Valid => Key > 0 && Key <= 255 && Modifiers <= 7 &&
        Enum.IsDefined(typeof(Keys), (Keys)Key) &&
        Key is not (16 or 17 or 18 or 91 or 92 or 93 or 123 or 160 or 161 or 162 or 163 or 164 or 165 or 229 or 231) &&
        (Modifiers != 0 || Key is >= 112 and <= 135);
    internal static Hotkey Default(int slot, bool close = false) => new() { Key = '1' + slot, Modifiers = close ? 5u : 1u };
    internal Hotkey Copy() => new() { Key = Key, Modifiers = Modifiers };
    public override string ToString() => ((Modifiers & 2) != 0 ? "Ctrl+" : "") +
        ((Modifiers & 1) != 0 ? "Alt+" : "") + ((Modifiers & 4) != 0 ? "Shift+" : "") +
        (Key is >= 48 and <= 57 ? ((char)Key).ToString() : ((Keys)Key).ToString());
}

internal sealed class HotkeyBox : TextBox
{
    internal Hotkey Value { get; private set; } = Hotkey.Default(0);
    internal Action<string>? Rejected;
    internal HotkeyBox() { ReadOnly = true; Width = 180; ShortcutsEnabled = false; }
    internal void Set(Hotkey value) { Value = value.Copy(); Text = Value.ToString(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab) || keyData == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        int key = (int)(keyData & Keys.KeyCode);
        if (key is 16 or 17 or 18) return true;
        var value = new Hotkey { Key = key, Modifiers =
            ((keyData & Keys.Alt) != 0 ? 1u : 0u) | ((keyData & Keys.Control) != 0 ? 2u : 0u) | ((keyData & Keys.Shift) != 0 ? 4u : 0u) };
        if (value.Valid) Set(value);
        else Rejected?.Invoke("请使用 Ctrl／Alt／Shift 组合键或功能键；不支持裸字符键、Win 键组合及系统保留的 F12。");
        return true;
    }
}
