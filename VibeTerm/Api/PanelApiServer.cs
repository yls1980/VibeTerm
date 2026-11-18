using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VibeTerm.Api;

/// <summary>
/// Loopback HTTP API. It accepts only 127.0.0.1 and requires the token
/// written next to the process.
/// </summary>
public sealed class PanelApiServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly IPanelController _controller;
    private readonly string _token;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public PanelApiServer(IPanelController controller, string? token = null)
    {
        _controller = controller;
        _token = string.IsNullOrWhiteSpace(token) ? CreateToken() : token;
    }

    public string Token => _token;

    public string? BaseUrl { get; private set; }

    public void Start()
    {
        if (_listener != null)
            throw new InvalidOperationException("Panel API is already started.");

        int port = GetFreeLoopbackPort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        _listener = listener;
        BaseUrl = $"http://127.0.0.1:{port}";
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => ListenAsync(listener, _cts.Token));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts?.Cancel();
        try { _listener?.Stop(); }
        catch (ObjectDisposedException) { }
        catch (HttpListenerException) { }

        try { _loop?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }

        _listener?.Close();
        _cts?.Dispose();
    }

    private async Task ListenAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break;
            }

            _ = Task.Run(() => HandleSafeAsync(context));
        }
    }

    private async Task HandleSafeAsync(HttpListenerContext context)
    {
        try
        {
            await HandleAsync(context).ConfigureAwait(false);
        }
        catch (PanelApiException ex)
        {
            await TryWriteErrorAsync(context.Response, ex.StatusCode, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Panel API request failed: " + ex.Message);
            await TryWriteErrorAsync(context.Response, 500, "internal error").ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        response.Headers["Cache-Control"] = "no-store";

        if (!IsAuthorized(request))
        {
            await WriteJsonAsync(response, 401, new { error = "unauthorized" }).ConfigureAwait(false);
            return;
        }

        string path = (request.Url?.AbsolutePath ?? "/").TrimEnd('/');
        string method = request.HttpMethod;

        if (method == "GET" && path == "/api/v1/health")
        {
            await WriteJsonAsync(response, 200, new { ok = true }).ConfigureAwait(false);
            return;
        }

        if (method == "GET" && path == "/api/v1/panels")
        {
            await WriteJsonAsync(response, 200, new { panels = _controller.List() }).ConfigureAwait(false);
            return;
        }

        if (method == "POST" && path == "/api/v1/panels")
        {
            CreatePanelRequest? body = await ReadJsonAsync<CreatePanelRequest>(request).ConfigureAwait(false);
            PanelSnapshot created = _controller.Create(body?.Name, body?.WorkingDirectory);
            await WriteJsonAsync(response, 201, created).ConfigureAwait(false);
            return;
        }

        if (!path.StartsWith("/api/v1/panels/", StringComparison.Ordinal))
        {
            await WriteJsonAsync(response, 404, new { error = "not found" }).ConfigureAwait(false);
            return;
        }

        string[] parts = path["/api/v1/panels/".Length..].Split('/');
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out int id))
        {
            await WriteJsonAsync(response, 404, new { error = "not found" }).ConfigureAwait(false);
            return;
        }

        string action = parts.Length == 2 ? parts[1] : string.Empty;
        switch (action)
        {
            case "" when method == "GET":
                await WriteJsonAsync(response, 200, _controller.Get(id)).ConfigureAwait(false);
                return;
            case "" when method == "DELETE":
                await _controller.CloseAsync(id).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, new { id, closed = true }).ConfigureAwait(false);
                return;
            case "select" when method == "POST":
                await WriteJsonAsync(response, 200, _controller.Select(id)).ConfigureAwait(false);
                return;
            case "rename" when method == "POST":
                RenamePanelRequest? rename = await ReadJsonAsync<RenamePanelRequest>(request).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, _controller.Rename(id, rename?.Name ?? string.Empty)).ConfigureAwait(false);
                return;
            case "input" when method == "POST":
                InputRequest? input = await ReadJsonAsync<InputRequest>(request).ConfigureAwait(false);
                await _controller.SendTextAsync(id, input?.Text ?? string.Empty, input?.Submit ?? false).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, new { id, sent = true }).ConfigureAwait(false);
                return;
            case "keys" when method == "POST":
                KeysRequest? keys = await ReadJsonAsync<KeysRequest>(request).ConfigureAwait(false);
                await _controller.SendKeysAsync(id, keys?.Keys ?? string.Empty).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, new { id, sent = true }).ConfigureAwait(false);
                return;
            case "output" when method == "GET":
                await WriteOutputAsync(response, id, request).ConfigureAwait(false);
                return;
            case "wait" when method == "POST":
                await WriteWaitAsync(response, id, request).ConfigureAwait(false);
                return;
            default:
                await WriteJsonAsync(response, 404, new { error = "not found" }).ConfigureAwait(false);
                return;
        }
    }

    private async Task WriteOutputAsync(HttpListenerResponse response, int id, HttpListenerRequest request)
    {
        var query = ParseQuery(request.Url?.Query);
        long since = ReadLong(query, "since", 0);
        int maxChars = (int)Math.Clamp(ReadLong(query, "maxChars", 16_000), 1, 100_000);
        if (since < 0)
            throw new PanelApiException(400, "since must be >= 0");

        TextChunk chunk = _controller.ReadOutput(id, since, maxChars);
        await WriteJsonAsync(response, 200, new
        {
            id,
            since = chunk.Since,
            nextOffset = chunk.NextOffset,
            text = chunk.Text,
            truncated = chunk.Truncated
        }).ConfigureAwait(false);
    }

    private async Task WriteWaitAsync(HttpListenerResponse response, int id, HttpListenerRequest request)
    {
        WaitRequest? body = await ReadJsonAsync<WaitRequest>(request).ConfigureAwait(false);
        string text = body?.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            throw new PanelApiException(400, "text is required");
        if (text.Length > 500)
            throw new PanelApiException(400, "text is too long");

        long since = body?.Since ?? 0;
        if (since < 0)
            throw new PanelApiException(400, "since must be >= 0");

        int timeoutMs = Math.Clamp(body?.TimeoutMs ?? 5000, 0, 30_000);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            TextChunk chunk = _controller.ReadOutput(id, since, 100_000);
            bool matched = chunk.Text.Contains(text, StringComparison.Ordinal);
            if (matched || DateTime.UtcNow >= deadline)
            {
                await WriteJsonAsync(response, 200, new
                {
                    matched,
                    id,
                    since = chunk.Since,
                    nextOffset = chunk.NextOffset,
                    text = chunk.Text,
                    truncated = chunk.Truncated
                }).ConfigureAwait(false);
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        string? header = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        string provided = header[7..].Trim();
        byte[] actual = Encoding.UTF8.GetBytes(provided);
        byte[] expected = Encoding.UTF8.GetBytes(_token);
        if (actual.Length != expected.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
            return default;

        if (request.ContentLength64 > 256 * 1024)
            throw new PanelApiException(400, "request body is too large");

        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        char[] buffer = new char[256 * 1024 + 1];
        int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        if (read > 256 * 1024)
            throw new PanelApiException(400, "request body is too large");

        string json = new string(buffer, 0, read);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw new PanelApiException(400, "request body is not valid JSON");
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object body)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static async Task TryWriteErrorAsync(HttpListenerResponse response, int statusCode, string message)
    {
        try
        {
            await WriteJsonAsync(response, statusCode, new { error = message }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return result;

        string text = query.StartsWith('?') ? query[1..] : query;
        foreach (string part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            string value = eq < 0 ? string.Empty : Uri.UnescapeDataString(part[(eq + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static long ReadLong(Dictionary<string, string> query, string name, long fallback)
    {
        if (!query.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!long.TryParse(value, out long parsed))
            throw new PanelApiException(400, name + " must be an integer");

        return parsed;
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CreateToken()
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return token.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class CreatePanelRequest
    {
        public string? Name { get; set; }
        public string? WorkingDirectory { get; set; }
    }

    private sealed class RenamePanelRequest
    {
        public string? Name { get; set; }
    }

    private sealed class InputRequest
    {
        public string? Text { get; set; }
        public bool? Submit { get; set; }
    }

    private sealed class KeysRequest
    {
        public string? Keys { get; set; }
    }

    private sealed class WaitRequest
    {
        public string? Text { get; set; }
        public long? Since { get; set; }
        public int? TimeoutMs { get; set; }
    }
}
