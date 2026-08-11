using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace Lumui.Browser.Rendering;

public static class SemanticAssetCache
{
    private const Int32 MaximumEntries = 32;
    private const Int64 MaximumByteSize = 48L * 1024L * 1024L;
    private static readonly ConcurrentDictionary<String, Lazy<Task<Byte[]>>> Entries =
        new ConcurrentDictionary<String, Lazy<Task<Byte[]>>>(
            StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<String> Order =
        new ConcurrentQueue<String>();
    private static readonly ConcurrentDictionary<String, Int64> EntrySizes =
        new ConcurrentDictionary<String, Int64>(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<String, Lazy<Task<SemanticSvgDocument>>>
        SvgEntries = new ConcurrentDictionary<String, Lazy<Task<SemanticSvgDocument>>>(
            StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<String, WeakReference<Bitmap>> BitmapEntries =
        new ConcurrentDictionary<String, WeakReference<Bitmap>>(
            StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<String> SvgOrder =
        new ConcurrentQueue<String>();
    private static readonly ConcurrentQueue<String> BitmapOrder =
        new ConcurrentQueue<String>();
    private static Int64 _byteSize;

    public static async Task<Byte[]> GetAsync(
        Uri uri,
        Func<Task<Byte[]>> load,
        CancellationToken cancellationToken)
    {
        String key = uri.AbsoluteUri;
        Lazy<Task<Byte[]>> candidate = new Lazy<Task<Byte[]>>(
            load,
            LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<Byte[]>> entry = Entries.GetOrAdd(key, candidate);
        if (ReferenceEquals(entry, candidate))
        {
            Order.Enqueue(key);
            Trim();
        }
        try
        {
            Byte[] value = await entry.Value.WaitAsync(cancellationToken);
            if (EntrySizes.TryAdd(key, value.LongLength))
            {
                Interlocked.Add(ref _byteSize, value.LongLength);
                Trim();
            }
            return value;
        }
        catch
        {
            if (Entries.TryGetValue(key, out Lazy<Task<Byte[]>>? current)
                && ReferenceEquals(current, entry))
            {
                Entries.TryRemove(key, out _);
            }
            throw;
        }
    }

    public static async Task<SemanticSvgDocument> GetSvgAsync(
        Uri uri,
        Func<Task<SemanticSvgDocument>> load,
        CancellationToken cancellationToken)
    {
        String key = uri.AbsoluteUri;
        Lazy<Task<SemanticSvgDocument>> candidate =
            new Lazy<Task<SemanticSvgDocument>>(
                load,
                LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<SemanticSvgDocument>> entry = SvgEntries.GetOrAdd(
            key,
            candidate);
        if (ReferenceEquals(entry, candidate))
        {
            SvgOrder.Enqueue(key);
            TrimSvg();
        }
        try
        {
            return await entry.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            if (SvgEntries.TryGetValue(
                    key,
                    out Lazy<Task<SemanticSvgDocument>>? current)
                && ReferenceEquals(current, entry))
            {
                SvgEntries.TryRemove(key, out _);
            }
            throw;
        }
    }

    public static Boolean TryGetBitmap(Uri uri, out Bitmap? bitmap)
    {
        bitmap = null;
        return BitmapEntries.TryGetValue(
                uri.AbsoluteUri,
                out WeakReference<Bitmap>? reference)
            && reference.TryGetTarget(out bitmap);
    }

    public static void StoreBitmap(Uri uri, Bitmap bitmap)
    {
        String key = uri.AbsoluteUri;
        if (BitmapEntries.TryAdd(key, new WeakReference<Bitmap>(bitmap)))
        {
            BitmapOrder.Enqueue(key);
        }
        else
        {
            BitmapEntries[key] = new WeakReference<Bitmap>(bitmap);
        }
        TrimBitmaps();
    }

    private static void Trim()
    {
        while ((Entries.Count > MaximumEntries
                || Interlocked.Read(ref _byteSize) > MaximumByteSize)
            && Order.TryDequeue(out String? key))
        {
            if (Entries.TryRemove(key, out _)
                && EntrySizes.TryRemove(key, out Int64 size))
            {
                Interlocked.Add(ref _byteSize, -size);
            }
        }
    }

    private static void TrimSvg()
    {
        while (SvgEntries.Count > MaximumEntries
            && SvgOrder.TryDequeue(out String? key))
        {
            SvgEntries.TryRemove(key, out _);
        }
    }

    private static void TrimBitmaps()
    {
        while (BitmapEntries.Count > MaximumEntries
            && BitmapOrder.TryDequeue(out String? key))
        {
            BitmapEntries.TryRemove(key, out _);
        }
    }
}
