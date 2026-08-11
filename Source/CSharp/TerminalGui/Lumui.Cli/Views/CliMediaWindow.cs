using Lumui.Cli.Rendering;

namespace Lumui.Cli.Views;

public sealed class CliMediaWindow : Dialog
{
    private const Int32 MaximumCachedFrames = 48;

    private readonly SemanticComponent _component;
    private readonly Boolean _image;
    private readonly Boolean _video;
    private readonly TerminalMediaView _display;
    private readonly Label _status;
    private readonly Label _timeline;
    private readonly View _controls;
    private readonly Button _rewind;
    private readonly Button _play;
    private readonly Button _stop;
    private readonly Button _forward;
    private readonly Button _volumeDown;
    private readonly Button _volumeUp;
    private readonly Button _sources;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    private readonly Object _frameCacheSync = new Object();
    private readonly Dictionary<Int32, TerminalPixelFrame> _frameCache =
        new Dictionary<Int32, TerminalPixelFrame>();
    private readonly Queue<Int32> _frameCacheOrder = new Queue<Int32>();
    private PreparedTerminalMedia? _prepared;
    private PcmAudioPlayer? _audio;
    private CancellationTokenSource? _playback;
    private DateTimeOffset _clock;
    private TimeSpan _position;
    private Double _volume = 0.8D;
    private Boolean _playing;
    private Boolean _hasPreviewFrame;
    private Boolean _closed;
    private Int32 _displayedFrameIndex = -1;
    private Int32 _playbackUpdatePending;

    public CliMediaWindow(SemanticComponent component)
    {
        _component = component;
        _image = component.Kind is LumuiProtocol.ComponentKinds.Image
            or LumuiProtocol.ComponentKinds.ImageOption
            or LumuiProtocol.ComponentKinds.Graphic
            or LumuiProtocol.ComponentKinds.Icon;
        _video = component.Kind is LumuiProtocol.ComponentKinds.Video or LumuiProtocol.ComponentKinds.VideoPlayer;
        Title = (component.Label.Length > 0 ? component.Label : _image ? "Image" : _video ? "Video" : "Audio Player") + " | LUMUI Browser";
        Width = Dim.Percent(92);
        Height = _image || _video ? Dim.Percent(90) : 18;

        _status = new Label
        {
            Text = "Preparing media",
            X = 1,
            Y = 0,
            Width = Dim.Fill(2),
            SchemeName = "Accent"
        };
        FrameView displayFrame = new FrameView
        {
            Title = _image ? "Image preview" : _video ? "Video preview" : "Audio player",
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(6),
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        _display = new TerminalMediaView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            SchemeName = "Accent",
            ExactColors = _image
        };
        _display.ShowMessage(_image
            ? "Downloading and decoding image…"
            : _video
                ? "Downloading and decoding video frames…"
                : AudioArtwork());
        displayFrame.Add(_display);
        _controls = new View
        {
            X = 1,
            Y = Pos.AnchorEnd(6),
            Width = Dim.Fill(2),
            Height = 3,
            SchemeName = "Menu",
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        _rewind = new CliButton
        {
            Text = "◀◀ 10s",
            Enabled = false,
            Visible = !_image
        };
        _play = new CliButton
        {
            Text = "▶ Play",
            SchemeName = "Accent",
            Enabled = false,
            Visible = !_image
        };
        _stop = new CliButton
        {
            Text = "■ Stop",
            Enabled = false,
            Visible = !_image
        };
        _forward = new CliButton
        {
            Text = "10s ▶▶",
            Enabled = false,
            Visible = !_image
        };
        _volumeDown = new CliButton
        {
            Text = "Vol −",
            Enabled = false,
            Visible = !_image && !_video
        };
        _volumeUp = new CliButton
        {
            Text = "Vol +",
            Enabled = false,
            Visible = !_image && !_video
        };
        _sources = new CliButton
        {
            Text = "Sources"
        };
        _timeline = new Label
        {
            Text = "00:00 / --:--",
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(13),
            Visible = !_image,
            SchemeName = "Accent"
        };
        Button close = new CliButton
        {
            Text = "Close",
            X = Pos.AnchorEnd(11),
            Y = Pos.AnchorEnd(2),
            IsDefault = true
        };

        _rewind.Accepting += (_, _) => Seek(TimeSpan.FromSeconds(-10));
        _play.Accepting += (_, _) => TogglePlayback();
        _stop.Accepting += (_, _) => Stop();
        _forward.Accepting += (_, _) => Seek(TimeSpan.FromSeconds(10));
        _volumeDown.Accepting += (_, _) => ChangeVolume(-0.1D);
        _volumeUp.Accepting += (_, _) => ChangeVolume(0.1D);
        _sources.Accepting += (_, _) => OpenSources();
        close.Accepting += (_, _) => App?.RequestStop(this);
        _controls.Add(_rewind, _play, _stop, _forward, _volumeDown, _volumeUp, _sources);
        _controls.FrameChanged += (_, _) => LayoutControls();
        _timeline.FrameChanged += (_, _) => UpdateTimeline();
        _display.FrameChanged += (_, _) => UpdateAudioPresentation();
        Initialized += (_, _) =>
        {
            LayoutControls();
            UpdateTimeline();
            UpdateAudioPresentation();
            _ = PrepareAsync();
        };
        Disposing += (_, _) => StopPlayback();
        Add(_status, displayFrame, _controls, _timeline, close);
    }

    public void StopPlayback()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        _lifetime.Cancel();
        _playback?.Cancel();
        _playback?.Dispose();
        _playback = null;
        _audio?.Dispose();
        _audio = null;
        lock (_frameCacheSync)
        {
            _frameCache.Clear();
            _frameCacheOrder.Clear();
        }
        _lifetime.Dispose();
    }

    private async Task PrepareAsync()
    {
        CancellationToken cancellationToken = _lifetime.Token;
        try
        {
            MediaSourceDescriptor? source = _component.MediaSources.FirstOrDefault();
            if (source is null)
            {
                throw new InvalidDataException("No playable media source is provided by this component.");
            }
            if (_image || _video)
            {
                TerminalPixelFrame preview = await Task.Run(
                    () => TerminalMediaService.PreparePreviewFrameAsync(
                        source,
                        _video,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                App?.Invoke(() =>
                {
                    if (!_closed)
                    {
                        _display.ShowFrame(preview);
                        _hasPreviewFrame = true;
                        _status.Text = _image ? "Ready  ·  terminal image preview" : "Preparing video playback…";
                    }
                });
                if (_image)
                {
                    return;
                }
            }
            PreparedTerminalMedia prepared = await TerminalMediaService.PrepareAsync(
                source,
                _video,
                _image,
                DurationHint(_component.Element),
                progress => App?.Invoke(() => ShowProgress(progress)),
                cancellationToken).ConfigureAwait(false);
            App?.Invoke(() =>
            {
                if (_closed)
                {
                    return;
                }
                _prepared = prepared;
                _rewind.Enabled = !_image;
                _play.Enabled = !_image;
                _stop.Enabled = !_image;
                _forward.Enabled = !_image;
                _volumeDown.Enabled = !_image && !_video;
                _volumeUp.Enabled = !_image && !_video;
                if (!_image)
                {
                    _play.SetFocus();
                }
                _status.Text = _image
                    ? "Ready  ·  terminal image preview"
                    : prepared.AudioPath is null && prepared.HasVideo
                    ? "Ready  ·  video has no audio track  ·  Space play/pause  ·  Left/Right seek"
                    : _video
                        ? "Ready  ·  Space play/pause  ·  Left/Right seek"
                        : "Audio ready  ·  Space play/pause  ·  Left/Right seek  ·  Up/Down volume";
                UpdateTimeline();
                if (prepared.HasVideo && prepared.Frames.Count > 0)
                {
                    ShowFrame(0);
                    _hasPreviewFrame = true;
                }
                else
                {
                    UpdateAudioPresentation();
                }
                LayoutControls();
            });
            if (prepared.HasVideo && prepared.Frames.Count > 1)
            {
                WarmFrameCache(
                    prepared,
                    1,
                    Math.Min(12, prepared.Frames.Count - 1),
                    cancellationToken);
            }
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
            App?.Invoke(() =>
            {
                if (!_closed)
                {
                    _status.Text = _hasPreviewFrame && _video
                        ? "Playback unavailable  ·  " + exception.Message
                        : "Media unavailable";
                    if (!_hasPreviewFrame)
                    {
                        _display.ShowMessage(exception.Message);
                    }
                }
            });
        }
    }

    protected override Boolean OnKeyDown(Key key)
    {
        if (!_image && key == Key.Space)
        {
            TogglePlayback();
            return true;
        }
        if (!_image && key == Key.CursorLeft)
        {
            Seek(TimeSpan.FromSeconds(-10));
            return true;
        }
        if (!_image && key == Key.CursorRight)
        {
            Seek(TimeSpan.FromSeconds(10));
            return true;
        }
        if (!_image && !_video && key == Key.CursorUp)
        {
            ChangeVolume(0.1D);
            return true;
        }
        if (!_image && !_video && key == Key.CursorDown)
        {
            ChangeVolume(-0.1D);
            return true;
        }
        if (!_image && key == Key.Home)
        {
            Stop();
            return true;
        }
        return base.OnKeyDown(key);
    }

    private void ShowProgress(TerminalMediaProgress progress)
    {
        if (_closed)
        {
            return;
        }
        _status.Text = progress.Percentage is Int32 percentage
            ? progress.Stage + "  " + percentage.ToString(CultureInfo.InvariantCulture) + "%"
            : progress.Stage + "…";
    }

    private void TogglePlayback()
    {
        if (_prepared is null)
        {
            return;
        }
        if (_playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    private void Play()
    {
        PreparedTerminalMedia? prepared = _prepared;
        if (prepared is null)
        {
            return;
        }
        if (_position >= prepared.Duration)
        {
            _position = TimeSpan.Zero;
        }
        _playing = true;
        _clock = DateTimeOffset.UtcNow - _position;
        _play.Text = "Ⅱ Pause";
        _playback?.Cancel();
        _playback?.Dispose();
        CancellationTokenSource playback = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _playback = playback;
        CancellationToken playbackToken = playback.Token;
        if (prepared.HasVideo && prepared.Frames.Count > 0)
        {
            Int32 firstFrame = Math.Clamp(
                (Int32)Math.Floor(_position.TotalSeconds * prepared.FrameRate),
                0,
                prepared.Frames.Count - 1);
            _ = Task.Run(
                () => WarmFrameCache(
                    prepared,
                    firstFrame,
                    Math.Min(MaximumCachedFrames, prepared.Frames.Count - firstFrame),
                    playbackToken),
                playbackToken);
        }
        if (prepared.AudioPath is not null)
        {
            try
            {
                _audio ??= new PcmAudioPlayer();
                _audio.Play(prepared.AudioPath, _position, _volume);
                _status.Text = _video
                    ? "Playing video with audio"
                    : "Playing audio  ·  volume " + VolumeText();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or PlatformNotSupportedException)
            {
                _status.Text = prepared.HasVideo ? "Playing without audio  ·  " + exception.Message : "Audio unavailable  ·  " + exception.Message;
                if (!prepared.HasVideo)
                {
                    _playing = false;
                    _play.Text = "▶ Play";
                    LayoutControls();
                    UpdateAudioPresentation();
                    return;
                }
            }
        }
        else if (prepared.HasVideo)
        {
            _status.Text = "Playing video without audio";
        }
        LayoutControls();
        UpdateTimeline();
        UpdateAudioPresentation();
        _ = PlaybackLoopAsync(playbackToken);
    }

    private void Pause()
    {
        if (!_playing)
        {
            return;
        }
        _position = CurrentPosition();
        _playing = false;
        _play.Text = "▶ Play";
        _playback?.Cancel();
        _audio?.Stop();
        _status.Text = _video ? "Video paused" : "Audio paused  ·  " + VolumeText();
        LayoutControls();
        UpdateTimeline();
        UpdateAudioPresentation();
    }

    private void Stop()
    {
        _playing = false;
        _position = TimeSpan.Zero;
        _play.Text = "▶ Play";
        _playback?.Cancel();
        _audio?.Stop();
        if (_prepared?.HasVideo == true && _prepared.Frames.Count > 0)
        {
            ShowFrame(0);
        }
        _status.Text = _video ? "Video stopped" : "Audio stopped  ·  ready to play";
        LayoutControls();
        UpdateTimeline();
        UpdateAudioPresentation();
    }

    private void Seek(TimeSpan change)
    {
        PreparedTerminalMedia? prepared = _prepared;
        if (prepared is null)
        {
            return;
        }
        Boolean resume = _playing;
        if (resume)
        {
            Pause();
        }
        _position = TimeSpan.FromTicks(Math.Clamp(
            (_position + change).Ticks,
            0L,
            prepared.Duration.Ticks));
        if (prepared.HasVideo && prepared.Frames.Count > 0)
        {
            Int32 frame = Math.Clamp(
                (Int32)Math.Floor(_position.TotalSeconds * prepared.FrameRate),
                0,
                prepared.Frames.Count - 1);
            ShowFrame(frame);
        }
        UpdateTimeline();
        UpdateAudioPresentation();
        if (resume)
        {
            Play();
        }
    }

    private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nextRefresh = DateTimeOffset.UtcNow;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PreparedTerminalMedia? prepared = _prepared;
                if (prepared is null)
                {
                    return;
                }
                TimeSpan position = CurrentPosition();
                if (position >= prepared.Duration)
                {
                    App?.Invoke(Stop);
                    return;
                }
                Int32 frame = prepared.HasVideo && prepared.Frames.Count > 0
                    ? Math.Clamp(
                        (Int32)Math.Floor(position.TotalSeconds * prepared.FrameRate),
                        0,
                        prepared.Frames.Count - 1)
                    : -1;
                if (Interlocked.CompareExchange(ref _playbackUpdatePending, 1, 0) == 0)
                {
                    TerminalPixelFrame? loadedFrame = null;
                    String? frameError = null;
                    if (frame >= 0 && frame != Volatile.Read(ref _displayedFrameIndex))
                    {
                        try
                        {
                            loadedFrame = LoadCachedFrame(prepared, frame);
                        }
                        catch (Exception exception) when (
                            exception is IOException
                            or InvalidDataException
                            or UnauthorizedAccessException
                            or OverflowException)
                        {
                            frameError = exception.Message;
                        }
                    }
                    IApplication? application = App;
                    if (application is null)
                    {
                        Interlocked.Exchange(ref _playbackUpdatePending, 0);
                        return;
                    }
                    application.Invoke(() =>
                    {
                        try
                        {
                            if (_closed || !_playing)
                            {
                                return;
                            }
                            _position = position;
                            if (frame >= 0 && loadedFrame is not null)
                            {
                                DisplayFrame(frame, loadedFrame);
                            }
                            else if (frameError is not null)
                            {
                                _status.Text = "Frame unavailable  ·  " + frameError;
                            }
                            UpdateTimeline();
                            UpdateAudioPresentation();
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _playbackUpdatePending, 0);
                        }
                    });
                }
                Double refreshRate = prepared.HasVideo
                    ? Math.Clamp(prepared.FrameRate, 1D, 30D)
                    : 10D;
                nextRefresh += TimeSpan.FromSeconds(1D / refreshRate);
                TimeSpan delay = nextRefresh - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    nextRefresh = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private TimeSpan CurrentPosition()
    {
        PreparedTerminalMedia? prepared = _prepared;
        if (!_playing || prepared is null)
        {
            return _position;
        }
        TimeSpan position = DateTimeOffset.UtcNow - _clock;
        return position > prepared.Duration ? prepared.Duration : position;
    }

    private void ShowFrame(Int32 index)
    {
        PreparedTerminalMedia? prepared = _prepared;
        if (prepared is null
            || index < 0
            || index >= prepared.Frames.Count
            || index == Volatile.Read(ref _displayedFrameIndex))
        {
            return;
        }
        try
        {
            DisplayFrame(index, LoadCachedFrame(prepared, index));
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or OverflowException)
        {
            _status.Text = "Frame unavailable  ·  " + exception.Message;
        }
    }

    private void DisplayFrame(Int32 index, TerminalPixelFrame frame)
    {
        _display.ShowFrame(frame);
        Volatile.Write(ref _displayedFrameIndex, index);
    }

    private TerminalPixelFrame LoadCachedFrame(PreparedTerminalMedia prepared, Int32 index)
    {
        lock (_frameCacheSync)
        {
            if (_frameCache.TryGetValue(index, out TerminalPixelFrame? cached)
                && cached is not null)
            {
                return cached;
            }
        }

        TerminalPixelFrame loaded = TerminalMediaService.LoadFrame(prepared.Frames[index]);
        lock (_frameCacheSync)
        {
            if (_frameCache.TryGetValue(index, out TerminalPixelFrame? cached)
                && cached is not null)
            {
                return cached;
            }
            _frameCache[index] = loaded;
            _frameCacheOrder.Enqueue(index);
            while (_frameCache.Count > MaximumCachedFrames && _frameCacheOrder.Count > 0)
            {
                _frameCache.Remove(_frameCacheOrder.Dequeue());
            }
        }
        return loaded;
    }

    private void WarmFrameCache(
        PreparedTerminalMedia prepared,
        Int32 start,
        Int32 count,
        CancellationToken cancellationToken)
    {
        Int32 end = Math.Min(prepared.Frames.Count, start + count);
        for (Int32 index = Math.Max(0, start); index < end; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                LoadCachedFrame(prepared, index);
            }
            catch (Exception exception) when (
                exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or OverflowException)
            {
                return;
            }
        }
    }

    private void UpdateTimeline()
    {
        PreparedTerminalMedia? prepared = _prepared;
        if (prepared is null)
        {
            Int32 preparingWidth = Math.Clamp(
                Math.Max(8, _timeline.Viewport.Width - 17),
                8,
                36);
            SetTimelineText(
                "00:00 "
                    + ProgressBar(TimeSpan.Zero, TimeSpan.Zero, preparingWidth)
                    + " --:--");
            return;
        }
        Int32 reserved = _video ? 15 : 25;
        Int32 barWidth = Math.Clamp(
            Math.Max(8, _timeline.Viewport.Width - reserved),
            8,
            48);
        SetTimelineText(
            Clock(_position)
                + " "
                + ProgressBar(_position, prepared.Duration, barWidth)
                + " "
                + Clock(prepared.Duration)
                + (!_video ? "  Vol " + VolumeText() : String.Empty));
    }

    private void SetTimelineText(String text)
    {
        if (!String.Equals(_timeline.Text, text, StringComparison.Ordinal))
        {
            _timeline.Text = text;
        }
    }

    private void LayoutControls()
    {
        Int32 available = _controls.Viewport.Width;
        if (available <= 0)
        {
            return;
        }
        View[] active = _image
            ? new View[] { _sources }
            : _video
                ? new View[] { _rewind, _play, _stop, _forward, _sources }
                : new View[] { _rewind, _play, _stop, _forward, _volumeDown, _volumeUp, _sources };
        foreach (View button in _controls.SubViews)
        {
            button.Visible = active.Contains(button);
        }
        Int32 x = 0;
        Int32 y = 0;
        foreach (View button in active)
        {
            Int32 width = button.Text.Length + 4;
            if (x > 0 && x + width > available)
            {
                x = 0;
                y++;
            }
            button.X = x;
            button.Y = y;
            button.Width = Math.Min(width, available);
            x += width + 1;
        }
    }

    private void ChangeVolume(Double change)
    {
        if (_image || _video || _prepared is null)
        {
            return;
        }
        _volume = Math.Clamp(_volume + change, 0D, 1D);
        try
        {
            _audio?.SetVolume(_volume);
            _status.Text = (_playing ? "Playing audio" : "Audio ready")
                + "  ·  volume "
                + VolumeText();
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            _status.Text = "Volume unavailable  ·  " + exception.Message;
        }
        UpdateTimeline();
        UpdateAudioPresentation();
    }

    private void UpdateAudioPresentation()
    {
        if (_image || _video)
        {
            return;
        }
        TimeSpan duration = _prepared?.Duration ?? DurationHint(_component.Element);
        String state = _prepared is null
            ? "PREPARING"
            : _playing
                ? "NOW PLAYING"
                : _position > TimeSpan.Zero
                    ? "PAUSED"
                    : "READY";
        Int32 width = Math.Clamp(
            _display.Viewport.Width > 0 ? _display.Viewport.Width : 48,
            18,
            72);
        String title = _component.Label.Length > 0 ? _component.Label : "Audio";
        String artist = ElementText(_component.Element, "artist");
        String album = ElementText(_component.Element, "album");
        String byline = String.Join(
            "  ·  ",
            new[] { artist, album }.Where(value => value.Length > 0));
        Int32 barWidth = Math.Clamp(Math.Max(4, width - 16), 4, 40);
        String transport = _playing
            ? "◀◀ 10s     Ⅱ PAUSE     ■ STOP     10s ▶▶"
            : "◀◀ 10s      ▶ PLAY      ■ STOP     10s ▶▶";
        List<String> lines = new List<String>
        {
            Center("♪  " + state, width),
            Center(Fit(title, width), width)
        };
        if (byline.Length > 0)
        {
            lines.Add(Center(Fit(byline, width), width));
        }
        lines.Add(
            Center(
                Clock(_position)
                    + " "
                    + ProgressBar(_position, duration, barWidth)
                    + " "
                    + (duration > TimeSpan.Zero ? Clock(duration) : "--:--"),
                width));
        lines.Add(Center(Fit(transport, width), width));
        _display.UpdateMessage(String.Join(Environment.NewLine, lines));
    }

    private void OpenSources()
    {
        if (App is null)
        {
            return;
        }
        IReadOnlyList<MediaLink> links = Links(_component);
        MediaLink? selected = CliDialogs.Choose(App, "Source and license", links, "No source or license links are provided.");
        if (selected is not null)
        {
            using CliResourceWindow resource = new CliResourceWindow(selected.Label, selected.Address);
            App.Run(resource);
        }
    }

    private static IReadOnlyList<MediaLink> Links(SemanticComponent component)
    {
        List<MediaLink> links = component.MediaSources
            .Select(source => new MediaLink("Source", source.Uri))
            .ToList();
        Uri? baseUri = component.MediaSources.FirstOrDefault()?.Uri ?? component.Target;
        if (baseUri is null)
        {
            return links;
        }
        AddLink(component.Element, baseUri, "source", "Source", links);
        AddLink(component.Element, baseUri, "license", "License", links);
        AddLink(component.Element, baseUri, "poster", "Poster", links);
        AddLink(component.Element, baseUri, "artwork", "Artwork", links);
        AddLink(component.Element, baseUri, "transcript", "Transcript", links);
        AddLink(component.Element, baseUri, "captions", "Captions", links);
        AddLink(component.Element, baseUri, "audio_description", "Audio description", links);
        if (component.Element.TryGetProperty("source", out JsonElement sources))
        {
            AddNestedLinks(sources, baseUri, links);
        }
        return links
            .GroupBy(link => link.Address.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddNestedLinks(
        JsonElement value,
        Uri baseUri,
        ICollection<MediaLink> links)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddNestedLinks(item, baseUri, links);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        AddLink(value, baseUri, "license", "License", links);
        AddLink(value, baseUri, "poster", "Poster", links);
        AddLink(value, baseUri, "artwork", "Artwork", links);
        AddLink(value, baseUri, "transcript", "Transcript", links);
        AddLink(value, baseUri, "captions", "Captions", links);
        AddLink(value, baseUri, "audio_description", "Audio description", links);
    }

    private static void AddLink(
        JsonElement element,
        Uri baseUri,
        String field,
        String label,
        ICollection<MediaLink> links)
    {
        if (!element.TryGetProperty(field, out JsonElement value))
        {
            return;
        }
        foreach (String address in AddressValues(value))
        {
            if (Uri.TryCreate(baseUri, address, out Uri? resolved))
            {
                links.Add(new MediaLink(label, resolved));
            }
        }
    }

    private static IEnumerable<String> AddressValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            String? text = value.GetString();
            if (!String.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
            yield break;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                foreach (String text in AddressValues(item))
                {
                    yield return text;
                }
            }
            yield break;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }
        foreach (String field in new[] { "href", "src", "url", "uri" })
        {
            if (value.TryGetProperty(field, out JsonElement address) && address.ValueKind == JsonValueKind.String)
            {
                String? text = address.GetString();
                if (!String.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }
    }

    private static TimeSpan DurationHint(JsonElement element)
    {
        if (element.TryGetProperty("duration_ms", out JsonElement milliseconds)
            && milliseconds.ValueKind == JsonValueKind.Number
            && milliseconds.TryGetDouble(out Double millisecondsValue))
        {
            return TimeSpan.FromMilliseconds(Math.Max(0D, millisecondsValue));
        }
        foreach (String field in new[] { "duration_seconds", "duration" })
        {
            if (!element.TryGetProperty(field, out JsonElement value))
            {
                continue;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out Double seconds))
            {
                return TimeSpan.FromSeconds(Math.Max(0D, seconds));
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                String? text = value.GetString()?.Trim();
                if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan parsed))
                {
                    return parsed;
                }
                if (text is not null
                    && text.EndsWith('s')
                    && Double.TryParse(
                        text.AsSpan(0, text.Length - 1),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out Double secondsValue))
                {
                    return TimeSpan.FromSeconds(Math.Max(0D, secondsValue));
                }
                try
                {
                    return System.Xml.XmlConvert.ToTimeSpan(text ?? String.Empty);
                }
                catch (FormatException)
                {
                }
            }
        }
        return TimeSpan.Zero;
    }

    private static String Clock(TimeSpan value) =>
        value.TotalHours >= 1D
            ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private static String ProgressBar(TimeSpan position, TimeSpan duration, Int32 width)
    {
        Int32 safeWidth = Math.Max(1, width);
        if (duration <= TimeSpan.Zero)
        {
            return "[" + new String('─', safeWidth) + "]";
        }
        Double ratio = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0D, 1D);
        Int32 marker = Math.Clamp(
            (Int32)Math.Round(ratio * (safeWidth - 1)),
            0,
            safeWidth - 1);
        return "["
            + new String('━', marker)
            + "●"
            + new String('─', safeWidth - marker - 1)
            + "]";
    }

    private String VolumeText() =>
        (_volume * 100D).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static String ElementText(JsonElement element, String field) =>
        element.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : String.Empty;

    private static String Fit(String value, Int32 width)
    {
        if (width <= 0)
        {
            return String.Empty;
        }
        if (value.Length <= width)
        {
            return value;
        }
        return width == 1 ? "…" : value[..(width - 1)] + "…";
    }

    private static String Center(String value, Int32 width)
    {
        String displayed = Fit(value, width);
        return displayed.PadLeft(displayed.Length + Math.Max(0, (width - displayed.Length) / 2));
    }

    private static String AudioArtwork() =>
        "♪  AUDIO PLAYER" + Environment.NewLine
        + Environment.NewLine
        + "Preparing audio…" + Environment.NewLine
        + Environment.NewLine
        + "◀◀ 10s      ▶ PLAY      ■ STOP      10s ▶▶";
}
