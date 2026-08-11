namespace Lumui.Cli.Views;

public static class CliDialogs
{
    public static Boolean Confirm(IApplication app, String title, String message) =>
        MessageBox.Query(app, title, message, "No", "Yes") == 1;

    public static void Show(IApplication app, String title, String message) =>
        MessageBox.Query(app, title, message, "OK");

    public static String? Prompt(
        IApplication app,
        String title,
        String label,
        String value = "",
        Boolean secret = false)
    {
        String? result = null;
        using Dialog dialog = new Dialog
        {
            Title = title,
            Width = Dim.Percent(70),
            Height = 9
        };
        Label prompt = new Label
        {
            Text = label,
            X = 1,
            Y = 1,
            Width = Dim.Fill(2)
        };
        TextField field = new TextField
        {
            Text = value,
            Secret = secret,
            X = 1,
            Y = 3,
            Width = Dim.Fill(2)
        };
        Button cancel = new CliButton
        {
            Text = "Cancel",
            X = Pos.AnchorEnd(22),
            Y = Pos.AnchorEnd(2)
        };
        Button accept = new CliButton
        {
            Text = "Save",
            X = Pos.AnchorEnd(11),
            Y = Pos.AnchorEnd(2),
            IsDefault = true,
            SchemeName = "Accent"
        };
        cancel.Accepting += (_, _) => app.RequestStop(dialog);
        accept.Accepting += (_, _) =>
        {
            result = field.Text;
            app.RequestStop(dialog);
        };
        field.Accepted += (_, _) =>
        {
            result = field.Text;
            app.RequestStop(dialog);
        };
        dialog.Add(prompt, field, cancel, accept);
        dialog.Initialized += (_, _) => field.SetFocus();
        app.Run(dialog);
        return result;
    }

    public static T? Choose<T>(
        IApplication app,
        String title,
        IEnumerable<T> values,
        String emptyMessage = "Nothing is available.")
        where T : class
    {
        ObservableCollection<T> source = new ObservableCollection<T>(values);
        if (source.Count == 0)
        {
            Show(app, title, emptyMessage);
            return null;
        }
        T? result = null;
        using Dialog dialog = new Dialog
        {
            Title = title,
            Width = Dim.Percent(72),
            Height = Math.Clamp(source.Count + 7, 10, 19)
        };
        ListView list = new ListView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(4)
        };
        list.SetSource(source);
        Label hint = new Label
        {
            Text = "↑↓ select   Enter choose   Tab move",
            X = 1,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(24),
            SchemeName = "Muted"
        };
        Button cancel = new CliButton
        {
            Text = "Cancel",
            X = Pos.AnchorEnd(22),
            Y = Pos.AnchorEnd(2)
        };
        Button choose = new CliButton
        {
            Text = "Choose",
            X = Pos.AnchorEnd(11),
            Y = Pos.AnchorEnd(2),
            IsDefault = true,
            SchemeName = "Accent"
        };
        void Accept()
        {
            if (list.Value is Int32 index && index >= 0 && index < source.Count)
            {
                result = source[index];
                app.RequestStop(dialog);
            }
        }
        cancel.Accepting += (_, _) => app.RequestStop(dialog);
        choose.Accepting += (_, _) => Accept();
        list.Accepted += (_, _) => Accept();
        dialog.Add(list, hint, cancel, choose);
        dialog.Initialized += (_, _) => list.SetFocus();
        app.Run(dialog);
        return result;
    }
}
