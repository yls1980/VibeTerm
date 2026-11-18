using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VibeTerm.Mcp;

internal sealed class PanelApiEndpoint
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int Pid { get; set; }
}

internal static class VibeTermApiClient
{
    public const string EndpointEnvironmentVariable = "VIBETERM_PANEL_API_FILE";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(70)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Task<string> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, null, cancellationToken);

    public static Task<string> DeleteAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, path, null, cancellationToken);

    public static Task<string> PostAsync(string path, object? body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, body, cancellationToken);

    private static async Task<string> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        PanelApiEndpoint endpoint = LoadEndpoint();
        var request = new HttpRequestMessage(method, endpoint.BaseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
        if (body != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(responseBody)
                ? $"Panel API returned {(int)response.StatusCode}."
                : responseBody);
        }

        return responseBody;
    }

    private static PanelApiEndpoint LoadEndpoint()
    {
        string path = ResolvePath();
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "VibeTerm panel API is not available. Start VibeTerm and keep the local panel API enabled.");
        }

        PanelApiEndpoint? endpoint;
        try
        {
            endpoint = JsonSerializer.Deserialize<PanelApiEndpoint>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("VibeTerm panel API address file cannot be read.", ex);
        }

        if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.BaseUrl) || string.IsNullOrWhiteSpace(endpoint.Token))
            throw new InvalidOperationException("VibeTerm panel API address file is incomplete.");

        return endpoint;
    }

    private static string ResolvePath()
    {
        string? configured = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VibeTerm",
            "panel-api.json");
    }
}
