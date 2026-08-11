using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lumui.Browser.Controls;

namespace Lumui.Browser.Rendering;

public sealed class NativeMediaPlayer : Grid, IDisposable
{
    private readonly IReadOnlyList<MediaSourceDescriptor> _sources;
    private readonly Boolean _video;
    private readonly Control _preview;
    private readonly Image _videoFrame;
    private readonly Button _playButton;
    private readonly Button _stopButton;
    private readonly FontAwesomeIcon _playIcon;
    private readonly Slider _positionSlider;
    private readonly Slider _volumeSlider;
    private readonly TextBlock _timeText;
    private readonly Border _messageHost;
    private readonly TextBlock _message;
    private readonly Action<String> _status;
    private readonly TimeSpan _initialPosition;
    private readonly TimeSpan _declaredDuration;
    private readonly DispatcherTimer _playbackTimer;
    private readonly Stopwatch _clock = new Stopwatch();
    private readonly CancellationTokenSource _lifetime =
        new CancellationTokenSource();
    private PreparedMedia? _media;
    private PcmAudioPlayer? _audio;
    private Bitmap? _bitmap;
    private TimeSpan _basePosition;
    private TimeSpan _duration;
    private Int32 _frameIndex = -1;
    private Int32 _generation;
    private Boolean _shouldPlay;
    private Boolean _updatingPosition;
    private Boolean _disposed;
    private PlayerState _state;

    public NativeMediaPlayer(
        IReadOnlyList<MediaSourceDescriptor> sources,
        IReadOnlyList<Uri> captions,
        Boolean video,
        String name,
        Control preview,
        Double mediaHeight,
        Double durationMilliseconds,
        Double positionMilliseconds,
        String state,
        Boolean allowAutoPlay,
        NativeMediaPalette palette,
        Action<String> status)
    {
        if (sources.Count == 0)
        {
            throw new ArgumentException(
                "At least one media source is required.",
                nameof(sources));
        }

        _sources = sources;
        _video = video;
        _preview = preview;
        _status = status;
        _declaredDuration = TimeSpan.FromMilliseconds(
            Math.Max(0D, durationMilliseconds));
        _duration = _declaredDuration;
        _initialPosition = TimeSpan.FromMilliseconds(
            Math.Max(0D, positionMilliseconds));
        _basePosition = _initialPosition;
        _shouldPlay = allowAutoPlay
            && state.Equals("playing", StringComparison.OrdinalIgnoreCase);
        _state = PlayerState.Idle;

        RowDefinitions = new RowDefinitions("Auto,Auto");
        RowSpacing = 10D;
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(
            this,
            (video ? "Video player: " : "Audio player: ") + name);

        Double requestedHeight = Double.IsFinite(mediaHeight)
            ? mediaHeight
            : 0D;
        Double stageHeight = video
            ? Math.Clamp(requestedHeight, 150D, 320D)
            : Math.Clamp(requestedHeight, 108D, 156D);
        Grid stageContent = new Grid { ClipToBounds = true };
        _preview.HorizontalAlignment = HorizontalAlignment.Stretch;
        _preview.VerticalAlignment = VerticalAlignment.Stretch;
        stageContent.Children.Add(_preview);

        if (!video)
        {
            Grid audioIdentity = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 16D,
                Margin = new Thickness(20D, 16D),
                VerticalAlignment = VerticalAlignment.Center
            };
            FontAwesomeIcon audioMark = new FontAwesomeIcon
            {
                Icon = BrowserIcons.Music,
                Foreground = palette.Accent,
                IconSize = 42D,
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel audioText = new StackPanel
            {
                Spacing = 2D,
                VerticalAlignment = VerticalAlignment.Center
            };
            audioText.Children.Add(new TextBlock
            {
                Text = "AUDIO",
                Foreground = palette.Accent,
                FontFamily = palette.FontFamily,
                FontSize = 11D,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 1.8D
            });
            audioText.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = palette.Text,
                FontFamily = palette.FontFamily,
                FontSize = 20D,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
            audioIdentity.Children.Add(audioMark);
            audioIdentity.Children.Add(audioText);
            Grid.SetColumn(audioText, 1);
            stageContent.Children.Add(audioIdentity);
        }

        _videoFrame = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsVisible = false
        };
        stageContent.Children.Add(_videoFrame);

        _message = new TextBlock
        {
            Foreground = palette.OnAccent,
            FontFamily = palette.FontFamily,
            FontSize = 11D,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1D,
            TextWrapping = TextWrapping.Wrap
        };
        _messageHost = new Border
        {
            Background = palette.Accent,
            Padding = new Thickness(10D, 6D),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12D),
            Child = _message
        };
        stageContent.Children.Add(_messageHost);

        Children.Add(new Border
        {
            Height = stageHeight,
            MinHeight = 108D,
            Background = video ? Brushes.Black : palette.SurfaceAlternate,
            ClipToBounds = true,
            Child = stageContent
        });

        _playIcon = MediaIcon(BrowserIcons.Play, palette.OnAccent, 15D);
        _playButton = ActionButton(_playIcon, "Play media", true, palette);
        _playButton.Click += PlayClicked;
        _stopButton = ActionButton(
            MediaIcon(BrowserIcons.Stop, palette.Accent, 13D),
            "Stop media",
            false,
            palette);
        _stopButton.Click += StopClicked;

        _positionSlider = new Slider
        {
            Minimum = 0D,
            Maximum = Math.Max(1D, _duration.TotalSeconds),
            Value = Math.Max(0D, _initialPosition.TotalSeconds),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = palette.Accent
        };
        _positionSlider.PropertyChanged += PositionChanged;
        AutomationProperties.SetName(_positionSlider, "Playback position");

        _timeText = new TextBlock
        {
            Text = TimeLabel(_initialPosition.TotalSeconds, _duration.TotalSeconds),
            Foreground = palette.Muted,
            FontFamily = palette.FontFamily,
            FontSize = 12D,
            MinWidth = 92D,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };

        _volumeSlider = new Slider
        {
            Minimum = 0D,
            Maximum = 100D,
            Value = 80D,
            Width = 92D,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = palette.Accent
        };
        _volumeSlider.PropertyChanged += VolumeChanged;
        AutomationProperties.SetName(_volumeSlider, "Volume");

        Grid timeline = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        timeline.Children.Add(_positionSlider);
        timeline.Children.Add(_timeText);
        Grid.SetColumn(_timeText, 1);

        Grid commands = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 8D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        commands.Children.Add(_playButton);
        commands.Children.Add(_stopButton);
        Grid.SetColumn(_stopButton, 1);
        FontAwesomeIcon volumeLabel = new FontAwesomeIcon
        {
            Icon = BrowserIcons.Volume,
            Foreground = palette.Muted,
            IconSize = 16D,
            VerticalAlignment = VerticalAlignment.Center
        };
        commands.Children.Add(volumeLabel);
        Grid.SetColumn(volumeLabel, 3);
        commands.Children.Add(_volumeSlider);
        Grid.SetColumn(_volumeSlider, 4);

        Grid controls = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 7D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        controls.Children.Add(timeline);
        controls.Children.Add(commands);
        Grid.SetRow(commands, 1);
        Border controlBar = new Border
        {
            Background = palette.Surface,
            Padding = new Thickness(0D, 2D),
            Child = controls
        };
        Children.Add(controlBar);
        Grid.SetRow(controlBar, 1);

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50D)
        };
        _playbackTimer.Tick += PlaybackTick;
        Loaded += PlayerLoaded;

        if (captions.Count > 0)
        {
            AutomationProperties.SetName(
                _videoFrame,
                $"Video surface with {captions.Count} caption track(s)");
        }
        if (!allowAutoPlay
            && state.Equals("playing", StringComparison.OrdinalIgnoreCase))
        {
            ShowMessage("PAUSED FOR REDUCED MOTION");
        }
        else
        {
            ShowMessage(_shouldPlay ? "PREPARING" : "READY");
        }
    }

    private static FontAwesomeIcon MediaIcon(
        String icon,
        IBrush foreground,
        Double size) => new FontAwesomeIcon
        {
            Icon = icon,
            Foreground = foreground,
            IconSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Button ActionButton(
        Control content,
        String name,
        Boolean primary,
        NativeMediaPalette palette)
    {
        Button button = new Button
        {
            Content = content,
            Background = primary ? palette.Accent : Brushes.Transparent,
            Foreground = primary ? palette.OnAccent : palette.Accent,
            BorderBrush = palette.Accent,
            BorderThickness = new Thickness(primary ? 0D : 2D),
            CornerRadius = new CornerRadius(0D),
            MinWidth = 42D,
            MinHeight = 40D,
            Padding = new Thickness(11D),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    private void PlayerLoaded(Object? sender, RoutedEventArgs eventArgs)
    {
        if (_shouldPlay && _state == PlayerState.Idle)
        {
            BeginPreparation();
        }
    }

    private void BeginPreparation()
    {
        if (_disposed || _state == PlayerState.Preparing)
        {
            return;
        }
        _state = PlayerState.Preparing;
        Int32 generation = ++_generation;
        UpdatePlayButton();
        _ = PrepareAsync(generation);
    }

    private async Task PrepareAsync(Int32 generation)
    {
        Exception? failure = null;
        foreach (MediaSourceDescriptor source in _sources)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }
            try
            {
                PreparedMedia media = await MediaPreparationService.PrepareAsync(
                    source,
                    _video,
                    _declaredDuration,
                    progress => PostToInterface(() => ShowProgress(progress)),
                    _lifetime.Token).ConfigureAwait(false);
                PostToInterface(() => AcceptPrepared(media, generation));
                return;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }
        String message = failure?.Message ?? "No playable media source was found.";
        PostToInterface(() => Fail(message, generation));
    }

    private void ShowProgress(MediaPreparationProgress progress)
    {
        if (_disposed || _state != PlayerState.Preparing)
        {
            return;
        }
        ShowMessage(progress.Percentage is null
            ? progress.Stage
            : $"{progress.Stage} {progress.Percentage}%");
    }

    private void AcceptPrepared(PreparedMedia media, Int32 generation)
    {
        if (_disposed || generation != _generation)
        {
            return;
        }
        _media = media;
        _duration = media.Duration > TimeSpan.Zero
            ? media.Duration
            : _declaredDuration;
        _basePosition = ClampPosition(_initialPosition);
        _positionSlider.Maximum = Math.Max(1D, _duration.TotalSeconds);
        _state = PlayerState.Ready;
        if (!ShowFrameForPosition(_basePosition))
        {
            return;
        }
        UpdateTimeline(_basePosition);
        ShowMessage("READY");
        if (_shouldPlay)
        {
            StartPlayback();
        }
        else
        {
            UpdatePlayButton();
        }
    }

    private void StartPlayback()
    {
        if (_disposed)
        {
            return;
        }
        if (_media is null)
        {
            _shouldPlay = true;
            BeginPreparation();
            return;
        }
        if (_basePosition >= _duration)
        {
            _basePosition = TimeSpan.Zero;
        }
        try
        {
            if (_media.AudioPath is not null)
            {
                _audio ??= new PcmAudioPlayer();
                _audio.Play(
                    _media.AudioPath,
                    _basePosition,
                    _volumeSlider.Value / 100D);
            }
        }
        catch (Exception exception)
        {
            Fail(exception.Message, _generation);
            return;
        }
        _clock.Restart();
        _state = PlayerState.Playing;
        _shouldPlay = true;
        _playbackTimer.Start();
        HideMessage();
        UpdatePlayButton();
    }

    private void PausePlayback()
    {
        if (_state != PlayerState.Playing)
        {
            return;
        }
        _basePosition = CurrentPosition();
        _clock.Reset();
        _playbackTimer.Stop();
        StopAudio();
        _state = PlayerState.Paused;
        _shouldPlay = false;
        ShowMessage("PAUSED");
        UpdateTimeline(_basePosition);
        UpdatePlayButton();
    }

    private void StopPlayback()
    {
        _clock.Reset();
        _playbackTimer.Stop();
        StopAudio();
        _basePosition = TimeSpan.Zero;
        _state = _media is null ? PlayerState.Idle : PlayerState.Ready;
        _shouldPlay = false;
        if (!ShowFrameForPosition(_basePosition))
        {
            return;
        }
        UpdateTimeline(_basePosition);
        ShowMessage("STOPPED");
        UpdatePlayButton();
    }

    private void PlaybackTick(Object? sender, EventArgs eventArgs)
    {
        if (_disposed || _state != PlayerState.Playing)
        {
            return;
        }
        TimeSpan position = CurrentPosition();
        if (_duration > TimeSpan.Zero && position >= _duration)
        {
            _basePosition = _duration;
            _clock.Reset();
            _playbackTimer.Stop();
            StopAudio();
            _state = PlayerState.Ended;
            _shouldPlay = false;
            if (!ShowFrameForPosition(_duration))
            {
                return;
            }
            UpdateTimeline(_duration);
            ShowMessage("FINISHED");
            UpdatePlayButton();
            return;
        }
        if (!ShowFrameForPosition(position))
        {
            return;
        }
        UpdateTimeline(position);
    }

    private Boolean ShowFrameForPosition(TimeSpan position)
    {
        PreparedMedia? media = _media;
        if (!_video || media is null || media.Frames.Count == 0)
        {
            return true;
        }
        Int32 index = Math.Clamp(
            (Int32)Math.Floor(Math.Max(0D, position.TotalSeconds) * media.FrameRate),
            0,
            media.Frames.Count - 1);
        if (index == _frameIndex)
        {
            return true;
        }
        try
        {
            using FileStream stream = File.OpenRead(media.Frames[index]);
            Bitmap next = new Bitmap(stream);
            Bitmap? previous = _bitmap;
            _bitmap = next;
            _videoFrame.Source = next;
            _videoFrame.IsVisible = true;
            _preview.IsVisible = false;
            _frameIndex = index;
            previous?.Dispose();
            return true;
        }
        catch (Exception exception)
        {
            Fail(exception.Message, _generation);
            return false;
        }
    }

    private void PlayClicked(Object? sender, RoutedEventArgs eventArgs)
    {
        if (_state == PlayerState.Playing)
        {
            PausePlayback();
            return;
        }
        if (_state == PlayerState.Preparing)
        {
            _shouldPlay = !_shouldPlay;
            UpdatePlayButton();
            return;
        }
        if (_state == PlayerState.Failed)
        {
            _state = PlayerState.Idle;
            _media = null;
            _frameIndex = -1;
        }
        _shouldPlay = true;
        StartPlayback();
    }

    private void StopClicked(Object? sender, RoutedEventArgs eventArgs) => StopPlayback();

    private void PositionChanged(
        Object? sender,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (_disposed
            || _updatingPosition
            || _media is null
            || eventArgs.Property != RangeBase.ValueProperty)
        {
            return;
        }
        Seek(TimeSpan.FromSeconds(_positionSlider.Value));
    }

    private void Seek(TimeSpan position)
    {
        Boolean playing = _state == PlayerState.Playing;
        _basePosition = ClampPosition(position);
        _clock.Reset();
        StopAudio();
        if (!ShowFrameForPosition(_basePosition))
        {
            return;
        }
        UpdateTimeline(_basePosition);
        if (playing)
        {
            StartPlayback();
        }
        else if (_basePosition >= _duration)
        {
            _state = PlayerState.Ended;
            ShowMessage("FINISHED");
        }
    }

    private void VolumeChanged(
        Object? sender,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (!_disposed && eventArgs.Property == RangeBase.ValueProperty)
        {
            _audio?.SetVolume(_volumeSlider.Value / 100D);
        }
    }

    private TimeSpan CurrentPosition()
    {
        TimeSpan position = _state == PlayerState.Playing
            ? _basePosition + _clock.Elapsed
            : _basePosition;
        return ClampPosition(position);
    }

    private TimeSpan ClampPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }
        return _duration > TimeSpan.Zero && position > _duration
            ? _duration
            : position;
    }

    private void StopAudio()
    {
        try
        {
            _audio?.Stop();
        }
        catch (Exception exception)
        {
            _status($"Audio playback failed: {exception.Message}");
        }
    }

    private void Fail(String message, Int32 generation)
    {
        if (_disposed || generation != _generation)
        {
            return;
        }
        _clock.Reset();
        _playbackTimer.Stop();
        StopAudio();
        _state = PlayerState.Failed;
        _shouldPlay = false;
        String kind = _video ? "Video" : "Audio";
        ShowMessage($"{kind.ToUpperInvariant()} UNAVAILABLE");
        _status($"{kind} playback failed: {message}");
        UpdatePlayButton();
    }

    private void UpdateTimeline(TimeSpan position)
    {
        _updatingPosition = true;
        try
        {
            _positionSlider.Maximum = Math.Max(1D, _duration.TotalSeconds);
            _positionSlider.Value = Math.Clamp(
                position.TotalSeconds,
                _positionSlider.Minimum,
                _positionSlider.Maximum);
            _timeText.Text = TimeLabel(position.TotalSeconds, _duration.TotalSeconds);
        }
        finally
        {
            _updatingPosition = false;
        }
    }

    private void UpdatePlayButton()
    {
        Boolean playing = _state == PlayerState.Playing ||
            (_state == PlayerState.Preparing && _shouldPlay);
        _playIcon.Icon = playing ? BrowserIcons.Pause : BrowserIcons.Play;
        AutomationProperties.SetName(
            _playButton,
            playing ? "Pause media" : "Play media");
        ToolTip.SetTip(
            _playButton,
            playing ? "Pause media" : "Play media");
    }

    private void PostToInterface(Action action)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                action();
            }
        });
    }

    private void ShowMessage(String message)
    {
        _message.Text = message;
        _messageHost.IsVisible = true;
    }

    private void HideMessage()
    {
        _message.Text = String.Empty;
        _messageHost.IsVisible = false;
    }

    private static String TimeLabel(Double position, Double duration) =>
        FormatTime(position) + " / " + FormatTime(duration);

    private static String FormatTime(Double seconds)
    {
        TimeSpan value = TimeSpan.FromSeconds(Math.Max(0D, seconds));
        return value.TotalHours >= 1D
            ? $"{(Int32)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _generation++;
        _lifetime.Cancel();
        Loaded -= PlayerLoaded;
        _playbackTimer.Stop();
        _playbackTimer.Tick -= PlaybackTick;
        _playButton.Click -= PlayClicked;
        _stopButton.Click -= StopClicked;
        _positionSlider.PropertyChanged -= PositionChanged;
        _volumeSlider.PropertyChanged -= VolumeChanged;
        _clock.Reset();
        _audio?.Dispose();
        _audio = null;
        _videoFrame.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _lifetime.Dispose();
    }

    private enum PlayerState
    {
        Idle,
        Preparing,
        Ready,
        Playing,
        Paused,
        Ended,
        Failed
    }
}
