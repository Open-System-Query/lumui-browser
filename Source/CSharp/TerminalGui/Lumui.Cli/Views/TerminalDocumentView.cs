namespace Lumui.Cli.Views;

public sealed class TerminalDocumentView : View
{
    private View? _content;
    private Int32 _contentHeight;

    public TerminalDocumentView()
    {
        CanFocus = true;
        TabStop = TabBehavior.TabStop;
        ContentSizeTracksViewport = false;
        VerticalScrollBar.Visible = true;
        FrameChanged += (_, _) => SynchronizeViewport();
    }

    public Int32 ScrollPosition => Viewport.Y;

    public String FocusedSemanticId => FindFocusedId(_content) ?? String.Empty;

    public void EnsureVisible(View target)
    {
        if (_content is null || ReferenceEquals(target, this))
        {
            return;
        }

        Int32 y = target.Frame.Y;
        Int32 height = Math.Max(1, target.Frame.Height);
        View? current = target.SuperView;
        while (current is not null && !ReferenceEquals(current, this))
        {
            y += current.Frame.Y - current.Viewport.Y;
            current = current.SuperView;
        }
        if (!ReferenceEquals(current, this))
        {
            return;
        }

        Int32 visibleHeight = Math.Max(1, Viewport.Height);
        Int32 top = Viewport.Y;
        Int32 bottom = top + visibleHeight;
        Int32 next = top;
        if (y < top)
        {
            next = y;
        }
        else if (y + height > bottom)
        {
            next = y + height - visibleHeight;
        }
        Int32 maximum = Math.Max(0, _contentHeight - visibleHeight);
        next = Math.Clamp(next, 0, maximum);
        if (next == top)
        {
            return;
        }
        Viewport = Viewport with { Y = next };
        SetNeedsDraw();
    }

    public void ScrollToHeading(String heading)
    {
        if (_content is null || String.IsNullOrWhiteSpace(heading))
        {
            return;
        }
        View? match = FindHeading(_content, heading.Trim());
        if (match is null)
        {
            return;
        }
        Int32 y = 0;
        View? current = match;
        while (current is not null && !ReferenceEquals(current, this))
        {
            y += current.Frame.Y;
            current = current.SuperView;
        }
        Int32 maximum = Math.Max(0, _contentHeight - Math.Max(1, Viewport.Height));
        Viewport = Viewport with { Y = Math.Clamp(y, 0, maximum) };
        if (match.CanFocus)
        {
            match.SetFocus();
        }
        else
        {
            SetFocus();
        }
        SetNeedsDraw();
    }

    public void SetDocument(View content, Int32 contentHeight, Int32 scrollPosition, String focusedSemanticId)
    {
        View? previous = _content;
        RemoveAll();
        previous?.Dispose();
        _content = content;
        _contentHeight = Math.Max(contentHeight, 1);
        content.X = 0;
        content.Y = 0;
        content.Width = Dim.Fill();
        content.Height = _contentHeight;
        Add(content);
        SynchronizeViewport();
        Int32 maximum = Math.Max(0, _contentHeight - Math.Max(1, Viewport.Height));
        Viewport = Viewport with { Y = Math.Clamp(scrollPosition, 0, maximum) };
        if (focusedSemanticId.Length > 0)
        {
            FindById(content, focusedSemanticId)?.SetFocus();
        }
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void SynchronizeViewport()
    {
        SetContentWidth(Math.Max(1, Viewport.Width));
        SetContentHeight(_contentHeight);
        VerticalScrollBar.Visible = _contentHeight > Viewport.Height;
        Int32 maximum = Math.Max(0, _contentHeight - Math.Max(1, Viewport.Height));
        if (Viewport.Y > maximum)
        {
            Viewport = Viewport with { Y = maximum };
        }
    }

    protected override Boolean OnKeyDown(Key key)
    {
        Int32 page = Math.Max(1, Viewport.Height - 2);
        if (key == Key.CursorDown)
        {
            ScrollLines(1);
            return true;
        }
        if (key == Key.CursorUp)
        {
            ScrollLines(-1);
            return true;
        }
        if (key == Key.PageDown)
        {
            ScrollLines(page);
            return true;
        }
        if (key == Key.PageUp)
        {
            ScrollLines(-page);
            return true;
        }
        if (key == Key.Home || key == Key.Home.WithCtrl)
        {
            if (Viewport.Y != 0)
            {
                Viewport = Viewport with { Y = 0 };
                SetNeedsDraw();
            }
            return true;
        }
        if (key == Key.End || key == Key.End.WithCtrl)
        {
            Int32 end = Math.Max(0, _contentHeight - Viewport.Height);
            if (Viewport.Y != end)
            {
                Viewport = Viewport with { Y = end };
                SetNeedsDraw();
            }
            return true;
        }
        return base.OnKeyDown(key);
    }

    public void ScrollLines(Int32 lines)
    {
        Int32 previous = Viewport.Y;
        ScrollVertical(lines);
        if (Viewport.Y != previous)
        {
            SetNeedsDraw();
        }
    }

    private static String? FindFocusedId(View? view)
    {
        if (view is null)
        {
            return null;
        }
        if (view.HasFocus && !String.IsNullOrWhiteSpace(view.Id))
        {
            return view.Id;
        }
        foreach (View child in view.SubViews)
        {
            String? id = FindFocusedId(child);
            if (!String.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }
        return null;
    }

    private static View? FindById(View view, String id)
    {
        if (String.Equals(view.Id, id, StringComparison.Ordinal))
        {
            return view;
        }
        foreach (View child in view.SubViews)
        {
            View? match = FindById(child, id);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    private static View? FindHeading(View view, String heading)
    {
        if (view.Visible
            && ((view is Label label
                    && String.Equals(label.Text.Trim(), heading, StringComparison.CurrentCultureIgnoreCase))
                || (view is FrameView frame
                    && String.Equals(frame.Title.Trim(), heading, StringComparison.CurrentCultureIgnoreCase))))
        {
            return view;
        }
        foreach (View child in view.SubViews)
        {
            View? match = FindHeading(child, heading);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }
}
