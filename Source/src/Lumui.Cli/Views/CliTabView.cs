using Lumui.Cli.Navigation;
using Lumui.Cli.Rendering;

namespace Lumui.Cli.Views;

public sealed class CliTabView : View
{
    private readonly CliMainWindow _owner;
    private readonly TerminalViewRenderer _viewRenderer;
    private readonly View _menuBar;
    private readonly Button _lumiMenu;
    private readonly Button[] _categoryMenus;
    private readonly View _navigationBar;
    private readonly TextField _address;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _reload;
    private readonly Button _home;
    private readonly Button _open;
    private readonly Button _bookmark;
    private readonly Button _guide;
    private readonly Button _settings;
    private readonly View _workspace;
    private readonly Label _minimumSize;
    private readonly FrameView _outlineFrame;
    private readonly ListView _outline;
    private readonly ObservableCollection<CliOutlineItem> _outlineItems = new ObservableCollection<CliOutlineItem>();
    private readonly TerminalDocumentView _document;
    private readonly View _guidedFooter;
    private readonly Button _previousStep;
    private readonly Button _nextStep;
    private readonly Label _step;
    private readonly View _statusBar;
    private readonly Label _status;
    private readonly Label _shortcuts;
    private Boolean _guided;
    private Boolean _outlineVisible = true;
    private Int32 _guidedStepCount = 1;
    private Int32 _lastWidth;
    private Int32 _lastHeight;
    private Boolean _rendering;
    private TerminalSurfaceDocument? _lastDocument;
    private Boolean _resetDocumentPosition;
    private TerminalSurfaceDocument? _renderedDocument;
    private Int32 _renderedPageIndex = -1;
    private Int32 _renderedContentWidth = -1;
    private Int32 _renderedGuidedStep = -1;
    private Int32 _renderedInputHash;
    private Boolean _renderedGuided;
    private Boolean _documentRenderRequired = true;
    private String _renderedStartTitle = String.Empty;
    private String _renderedStartMessage = String.Empty;
    private Int32 _renderedStartWidth = -1;
    private readonly List<View> _documentFocusTargets = new List<View>();
    private readonly List<View> _cachedFocusTargets = new List<View>();
    private Boolean _focusCacheDirty = true;

    public CliTabView(
        CliMainWindow owner,
        CliTabSession session)
    {
        _owner = owner;
        Session = session;
        _viewRenderer = new TerminalViewRenderer(owner.Preferences);
        _outlineVisible = owner.Preferences.ShowOutline;
        Title = "New tab";
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        SchemeName = "Base";

        _menuBar = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = "Menu",
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        _lumiMenu = MenuButton("Lumi", 0, "All");
        _categoryMenus = new[]
        {
            MenuButton("File", 0, "File"),
            MenuButton("View", 9, "View"),
            MenuButton("Go", 18, "Go"),
            MenuButton("Library", 25, "Library"),
            MenuButton("Tools", 37, "Tools"),
            MenuButton("Help", 47, "Help")
        };
        _menuBar.Add(_lumiMenu);
        _menuBar.Add(_categoryMenus);

        _navigationBar = new View
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 2,
            SchemeName = "Base",
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        _back = NavigationButton("Back", 0);
        _forward = NavigationButton("Forward", 9);
        _reload = NavigationButton("Reload", 21);
        _home = NavigationButton("Home", 32);
        _address = new TextField
        {
            X = 41,
            Y = 0,
            Width = Dim.Fill(38),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };
        _open = new CliButton
        {
            Text = "Open",
            X = Pos.AnchorEnd(46),
            Y = 0,
            Width = 9,
            SchemeName = "Accent"
        };
        _bookmark = new CliButton
        {
            Text = "Bookmark",
            X = Pos.AnchorEnd(35),
            Y = 0,
            Width = 11
        };
        _guide = new CliButton
        {
            Text = "Guided",
            X = Pos.AnchorEnd(23),
            Y = 0,
            Width = 10
        };
        _settings = new CliButton
        {
            Text = "Settings",
            X = Pos.AnchorEnd(12),
            Y = 0,
            Width = 10
        };
        _navigationBar.Add(_back, _forward, _reload, _home, _address, _open, _bookmark, _guide, _settings);

        _workspace = new View
        {
            X = 0,
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        _outlineFrame = new FrameView
        {
            Title = "Contents",
            X = 0,
            Y = 0,
            Width = 27,
            Height = Dim.Fill(),
            SchemeName = "Accent",
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        _outline = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };
        _outline.HorizontalScrollBar.Visible = true;
        _outline.VerticalScrollBar.Visible = true;
        _outline.SetSource(_outlineItems);
        _outlineFrame.Add(_outline);
        _document = new TerminalDocumentView
        {
            X = 28,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _workspace.Add(_outlineFrame, _document);
        _minimumSize = new Label
        {
            Text = "Lumi needs a terminal of at least 48 columns by 12 rows.\nResize the terminal to continue.",
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Visible = false,
            SchemeName = "Error"
        };

        _guidedFooter = new View
        {
            X = 0,
            Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(),
            Height = 3,
            Visible = false,
            SchemeName = "Menu",
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        _previousStep = new CliButton
        {
            Text = "Previous",
            X = 1,
            Y = 1,
            Width = 12
        };
        _step = new Label
        {
            Text = "Step 1 of 1",
            X = Pos.Center(),
            Y = 1,
            Width = 28
        };
        _nextStep = new CliButton
        {
            Text = "Next",
            X = Pos.AnchorEnd(12),
            Y = 1,
            Width = 11,
            SchemeName = "Accent"
        };
        _guidedFooter.Add(_previousStep, _step, _nextStep);

        _statusBar = new View
        {
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = "Menu"
        };
        _status = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(34),
            Text = "Ready"
        };
        _shortcuts = new Label
        {
            X = Pos.AnchorEnd(52),
            Y = 0,
            Width = 51,
            Text = "Tab/Shift+Tab Focus  Arrows Move  PgUp/PgDn Scroll"
        };
        _statusBar.Add(_status, _shortcuts);

        _back.Accepting += (_, _) => _ = _owner.GoBackAsync(this);
        _forward.Accepting += (_, _) => _ = _owner.GoForwardAsync(this);
        _reload.Accepting += (_, _) => _ = _owner.ReloadOrStopAsync(this);
        _home.Accepting += (_, _) => _ = _owner.GoHomeAsync(this);
        _open.Accepting += (_, _) => _ = _owner.NavigateAsync(this, _address.Text);
        _address.Accepted += (_, _) => _ = _owner.NavigateAsync(this, _address.Text);
        _bookmark.Accepting += (_, _) => _owner.ToggleBookmark(this);
        _guide.Accepting += (_, _) => ToggleGuided();
        _settings.Accepting += (_, _) => _owner.OpenSettings();
        _outline.Accepted += (_, _) => ActivateOutline();
        _previousStep.Accepting += (_, _) => MoveGuided(-1);
        _nextStep.Accepting += (_, _) => MoveGuided(1);

        Add(_menuBar, _navigationBar, _workspace, _guidedFooter, _minimumSize, _statusBar);
        FrameChanged += (_, _) => ApplyResponsiveLayout();
        Initialized += (_, _) =>
        {
            ApplyResponsiveLayout();
            Refresh();
        };
        Refresh();
    }

    public CliTabSession Session { get; }

    public Boolean Guided => _guided;

    public Boolean OutlineVisible => _outlineVisible;

    public void InvalidateDocument()
    {
        _documentRenderRequired = true;
    }

    public void FocusAddress()
    {
        _address.SetFocus();
        _address.SelectAll();
    }

    public void HandleEscape()
    {
        if (_address.HasFocus)
        {
            _address.Text = Session.Address?.AbsoluteUri ?? String.Empty;
        }
        if (_guided)
        {
            ToggleGuided();
            FocusDocument();
            return;
        }
        FocusDocument();
    }

    public void ToggleGuided()
    {
        _guided = !_guided;
        Session.GuidedStep = 0;
        _resetDocumentPosition = true;
        _guide.Text = _guided ? "Standard" : "Guided";
        _lastWidth = 0;
        _lastHeight = 0;
        ApplyResponsiveLayout();
        Refresh();
    }

    public void ToggleOutline()
    {
        _outlineVisible = !_outlineVisible;
        _lastWidth = 0;
        _lastHeight = 0;
        ApplyResponsiveLayout();
        Refresh();
    }

    public void ApplyPreferences()
    {
        _outlineVisible = _owner.Preferences.ShowOutline;
        InvalidateDocument();
        _lastWidth = 0;
        _lastHeight = 0;
        ApplyResponsiveLayout();
        Refresh();
    }

    public void Refresh()
    {
        if (_rendering)
        {
            return;
        }
        _rendering = true;
        try
        {
            String tabTitle = ShortTitle(Session.Title);
            if (!String.Equals(Title, tabTitle, StringComparison.Ordinal))
            {
                Title = tabTitle;
            }
            String address = Session.Address?.AbsoluteUri ?? String.Empty;
            if (!String.Equals(_address.Text, address, StringComparison.Ordinal))
            {
                _address.Text = address;
            }
            _back.Enabled = Session.History.CanMoveBack && !Session.IsBusy;
            _forward.Enabled = Session.History.CanMoveForward && !Session.IsBusy;
            _reload.Enabled = Session.Address is not null;
            _reload.Text = Session.IsBusy ? "Stop" : _lastWidth >= 100 ? "Reload" : "R";
            _bookmark.Text = Session.Address is not null && _owner.IsBookmarked(Session.Address)
                ? _lastWidth >= 100 ? "Saved" : "★"
                : _lastWidth >= 100 ? "Bookmark" : "☆";
            _status.Text = (Session.IsBusy ? "Loading  ·  " : String.Empty)
                + Session.Status
                + (Session.DocumentInfo.Length > 0 ? "  ·  " + Session.DocumentInfo : String.Empty);

            Int32 previousScroll = _document.ScrollPosition;
            String previousFocus = _document.FocusedSemanticId;
            TerminalSurfaceDocument? document = Session.Document;
            if (!ReferenceEquals(document, _lastDocument) || _resetDocumentPosition)
            {
                previousScroll = 0;
                previousFocus = String.Empty;
                _lastDocument = document;
                _resetDocumentPosition = false;
                _documentRenderRequired = true;
            }
            if (document is null)
            {
                _renderedDocument = null;
                String startTitle = Session.Address is null
                    ? "LUMI"
                    : Session.Title.ToUpperInvariant();
                String startMessage = Session.IsBusy
                    ? "Loading semantic surface…"
                    : "Enter a LUMUI address above to begin.";
                Int32 startWidth = Math.Max(
                    24,
                    (_lastWidth > 0 ? _lastWidth : Viewport.Width)
                        - (_outlineFrame.Visible ? 29 : 2));
                if (_documentRenderRequired
                    || startWidth != _renderedStartWidth
                    || !String.Equals(startTitle, _renderedStartTitle, StringComparison.Ordinal)
                    || !String.Equals(startMessage, _renderedStartMessage, StringComparison.Ordinal))
                {
                    _outlineItems.Clear();
                    StackStartPage(startWidth, previousScroll, previousFocus);
                    _renderedStartTitle = startTitle;
                    _renderedStartMessage = startMessage;
                    _renderedStartWidth = startWidth;
                    _documentRenderRequired = false;
                }
                UpdateGuidedFooter();
                return;
            }

            Session.PageIndex = Math.Clamp(Session.PageIndex, 0, document.Pages.Count - 1);
            Int32 contentWidth = Math.Max(
                24,
                (_lastWidth > 0 ? _lastWidth : Viewport.Width)
                - (_outlineFrame.Visible ? 29 : 2));
            Int32 inputHash = InputHash(Session.Input);
            if (!_documentRenderRequired
                && ReferenceEquals(document, _renderedDocument)
                && Session.PageIndex == _renderedPageIndex
                && contentWidth == _renderedContentWidth
                && _guided == _renderedGuided
                && Session.GuidedStep == _renderedGuidedStep
                && inputHash == _renderedInputHash)
            {
                UpdateGuidedFooter();
                return;
            }

            _outlineItems.Clear();
            TerminalViewPage rendered = _viewRenderer.Render(
                document,
                Session.PageIndex,
                Session.Input,
                _guided,
                Session.GuidedStep,
                contentWidth,
                (component, interaction) => _owner.InteractAsync(this, component, interaction));
            _guidedStepCount = rendered.GuidedStepCount;
            _document.SetDocument(rendered.Content, rendered.Height, previousScroll, previousFocus);
            RebuildDocumentFocusTargets();
            _renderedDocument = document;
            _renderedPageIndex = Session.PageIndex;
            _renderedContentWidth = contentWidth;
            _renderedGuided = _guided;
            _renderedGuidedStep = Session.GuidedStep;
            _renderedInputHash = inputHash;
            _documentRenderRequired = false;

            for (Int32 pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
            {
                TerminalPage page = document.Pages[pageIndex];
                String prefix = pageIndex == Session.PageIndex ? "› " : "  ";
                _outlineItems.Add(new CliOutlineItem(prefix + page.Title, pageIndex, 0));
                if (pageIndex == Session.PageIndex)
                {
                    foreach (String heading in rendered.Outline)
                    {
                        _outlineItems.Add(new CliOutlineItem("  § " + heading.Trim(), pageIndex, -1));
                    }
                }
            }
            UpdateGuidedFooter();
        }
        finally
        {
            _rendering = false;
        }
    }

    private Button MenuButton(String text, Int32 x, String category)
    {
        Button button = new CliButton
        {
            Text = text,
            X = x,
            Y = 0,
            Width = text.Length + 4,
            SchemeName = "Menu"
        };
        button.Accepting += (_, _) => _owner.OpenMenu(this, category);
        return button;
    }

    private static Int32 InputHash(IReadOnlyDictionary<String, Object?> input)
    {
        HashCode hash = new HashCode();
        foreach (KeyValuePair<String, Object?> entry in input.OrderBy(
            pair => pair.Key,
            StringComparer.Ordinal))
        {
            hash.Add(entry.Key, StringComparer.Ordinal);
            AddHashValue(ref hash, entry.Value);
        }
        return hash.ToHashCode();
    }

    private static void AddHashValue(ref HashCode hash, Object? value)
    {
        if (value is null)
        {
            hash.Add(0);
            return;
        }
        if (value is String text)
        {
            hash.Add(text, StringComparer.Ordinal);
            return;
        }
        if (value is JsonElement element)
        {
            hash.Add(element.GetRawText(), StringComparer.Ordinal);
            return;
        }
        if (value is System.Collections.IEnumerable values)
        {
            hash.Add(1);
            foreach (Object? item in values)
            {
                AddHashValue(ref hash, item);
            }
            hash.Add(2);
            return;
        }
        hash.Add(value);
    }

    private static Button NavigationButton(String text, Int32 x) => new CliButton
    {
        Text = text,
        X = x,
        Y = 0,
        Width = text.Length + 4
    };

    private void ApplyResponsiveLayout()
    {
        Int32 width = Math.Max(1, Viewport.Width);
        Int32 height = Math.Max(1, Viewport.Height);
        if (width == _lastWidth && height == _lastHeight && _lastWidth > 0)
        {
            return;
        }
        _lastWidth = width;
        _lastHeight = height;
        _focusCacheDirty = true;
        Boolean usable = width >= 48 && height >= 12;
        _minimumSize.Visible = !usable;
        _navigationBar.Visible = usable;
        _workspace.Visible = usable;
        _guidedFooter.Visible = usable && _guided;
        if (!usable)
        {
            _shortcuts.Visible = false;
            return;
        }
        Boolean categoryMenus = width >= 78;
        _lumiMenu.Visible = !categoryMenus;
        foreach (Button menu in _categoryMenus)
        {
            menu.Visible = categoryMenus;
        }

        Boolean wide = width >= 100;
        _back.Text = wide ? "Back" : "<";
        _forward.Text = wide ? "Forward" : ">";
        _reload.Text = Session.IsBusy ? "Stop" : wide ? "Reload" : "R";
        _home.Text = wide ? "Home" : "H";
        _back.X = 0;
        _back.Y = 0;
        _back.Width = wide ? 8 : 5;
        _forward.X = wide ? 9 : 5;
        _forward.Y = 0;
        _forward.Width = wide ? 11 : 5;
        _reload.X = wide ? 21 : 10;
        _reload.Y = 0;
        _reload.Width = wide ? 10 : 5;
        _home.X = wide ? 32 : 15;
        _home.Y = 0;
        _home.Width = wide ? 8 : 5;
        _home.Visible = width >= 58;
        _address.X = wide ? 41 : 0;
        _address.Y = wide ? 0 : 1;
        _address.Width = wide ? Dim.Fill(49) : Dim.Fill(10);
        _open.Text = wide ? "Open" : "Go";
        _open.X = Pos.AnchorEnd(wide ? 46 : 8);
        _open.Y = wide ? 0 : 1;
        _open.Width = wide ? 9 : 7;
        _bookmark.Visible = wide;
        _guide.Visible = wide;
        _settings.Visible = wide;

        Boolean showOutline = _outlineVisible && width >= 92 && !_guided;
        _outlineFrame.Visible = showOutline;
        _document.X = showOutline ? 28 : 0;
        _document.Width = Dim.Fill();
        _guidedFooter.Visible = _guided;
        _workspace.Height = Dim.Fill(_guided ? 4 : 1);
        _shortcuts.Visible = width >= 96;
        _status.Width = Dim.Fill(width >= 96 ? 54 : 2);
        if (IsInitialized)
        {
            Refresh();
        }
    }

    protected override Boolean OnKeyDown(Key key)
    {
        if (key == Key.Tab.WithShift)
        {
            return MoveFocus(-1);
        }
        if (key == Key.Tab)
        {
            return MoveFocus(1);
        }
        if (key == Key.CursorLeft
            || key == Key.CursorRight
            || key == Key.CursorUp
            || key == Key.CursorDown)
        {
            return MoveDirectionalFocus(key) || base.OnKeyDown(key);
        }
        return base.OnKeyDown(key);
    }

    public Boolean MoveFocus(Int32 direction)
    {
        List<View> targets = FocusTargets();
        if (targets.Count == 0)
        {
            return false;
        }
        View? focused = CurrentFocusTarget(targets);
        Int32 current = focused is null ? -1 : IndexOfTarget(targets, focused);
        Int32 next = current < 0
            ? direction > 0 ? 0 : targets.Count - 1
            : (current + direction + targets.Count) % targets.Count;
        for (Int32 attempts = 0; attempts < targets.Count; attempts++)
        {
            View target = targets[next];
            if (IsAvailableFocusTarget(target))
            {
                target.SetFocus();
                _document.EnsureVisible(target);
                return true;
            }
            next = (next + direction + targets.Count) % targets.Count;
        }
        return false;
    }

    public Boolean MoveDirectionalFocus(Key key)
    {
        if (key != Key.CursorLeft
            && key != Key.CursorRight
            && key != Key.CursorUp
            && key != Key.CursorDown)
        {
            return false;
        }
        List<View> targets = FocusTargets();
        View? focused = CurrentFocusTarget(targets);
        if (focused is not Button
            && focused is not CheckBox
            && focused is not FrameView
            && focused is not TerminalMediaPreviewView)
        {
            return false;
        }
        Int32 direction = key == Key.CursorLeft || key == Key.CursorUp ? -1 : 1;
        return MoveFocus(direction);
    }

    private List<View> FocusTargets()
    {
        if (_focusCacheDirty)
        {
            _cachedFocusTargets.Clear();
            AddOrderedFocusTargets(_menuBar, _cachedFocusTargets);
            AddOrderedFocusTargets(_navigationBar, _cachedFocusTargets);
            if (_outlineFrame.Visible)
            {
                AddOrderedFocusTargets(_outlineFrame, _cachedFocusTargets);
            }
            if (_documentFocusTargets.Count == 0)
            {
                _cachedFocusTargets.Add(_document);
            }
            else
            {
                _cachedFocusTargets.AddRange(_documentFocusTargets);
            }
            if (_guidedFooter.Visible)
            {
                AddOrderedFocusTargets(_guidedFooter, _cachedFocusTargets);
            }
            _focusCacheDirty = false;
        }
        return _cachedFocusTargets;
    }

    private View? CurrentFocusTarget(IReadOnlyList<View> targets)
    {
        View? focused = App?.Navigation?.GetFocused() ?? MostFocused;
        while (focused is not null && !ReferenceEquals(focused, this))
        {
            Int32 index = IndexOfTarget(targets, focused);
            if (index >= 0)
            {
                return targets[index];
            }
            focused = focused.SuperView;
        }
        for (Int32 index = targets.Count - 1; index >= 0; index--)
        {
            if (targets[index].HasFocus)
            {
                return targets[index];
            }
        }
        return null;
    }

    private static Int32 IndexOfTarget(IReadOnlyList<View> targets, View target)
    {
        for (Int32 index = 0; index < targets.Count; index++)
        {
            if (ReferenceEquals(targets[index], target))
            {
                return index;
            }
        }
        return -1;
    }

    private void FocusDocument()
    {
        _document.SetFocus();
    }

    private void RebuildDocumentFocusTargets()
    {
        _documentFocusTargets.Clear();
        foreach (View child in _document.SubViews)
        {
            AddFocusTargets(child, _documentFocusTargets);
        }
        _focusCacheDirty = true;
    }

    private void AddOrderedFocusTargets(View view, ICollection<View> targets)
    {
        AddFocusTargets(view, targets);
    }

    private static void AddFocusTargets(View view, ICollection<View> targets)
    {
        if (!view.Visible)
        {
            return;
        }
        if (view.CanFocus && view.TabStop != TabBehavior.NoStop)
        {
            targets.Add(view);
        }
        foreach (View child in view.SubViews)
        {
            AddFocusTargets(child, targets);
        }
    }

    private static Boolean IsAvailableFocusTarget(View view)
    {
        View? current = view;
        while (current is not null)
        {
            if (!current.Visible || !current.Enabled)
            {
                return false;
            }
            current = current.SuperView;
        }
        return view.CanFocus && view.TabStop != TabBehavior.NoStop;
    }

    private void StackStartPage(Int32 width, Int32 scroll, String focus)
    {
        View content = new View { Width = width, Height = 5 };
        Label title = new Label
        {
            Text = Session.Address is null ? "LUMI" : Session.Title.ToUpperInvariant(),
            X = 0,
            Y = 0,
            Width = width,
            Height = 1,
            SchemeName = "Accent"
        };
        Label message = new Label
        {
            Text = Session.IsBusy ? "Loading semantic surface…" : "Enter a LUMUI address above to begin.",
            X = 0,
            Y = 2,
            Width = width,
            Height = 2
        };
        content.Add(title, message);
        _document.SetDocument(content, 5, scroll, focus);
        RebuildDocumentFocusTargets();
        _outlineItems.Add(new CliOutlineItem("› Start", 0, 0));
    }

    private void ActivateOutline()
    {
        if (_outline.Value is not Int32 index || index < 0 || index >= _outlineItems.Count)
        {
            return;
        }
        CliOutlineItem item = _outlineItems[index];
        if (item.PageIndex == Session.PageIndex && item.LineIndex < 0)
        {
            _document.ScrollToHeading(item.Label.Trim().TrimStart('§').Trim());
            return;
        }
        if (item.PageIndex != Session.PageIndex)
        {
            Session.PageIndex = item.PageIndex;
            Session.GuidedStep = 0;
            _resetDocumentPosition = true;
            Refresh();
            FocusDocument();
        }
    }

    private void MoveGuided(Int32 offset)
    {
        TerminalSurfaceDocument? document = Session.Document;
        if (!_guided || document is null)
        {
            return;
        }
        Int32 next = Session.GuidedStep + offset;
        if (next >= 0 && next < _guidedStepCount)
        {
            Session.GuidedStep = next;
            _resetDocumentPosition = true;
            Refresh();
            return;
        }
        Int32 page = Session.PageIndex + Math.Sign(offset);
        if (page < 0 || page >= document.Pages.Count)
        {
            if (offset > 0)
            {
                ToggleGuided();
            }
            return;
        }
        Session.PageIndex = page;
        Int32 count = Math.Max(1, _viewRenderer.GuidedSteps(document.Pages[page]).Count);
        Session.GuidedStep = offset < 0 ? count - 1 : 0;
        _resetDocumentPosition = true;
        Refresh();
    }

    private void UpdateGuidedFooter()
    {
        _guidedFooter.Visible = _guided;
        if (!_guided)
        {
            return;
        }
        TerminalSurfaceDocument? document = Session.Document;
        Int32 pageCount = document?.Pages.Count ?? 1;
        _step.Text = "Page " + (Session.PageIndex + 1).ToString(CultureInfo.InvariantCulture)
            + "/" + pageCount.ToString(CultureInfo.InvariantCulture)
            + "  ·  Step " + (Session.GuidedStep + 1).ToString(CultureInfo.InvariantCulture)
            + "/" + _guidedStepCount.ToString(CultureInfo.InvariantCulture);
        _previousStep.Enabled = Session.PageIndex > 0 || Session.GuidedStep > 0;
        Boolean final = Session.PageIndex + 1 >= pageCount && Session.GuidedStep + 1 >= _guidedStepCount;
        _nextStep.Text = final ? "Finish" : "Next";
        _nextStep.Enabled = true;
    }

    private static String ShortTitle(String value)
    {
        String title = String.IsNullOrWhiteSpace(value) ? "New tab" : value.Trim();
        return title.Length <= 24 ? title : title[..21] + "…";
    }
}
