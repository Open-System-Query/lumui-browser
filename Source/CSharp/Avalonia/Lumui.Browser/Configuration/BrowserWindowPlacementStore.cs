using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace Lumui.Browser.Configuration;

public sealed class BrowserWindowPlacementStore
{
    private static readonly SemaphoreSlim SaveGate = new SemaphoreSlim(1, 1);
    private readonly String _path = BrowserPaths.WindowPlacementFile;

    public void Apply(Window window, String key)
    {
        IReadOnlyDictionary<String, String> values = Read();
        if (!values.TryGetValue(key, out String? value))
        {
            return;
        }
        String[] parts = value.Split(',');
        if (parts.Length != 4
            || !Double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out Double width)
            || !Double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out Double height)
            || !Int32.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 x)
            || !Int32.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 y))
        {
            return;
        }
        window.Width = Math.Max(window.MinWidth, width);
        window.Height = Math.Max(window.MinHeight, height);
        window.Position = new PixelPoint(Math.Max(0, x), Math.Max(0, y));
        window.WindowStartupLocation = WindowStartupLocation.Manual;
    }

    public void Save(Window window, String key)
    {
        if (window.WindowState != WindowState.Normal)
        {
            return;
        }
        Double width = window.Width;
        Double height = window.Height;
        PixelPoint position = window.Position;
        _ = Task.Run(() => SavePlacement(
            key,
            width,
            height,
            position));
    }

    private void SavePlacement(
        String key,
        Double width,
        Double height,
        PixelPoint position)
    {
        SaveGate.Wait();
        try
        {
            Dictionary<String, String> values = Read();
            values[key] = String.Join(
                ",",
                width.ToString(CultureInfo.InvariantCulture),
                height.ToString(CultureInfo.InvariantCulture),
                position.X.ToString(CultureInfo.InvariantCulture),
                position.Y.ToString(CultureInfo.InvariantCulture));
            String? directory = Path.GetDirectoryName(_path);
            if (String.IsNullOrWhiteSpace(directory))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(directory);
                String temporary = _path + ".tmp";
                File.WriteAllLines(
                    temporary,
                    values.OrderBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => item.Key + "=" + item.Value));
                File.Move(temporary, _path, true);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
        finally
        {
            SaveGate.Release();
        }
    }

    private Dictionary<String, String> Read()
    {
        Dictionary<String, String> values = new Dictionary<String, String>(StringComparer.Ordinal);
        if (!File.Exists(_path))
        {
            return values;
        }
        try
        {
            foreach (String line in File.ReadLines(_path))
            {
                Int32 separator = line.IndexOf('=');
                if (separator > 0)
                {
                    values[line[..separator]] = line[(separator + 1)..];
                }
            }
        }
        catch (IOException)
        {
            return new Dictionary<String, String>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<String, String>(StringComparer.Ordinal);
        }
        return values;
    }
}
