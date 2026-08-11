using System.Xml;
using System.Xml.Linq;

namespace Lumui.Cli.Rendering;

internal static class SvgTerminalRasterizer
{
    public static Boolean TryRender(String input, String output, Int32 targetWidth)
    {
        if (!Path.GetExtension(input).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Render(input, output, targetWidth);
            return true;
        }
        catch (Exception exception) when (exception is XmlException or FormatException or OverflowException)
        {
            throw new InvalidDataException("The SVG image could not be decoded.", exception);
        }
    }

    private static void Render(String input, String output, Int32 targetWidth)
    {
        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using XmlReader reader = XmlReader.Create(input, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        XElement root = document.Root
            ?? throw new InvalidDataException("The SVG image has no root element.");
        Double[] viewBox = Numbers(Attribute(root, "viewBox"));
        Double viewX = viewBox.Length >= 4 ? viewBox[0] : 0D;
        Double viewY = viewBox.Length >= 4 ? viewBox[1] : 0D;
        Double viewWidth = viewBox.Length >= 4 ? viewBox[2] : Length(Attribute(root, "width"), 96D);
        Double viewHeight = viewBox.Length >= 4 ? viewBox[3] : Length(Attribute(root, "height"), viewWidth);
        if (viewWidth <= 0D || viewHeight <= 0D)
        {
            throw new InvalidDataException("The SVG image has invalid dimensions.");
        }

        Int32 width = Math.Clamp(targetWidth, 8, 256);
        Int32 height = Math.Clamp((Int32)Math.Round(width * viewHeight / viewWidth), 1, 256);
        Byte[] rgb = new Byte[checked(width * height * 3)];
        Array.Fill(rgb, (Byte)255);
        Dictionary<String, XElement> definitions = root
            .Descendants()
            .Where(element => Attribute(element, "id").Length > 0)
            .GroupBy(element => Attribute(element, "id"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Canvas canvas = new Canvas(rgb, width, height, viewX, viewY, viewWidth, viewHeight, definitions);
        DrawChildren(root, canvas, Style.Default, Affine.Identity);

        Byte[] header = Encoding.ASCII.GetBytes(
            "P6\n" + width.ToString(CultureInfo.InvariantCulture)
            + " " + height.ToString(CultureInfo.InvariantCulture) + "\n255\n");
        Byte[] ppm = new Byte[header.Length + rgb.Length];
        Buffer.BlockCopy(header, 0, ppm, 0, header.Length);
        Buffer.BlockCopy(rgb, 0, ppm, header.Length, rgb.Length);
        File.WriteAllBytes(output, ppm);
    }

    private static void DrawChildren(
        XElement parent,
        Canvas canvas,
        Style inherited,
        Affine inheritedTransform)
    {
        foreach (XElement element in parent.Elements())
        {
            String name = element.Name.LocalName;
            if (name is "defs" or "title" or "desc" or "metadata")
            {
                continue;
            }
            Style style = Style.From(element, inherited);
            Affine transform = Affine.Combine(
                inheritedTransform,
                Affine.Parse(Attribute(element, "transform")));
            if (name is "g" or "svg" or "a")
            {
                DrawChildren(element, canvas, style, transform);
                continue;
            }
            Shape? shape = name switch
            {
                "rect" => Rect(element),
                "circle" => Circle(element),
                "ellipse" => Ellipse(element),
                "line" => Line(element),
                "polygon" => Polygon(element, true),
                "polyline" => Polygon(element, false),
                "path" => PathShape(element),
                _ => null
            };
            if (shape is not null)
            {
                canvas.Draw(shape, style, transform);
            }
        }
    }

    private static Shape Rect(XElement element)
    {
        Double x = Length(Attribute(element, "x"));
        Double y = Length(Attribute(element, "y"));
        Double width = Math.Max(0D, Length(Attribute(element, "width")));
        Double height = Math.Max(0D, Length(Attribute(element, "height")));
        Double radius = Math.Max(0D, Length(Attribute(element, "rx")));
        Bounds bounds = new Bounds(x, y, x + width, y + height);
        return new Shape(
            bounds,
            (px, py) => RoundedRectangle(px, py, bounds, radius),
            (px, py) => DistanceToRectangle(px, py, bounds, radius));
    }

    private static Shape Circle(XElement element)
    {
        Double cx = Length(Attribute(element, "cx"));
        Double cy = Length(Attribute(element, "cy"));
        Double radius = Math.Max(0D, Length(Attribute(element, "r")));
        Bounds bounds = new Bounds(cx - radius, cy - radius, cx + radius, cy + radius);
        return new Shape(
            bounds,
            (x, y) => Square(x - cx) + Square(y - cy) <= Square(radius),
            (x, y) => Math.Abs(Math.Sqrt(Square(x - cx) + Square(y - cy)) - radius));
    }

    private static Shape Ellipse(XElement element)
    {
        Double cx = Length(Attribute(element, "cx"));
        Double cy = Length(Attribute(element, "cy"));
        Double rx = Math.Max(0.0001D, Length(Attribute(element, "rx")));
        Double ry = Math.Max(0.0001D, Length(Attribute(element, "ry")));
        Bounds bounds = new Bounds(cx - rx, cy - ry, cx + rx, cy + ry);
        return new Shape(
            bounds,
            (x, y) => Square((x - cx) / rx) + Square((y - cy) / ry) <= 1D,
            (x, y) => Math.Abs(Math.Sqrt(Square((x - cx) / rx) + Square((y - cy) / ry)) - 1D)
                * Math.Min(rx, ry));
    }

    private static Shape Line(XElement element)
    {
        Point start = new Point(Length(Attribute(element, "x1")), Length(Attribute(element, "y1")));
        Point end = new Point(Length(Attribute(element, "x2")), Length(Attribute(element, "y2")));
        Bounds bounds = Bounds.From(new[] { start, end });
        return new Shape(bounds, (_, _) => false, (x, y) => SegmentDistance(new Point(x, y), start, end));
    }

    private static Shape Polygon(XElement element, Boolean closed)
    {
        Double[] values = Numbers(Attribute(element, "points"));
        List<Point> points = new List<Point>();
        for (Int32 index = 0; index + 1 < values.Length; index += 2)
        {
            points.Add(new Point(values[index], values[index + 1]));
        }
        return PolyShape(new[] { new SubPath(points, closed) });
    }

    private static Shape PathShape(XElement element) => PolyShape(ParsePath(Attribute(element, "d")));

    private static Shape PolyShape(IReadOnlyList<SubPath> paths)
    {
        Point[] all = paths.SelectMany(path => path.Points).ToArray();
        Bounds bounds = Bounds.From(all);
        return new Shape(
            bounds,
            (x, y) => paths.Where(path => path.Closed).Aggregate(
                false,
                (inside, path) => PointInPolygon(new Point(x, y), path.Points) ? !inside : inside),
            (x, y) => paths.Count == 0
                ? Double.MaxValue
                : paths.Min(path => PolylineDistance(new Point(x, y), path)));
    }

    private static IReadOnlyList<SubPath> ParsePath(String data)
    {
        List<String> tokens = PathTokens(data);
        List<SubPath> paths = new List<SubPath>();
        List<Point>? points = null;
        Point current = new Point(0D, 0D);
        Point control = current;
        Point start = current;
        Char command = 'M';
        Int32 cursor = 0;
        while (cursor < tokens.Count)
        {
            if (tokens[cursor].Length == 1 && Char.IsLetter(tokens[cursor][0]))
            {
                command = tokens[cursor++][0];
                if (command is 'Z' or 'z')
                {
                    if (points is not null)
                    {
                        paths.Add(new SubPath(points, true));
                        points = null;
                        current = start;
                    }
                    continue;
                }
            }
            Boolean relative = Char.IsLower(command);
            Char operation = Char.ToUpperInvariant(command);
            Int32 required = operation switch
            {
                'M' or 'L' or 'T' => 2,
                'H' or 'V' => 1,
                'C' => 6,
                'S' or 'Q' => 4,
                'A' => 7,
                _ => 0
            };
            if (required == 0 || cursor + required > tokens.Count)
            {
                break;
            }
            Double[] value = new Double[required];
            for (Int32 index = 0; index < required; index++)
            {
                value[index] = Double.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
            }
            Point RelativePoint(Double x, Double y) => relative
                ? new Point(current.X + x, current.Y + y)
                : new Point(x, y);
            if (operation == 'M')
            {
                if (points is not null && points.Count > 0)
                {
                    paths.Add(new SubPath(points, false));
                }
                current = RelativePoint(value[0], value[1]);
                start = current;
                control = current;
                points = new List<Point> { current };
                command = relative ? 'l' : 'L';
                continue;
            }
            points ??= new List<Point> { current };
            Point end;
            if (operation == 'H')
            {
                end = new Point(relative ? current.X + value[0] : value[0], current.Y);
                points.Add(end);
            }
            else if (operation == 'V')
            {
                end = new Point(current.X, relative ? current.Y + value[0] : value[0]);
                points.Add(end);
            }
            else if (operation == 'C')
            {
                Point first = RelativePoint(value[0], value[1]);
                Point second = RelativePoint(value[2], value[3]);
                end = RelativePoint(value[4], value[5]);
                FlattenCubic(points, current, first, second, end);
                control = second;
            }
            else if (operation == 'S')
            {
                Point first = new Point(2D * current.X - control.X, 2D * current.Y - control.Y);
                Point second = RelativePoint(value[0], value[1]);
                end = RelativePoint(value[2], value[3]);
                FlattenCubic(points, current, first, second, end);
                control = second;
            }
            else if (operation == 'Q')
            {
                Point quadratic = RelativePoint(value[0], value[1]);
                end = RelativePoint(value[2], value[3]);
                Point first = new Point(
                    current.X + (quadratic.X - current.X) * 2D / 3D,
                    current.Y + (quadratic.Y - current.Y) * 2D / 3D);
                Point second = new Point(
                    end.X + (quadratic.X - end.X) * 2D / 3D,
                    end.Y + (quadratic.Y - end.Y) * 2D / 3D);
                FlattenCubic(points, current, first, second, end);
                control = quadratic;
            }
            else if (operation == 'T')
            {
                end = RelativePoint(value[0], value[1]);
                points.Add(end);
            }
            else if (operation == 'A')
            {
                end = RelativePoint(value[5], value[6]);
                points.Add(end);
            }
            else
            {
                end = RelativePoint(value[0], value[1]);
                points.Add(end);
            }
            current = end;
            if (operation is not 'C' and not 'S' and not 'Q')
            {
                control = current;
            }
        }
        if (points is not null && points.Count > 0)
        {
            paths.Add(new SubPath(points, false));
        }
        return paths;
    }

    private static void FlattenCubic(
        ICollection<Point> points,
        Point start,
        Point first,
        Point second,
        Point end)
    {
        for (Int32 step = 1; step <= 16; step++)
        {
            Double t = step / 16D;
            Double inverse = 1D - t;
            points.Add(new Point(
                inverse * inverse * inverse * start.X
                    + 3D * inverse * inverse * t * first.X
                    + 3D * inverse * t * t * second.X
                    + t * t * t * end.X,
                inverse * inverse * inverse * start.Y
                    + 3D * inverse * inverse * t * first.Y
                    + 3D * inverse * t * t * second.Y
                    + t * t * t * end.Y));
        }
    }

    private static List<String> PathTokens(String value)
    {
        List<String> tokens = new List<String>();
        Int32 cursor = 0;
        while (cursor < value.Length)
        {
            Char character = value[cursor];
            if (Char.IsLetter(character) && character is not 'e' and not 'E')
            {
                tokens.Add(character.ToString());
                cursor++;
                continue;
            }
            if (Char.IsWhiteSpace(character) || character == ',')
            {
                cursor++;
                continue;
            }
            Int32 start = cursor++;
            while (cursor < value.Length)
            {
                Char next = value[cursor];
                if (Char.IsWhiteSpace(next) || next == ',' || Char.IsLetter(next)
                    || ((next == '-' || next == '+') && value[cursor - 1] is not 'e' and not 'E'))
                {
                    break;
                }
                cursor++;
            }
            tokens.Add(value[start..cursor]);
        }
        return tokens;
    }

    private static Boolean RoundedRectangle(Double x, Double y, Bounds bounds, Double radius)
    {
        if (x < bounds.Left || x > bounds.Right || y < bounds.Top || y > bounds.Bottom)
        {
            return false;
        }
        Double r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2D);
        if (r <= 0D || (x >= bounds.Left + r && x <= bounds.Right - r)
            || (y >= bounds.Top + r && y <= bounds.Bottom - r))
        {
            return true;
        }
        Double cx = x < bounds.Left + r ? bounds.Left + r : bounds.Right - r;
        Double cy = y < bounds.Top + r ? bounds.Top + r : bounds.Bottom - r;
        return Square(x - cx) + Square(y - cy) <= Square(r);
    }

    private static Double DistanceToRectangle(Double x, Double y, Bounds bounds, Double radius)
    {
        if (radius > 0D)
        {
            Double r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2D);
            Double cx = Math.Clamp(x, bounds.Left + r, bounds.Right - r);
            Double cy = Math.Clamp(y, bounds.Top + r, bounds.Bottom - r);
            return Math.Abs(Math.Sqrt(Square(x - cx) + Square(y - cy)) - r);
        }
        return Math.Min(
            Math.Min(Math.Abs(x - bounds.Left), Math.Abs(x - bounds.Right)),
            Math.Min(Math.Abs(y - bounds.Top), Math.Abs(y - bounds.Bottom)));
    }

    private static Boolean PointInPolygon(Point point, IReadOnlyList<Point> polygon)
    {
        Boolean inside = false;
        for (Int32 current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            Point a = polygon[current];
            Point b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static Double PolylineDistance(Point point, SubPath path)
    {
        if (path.Points.Count < 2)
        {
            return Double.MaxValue;
        }
        Double distance = Double.MaxValue;
        for (Int32 index = 1; index < path.Points.Count; index++)
        {
            distance = Math.Min(distance, SegmentDistance(point, path.Points[index - 1], path.Points[index]));
        }
        if (path.Closed)
        {
            distance = Math.Min(distance, SegmentDistance(point, path.Points[^1], path.Points[0]));
        }
        return distance;
    }

    private static Double SegmentDistance(Point point, Point start, Point end)
    {
        Double dx = end.X - start.X;
        Double dy = end.Y - start.Y;
        Double length = dx * dx + dy * dy;
        Double t = length <= Double.Epsilon
            ? 0D
            : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length, 0D, 1D);
        return Math.Sqrt(Square(point.X - (start.X + t * dx)) + Square(point.Y - (start.Y + t * dy)));
    }

    private static Double Square(Double value) => value * value;

    private static String Attribute(XElement element, String name) =>
        element.Attribute(name)?.Value?.Trim() ?? String.Empty;

    private static Double Length(String value, Double fallback = 0D)
    {
        String number = new String(value.TakeWhile(character =>
            Char.IsDigit(character) || character is '.' or '-' or '+' or 'e' or 'E').ToArray());
        return Double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out Double parsed)
            ? parsed
            : fallback;
    }

    private static Double[] Numbers(String value) => value
        .Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(item => Double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out Double number)
            ? number
            : 0D)
        .ToArray();

    private sealed class Canvas
    {
        private readonly Byte[] _rgb;
        private readonly Int32 _width;
        private readonly Int32 _height;
        private readonly Double _viewX;
        private readonly Double _viewY;
        private readonly Double _viewWidth;
        private readonly Double _viewHeight;
        private readonly IReadOnlyDictionary<String, XElement> _definitions;

        public Canvas(
            Byte[] rgb,
            Int32 width,
            Int32 height,
            Double viewX,
            Double viewY,
            Double viewWidth,
            Double viewHeight,
            IReadOnlyDictionary<String, XElement> definitions)
        {
            _rgb = rgb;
            _width = width;
            _height = height;
            _viewX = viewX;
            _viewY = viewY;
            _viewWidth = viewWidth;
            _viewHeight = viewHeight;
            _definitions = definitions;
        }

        public void Draw(Shape shape, Style style, Affine transform)
        {
            if (!transform.CanInvert)
            {
                return;
            }
            Double strokeWidth = Math.Max(0D, style.StrokeWidth);
            Bounds paint = transform.TransformBounds(shape.Bounds.Expand(strokeWidth / 2D));
            Int32 left = Math.Clamp((Int32)Math.Floor((paint.Left - _viewX) * _width / _viewWidth), 0, _width - 1);
            Int32 right = Math.Clamp((Int32)Math.Ceiling((paint.Right - _viewX) * _width / _viewWidth), 0, _width - 1);
            Int32 top = Math.Clamp((Int32)Math.Floor((paint.Top - _viewY) * _height / _viewHeight), 0, _height - 1);
            Int32 bottom = Math.Clamp((Int32)Math.Ceiling((paint.Bottom - _viewY) * _height / _viewHeight), 0, _height - 1);
            for (Int32 row = top; row <= bottom; row++)
            {
                Double y = _viewY + (row + 0.5D) * _viewHeight / _height;
                for (Int32 column = left; column <= right; column++)
                {
                    Double x = _viewX + (column + 0.5D) * _viewWidth / _width;
                    Point local = transform.Inverse(new Point(x, y));
                    if (style.Fill != "none" && shape.Contains(local.X, local.Y))
                    {
                        Blend(
                            column,
                            row,
                            Paint(
                                style.Fill,
                                local.X,
                                local.Y,
                                shape.Bounds,
                                style.Opacity * style.FillOpacity));
                    }
                    if (style.Stroke != "none" && strokeWidth > 0D
                        && shape.StrokeDistance(local.X, local.Y) <= strokeWidth / 2D)
                    {
                        Blend(
                            column,
                            row,
                            Paint(
                                style.Stroke,
                                local.X,
                                local.Y,
                                shape.Bounds,
                                style.Opacity * style.StrokeOpacity));
                    }
                }
            }
        }

        private Rgba Paint(String value, Double x, Double y, Bounds bounds, Double opacity)
        {
            if (value.StartsWith("url(#", StringComparison.Ordinal) && value.EndsWith(')'))
            {
                String id = value[5..^1];
                if (_definitions.TryGetValue(id, out XElement? gradient) && gradient is not null)
                {
                    return Gradient(gradient, x, y, bounds, opacity);
                }
            }
            Rgba color = Rgba.Parse(value);
            return color with { Alpha = color.Alpha * opacity };
        }

        private static Rgba Gradient(XElement element, Double x, Double y, Bounds bounds, Double opacity)
        {
            List<(Double Offset, Rgba Color)> stops = element.Elements()
                .Where(child => child.Name.LocalName == "stop")
                .Select(child =>
                {
                    String offsetText = Attribute(child, "offset");
                    Double offset = Length(offsetText);
                    if (offsetText.EndsWith('%'))
                    {
                        offset /= 100D;
                    }
                    String stopColor = Attribute(child, "stop-color");
                    if (stopColor.Length == 0)
                    {
                        stopColor = StyleValue(Attribute(child, "style"), "stop-color", "#000000");
                    }
                    Double stopOpacity = Length(Attribute(child, "stop-opacity"), 1D);
                    return (Math.Clamp(offset, 0D, 1D), Rgba.Parse(stopColor) with { Alpha = stopOpacity });
                })
                .OrderBy(stop => stop.Item1)
                .ToList();
            if (stops.Count == 0)
            {
                return new Rgba(0, 0, 0, opacity);
            }
            Double t;
            if (element.Name.LocalName == "radialGradient")
            {
                Double cx = Coordinate(Attribute(element, "cx"), bounds.Left, bounds.Width, 0.5D);
                Double cy = Coordinate(Attribute(element, "cy"), bounds.Top, bounds.Height, 0.5D);
                Double radius = Coordinate(Attribute(element, "r"), 0D, Math.Max(bounds.Width, bounds.Height), 0.5D);
                t = radius <= 0D ? 0D : Math.Sqrt(Square(x - cx) + Square(y - cy)) / radius;
            }
            else
            {
                Double x1 = Coordinate(Attribute(element, "x1"), bounds.Left, bounds.Width, 0D);
                Double y1 = Coordinate(Attribute(element, "y1"), bounds.Top, bounds.Height, 0D);
                Double x2 = Coordinate(Attribute(element, "x2"), bounds.Left, bounds.Width, 1D);
                Double y2 = Coordinate(Attribute(element, "y2"), bounds.Top, bounds.Height, 0D);
                Double dx = x2 - x1;
                Double dy = y2 - y1;
                Double length = dx * dx + dy * dy;
                t = length <= Double.Epsilon ? 0D : ((x - x1) * dx + (y - y1) * dy) / length;
            }
            t = Math.Clamp(t, 0D, 1D);
            (Double Offset, Rgba Color) lower = stops.LastOrDefault(stop => stop.Offset <= t);
            (Double Offset, Rgba Color) upper = stops.FirstOrDefault(stop => stop.Offset >= t);
            if (upper == default)
            {
                upper = stops[^1];
            }
            Double range = upper.Offset - lower.Offset;
            Double blend = range <= Double.Epsilon ? 0D : (t - lower.Offset) / range;
            return Rgba.Lerp(lower.Color, upper.Color, blend) with
            {
                Alpha = (lower.Color.Alpha + (upper.Color.Alpha - lower.Color.Alpha) * blend) * opacity
            };
        }

        private static Double Coordinate(String value, Double start, Double length, Double fallback)
        {
            if (value.Length == 0)
            {
                return start + fallback * length;
            }
            Double parsed = Length(value);
            return value.EndsWith('%') ? start + parsed * length / 100D : start + parsed * length;
        }

        private void Blend(Int32 x, Int32 y, Rgba color)
        {
            Double alpha = Math.Clamp(color.Alpha, 0D, 1D);
            if (alpha <= 0D)
            {
                return;
            }
            Int32 offset = (y * _width + x) * 3;
            _rgb[offset] = (Byte)Math.Clamp(Math.Round(color.Red * alpha + _rgb[offset] * (1D - alpha)), 0D, 255D);
            _rgb[offset + 1] = (Byte)Math.Clamp(Math.Round(color.Green * alpha + _rgb[offset + 1] * (1D - alpha)), 0D, 255D);
            _rgb[offset + 2] = (Byte)Math.Clamp(Math.Round(color.Blue * alpha + _rgb[offset + 2] * (1D - alpha)), 0D, 255D);
        }
    }

    private readonly record struct Style(
        String Fill,
        String Stroke,
        Double StrokeWidth,
        Double Opacity,
        Double FillOpacity,
        Double StrokeOpacity)
    {
        public static Style Default => new Style("#000000", "none", 1D, 1D, 1D, 1D);

        public static Style From(XElement element, Style inherited)
        {
            String declarations = Attribute(element, "style");
            String Value(String name, String fallback)
            {
                String direct = Attribute(element, name);
                return direct.Length > 0 ? direct : StyleValue(declarations, name, fallback);
            }
            return new Style(
                Value("fill", inherited.Fill),
                Value("stroke", inherited.Stroke),
                Length(Value("stroke-width", inherited.StrokeWidth.ToString(CultureInfo.InvariantCulture)), inherited.StrokeWidth),
                inherited.Opacity * Length(Value("opacity", "1"), 1D),
                Length(Value("fill-opacity", inherited.FillOpacity.ToString(CultureInfo.InvariantCulture)), inherited.FillOpacity),
                Length(Value("stroke-opacity", inherited.StrokeOpacity.ToString(CultureInfo.InvariantCulture)), inherited.StrokeOpacity));
        }
    }

    private static String StyleValue(String declarations, String name, String fallback)
    {
        foreach (String declaration in declarations.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            Int32 separator = declaration.IndexOf(':');
            if (separator > 0 && declaration[..separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return declaration[(separator + 1)..].Trim();
            }
        }
        return fallback;
    }

    private sealed record Shape(
        Bounds Bounds,
        Func<Double, Double, Boolean> Contains,
        Func<Double, Double, Double> StrokeDistance);

    private sealed record SubPath(IReadOnlyList<Point> Points, Boolean Closed);

    private readonly record struct Point(Double X, Double Y);

    private readonly record struct Affine(
        Double A,
        Double B,
        Double C,
        Double D,
        Double E,
        Double F)
    {
        public static Affine Identity => new Affine(1D, 0D, 0D, 1D, 0D, 0D);

        public Boolean CanInvert => Math.Abs(A * D - B * C) > Double.Epsilon;

        public Point Apply(Point point) => new Point(
            A * point.X + C * point.Y + E,
            B * point.X + D * point.Y + F);

        public Point Inverse(Point point)
        {
            Double determinant = A * D - B * C;
            Double x = point.X - E;
            Double y = point.Y - F;
            return new Point(
                (D * x - C * y) / determinant,
                (-B * x + A * y) / determinant);
        }

        public Bounds TransformBounds(Bounds bounds)
        {
            Point[] corners =
            {
                Apply(new Point(bounds.Left, bounds.Top)),
                Apply(new Point(bounds.Right, bounds.Top)),
                Apply(new Point(bounds.Right, bounds.Bottom)),
                Apply(new Point(bounds.Left, bounds.Bottom))
            };
            return Bounds.From(corners);
        }

        public static Affine Combine(Affine parent, Affine local) => new Affine(
            parent.A * local.A + parent.C * local.B,
            parent.B * local.A + parent.D * local.B,
            parent.A * local.C + parent.C * local.D,
            parent.B * local.C + parent.D * local.D,
            parent.A * local.E + parent.C * local.F + parent.E,
            parent.B * local.E + parent.D * local.F + parent.F);

        public static Affine Parse(String value)
        {
            Affine result = Identity;
            Int32 cursor = 0;
            while (cursor < value.Length)
            {
                while (cursor < value.Length
                    && (Char.IsWhiteSpace(value[cursor]) || value[cursor] == ','))
                {
                    cursor++;
                }
                Int32 nameStart = cursor;
                while (cursor < value.Length && Char.IsLetter(value[cursor]))
                {
                    cursor++;
                }
                if (nameStart == cursor)
                {
                    break;
                }
                String name = value[nameStart..cursor];
                while (cursor < value.Length && Char.IsWhiteSpace(value[cursor]))
                {
                    cursor++;
                }
                if (cursor >= value.Length || value[cursor] != '(')
                {
                    break;
                }
                Int32 argumentStart = ++cursor;
                while (cursor < value.Length && value[cursor] != ')')
                {
                    cursor++;
                }
                if (cursor >= value.Length)
                {
                    break;
                }
                Double[] arguments = Numbers(value[argumentStart..cursor]);
                cursor++;
                Affine operation = Operation(name, arguments);
                result = Combine(result, operation);
            }
            return result;
        }

        private static Affine Operation(String name, IReadOnlyList<Double> values)
        {
            if (name.Equals("matrix", StringComparison.OrdinalIgnoreCase) && values.Count >= 6)
            {
                return new Affine(values[0], values[1], values[2], values[3], values[4], values[5]);
            }
            if (name.Equals("translate", StringComparison.OrdinalIgnoreCase) && values.Count >= 1)
            {
                return new Affine(1D, 0D, 0D, 1D, values[0], values.Count > 1 ? values[1] : 0D);
            }
            if (name.Equals("scale", StringComparison.OrdinalIgnoreCase) && values.Count >= 1)
            {
                Double y = values.Count > 1 ? values[1] : values[0];
                return new Affine(values[0], 0D, 0D, y, 0D, 0D);
            }
            if (name.Equals("rotate", StringComparison.OrdinalIgnoreCase) && values.Count >= 1)
            {
                Double radians = values[0] * Math.PI / 180D;
                Affine rotation = new Affine(
                    Math.Cos(radians),
                    Math.Sin(radians),
                    -Math.Sin(radians),
                    Math.Cos(radians),
                    0D,
                    0D);
                if (values.Count < 3)
                {
                    return rotation;
                }
                Affine toCenter = new Affine(1D, 0D, 0D, 1D, values[1], values[2]);
                Affine fromCenter = new Affine(1D, 0D, 0D, 1D, -values[1], -values[2]);
                return Combine(Combine(toCenter, rotation), fromCenter);
            }
            if (name.Equals("skewX", StringComparison.OrdinalIgnoreCase) && values.Count >= 1)
            {
                return new Affine(1D, 0D, Math.Tan(values[0] * Math.PI / 180D), 1D, 0D, 0D);
            }
            if (name.Equals("skewY", StringComparison.OrdinalIgnoreCase) && values.Count >= 1)
            {
                return new Affine(1D, Math.Tan(values[0] * Math.PI / 180D), 0D, 1D, 0D, 0D);
            }
            return Identity;
        }
    }

    private readonly record struct Bounds(Double Left, Double Top, Double Right, Double Bottom)
    {
        public Double Width => Math.Max(0D, Right - Left);

        public Double Height => Math.Max(0D, Bottom - Top);

        public Bounds Expand(Double amount) =>
            new Bounds(Left - amount, Top - amount, Right + amount, Bottom + amount);

        public static Bounds From(IReadOnlyList<Point> points) => points.Count == 0
            ? new Bounds(0D, 0D, 0D, 0D)
            : new Bounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y));
    }

    private readonly record struct Rgba(Byte Red, Byte Green, Byte Blue, Double Alpha)
    {
        public static Rgba Parse(String value)
        {
            String color = value.Trim();
            if (color.StartsWith('#'))
            {
                String hex = color[1..];
                if (hex.Length == 3)
                {
                    hex = String.Concat(hex.Select(character => new String(character, 2)));
                }
                if (hex.Length >= 6 && Int32.TryParse(hex[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out Int32 rgb))
                {
                    return new Rgba((Byte)(rgb >> 16), (Byte)(rgb >> 8), (Byte)rgb, 1D);
                }
            }
            return color.ToLowerInvariant() switch
            {
                "white" => new Rgba(255, 255, 255, 1D),
                "red" => new Rgba(255, 0, 0, 1D),
                "green" => new Rgba(0, 128, 0, 1D),
                "blue" => new Rgba(0, 0, 255, 1D),
                "transparent" or "none" => new Rgba(0, 0, 0, 0D),
                _ => new Rgba(0, 0, 0, 1D)
            };
        }

        public static Rgba Lerp(Rgba start, Rgba end, Double value) => new Rgba(
            (Byte)Math.Clamp(Math.Round(start.Red + (end.Red - start.Red) * value), 0D, 255D),
            (Byte)Math.Clamp(Math.Round(start.Green + (end.Green - start.Green) * value), 0D, 255D),
            (Byte)Math.Clamp(Math.Round(start.Blue + (end.Blue - start.Blue) * value), 0D, 255D),
            start.Alpha + (end.Alpha - start.Alpha) * value);
    }
}
