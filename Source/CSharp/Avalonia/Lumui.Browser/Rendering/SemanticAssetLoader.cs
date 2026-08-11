using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lumui.Client;

namespace Lumui.Browser.Rendering;

public sealed class SemanticAssetLoader
{
    private static readonly SemaphoreSlim LoadGate = new SemaphoreSlim(8, 8);
    private static readonly HttpClient MapClient = CreateMapClient();
    private const String SvgMediaType = "image/svg+xml";
    private const String SvgExtension = ".svg";
    private const Int64 MaximumSvgCharacters = 1_048_576;
    private const Int32 MaximumBitmapDimension = 1920;
    private const Int32 MaximumAssetBytes = 24 * 1024 * 1024;
    private readonly LumuiClient _client;
    private readonly Uri _surfaceUri;
    private readonly Action<String> _status;
    private readonly CancellationToken _documentCancellation;

    public SemanticAssetLoader(
        LumuiClient client,
        Uri surfaceUri,
        Action<String> status,
        CancellationToken documentCancellation)
    {
        _client = client;
        _surfaceUri = surfaceUri;
        _status = status;
        _documentCancellation = documentCancellation;
    }

    public async Task LoadAsync(
        ContentControl host,
        Uri uri,
        String mediaType)
    {
        Boolean entered = false;
        try
        {
            await LoadGate.WaitAsync(_documentCancellation);
            entered = true;
            Byte[] data = await SemanticAssetCache.GetAsync(
                uri,
                () => LoadBytesAsync(uri),
                _documentCancellation);
            _documentCancellation.ThrowIfCancellationRequested();
            if (IsSvg(uri, mediaType))
            {
                SemanticSvgDocument svg = await SemanticAssetCache.GetSvgAsync(
                    uri,
                    () => ReadSvgAsync(data),
                    _documentCancellation);
                _documentCancellation.ThrowIfCancellationRequested();
                await SetContentAsync(host, svg.CreateView);
            }
            else
            {
                Bitmap bitmap;
                if (!SemanticAssetCache.TryGetBitmap(uri, out Bitmap? cached)
                    || cached is null)
                {
                    bitmap = await Task.Run(
                        () => DecodeBitmap(data),
                        _documentCancellation);
                    SemanticAssetCache.StoreBitmap(uri, bitmap);
                }
                else
                {
                    bitmap = cached;
                }
                _documentCancellation.ThrowIfCancellationRequested();
                await SetContentAsync(
                    host,
                    () => CreateImage(bitmap, Stretch.Uniform));
            }
        }
        catch (OperationCanceledException) when (_documentCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is LumuiProtocolException
                or HttpRequestException
                or InvalidDataException
                or XmlException
                or FormatException)
        {
            ReportStatus(RendererText.ImageUnavailable(exception.Message));
        }
        finally
        {
            if (entered)
            {
                LoadGate.Release();
            }
        }
    }

    public async Task LoadMapTileAsync(
        ContentControl host,
        Uri uri)
    {
        if (!uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals(
                "tile.openstreetmap.org",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        Boolean entered = false;
        try
        {
            await LoadGate.WaitAsync(_documentCancellation);
            entered = true;
            Byte[] data = await SemanticAssetCache.GetAsync(
                uri,
                () => LoadMapBytesAsync(uri),
                _documentCancellation);
            _documentCancellation.ThrowIfCancellationRequested();
            Bitmap bitmap;
            if (!SemanticAssetCache.TryGetBitmap(uri, out Bitmap? cached)
                || cached is null)
            {
                bitmap = await Task.Run(
                    () => DecodeBitmap(data),
                    _documentCancellation);
                SemanticAssetCache.StoreBitmap(uri, bitmap);
            }
            else
            {
                bitmap = cached;
            }
            await SetContentAsync(
                host,
                () => CreateImage(bitmap, Stretch.UniformToFill));
        }
        catch (OperationCanceledException) when (
            _documentCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidDataException
                or FormatException)
        {
            ReportStatus(RendererText.ImageUnavailable(exception.Message));
        }
        finally
        {
            if (entered)
            {
                LoadGate.Release();
            }
        }
    }

    private async Task<SemanticSvgDocument> ReadSvgAsync(Byte[] data)
    {
        XDocument document = await Task.Run(
            () => ReadSvgDocument(data),
            _documentCancellation);
        _documentCancellation.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ReadSvg(document);
        }
        return await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                _documentCancellation.ThrowIfCancellationRequested();
                return ReadSvg(document);
            });
    }

    private async Task SetContentAsync(
        ContentControl host,
        Func<Control> contentFactory)
    {
        _documentCancellation.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            host.Content = contentFactory();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                _documentCancellation.ThrowIfCancellationRequested();
                host.Content = contentFactory();
            });
    }

    private static Image CreateImage(Bitmap bitmap, Stretch stretch)
    {
        Image image = new Image
        {
            Source = bitmap,
            Stretch = stretch,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapInterpolationMode(
            image,
            BitmapInterpolationMode.MediumQuality);
        return image;
    }

    private void ReportStatus(String message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _status(message);
            return;
        }
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_documentCancellation.IsCancellationRequested)
                {
                    _status(message);
                }
            });
    }

    private async Task<Byte[]> LoadBytesAsync(Uri uri)
    {
        await using Stream stream = await _client.GetAssetAsync(
            uri,
            _surfaceUri);
        using MemoryStream buffer = new MemoryStream();
        Byte[] block = new Byte[81920];
        while (true)
        {
            Int32 read = await stream.ReadAsync(
                block,
                _documentCancellation);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaximumAssetBytes)
            {
                throw new InvalidDataException("The image is too large.");
            }
            await buffer.WriteAsync(
                block.AsMemory(0, read),
                _documentCancellation);
        }
        return buffer.ToArray();
    }

    private async Task<Byte[]> LoadMapBytesAsync(Uri uri)
    {
        using HttpResponseMessage response = await MapClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            _documentCancellation);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(
            _documentCancellation);
        using MemoryStream buffer = new MemoryStream();
        Byte[] block = new Byte[32768];
        while (true)
        {
            Int32 read = await stream.ReadAsync(
                block,
                _documentCancellation);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > 4 * 1024 * 1024)
            {
                throw new InvalidDataException("The map tile is too large.");
            }
            await buffer.WriteAsync(
                block.AsMemory(0, read),
                _documentCancellation);
        }
        return buffer.ToArray();
    }

    private static HttpClient CreateMapClient()
    {
        HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12D)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LUMUI-Browser-Avalonia/1.0 (map renderer)");
        return client;
    }

    private static Bitmap DecodeBitmap(Byte[] data)
    {
        using MemoryStream stream = new MemoryStream(data, false);
        if (!TryImageSize(data, out Int32 width, out Int32 height))
        {
            return new Bitmap(stream);
        }
        if (width >= height && width > MaximumBitmapDimension)
        {
            return Bitmap.DecodeToWidth(
                stream,
                MaximumBitmapDimension,
                BitmapInterpolationMode.MediumQuality);
        }
        if (height > MaximumBitmapDimension)
        {
            return Bitmap.DecodeToHeight(
                stream,
                MaximumBitmapDimension,
                BitmapInterpolationMode.MediumQuality);
        }
        return new Bitmap(stream);
    }

    private static Boolean TryImageSize(
        ReadOnlySpan<Byte> data,
        out Int32 width,
        out Int32 height)
    {
        width = 0;
        height = 0;
        if (data.Length >= 24
            && data[0] == 0x89
            && data[1] == 0x50
            && data[2] == 0x4E
            && data[3] == 0x47)
        {
            width = BigEndian(data, 16);
            height = BigEndian(data, 20);
            return width > 0 && height > 0;
        }
        if (data.Length >= 10
            && data[0] == 0x47
            && data[1] == 0x49
            && data[2] == 0x46)
        {
            width = data[6] | (data[7] << 8);
            height = data[8] | (data[9] << 8);
            return width > 0 && height > 0;
        }
        if (data.Length >= 30
            && data[0] == 0x52
            && data[1] == 0x49
            && data[2] == 0x46
            && data[8] == 0x57
            && data[9] == 0x45
            && data[10] == 0x42
            && data[11] == 0x50
            && data[12] == 0x56
            && data[13] == 0x50
            && data[14] == 0x38
            && data[15] == 0x58)
        {
            width = 1 + data[24] + (data[25] << 8) + (data[26] << 16);
            height = 1 + data[27] + (data[28] << 8) + (data[29] << 16);
            return width > 0 && height > 0;
        }
        return TryJpegSize(data, out width, out height);
    }

    private static Boolean TryJpegSize(
        ReadOnlySpan<Byte> data,
        out Int32 width,
        out Int32 height)
    {
        width = 0;
        height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }
        Int32 offset = 2;
        while (offset + 8 < data.Length)
        {
            while (offset < data.Length && data[offset] != 0xFF)
            {
                offset++;
            }
            while (offset < data.Length && data[offset] == 0xFF)
            {
                offset++;
            }
            if (offset >= data.Length)
            {
                return false;
            }
            Byte marker = data[offset++];
            if (marker is 0xD8 or 0xD9)
            {
                continue;
            }
            if (offset + 1 >= data.Length)
            {
                return false;
            }
            Int32 length = (data[offset] << 8) | data[offset + 1];
            if (length < 2 || offset + length > data.Length)
            {
                return false;
            }
            if (IsJpegFrame(marker) && length >= 7)
            {
                height = (data[offset + 3] << 8) | data[offset + 4];
                width = (data[offset + 5] << 8) | data[offset + 6];
                return width > 0 && height > 0;
            }
            offset += length;
        }
        return false;
    }

    private static Boolean IsJpegFrame(Byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or
        0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or
        0xCD or 0xCE or 0xCF;

    private static Int32 BigEndian(ReadOnlySpan<Byte> data, Int32 offset) =>
        (data[offset] << 24)
        | (data[offset + 1] << 16)
        | (data[offset + 2] << 8)
        | data[offset + 3];

    private static Boolean IsSvg(Uri uri, String mediaType) =>
        String.Equals(
            mediaType,
            SvgMediaType,
            StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.EndsWith(
            SvgExtension,
            StringComparison.OrdinalIgnoreCase);

    private static XDocument ReadSvgDocument(Byte[] data)
    {
        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaximumSvgCharacters,
            XmlResolver = null
        };
        using MemoryStream stream = new MemoryStream(data, false);
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static SemanticSvgDocument ReadSvg(XDocument document)
    {
        XElement root = document.Root
            ?? throw new InvalidDataException("The SVG document has no root element.");
        (Double width, Double height) = ViewBox(root);
        IReadOnlyDictionary<String, IBrush> gradients = ReadGradients(root);
        DrawingGroup drawing = new DrawingGroup();
        Int32 shapeCount = 0;
        using DrawingContext context = drawing.Open();
        DrawSvgChildren(
            context,
            root,
            gradients,
            String.Empty,
            String.Empty,
            1D,
            0D,
            0D,
            ref shapeCount);
        if (shapeCount == 0)
        {
            throw new InvalidDataException(
                "The SVG document contains no supported shapes.");
        }
        return new SemanticSvgDocument(width, height, drawing);
    }

    private static void DrawSvgChildren(
        DrawingContext context,
        XElement parent,
        IReadOnlyDictionary<String, IBrush> gradients,
        String inheritedFill,
        String inheritedStroke,
        Double inheritedStrokeWidth,
        Double offsetX,
        Double offsetY,
        ref Int32 shapeCount)
    {
        foreach (XElement element in parent.Elements())
        {
            String name = element.Name.LocalName;
            if (name is "defs" or "title" or "desc")
            {
                continue;
            }
            String fill = Attribute(
                element,
                "fill",
                inheritedFill);
            String stroke = Attribute(
                element,
                "stroke",
                inheritedStroke);
            Double strokeWidth = NumberAttribute(
                element,
                "stroke-width",
                inheritedStrokeWidth);
            (Double translatedX, Double translatedY) =
                Translation(element);
            Double currentX = offsetX + translatedX;
            Double currentY = offsetY + translatedY;
            if (name == "g")
            {
                DrawSvgChildren(
                    context,
                    element,
                    gradients,
                    fill,
                    stroke,
                    strokeWidth,
                    currentX,
                    currentY,
                    ref shapeCount);
                continue;
            }

            Geometry? geometry = name switch
            {
                "path" => PathGeometry(element),
                "rect" => Rectangle(element),
                "circle" => Circle(element),
                "ellipse" => Ellipse(element),
                _ => null
            };
            if (geometry is null)
            {
                continue;
            }
            if (currentX != 0D || currentY != 0D)
            {
                geometry.Transform = new MatrixTransform(
                    Matrix.CreateTranslation(currentX, currentY));
            }
            IBrush? fillBrush = Paint(fill, gradients);
            IBrush? strokeBrush = Paint(stroke, gradients);
            Pen? pen = strokeBrush is null
                ? null
                : new Pen(strokeBrush, Math.Max(0.5D, strokeWidth));
            context.DrawGeometry(fillBrush, pen, geometry);
            shapeCount++;
        }
    }

    private static Geometry? PathGeometry(XElement element)
    {
        String data = element.Attribute("d")?.Value ?? String.Empty;
        return data.Length == 0
            ? null
            : StreamGeometry.Parse(data);
    }

    private static Geometry Rectangle(XElement element)
    {
        Double x = NumberAttribute(element, "x", 0D);
        Double y = NumberAttribute(element, "y", 0D);
        Double width = NumberAttribute(element, "width", 0D);
        Double height = NumberAttribute(element, "height", 0D);
        return new RectangleGeometry(
            new Rect(x, y, Math.Max(0D, width), Math.Max(0D, height)));
    }

    private static Geometry Circle(XElement element)
    {
        Double centerX = NumberAttribute(element, "cx", 0D);
        Double centerY = NumberAttribute(element, "cy", 0D);
        Double radius = Math.Max(
            0D,
            NumberAttribute(element, "r", 0D));
        return new EllipseGeometry(
            new Rect(
                centerX - radius,
                centerY - radius,
                radius * 2D,
                radius * 2D));
    }

    private static Geometry Ellipse(XElement element)
    {
        Double centerX = NumberAttribute(element, "cx", 0D);
        Double centerY = NumberAttribute(element, "cy", 0D);
        Double radiusX = Math.Max(
            0D,
            NumberAttribute(element, "rx", 0D));
        Double radiusY = Math.Max(
            0D,
            NumberAttribute(element, "ry", 0D));
        return new EllipseGeometry(
            new Rect(
                centerX - radiusX,
                centerY - radiusY,
                radiusX * 2D,
                radiusY * 2D));
    }

    private static IReadOnlyDictionary<String, IBrush> ReadGradients(
        XElement root)
    {
        Dictionary<String, IBrush> gradients =
            new Dictionary<String, IBrush>(StringComparer.Ordinal);
        foreach (XElement gradient in root.Descendants())
        {
            String name = gradient.Name.LocalName;
            if (name is not "linearGradient" and not "radialGradient")
            {
                continue;
            }
            String id = gradient.Attribute("id")?.Value ?? String.Empty;
            if (id.Length == 0)
            {
                continue;
            }
            GradientStops stops = new GradientStops();
            foreach (XElement stop in gradient.Elements())
            {
                if (stop.Name.LocalName != "stop")
                {
                    continue;
                }
                Color color = Color.Parse(
                    stop.Attribute("stop-color")?.Value ?? "#000000");
                Double opacity = Math.Clamp(
                    NumberAttribute(stop, "stop-opacity", 1D),
                    0D,
                    1D);
                color = Color.FromArgb(
                    (Byte)Math.Round(opacity * Byte.MaxValue),
                    color.R,
                    color.G,
                    color.B);
                stops.Add(new GradientStop(
                    color,
                    Offset(stop.Attribute("offset")?.Value)));
            }
            if (stops.Count == 0)
            {
                continue;
            }
            if (name == "linearGradient")
            {
                gradients[id] = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(
                        NumberAttribute(gradient, "x1", 0D),
                        NumberAttribute(gradient, "y1", 0D),
                        RelativeUnit.Relative),
                    EndPoint = new RelativePoint(
                        NumberAttribute(gradient, "x2", 1D),
                        NumberAttribute(gradient, "y2", 1D),
                        RelativeUnit.Relative),
                    GradientStops = stops
                };
            }
            else
            {
                gradients[id] = new RadialGradientBrush
                {
                    Center = RelativePoint.Center,
                    GradientOrigin = RelativePoint.Center,
                    RadiusX = RelativeScalar.Middle,
                    RadiusY = RelativeScalar.Middle,
                    GradientStops = stops
                };
            }
        }
        return gradients;
    }

    private static IBrush? Paint(
        String value,
        IReadOnlyDictionary<String, IBrush> gradients)
    {
        if (value.Length == 0 || value == "none")
        {
            return null;
        }
        if (value.StartsWith("url(#", StringComparison.Ordinal)
            && value.EndsWith(')'))
        {
            String id = value.Substring(5, value.Length - 6);
            return gradients.TryGetValue(id, out IBrush? gradient)
                ? gradient
                : null;
        }
        return new SolidColorBrush(Color.Parse(value));
    }

    private static (Double X, Double Y) Translation(XElement element)
    {
        String transform = element.Attribute("transform")?.Value
            ?? String.Empty;
        if (!transform.StartsWith("translate(", StringComparison.Ordinal)
            || !transform.EndsWith(')'))
        {
            return (0D, 0D);
        }
        String[] values = transform
            .Substring(10, transform.Length - 11)
            .Split(
                new Char[] { ' ', ',' },
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);
        Double x = values.Length > 0
            && Double.TryParse(
                values[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out Double parsedX)
                    ? parsedX
                    : 0D;
        Double y = values.Length > 1
            && Double.TryParse(
                values[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out Double parsedY)
                    ? parsedY
                    : 0D;
        return (x, y);
    }

    private static String Attribute(
        XElement element,
        String name,
        String fallback) =>
        element.Attribute(name)?.Value ?? fallback;

    private static Double NumberAttribute(
        XElement element,
        String name,
        Double fallback)
    {
        return Double.TryParse(
            element.Attribute(name)?.Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out Double value)
                ? value
                : fallback;
    }

    private static Double Offset(String? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return 0D;
        }
        String normalized = value.Trim();
        if (normalized.EndsWith('%')
            && Double.TryParse(
                normalized.AsSpan(0, normalized.Length - 1),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out Double percent))
        {
            return Math.Clamp(percent / 100D, 0D, 1D);
        }
        return Double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out Double offset)
                ? Math.Clamp(offset, 0D, 1D)
                : 0D;
    }

    private static (Double Width, Double Height) ViewBox(XElement root)
    {
        String value = root.Attribute("viewBox")?.Value ?? String.Empty;
        String[] parts = value.Split(
            new Char[] { ' ', ',' },
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !Double.TryParse(
                parts[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out Double width)
            || !Double.TryParse(
                parts[3],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out Double height)
            || width <= 0D
            || height <= 0D)
        {
            throw new InvalidDataException(
                "The SVG document has an invalid viewBox.");
        }
        return (width, height);
    }
}
