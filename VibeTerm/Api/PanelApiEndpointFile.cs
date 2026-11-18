using System;
using System.IO;
using System.Text.Json;

namespace VibeTerm.Api;

public sealed class PanelApiEndpoint
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int Pid { get; set; }
}

/// <summary>
/// Writes the loopback address and access token where the MCP server can find them.
/// The path can be overridden with the VIBETERM_PANEL_API_FILE environment variable.
/// </summary>
public static class PanelApiEndpointFile
{
    public const string EnvironmentVariableName = "VIBETERM_PANEL_API_FILE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VibeTerm",
            "panel-api.json");

    public static string ResolvePath()
    {
        string? configured = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(configured) ? DefaultPath : configured;
    }

    public static void Write(string path, string baseUrl, string token)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var endpoint = new PanelApiEndpoint
        {
            BaseUrl = baseUrl,
            Token = token,
            Pid = Environment.ProcessId
        };

        string json = JsonSerializer.Serialize(endpoint, JsonOptions);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static PanelApiEndpoint? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PanelApiEndpoint>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void DeleteIfOwned(string path, int pid)
    {
        try
        {
            PanelApiEndpoint? endpoint = TryRead(path);
            if (endpoint == null || endpoint.Pid != pid)
                return;

            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
