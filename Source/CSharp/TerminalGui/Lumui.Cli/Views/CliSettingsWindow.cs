using Lumui.Cli.Configuration;

namespace Lumui.Cli.Views;

public sealed class CliSettingsWindow : Dialog
{
    private readonly CliBrowserServices _services;
    private readonly View _sectionBar;
    private readonly FrameView _sectionFrame;
    private readonly List<(CliButton Button, View Page, String Label, String CompactLabel)> _sections =
        new List<(CliButton Button, View Page, String Label, String CompactLabel)>();
    private readonly TextField _home;
    private readonly ChoiceButton<CliStartupMode> _startup;
    private readonly ChoiceButton<CliNewTabMode> _newTabMode;
    private readonly TextField _newTab;
    private readonly CheckBox _closeTabs;
    private readonly ChoiceButton<CliTerminalDensity> _density;
    private readonly ChoiceButton<CliTerminalOutput> _output;
    private readonly CheckBox _outline;
    private readonly CheckBox _unicode;
    private readonly CheckBox _bionic;
    private readonly CheckBox _reading;
    private readonly CheckBox _senior;
    private readonly ChoiceButton<CliColorScheme> _scheme;
    private readonly TextField _accent;
    private readonly TextField _textScale;
    private readonly TextField _pageZoom;
    private readonly ChoiceButton<CliColorVisionMode> _colorVision;
    private readonly CheckBox _contrast;
    private readonly CheckBox _motion;
    private readonly CheckBox _passwords;
    private readonly CheckBox _autoFill;
    private readonly CheckBox _history;
    private readonly CheckBox _clearOnExit;
    private readonly CheckBox _dnt;
    private readonly CheckBox _permissions;
    private readonly TextField _downloads;
    private readonly CheckBox _askDownload;

    public CliSettingsWindow(CliBrowserServices services)
    {
        _services = services;
        CliPreferences preferences = services.Preferences;
        Title = "Settings | LUMUI Browser";
        Width = Dim.Percent(90);
        Height = Dim.Percent(90);

        _sectionBar = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 2,
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = "Base"
        };
        _sectionFrame = new FrameView
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = "Base"
        };

        View general = Page("General");
        _home = TextEntry(general, 0, "Home page", preferences.HomePage);
        _startup = Choice(general, 3, "Startup", preferences.StartupMode);
        _newTabMode = Choice(general, 5, "New tab", preferences.NewTabMode);
        _newTab = TextEntry(general, 7, "Custom new-tab page", preferences.NewTabPage);
        _closeTabs = Toggle(general, 10, "Confirm closing multiple tabs", preferences.ConfirmClosingMultipleTabs);

        View viewing = Page("Viewing");
        _density = Choice(viewing, 0, "Content density", preferences.TerminalDensity);
        _output = Choice(viewing, 2, "Output", preferences.TerminalOutput);
        _outline = Toggle(viewing, 4, "Show page outline on wide terminals", preferences.ShowOutline);
        _unicode = Toggle(viewing, 6, "Use Unicode symbols and charts", preferences.UseUnicode);
        _bionic = Toggle(viewing, 8, "Bionic reading", preferences.BionicReading);
        _reading = Toggle(viewing, 10, "Simplified reading view", preferences.SimpleReadingView);
        _senior = Toggle(viewing, 12, "Senior-friendly spacing and labels", preferences.SeniorMode);

        View appearance = Page("Appearance");
        _scheme = Choice(appearance, 0, "Color scheme", preferences.ColorScheme);
        _accent = TextEntry(appearance, 2, "Accent color", preferences.AccentColor);
        _contrast = Toggle(appearance, 5, "High contrast", preferences.HighContrast);
        _motion = Toggle(appearance, 7, "Reduced motion", preferences.ReducedMotion);
        _textScale = TextEntry(appearance, 9, "Text scale percent (90-180)", preferences.TextScalePercent.ToString(CultureInfo.InvariantCulture));
        _pageZoom = TextEntry(appearance, 12, "Page zoom percent (50-200)", preferences.PageZoomPercent.ToString(CultureInfo.InvariantCulture));
        CliColorVisionMode colorVision = Enum.TryParse(
            preferences.ColorVision,
            true,
            out CliColorVisionMode parsedColorVision)
                ? parsedColorVision
                : CliColorVisionMode.Default;
        _colorVision = Choice(appearance, 15, "Color vision", colorVision);
        Label terminalFont = new Label
        {
            Text = "Font family and physical text size are managed by your terminal.",
            X = 1,
            Y = 18,
            Width = Dim.Fill(2),
            Height = 2,
            SchemeName = "Muted"
        };
        Button reset = new CliButton
        {
            Text = "Reset presentation",
            X = 1,
            Y = 21
        };
        appearance.Add(terminalFont, reset);

        View privacy = Page("Privacy");
        _passwords = Toggle(privacy, 0, "Offer to save passwords", preferences.OfferToSavePasswords);
        _autoFill = Toggle(privacy, 2, "Autofill saved passwords", preferences.AutoFillPasswords);
        _history = Toggle(privacy, 4, "Remember browsing history", preferences.RememberHistory);
        _clearOnExit = Toggle(privacy, 6, "Clear browsing data on exit", preferences.ClearBrowsingDataOnExit);
        _dnt = Toggle(privacy, 8, "Send Do Not Track", preferences.SendDoNotTrack);
        _permissions = Toggle(privacy, 10, "Ask before sensitive permissions", preferences.AskBeforeSensitivePermissions);
        Button clear = new CliButton
        {
            Text = "Clear browsing data now",
            X = 1,
            Y = 12
        };
        clear.Accepting += (_, _) =>
        {
            if (App is not null
                && CliDialogs.Confirm(App, "Clear browsing data", "Clear history, cache, and network diagnostics?"))
            {
                _services.ClearBrowsingData();
            }
        };
        privacy.Add(clear);

        View downloads = Page("Downloads");
        _downloads = TextEntry(downloads, 0, "Download folder", preferences.DownloadFolder);
        _askDownload = Toggle(downloads, 3, "Ask where to save downloads", preferences.AskWhereToSaveDownloads);

        reset.Accepting += (_, _) => ResetPresentation();
        AddSection(general, "General", "Gen");
        AddSection(viewing, "Viewing", "View");
        AddSection(appearance, "Appearance", "Look");
        AddSection(privacy, "Privacy", "Privacy");
        AddSection(downloads, "Downloads", "Files");
        SelectSection(0);

        Button cancel = new CliButton
        {
            Text = "Cancel",
            X = Pos.AnchorEnd(22),
            Y = Pos.AnchorEnd(2),
            Width = 10
        };
        Button save = new CliButton
        {
            Text = "Save",
            X = Pos.AnchorEnd(11),
            Y = Pos.AnchorEnd(2),
            Width = 9,
            IsDefault = true,
            SchemeName = "Accent"
        };
        cancel.Accepting += (_, _) => App?.RequestStop(this);
        save.Accepting += (_, _) => Save();
        _sectionBar.FrameChanged += (_, _) => LayoutSectionButtons();
        Initialized += (_, _) => LayoutSectionButtons();
        Add(_sectionBar, _sectionFrame, cancel, save);
    }

    public Boolean Saved { get; private set; }

    protected override Boolean OnKeyDown(Key key)
    {
        if (key == Key.Tab.WithCtrl.WithShift)
        {
            MoveSection(-1);
            return true;
        }
        if (key == Key.Tab.WithCtrl)
        {
            MoveSection(1);
            return true;
        }
        View? focused = App?.Navigation?.GetFocused() ?? MostFocused;
        Int32 focusedSection = _sections.FindIndex(
            section => ReferenceEquals(section.Button, focused));
        if (focusedSection >= 0)
        {
            if (key == Key.CursorLeft || key == Key.CursorUp)
            {
                SelectAndFocusSection(focusedSection - 1);
                return true;
            }
            if (key == Key.CursorRight || key == Key.CursorDown)
            {
                SelectAndFocusSection(focusedSection + 1);
                return true;
            }
        }
        return base.OnKeyDown(key);
    }

    private void AddSection(View page, String label, String compactLabel)
    {
        Int32 index = _sections.Count;
        CliButton button = new CliButton
        {
            Text = label,
            X = 0,
            Y = 0,
            SchemeName = "Base"
        };
        button.Accepting += (_, _) => SelectSection(index);
        page.X = 0;
        page.Y = 0;
        page.Width = Dim.Fill();
        page.Height = Dim.Fill();
        _sections.Add((button, page, label, compactLabel));
        _sectionBar.Add(button);
        _sectionFrame.Add(page);
    }

    private void SelectSection(Int32 index)
    {
        if (_sections.Count == 0)
        {
            return;
        }
        Int32 selected = (index % _sections.Count + _sections.Count) % _sections.Count;
        for (Int32 sectionIndex = 0; sectionIndex < _sections.Count; sectionIndex++)
        {
            CliButton button = _sections[sectionIndex].Button;
            View page = _sections[sectionIndex].Page;
            Boolean active = sectionIndex == selected;
            page.Visible = active;
            button.SchemeName = active ? "Accent" : "Base";
            button.SetNeedsDraw();
        }
        _sectionFrame.Title = _sections[selected].Label;
        _sectionFrame.SetNeedsDraw();
    }

    private void MoveSection(Int32 offset)
    {
        Int32 current = _sections.FindIndex(section => section.Page.Visible);
        SelectAndFocusSection((current < 0 ? 0 : current) + offset);
    }

    private void SelectAndFocusSection(Int32 index)
    {
        Int32 selected = (index % _sections.Count + _sections.Count) % _sections.Count;
        SelectSection(selected);
        _sections[selected].Button.SetFocus();
    }

    private void LayoutSectionButtons()
    {
        Int32 availableWidth = _sectionBar.Viewport.Width;
        if (availableWidth <= 0)
        {
            return;
        }
        if (!TryLayoutSectionButtons(availableWidth, false)
            && !TryLayoutSectionButtons(availableWidth, true))
        {
            LayoutInitialSectionButtons(availableWidth);
        }
    }

    private Boolean TryLayoutSectionButtons(Int32 availableWidth, Boolean compact)
    {
        Int32 x = 0;
        Int32 y = 0;
        for (Int32 index = 0; index < _sections.Count; index++)
        {
            String text = compact ? _sections[index].CompactLabel : _sections[index].Label;
            Int32 width = Math.Min(availableWidth, text.Length + 4);
            if (x > 0 && x + width > availableWidth)
            {
                x = 0;
                y++;
            }
            if (y >= 2)
            {
                return false;
            }
            CliButton button = _sections[index].Button;
            button.Text = text;
            button.X = x;
            button.Y = y;
            button.Width = width;
            x += width + 1;
        }
        return true;
    }

    private void LayoutInitialSectionButtons(Int32 availableWidth)
    {
        Int32 columns = Math.Min(3, Math.Max(1, availableWidth));
        Int32 gap = availableWidth >= 5 ? 1 : 0;
        Int32 width = Math.Max(1, (availableWidth - gap * (columns - 1)) / columns);
        for (Int32 index = 0; index < _sections.Count; index++)
        {
            Int32 column = index % columns;
            Int32 row = index / columns;
            CliButton button = _sections[index].Button;
            button.Text = _sections[index].Label[..1];
            button.X = column * (width + gap);
            button.Y = row;
            button.Width = width;
        }
    }

    private void Save()
    {
        CliPreferences preferences = _services.Preferences;
        preferences.HomePage = _home.Text;
        preferences.StartupMode = _startup.SelectedValue;
        preferences.NewTabMode = _newTabMode.SelectedValue;
        preferences.NewTabPage = _newTab.Text;
        preferences.ConfirmClosingMultipleTabs = Checked(_closeTabs);
        preferences.TerminalDensity = _density.SelectedValue;
        preferences.TerminalOutput = _output.SelectedValue;
        preferences.ShowOutline = Checked(_outline);
        preferences.UseUnicode = Checked(_unicode);
        preferences.BionicReading = Checked(_bionic);
        preferences.SimpleReadingView = Checked(_reading);
        preferences.SeniorMode = Checked(_senior);
        preferences.ColorScheme = _scheme.SelectedValue;
        preferences.AccentColor = _accent.Text;
        preferences.TextScalePercent = Integer(_textScale, preferences.TextScalePercent);
        preferences.PageZoomPercent = Integer(_pageZoom, preferences.PageZoomPercent);
        preferences.ColorVision = _colorVision.SelectedValue.ToString();
        preferences.HighContrast = Checked(_contrast);
        preferences.ReducedMotion = Checked(_motion);
        preferences.OfferToSavePasswords = Checked(_passwords);
        preferences.AutoFillPasswords = Checked(_autoFill);
        preferences.RememberHistory = Checked(_history);
        preferences.ClearBrowsingDataOnExit = Checked(_clearOnExit);
        preferences.SendDoNotTrack = Checked(_dnt);
        preferences.AskBeforeSensitivePermissions = Checked(_permissions);
        preferences.DownloadFolder = _downloads.Text;
        preferences.AskWhereToSaveDownloads = Checked(_askDownload);
        _services.SavePreferences();
        CliTheme.Apply(preferences);
        Saved = true;
        App?.RequestStop(this);
    }

    private void ResetPresentation()
    {
        _density.SelectedValue = CliTerminalDensity.Comfortable;
        _output.SelectedValue = CliTerminalOutput.Visual;
        _outline.Value = CheckState.Checked;
        _unicode.Value = CheckState.Checked;
        _bionic.Value = CheckState.UnChecked;
        _reading.Value = CheckState.UnChecked;
        _senior.Value = CheckState.UnChecked;
        _scheme.SelectedValue = CliColorScheme.Light;
        _accent.Text = CliPreferences.DefaultAccentColor;
        _textScale.Text = "100";
        _pageZoom.Text = "100";
        _colorVision.SelectedValue = CliColorVisionMode.Default;
        _contrast.Value = CheckState.UnChecked;
        _motion.Value = CheckState.UnChecked;
    }

    private static View Page(String title) => new SettingsPage(title);

    private static TextField TextEntry(View page, Int32 y, String label, String value)
    {
        Label caption = new Label
        {
            Text = label,
            X = 1,
            Y = y,
            Width = Dim.Fill(2)
        };
        TextField field = new TextField
        {
            Text = value,
            X = 1,
            Y = y + 1,
            Width = Dim.Fill(2)
        };
        page.Add(caption, field);
        return field;
    }

    private static ChoiceButton<T> Choice<T>(View page, Int32 y, String label, T value)
        where T : struct, Enum
    {
        Label caption = new Label
        {
            Text = label,
            X = 1,
            Y = y,
            Width = 24
        };
        ChoiceButton<T> field = new ChoiceButton<T>(value)
        {
            X = 26,
            Y = y,
            Width = Dim.Fill(2)
        };
        page.Add(caption, field);
        return field;
    }

    private static CheckBox Toggle(View page, Int32 y, String label, Boolean value)
    {
        CheckBox field = new CheckBox
        {
            Text = label,
            Value = value ? CheckState.Checked : CheckState.UnChecked,
            X = 1,
            Y = y,
            Width = Dim.Fill(2)
        };
        page.Add(field);
        return field;
    }

    private static Boolean Checked(CheckBox value) => value.Value == CheckState.Checked;

    private static Int32 Integer(TextField field, Int32 fallback) =>
        Int32.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 value)
            ? value
            : fallback;

}
