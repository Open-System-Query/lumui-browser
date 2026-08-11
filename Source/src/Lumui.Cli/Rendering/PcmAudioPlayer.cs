namespace Lumui.Cli.Rendering;

public sealed partial class PcmAudioPlayer : IDisposable
{
    private const UInt32 WaveMapper = 0xFFFFFFFF;
    private const UInt32 CallbackNull = 0;
    private const UInt32 StillPlaying = 33;
    private const UInt16 Channels = 2;
    private const UInt32 SamplesPerSecond = 48000;
    private const UInt16 BitsPerSample = 16;
    private const UInt16 BlockAlignment = Channels * BitsPerSample / 8;
    private const UInt32 AverageBytesPerSecond = SamplesPerSecond * BlockAlignment;
    private readonly Object _sync = new Object();
    private IntPtr _handle;
    private IntPtr _header;
    private GCHandle _audioHandle;
    private Byte[]? _audio;
    private String? _audioPath;
    private Boolean _disposed;

    public PcmAudioPlayer()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Terminal audio playback currently requires Windows.");
        }
        WaveFormat format = new WaveFormat
        {
            FormatTag = 1,
            Channels = Channels,
            SamplesPerSecond = SamplesPerSecond,
            AverageBytesPerSecond = AverageBytesPerSecond,
            BlockAlignment = BlockAlignment,
            BitsPerSample = BitsPerSample
        };
        EnsureSuccess(waveOutOpen(
            out _handle,
            WaveMapper,
            ref format,
            IntPtr.Zero,
            IntPtr.Zero,
            CallbackNull), "open the audio device");
    }

    public void Play(String path, TimeSpan position, Double volume)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!StopCore())
            {
                throw new InvalidOperationException("Windows could not reset the audio device.");
            }
            if (!String.Equals(_audioPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _audio = File.ReadAllBytes(path);
                _audioPath = path;
            }
            Byte[] audio = _audio ?? Array.Empty<Byte>();
            Int64 requested = (Int64)Math.Floor(Math.Max(0D, position.TotalSeconds) * AverageBytesPerSecond);
            Int32 offset = checked((Int32)Math.Min(audio.Length, requested - requested % BlockAlignment));
            Int32 length = audio.Length - offset;
            if (length <= 0)
            {
                return;
            }
            SetVolumeCore(volume);
            _audioHandle = GCHandle.Alloc(audio, GCHandleType.Pinned);
            WaveHeader header = new WaveHeader
            {
                Data = IntPtr.Add(_audioHandle.AddrOfPinnedObject(), offset),
                BufferLength = checked((UInt32)length)
            };
            _header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.StructureToPtr(header, _header, false);
            try
            {
                EnsureSuccess(waveOutPrepareHeader(
                    _handle,
                    _header,
                    checked((UInt32)Marshal.SizeOf<WaveHeader>())), "prepare audio");
                EnsureSuccess(waveOutWrite(
                    _handle,
                    _header,
                    checked((UInt32)Marshal.SizeOf<WaveHeader>())), "play audio");
            }
            catch
            {
                ReleaseHeader();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                StopCore();
            }
        }
    }

    public void SetVolume(Double volume)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            SetVolumeCore(volume);
        }
    }

    private Boolean StopCore()
    {
        if (_header == IntPtr.Zero)
        {
            return true;
        }
        return waveOutReset(_handle) == 0 && ReleaseHeader();
    }

    private Boolean ReleaseHeader()
    {
        if (_header != IntPtr.Zero)
        {
            UInt32 result = StillPlaying;
            for (Int32 attempt = 0; attempt < 50; attempt++)
            {
                result = waveOutUnprepareHeader(
                    _handle,
                    _header,
                    checked((UInt32)Marshal.SizeOf<WaveHeader>()));
                if (result != StillPlaying)
                {
                    break;
                }
                Thread.Sleep(2);
            }
            if (result != 0)
            {
                return false;
            }
            Marshal.FreeHGlobal(_header);
            _header = IntPtr.Zero;
        }
        if (_audioHandle.IsAllocated)
        {
            _audioHandle.Free();
        }
        return true;
    }

    private void SetVolumeCore(Double volume)
    {
        UInt32 level = checked((UInt32)Math.Round(Math.Clamp(volume, 0D, 1D) * UInt16.MaxValue));
        waveOutSetVolume(_handle, level | level << 16);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PcmAudioPlayer));
        }
    }

    private static void EnsureSuccess(UInt32 result, String action)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"Windows could not {action} ({result}).");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            Boolean stopped = StopCore();
            _disposed = true;
            if (stopped && _handle != IntPtr.Zero)
            {
                waveOutClose(_handle);
                _handle = IntPtr.Zero;
            }
            _audio = null;
            _audioPath = null;
        }
    }

    [LibraryImport("winmm.dll")]
    private static partial UInt32 waveOutOpen(
        out IntPtr handle,
        UInt32 deviceId,
        ref WaveFormat format,
        IntPtr callback,
        IntPtr instance,
        UInt32 flags);

    [LibraryImport("winmm.dll")]
    private static partial UInt32 waveOutPrepareHeader(IntPtr handle, IntPtr header, UInt32 size);

    [LibraryImport("winmm.dll")]
    private static partial UInt32 waveOutUnprepareHeader(IntPtr handle, IntPtr header, UInt32 size);

    [LibraryImport("winmm.dll")]
    private static partial UInt32 waveOutWrite(IntPtr handle, IntPtr header, UInt32 size);

    [LibraryImport("winmm.dll")]
    private static partial UInt32 waveOutReset(IntPtr handle);

    [LibraryImport("winmm.dll")]
    private static partial UInt32 waveOutSetVolume(IntPtr handle, UInt32 volume);

    [LibraryImport("winmm.dll")]
    private static partial UInt32 waveOutClose(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public UInt16 FormatTag;
        public UInt16 Channels;
        public UInt32 SamplesPerSecond;
        public UInt32 AverageBytesPerSecond;
        public UInt16 BlockAlignment;
        public UInt16 BitsPerSample;
        public UInt16 ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public UInt32 BufferLength;
        public UInt32 BytesRecorded;
        public IntPtr User;
        public UInt32 Flags;
        public UInt32 Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }
}
