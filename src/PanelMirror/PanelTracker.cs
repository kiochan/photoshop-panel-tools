namespace PanelMirror;

// Native identity tracking only. No pixel/template guesses, pointer movement,
// injection, activation, reparenting or moving-mode fallback.
internal sealed class PanelTracker : IDisposable
{
    private const int MaxWindows = 512, MaxDepth = 32;
    private readonly SourceWindow original;
    private readonly Binding binding;
    private readonly string property = "PanelMirror.Track." + Guid.NewGuid().ToString("N");
    private readonly Dictionary<nint, WindowStamp> stamps = new();
    private readonly Native.WinEventProc callback;
    private readonly HashSet<nint> ancestors = new();
    private nint anchor, hook;
    private long generation, acceptedAt;
    private bool disposed, identityLost;
    private TrackedFrame? accepted;
    internal SourceWindow CurrentSource { get; private set; }

    internal PanelTracker(SourceWindow source, Binding binding)
    {
        original = source; CurrentSource = source; this.binding = binding;
        callback = OnWinEvent;
    }

    internal static PanelAnchor? CaptureAnchor(SourceWindow source, Rectangle selected, out Rectangle crop, out string reason)
    {
        crop = selected;
        reason = UiText.T("未能唯一识别命名面板，已保存为仅预览；请重新拾取明确的面板。");
        try
        {
            if (!source.IsAlive() || !Native.ClientBounds(source.Handle, out var rootBounds)) return null;
            var client = new Rectangle(Point.Empty, rootBounds.Size);
            if (selected.Width == 0 && selected.Height == 0) selected = client;
            if (!client.Contains(selected)) return null;
            // Manual crops remain exactly as selected, relative to this one
            // floating panel's client area. Never snap to a dock/child surface.
            if (!Native.SupportedPanel(source.Identity) || !Identifiable(source.Handle)) return null;
            nint chosen = source.Handle;
            crop = selected;
            bool whole = crop == client;
            var chosenBounds = rootBounds;
            var descriptor = new PanelAnchor
            {
                WindowClass = Native.ClassName(chosen), Title = Native.WindowTitle(chosen),
                ControlId = Native.GetDlgCtrlID(chosen), TopLevel = chosen == source.Handle,
                WholeClient = whole, ReferenceWidth = chosenBounds.Width, ReferenceHeight = chosenBounds.Height,
                X = whole ? 0 : crop.X, Y = whole ? 0 : crop.Y,
                Width = whole ? chosenBounds.Width : crop.Width, Height = whole ? chosenBounds.Height : crop.Height
            };
            if (!descriptor.Valid || FindCandidates(ProcessRoots(source.Identity), descriptor).Count != 1) return null;
            reason = UiText.F("已保存命名面板绑定：{0}；布局变化时先暂停，确认后恢复跟踪。", descriptor.Title);
            return descriptor;
        }
        catch (InvalidOperationException) { return null; }
    }

    internal static SourceWindow? ResolveRoot(Binding binding)
    {
        if (binding.Anchor is not { Valid: true, TopLevel: true } descriptor) return null;
        var roots = Native.Windows().Where(w => w.ProcessName.Equals(binding.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            (binding.PhotoshopMajor == 0 || w.PhotoshopMajor == binding.PhotoshopMajor)).ToArray();
        if (roots.Select(w => (w.ProcessId, w.StartedAt)).Distinct().Count() != 1)
            throw new InvalidOperationException(UiText.T("源进程不唯一或已退出，已暂停跟踪；请明确重新绑定。"));
        var matches = FindCandidates(roots, descriptor);
        if (matches.Count != 1)
            throw new InvalidOperationException(UiText.T("面板跟踪匹配丢失或不唯一，已暂停输入；请重新绑定。"));
        nint root = Native.GetAncestor(matches[0], 2);
        return Native.Describe(root) is { } current ? new SourceWindow(current) : null;
    }

    internal TrackedFrame? Read(out string reason)
    {
        reason = "";
        if (disposed) return null;
        try
        {
            if (!original.IsAlive() || !Native.SupportedPanel(Native.Describe(original.Handle) ?? original.Identity))
            {
                if (anchor != 0) identityLost = true;
                throw new InvalidOperationException(UiText.T("仅支持独立浮动的 Photoshop 面板；主窗口、文档及停靠区域不可用，请手动浮动后重新绑定。"));
            }
            if (binding.Anchor is null)
            {
                if (!original.IsAlive() || !Native.IsWindowVisible(original.Handle) || Native.IsIconic(original.Handle) ||
                    !Native.IsWindowEnabled(original.Handle) || !original.ReadGeometry(binding, out var previewSize, out var previewArea) ||
                    !Native.ClientBounds(original.Handle, out var previewBounds)) return null;
                reason = UiText.T("固定裁剪尚未锁定面板身份：仅预览，禁止输入；请重新绑定以启用跟踪。");
                return new TrackedFrame(original, 0, previewSize, previewArea, previewBounds.Location, 0, Array.Empty<LayoutNode>(), false);
            }
            if (!binding.Anchor.TopLevel)
                throw new InvalidOperationException(UiText.T("仅支持独立浮动的 Photoshop 面板；主窗口、文档及停靠区域不可用，请手动浮动后重新绑定。"));
            EnsureAnchor();
            if (!Stamp(anchor).Valid || !Matches(anchor, binding.Anchor) || HasDocumentAncestor(anchor))
            {
                identityLost = true;
                throw new InvalidOperationException(UiText.T("面板身份已变化，已暂停输入；请重新绑定。"));
            }
            long before = Interlocked.Read(ref generation);
            nint root = Native.GetAncestor(anchor, 2);
            var identity = Native.Describe(root);
            if (identity is null || identity.ProcessId != original.Identity.ProcessId || identity.StartedAt != original.Identity.StartedAt || root != anchor || root != original.Handle || !Native.SupportedPanel(identity) ||
                !Native.IsWindowVisible(root) || Native.IsIconic(root) || !Native.IsWindowEnabled(root) ||
                !Native.ClientBounds(root, out var rootBounds) || !Native.ClientBounds(anchor, out var anchorBounds))
                throw new InvalidOperationException(UiText.T("面板或宿主状态无法确认，已暂停输入。"));
            ancestors.Clear();
            nint parent = anchor;
            var layout = new List<LayoutNode>();
            while (parent != 0 && ancestors.Add(parent) && ancestors.Count <= MaxDepth)
            {
                if (!Native.IsWindowVisible(parent) || !Native.IsWindowEnabled(parent) ||
                    !Native.ClientBounds(parent, out var parentBounds) || !parentBounds.Contains(anchorBounds))
                    throw new InvalidOperationException(UiText.T("面板被隐藏、禁用或裁切，已暂停输入。"));
                layout.Add(Node(parent));
                if (parent == root) break;
                parent = Native.GetAncestor(parent, 1);
            }
            if (parent != root) throw new InvalidOperationException(UiText.T("面板父级关系无法确认，已暂停输入。"));
            foreach (nint window in WindowTree(anchor, original.Identity.ProcessId)) layout.Add(Node(window));
            Rectangle area;
            var descriptor = binding.Anchor;
            if (descriptor.WholeClient) area = anchorBounds;
            else
            {
                if (anchorBounds.Size != new Size(descriptor.ReferenceWidth, descriptor.ReferenceHeight))
                    throw new InvalidOperationException(UiText.T("部分裁剪的面板尺寸已改变，已暂停输入；请重新裁剪并绑定。"));
                area = new Rectangle(anchorBounds.X + descriptor.X, anchorBounds.Y + descriptor.Y, descriptor.Width, descriptor.Height);
            }
            if (!anchorBounds.Contains(area) || !rootBounds.Contains(area) || before != Interlocked.Read(ref generation))
                throw new InvalidOperationException(UiText.T("布局正在变化，已暂停输入并等待稳定。"));
            area.Offset(-rootBounds.X, -rootBounds.Y);
            CurrentSource = root == CurrentSource.Handle ? CurrentSource : new SourceWindow(identity);
            return new TrackedFrame(CurrentSource, anchor, rootBounds.Size, area, rootBounds.Location, before, layout.ToArray(), true);
        }
        catch (InvalidOperationException error) { Block(); reason = error.Message; return null; }
    }

    internal void Accept(TrackedFrame frame) { accepted = frame.Trusted ? frame : null; acceptedAt = Environment.TickCount64; }
    internal void Block() => accepted = null;

    internal bool CanForward(out string reason)
    {
        reason = UiText.T("布局尚未重新确认，输入已暂停。");
        var previous = accepted;
        if (previous is null || Environment.TickCount64 - acceptedAt > 500) { Block(); return false; }
        var current = Read(out var error);
        if (current is null || !current.Trusted || !previous.SameLayout(current))
        { Block(); if (error.Length > 0) reason = error; return false; }
        return true;
    }

    internal nint Hit(Native.POINT screen)
    {
        var frame = accepted;
        if (frame is null) return 0;
        nint hit = frame.Source.Hit(screen);
        return hit != 0 && (hit == anchor || Native.IsChild(anchor, hit)) && HasStamp(hit) ? hit : 0;
    }

    internal bool CanPost(nint target, bool cleanup)
    {
        // Cleanup is restricted to already stamped original targets. It never
        // resolves a replacement and never initiates a click/key-down.
        if (!HasStamp(target)) return false;
        if (cleanup) return global::ProcessIdentity.Started(original.Identity.ProcessId) == original.Identity.StartedAt;
        return CanForward(out _) && Native.IsWindowEnabled(target) && (target == anchor || Native.IsChild(anchor, target));
    }

    private void EnsureAnchor()
    {
        if (identityLost) throw new InvalidOperationException(UiText.T("跟踪窗口已被重建或身份标记丢失；禁止自动换目标，请重新绑定。"));
        if (anchor != 0)
        {
            if (!HasStamp(anchor)) { identityLost = true; EnsureAnchor(); }
            if (hook == 0) throw new InvalidOperationException(UiText.T("无法监听布局变化，已暂停输入；请检查权限后重新绑定。"));
            return;
        }
        var matches = FindCandidates(ProcessRoots(original.Identity), binding.Anchor!);
        if (matches.Count != 1) throw new InvalidOperationException(UiText.T("面板跟踪匹配丢失或不唯一，已暂停输入；请重新绑定。"));
        anchor = matches[0];
        Stamp(anchor);
        // OUTOFCONTEXT | SKIPOWNPROCESS: callbacks run in this helper's message
        // loop. No DLL is injected into Photoshop or a plugin.
        hook = Native.SetWinEventHook(0x8000, 0x800F, 0, callback, original.Identity.ProcessId, 0, 2);
        if (hook == 0) throw new InvalidOperationException(UiText.T("无法监听布局变化，已暂停输入；请检查权限后重新绑定。"));
    }

    private void OnWinEvent(nint eventHook, uint type, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || window == 0 || childId != 0 || (objectId != 0 && objectId != -4)) return;
        bool relevant = type switch
        {
            0x800A => window == anchor || ancestors.Contains(window), // state: not a button's transient pressed state
            0x800C => window == anchor, // an anchor name is identity; descendant value/text changes are not layout
            0x8000 or 0x8001 or 0x8002 or 0x8003 or 0x8004 or 0x800B or 0x800F =>
                stamps.ContainsKey(window) || Native.IsChild(anchor, window) || Native.IsChild(window, anchor),
            _ => false
        };
        if (relevant) { Interlocked.Increment(ref generation); Block(); }
    }

    private LayoutNode Node(nint window)
    {
        var stamp = Stamp(window);
        bool visible = Native.IsWindowVisible(window);
        if (!Native.ClientBounds(window, out var bounds) && visible)
            throw new InvalidOperationException(UiText.T("控件布局无法读取，已暂停输入。"));
        return new LayoutNode(window, Native.GetAncestor(window, 1), stamp.Cookie, stamp.Class,
            Native.GetDlgCtrlID(window), bounds, visible, Native.IsWindowEnabled(window));
    }

    private bool HasStamp(nint window) => stamps.TryGetValue(window, out var stamp) && stamp.Valid;
    private WindowStamp Stamp(nint window)
    {
        if (stamps.TryGetValue(window, out var existing))
        {
            if (!existing.Valid) throw new InvalidOperationException(UiText.T("控件身份标记已变化，已暂停输入；请重新绑定。"));
            return existing;
        }
        if (stamps.Count >= 4096) throw new InvalidOperationException(UiText.T("窗口结构超出跟踪范围，已暂停输入。"));
        Native.GetWindowThreadProcessId(window, out uint pid);
        if (pid != original.Identity.ProcessId || !Native.IsWindow(window))
            throw new InvalidOperationException(UiText.T("控件不属于已绑定进程，已暂停输入。"));
        nint cookie = (nint)(BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) | 1);
        string name = Native.ClassName(window);
        if (name.Length == 0) throw new InvalidOperationException(UiText.T("控件身份标记已变化，已暂停输入；请重新绑定。"));
        var result = new WindowStamp(window, pid, name, cookie, property);
        if (!Native.SetProp(window, property, cookie) || !result.Valid)
        {
            if (Native.GetProp(window, property) == cookie) Native.RemoveProp(window, property);
            throw new InvalidOperationException(UiText.T("无法标记控件身份，已暂停输入；请检查与宿主的权限是否一致。"));
        }
        stamps.Add(window, result); return result;
    }

    private static WindowItem[] ProcessRoots(WindowItem identity) => Native.Windows().Where(w =>
        w.ProcessId == identity.ProcessId && w.StartedAt == identity.StartedAt).ToArray();

    private static List<nint> FindCandidates(IEnumerable<WindowItem> roots, PanelAnchor descriptor)
    {
        var result = new List<nint>();
        if (!descriptor.TopLevel) return result;
        foreach (var root in roots)
            if (Native.SupportedPanel(root) && Native.IsWindowVisible(root.Handle) && Matches(root.Handle, descriptor)) result.Add(root.Handle);
        return result;
    }

    private static bool Matches(nint window, PanelAnchor descriptor) => Identifiable(window) &&
        Native.ClassName(window) == descriptor.WindowClass && Native.WindowTitle(window) == descriptor.Title &&
        Native.GetDlgCtrlID(window) == descriptor.ControlId;

    private static bool Identifiable(nint window)
    {
        string name = Native.ClassName(window), title = Native.WindowTitle(window).Trim();
        if (name.Length == 0 || name.Length >= 255 || title.Length == 0 || title.Length >= 1023 ||
            title is "Chrome Legacy Window" or "Chrome_RenderWidgetHostHWND") return false;
        if (name is "Photoshop" or "PSViewC" or "PSDocC" or "OWL.DocumentWindow" or "#32770" or "#32768" or
            "Button" or "Edit" or "Static" or "ComboBox" or "ListBox" or "ScrollBar" or "tooltips_class32") return false;
        string upper = name.ToUpperInvariant();
        return !upper.StartsWith("WINDOWSFORMS10.BUTTON", StringComparison.Ordinal) &&
            !upper.StartsWith("WINDOWSFORMS10.EDIT", StringComparison.Ordinal) &&
            !upper.StartsWith("WINDOWSFORMS10.STATIC", StringComparison.Ordinal) &&
            !upper.StartsWith("WINDOWSFORMS10.COMBOBOX", StringComparison.Ordinal);
    }

    private static bool HasDocumentAncestor(nint window)
    {
        var visited = new HashSet<nint>();
        for (int depth = 0; window != 0 && depth < MaxDepth && visited.Add(window); depth++, window = Native.GetAncestor(window, 1))
            if (Native.ClassName(window) is "OWL.DocumentWindow" or "PSDocC") return true;
        return window != 0; // An unknown, cyclic or excessively deep chain is not a panel identity.
    }

    private static IEnumerable<nint> WindowTree(nint root, uint process)
    {
        var pending = new Stack<(nint Handle, int Depth)>();
        var seen = new HashSet<nint>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (window, depth) = pending.Pop();
            if (depth > MaxDepth || !seen.Add(window) || seen.Count > MaxWindows)
                throw new InvalidOperationException(UiText.T("窗口结构超出跟踪范围，已暂停输入。"));
            Native.GetWindowThreadProcessId(window, out uint pid);
            if (!Native.IsWindow(window) || pid != process)
                throw new InvalidOperationException(UiText.T("控件不属于已绑定进程，已暂停输入。"));
            yield return window;
            nint child = Native.GetWindow(window, 5);
            var siblings = new HashSet<nint>();
            while (child != 0)
            {
                if (!siblings.Add(child) || siblings.Count > MaxWindows)
                    throw new InvalidOperationException(UiText.T("窗口结构超出跟踪范围，已暂停输入。"));
                pending.Push((child, depth + 1)); child = Native.GetWindow(child, 2);
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Block();
        if (hook != 0) { Native.UnhookWinEvent(hook); hook = 0; }
        foreach (var stamp in stamps.Values) if (stamp.Valid) Native.RemoveProp(stamp.Window, property);
        stamps.Clear();
    }

    private sealed record WindowStamp(nint Window, uint Process, string Class, nint Cookie, string Property)
    {
        internal bool Valid
        {
            get
            {
                Native.GetWindowThreadProcessId(Window, out uint pid);
                return Native.IsWindow(Window) && pid == Process && Native.GetProp(Window, Property) == Cookie && Native.ClassName(Window) == Class;
            }
        }
    }
}

internal sealed record LayoutNode(nint Window, nint Parent, nint Cookie, string Class, int Id, Rectangle Bounds, bool Visible, bool Enabled);

internal sealed record TrackedFrame(SourceWindow Source, nint Anchor, Size ClientSize, Rectangle Area, Point Origin,
    long Generation, LayoutNode[] Layout, bool Trusted)
{
    internal bool SameLayout(TrackedFrame other) => Source.Handle == other.Source.Handle && Anchor == other.Anchor &&
        ClientSize == other.ClientSize && Area == other.Area && Origin == other.Origin && Generation == other.Generation &&
        Trusted == other.Trusted && Layout.SequenceEqual(other.Layout);
}
