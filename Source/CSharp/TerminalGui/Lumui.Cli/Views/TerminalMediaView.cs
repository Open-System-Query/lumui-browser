using Lumui.Cli.Rendering;

namespace Lumui.Cli.Views;

internal class TerminalMediaView : View
{
    private static readonly Rune BlankRune = new Rune(' ');
    private static readonly Rune UpperHalfBlockRune = new Rune('▀');

    private readonly Dictionary<Int32, TerminalColor> _colors =
        new Dictionary<Int32, TerminalColor>();
    private TerminalPixelFrame? _frame;
    private Boolean _backgroundDirty = true;
    private Int32 _lastViewportWidth = -1;
    private Int32 _lastViewportHeight = -1;
    private Int32 _lastTargetWidth = -1;
    private Int32 _lastTargetRows = -1;
    private Int32[] _sampleColumns = Array.Empty<Int32>();
    private Int32[] _sampleTopRows = Array.Empty<Int32>();
    private Int32[] _sampleBottomRows = Array.Empty<Int32>();
    private Int64[] _drawnCells = Array.Empty<Int64>();
    private Int64 _frameVersion;
    private Int64 _drawnFrameVersion = -1L;

    public Boolean ExactColors { get; set; }

    public void ShowFrame(TerminalPixelFrame frame)
    {
        _backgroundDirty = _frame is null
            || _frame.Width != frame.Width
            || _frame.Height != frame.Height;
        _frame = frame;
        _frameVersion++;
        Text = String.Empty;
        SetNeedsDraw();
    }

    public void ShowMessage(String message)
    {
        _frame = null;
        _backgroundDirty = true;
        _colors.Clear();
        _sampleColumns = Array.Empty<Int32>();
        _sampleTopRows = Array.Empty<Int32>();
        _sampleBottomRows = Array.Empty<Int32>();
        _drawnCells = Array.Empty<Int64>();
        _drawnFrameVersion = -1L;
        Text = message;
        SetNeedsDraw();
    }

    public void UpdateMessage(String message)
    {
        if (_frame is not null)
        {
            ShowMessage(message);
            return;
        }
        if (String.Equals(Text, message, StringComparison.Ordinal))
        {
            return;
        }
        Text = message;
        SetNeedsDraw();
    }

    protected override Boolean OnDrawingContent(DrawContext? context)
    {
        TerminalPixelFrame? frame = _frame;
        Int32 availableWidth = Viewport.Width;
        Int32 availableRows = Viewport.Height;
        if (frame is null || availableWidth <= 0 || availableRows <= 0)
        {
            return base.OnDrawingContent(context);
        }

        TerminalAttribute normal = GetAttributeForRole(Terminal.Gui.Drawing.VisualRole.Normal);
        Int32 availablePixelHeight = checked(availableRows * 2);
        Double scale = Math.Min(
            availableWidth / (Double)frame.Width,
            availablePixelHeight / (Double)frame.Height);
        if (!ExactColors)
        {
            scale = Math.Min(scale, 1D);
        }
        Int32 targetWidth = Math.Clamp(
            (Int32)Math.Round(frame.Width * scale),
            1,
            availableWidth);
        Int32 targetPixelHeight = Math.Clamp(
            (Int32)Math.Round(frame.Height * scale),
            1,
            availablePixelHeight);
        Int32 targetRows = (targetPixelHeight + 1) / 2;
        Int32 offsetX = Math.Max(0, (availableWidth - targetWidth) / 2);
        Int32 offsetY = Math.Max(0, (availableRows - targetRows) / 2);

        Boolean geometryChanged = _backgroundDirty
            || availableWidth != _lastViewportWidth
            || availableRows != _lastViewportHeight
            || targetWidth != _lastTargetWidth
            || targetRows != _lastTargetRows;
        if (geometryChanged)
        {
            SetAttribute(normal);
            for (Int32 row = 0; row < availableRows; row++)
            {
                for (Int32 column = 0; column < availableWidth; column++)
                {
                    AddRune(column, row, BlankRune);
                }
            }
            _backgroundDirty = false;
            _lastViewportWidth = availableWidth;
            _lastViewportHeight = availableRows;
            _lastTargetWidth = targetWidth;
            _lastTargetRows = targetRows;
            _sampleColumns = new Int32[targetWidth];
            for (Int32 column = 0; column < targetWidth; column++)
            {
                _sampleColumns[column] = Math.Min(
                    frame.Width - 1,
                    column * frame.Width / targetWidth);
            }
            _sampleTopRows = new Int32[targetRows];
            _sampleBottomRows = new Int32[targetRows];
            for (Int32 row = 0; row < targetRows; row++)
            {
                _sampleTopRows[row] = Math.Min(
                    frame.Height - 1,
                    row * 2 * frame.Height / targetPixelHeight);
                Int32 bottomPixel = Math.Min(targetPixelHeight - 1, row * 2 + 1);
                _sampleBottomRows[row] = Math.Min(
                    frame.Height - 1,
                    bottomPixel * frame.Height / targetPixelHeight);
            }
            _drawnCells = new Int64[checked(targetWidth * targetRows)];
            Array.Fill(_drawnCells, -1L);
        }

        Boolean incremental = !geometryChanged && _drawnFrameVersion != _frameVersion;
        Int64 activePair = -1L;
        for (Int32 row = 0; row < targetRows; row++)
        {
            Int32 topY = _sampleTopRows[row];
            Int32 bottomY = _sampleBottomRows[row];
            for (Int32 column = 0; column < targetWidth; column++)
            {
                Int32 sourceX = _sampleColumns[column];
                Int32 topKey = PixelKey(frame, sourceX, topY);
                Int32 bottomKey = PixelKey(frame, sourceX, bottomY);
                Int64 pair = ((Int64)topKey << 24) | (UInt32)bottomKey;
                Int32 cell = row * targetWidth + column;
                if (incremental && _drawnCells[cell] == pair)
                {
                    continue;
                }
                _drawnCells[cell] = pair;
                if (activePair != pair)
                {
                    SetAttribute(new TerminalAttribute(
                        ColorForKey(topKey),
                        ColorForKey(bottomKey)));
                    activePair = pair;
                }
                AddRune(offsetX + column, offsetY + row, UpperHalfBlockRune);
            }
        }
        _drawnFrameVersion = _frameVersion;
        SetAttribute(normal);
        return true;
    }

    private Int32 PixelKey(TerminalPixelFrame frame, Int32 x, Int32 y)
    {
        Int32 offset = checked((y * frame.Width + x) * 3);
        Byte red = frame.Rgb[offset];
        Byte green = frame.Rgb[offset + 1];
        Byte blue = frame.Rgb[offset + 2];
        if (!ExactColors)
        {
            red = (Byte)(red & 0xF8);
            green = (Byte)(green & 0xF8);
            blue = (Byte)(blue & 0xF8);
        }
        return red << 16 | green << 8 | blue;
    }

    private TerminalColor ColorForKey(Int32 key)
    {
        if (!_colors.TryGetValue(key, out TerminalColor color))
        {
            color = new TerminalColor("#" + key.ToString("X6", CultureInfo.InvariantCulture));
            _colors[key] = color;
        }
        return color;
    }
}

internal sealed class TerminalMediaPreviewView : TerminalMediaView
{
    private const Int32 ConcurrentPreviewLimit = 2;
    private static readonly SemaphoreSlim PreviewSlots = new SemaphoreSlim(
        ConcurrentPreviewLimit,
        ConcurrentPreviewLimit);

    private readonly MediaSourceDescriptor _source;
    private readonly Boolean _video;
    private readonly Action? _activate;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    private Boolean _disposed;

    public TerminalMediaPreviewView(
        MediaSourceDescriptor source,
        Boolean video,
        Action? activate = null)
    {
        _source = source;
        _video = video;
        _activate = activate;
        ExactColors = !video;
        CanFocus = activate is not null;
        TabStop = activate is not null ? TabBehavior.TabStop : TabBehavior.NoStop;
        if (activate is not null)
        {
            AddCommand(Command.Accept, ActivatePreview);
            KeyBindings.ReplaceCommands(Key.Enter, Command.Accept);
            KeyBindings.ReplaceCommands(Key.Space, Command.Accept);
            MouseBindings.Add(MouseFlags.LeftButtonClicked, Command.Accept);
        }
        ShowMessage(video ? "Decoding video preview…" : "Loading image preview…");
        Initialized += (_, _) => _ = PrepareAsync();
        Disposing += (_, _) => StopPreview();
    }

    private Boolean? ActivatePreview()
    {
        _activate?.Invoke();
        return true;
    }

    private async Task PrepareAsync()
    {
        IApplication? application = App;
        CancellationToken cancellationToken = _lifetime.Token;
        Boolean enteredPreviewSlot = false;
        try
        {
            await PreviewSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            enteredPreviewSlot = true;
            TerminalPixelFrame frame = await Task.Run(
                () => TerminalMediaService.PreparePreviewFrameAsync(
                    _source,
                    _video,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            application?.Invoke(() =>
            {
                if (!_disposed)
                {
                    ShowFrame(frame);
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or HttpRequestException
            or InvalidDataException
            or UnauthorizedAccessException
            or InvalidOperationException
            or OverflowException)
        {
            application?.Invoke(() =>
            {
                if (!_disposed)
                {
                    ShowMessage("Preview unavailable" + Environment.NewLine + exception.Message);
                }
            });
        }
        finally
        {
            if (enteredPreviewSlot)
            {
                PreviewSlots.Release();
            }
        }
    }

    private void StopPreview()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
