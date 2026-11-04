namespace PanelMirror;

// Reuse the identity-checked, journalled native mover. Only one real panel is
// away from its origin at a time; mirror slots remain independent.
internal sealed class RealPanelController : IDisposable
{
    private PanelRecall.RecallSession? active;
    internal bool Busy => active is not null;
    internal int ActiveSlot => active?.Record.Slot ?? -1;
    internal bool Present => active?.Present == true;
    internal bool Moves(SourceWindow source) => active is not null && active.Window == source.Handle;
    internal bool HasRecovery => PanelRecall.Storage.Read<PanelRecall.ReturnRecord>("return.json") is not null;

    internal void Summon(SourceWindow source, int slot)
    {
        if (active is not null && ActiveSlot != slot)
            throw new InvalidOperationException(UiText.T("请先放回当前真实面板，再召回另一个移动模式槽位。"));
        if (!source.IsAlive()) throw new InvalidOperationException(UiText.T("源窗口身份已变化，请重新绑定。"));
        if (!PanelRecall.Native.GetCursorPos(out var point)) throw new InvalidOperationException(UiText.T("无法读取指针位置。"));
        if (active is null)
        {
            if (HasRecovery) throw new InvalidOperationException(UiText.T("有待处理的真实面板恢复记录，请先点击恢复。"));
            var chosen = PanelRecall.Native.Describe(source.Handle);
            if (chosen is null || chosen.Pid != source.Identity.ProcessId || chosen.Started != source.Identity.StartedAt)
                throw new InvalidOperationException(UiText.T("请选择真实浮动面板；移动模式不能移动 PS 主窗口或停靠子面板。"));
            active = PanelRecall.RecallSession.Capture(chosen, slot);
        }
        active.Summon(point);
    }

    internal void Restore(int slot = -1)
    {
        if (active is null)
        {
            var record = PanelRecall.Storage.Read<PanelRecall.ReturnRecord>("return.json");
            if (record is null || (slot >= 0 && record.Slot != slot)) return;
            if (PanelRecall.RecallSession.IsDefinitelyGone(record)) { PanelRecall.Storage.ClearReturnRecord(); return; }
            active = PanelRecall.RecallSession.Recover(record);
        }
        if (slot >= 0 && ActiveSlot != slot) return;
        if (active.Returning) return;
        if (active.DiscardIfGone()) { Complete(); return; }
        active.Restore();
    }

    internal string? Tick()
    {
        if (active is null) return null;
        try
        {
            if (active.DiscardIfGone()) { Complete(); return UiText.T("原面板已关闭，已清理失效恢复记录。"); }
            string? message = active.Tick(false);
            if (active.Finished) Complete();
            return message;
        }
        catch { active?.Fail(); throw; }
    }

    private void Complete() { active?.Dispose(); active = null; }
    public void Dispose() { Complete(); } // Outstanding journal is deliberately preserved.
}
