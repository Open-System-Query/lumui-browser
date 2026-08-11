using Lumui.Cli.Rendering;

namespace Lumui.Cli.Views;

public sealed class CliResourceWindow : Dialog
{
    private const Int64 MaximumTextBytes = 4L * 1024L * 1024L;
    private static readonly HttpClient Client = CreateClient();
    private readonly Uri _address;
    private readonly Label _status;
    private readonly ReadOnlyTextPane _content;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    private Boolean _closed;

    public CliResourceWindow(String title, Uri address)
    {
        _address = address;
        Title = title + " | LUMUI Browser";
        Width = Dim.Percent(90);
        Height = Dim.Percent(88);
        _status = new Label
        {
            Text = "Loading " + address.Host,
            X = 1,
            Y = 0,
            Width = Dim.Fill(2),
            SchemeName = "Accent"
        };
        _content = new ReadOnlyTextPane
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(3)
        };
        _content.SetContent(address.AbsoluteUri);
        Button close = new CliButton
        {
            Text = "Close",
            X = Pos.AnchorEnd(11),
            Y = Pos.AnchorEnd(2),
            IsDefault = true,
            SchemeName = "Accent"
        };
        close.Accepting += (_, _) => App?.RequestStop(this);
        Initialized += (_, _) => _ = LoadAsync();
        Disposing += (_, _) => Close();
        Add(_status, _content, close);
    }

    private async Task LoadAsync()
    {
        CancellationToken cancellationToken = _lifetime.Token;
        try
        {
            if (!_address.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !(_address.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && _address.IsLoopback))
            {
                App?.Invoke(() =>
                {
                    _status.Text = "External resource";
                    _content.SetContent(_address.AbsoluteUri);
                });
                return;
            }
            using HttpResponseMessage response = await Client.GetAsync(
                _address,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            Int64? contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaximumTextBytes)
            {
                throw new InvalidDataException("This text resource is larger than 4 MiB.");
            }
            String mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            if (!IsText(mediaType))
            {
                App?.Invoke(() =>
                {
                    if (!_closed)
                    {
                        _status.Text = "Binary resource  ·  " + mediaType;
                        _content.SetContent(_address.AbsoluteUri
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Size  " + (contentLength?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + " bytes");
                    }
                });
                return;
            }
            Byte[] data = await ReadAsync(response, cancellationToken).ConfigureAwait(false);
            String text = Decode(data, response.Content.Headers.ContentType?.CharSet);
            if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                text = FormatJson(text);
            }
            App?.Invoke(() =>
            {
                if (!_closed)
                {
                    _status.Text = mediaType + "  ·  " + data.Length.ToString(CultureInfo.InvariantCulture) + " bytes";
                    _content.SetContent(_address.AbsoluteUri + Environment.NewLine + Environment.NewLine + text);
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
            or UnauthorizedAccessException)
        {
            App?.Invoke(() =>
            {
                if (!_closed)
                {
                    _status.Text = "Resource unavailable";
                    _content.SetContent(_address.AbsoluteUri + Environment.NewLine + Environment.NewLine + exception.Message);
                }
            });
        }
    }

    private static async Task<Byte[]> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream output = new MemoryStream();
        Byte[] buffer = new Byte[32768];
        while (true)
        {
            Int32 read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }
            if (output.Length + read > MaximumTextBytes)
            {
                throw new InvalidDataException("This text resource is larger than 4 MiB.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Boolean IsText(String mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("yaml", StringComparison.OrdinalIgnoreCase);

    private static String Decode(Byte[] data, String? characterSet)
    {
        if (!String.IsNullOrWhiteSpace(characterSet))
        {
            try
            {
                return Encoding.GetEncoding(characterSet.Trim(' ', '\"')).GetString(data);
            }
            catch (ArgumentException)
            {
            }
        }
        return Encoding.UTF8.GetString(data);
    }

    private static String FormatJson(String value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(
                document.RootElement,
                LumuiJsonSerializerContext.Default.JsonElement);
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LUMUI-Browser-TerminalGui/1.0");
        return client;
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
