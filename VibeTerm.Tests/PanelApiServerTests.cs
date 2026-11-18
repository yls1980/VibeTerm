using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VibeTerm.Api;
using Xunit;

namespace VibeTerm.Tests;

public sealed class PanelApiServerTests
{
    [Fact]
    public async Task ListsCreatesRenamesSelectsAndClosesPanels()
    {
        var panels = new FakePanels();
        using var server = Start(panels);
        using var client = new ApiClient(server);

        using JsonDocument empty = await client.GetAsync("/api/v1/panels");
        Assert.Empty(empty.RootElement.GetProperty("panels").EnumerateArray());

        using JsonDocument created = await client.SendAsync(HttpMethod.Post, "/api/v1/panels", """{"name":"Build","workingDirectory":null}""");
        Assert.Equal(201, client.LastStatus);
        int id = created.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("Build", created.RootElement.GetProperty("name").GetString());
        Assert.True(created.RootElement.GetProperty("selected").GetBoolean());

        using JsonDocument renamed = await client.SendAsync(HttpMethod.Post, $"/api/v1/panels/{id}/rename", """{"name":"Logs"}""");
        Assert.Equal("Logs", renamed.RootElement.GetProperty("name").GetString());

        using JsonDocument second = await client.SendAsync(HttpMethod.Post, "/api/v1/panels", """{"name":"Other"}""");
        int secondId = second.RootElement.GetProperty("id").GetInt32();
        using JsonDocument selected = await client.SendAsync(HttpMethod.Post, $"/api/v1/panels/{id}/select", "{}");
        Assert.True(selected.RootElement.GetProperty("selected").GetBoolean());
        Assert.Equal(id, selected.RootElement.GetProperty("id").GetInt32());

        using JsonDocument list = await client.GetAsync("/api/v1/panels");
        Assert.Equal(2, list.RootElement.GetProperty("panels").GetArrayLength());

        using JsonDocument closed = await client.SendAsync(HttpMethod.Delete, $"/api/v1/panels/{secondId}", null);
        Assert.True(closed.RootElement.GetProperty("closed").GetBoolean());
        using JsonDocument afterClose = await client.GetAsync("/api/v1/panels");
        Assert.Equal(1, afterClose.RootElement.GetProperty("panels").GetArrayLength());
    }

    [Fact]
    public async Task SendsInputAndReadsIncrementalOutput()
    {
        var panels = new FakePanels();
        int id = panels.Add("PowerShell 1");
        using var server = Start(panels);
        using var client = new ApiClient(server);

        using JsonDocument sent = await client.SendAsync(
            HttpMethod.Post,
            $"/api/v1/panels/{id}/input",
            """{"text":"Write-Output hello","submit":true}""");
        Assert.True(sent.RootElement.GetProperty("sent").GetBoolean());
        Assert.Equal("Write-Output hello", panels.LastInput);
        Assert.True(panels.LastSubmit);

        using JsonDocument output = await client.GetAsync($"/api/v1/panels/{id}/output?since=0&maxChars=100");
        string text = output.RootElement.GetProperty("text").GetString() ?? "";
        Assert.Contains("Write-Output hello", text);
        long next = output.RootElement.GetProperty("nextOffset").GetInt64();

        using JsonDocument again = await client.GetAsync($"/api/v1/panels/{id}/output?since={next}");
        Assert.Equal(string.Empty, again.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task WaitsUntilTextAppears()
    {
        var panels = new FakePanels();
        int id = panels.Add("PowerShell 1");
        using var server = Start(panels);
        using var client = new ApiClient(server);

        Task<JsonDocument> waiting = client.SendAsync(
            HttpMethod.Post,
            $"/api/v1/panels/{id}/wait",
            """{"text":"READY","since":0,"timeoutMs":2000}""");

        await Task.Delay(120);
        panels.Append(id, "line\nREADY\n");

        using JsonDocument result = await waiting;
        Assert.True(result.RootElement.GetProperty("matched").GetBoolean());
        Assert.Contains("READY", result.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task WaitReportsMissWhenTextDoesNotAppear()
    {
        var panels = new FakePanels();
        int id = panels.Add("PowerShell 1");
        using var server = Start(panels);
        using var client = new ApiClient(server);

        using JsonDocument result = await client.SendAsync(
            HttpMethod.Post,
            $"/api/v1/panels/{id}/wait",
            """{"text":"missing","since":0,"timeoutMs":150}""");

        Assert.False(result.RootElement.GetProperty("matched").GetBoolean());
    }

    [Fact]
    public async Task RejectsMissingTokenUnknownPanelAndBadJson()
    {
        var panels = new FakePanels();
        int id = panels.Add("PowerShell 1");
        using var server = Start(panels);
        using var client = new ApiClient(server);

        using HttpResponseMessage unauthorized = await client.SendRawAsync(HttpMethod.Get, "/api/v1/panels", null, token: "wrong");
        Assert.Equal(401, (int)unauthorized.StatusCode);

        using JsonDocument missing = await client.GetAsync("/api/v1/panels/999", expectSuccess: false);
        Assert.Equal(404, client.LastStatus);
        Assert.Equal("panel not found", missing.RootElement.GetProperty("error").GetString());

        using JsonDocument badJson = await client.SendAsync(HttpMethod.Post, $"/api/v1/panels/{id}/rename", "{", expectSuccess: false);
        Assert.Equal(400, client.LastStatus);
        Assert.Equal("request body is not valid JSON", badJson.RootElement.GetProperty("error").GetString());

        using JsonDocument badKey = await client.SendAsync(
            HttpMethod.Post,
            $"/api/v1/panels/{id}/keys",
            """{"keys":"not-a-key"}""",
            expectSuccess: false);
        Assert.Equal(400, client.LastStatus);
    }

    [Fact]
    public void EndpointFileRoundTripKeepsAddressAndToken()
    {
        string path = Path.Combine(Path.GetTempPath(), "vibeterm-endpoint-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            PanelApiEndpointFile.Write(path, "http://127.0.0.1:9", "token-value");
            PanelApiEndpoint? endpoint = PanelApiEndpointFile.TryRead(path);
            Assert.NotNull(endpoint);
            Assert.Equal("http://127.0.0.1:9", endpoint!.BaseUrl);
            Assert.Equal("token-value", endpoint.Token);
            Assert.Equal(Environment.ProcessId, endpoint.Pid);

            PanelApiEndpointFile.DeleteIfOwned(path, Environment.ProcessId);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static PanelApiServer Start(FakePanels panels)
    {
        var server = new PanelApiServer(panels, token: "test-token");
        server.Start();
        return server;
    }

    private sealed class ApiClient : IDisposable
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly string _baseUrl;
        private readonly string _token;

        public ApiClient(PanelApiServer server)
        {
            _baseUrl = server.BaseUrl ?? throw new InvalidOperationException("API did not start.");
            _token = server.Token;
        }

        public int LastStatus { get; private set; }

        public async Task<JsonDocument> GetAsync(string path, bool expectSuccess = true)
        {
            using HttpResponseMessage response = await SendRawAsync(HttpMethod.Get, path, null, _token);
            return await ReadAsync(response, expectSuccess);
        }

        public async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? json, bool expectSuccess = true)
        {
            using HttpResponseMessage response = await SendRawAsync(method, path, json, _token);
            return await ReadAsync(response, expectSuccess);
        }

        public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, string? json, string? token)
        {
            var request = new HttpRequestMessage(method, _baseUrl + path);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json != null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return await _http.SendAsync(request);
        }

        private async Task<JsonDocument> ReadAsync(HttpResponseMessage response, bool expectSuccess)
        {
            LastStatus = (int)response.StatusCode;
            string body = await response.Content.ReadAsStringAsync();
            if (expectSuccess)
                Assert.True(response.IsSuccessStatusCode, body);

            return JsonDocument.Parse(body);
        }

        public void Dispose() => _http.Dispose();
    }

    private sealed class FakePanels : IPanelController
    {
        private readonly object _gate = new();
        private readonly List<Panel> _panels = new();
        private int _nextId = 1;
        private int _selectedId;

        public string? LastInput { get; private set; }
        public bool LastSubmit { get; private set; }

        public int Add(string name)
        {
            lock (_gate)
            {
                int id = _nextId++;
                _panels.Add(new Panel { Id = id, Name = name });
                _selectedId = id;
                return id;
            }
        }

        public void Append(int id, string text)
        {
            lock (_gate)
                Require(id).Log.Append(Encoding.UTF8.GetBytes(text));
        }

        public IReadOnlyList<PanelSnapshot> List()
        {
            lock (_gate)
                return _panels.Select(ToSnapshot).ToList();
        }

        public PanelSnapshot Get(int id)
        {
            lock (_gate)
                return ToSnapshot(Require(id));
        }

        public PanelSnapshot Create(string? name, string? workingDirectory)
        {
            return Get(Add(string.IsNullOrWhiteSpace(name) ? "PowerShell" : name.Trim()));
        }

        public Task CloseAsync(int id)
        {
            lock (_gate)
                _panels.Remove(Require(id));
            return Task.CompletedTask;
        }

        public PanelSnapshot Select(int id)
        {
            lock (_gate)
            {
                Panel panel = Require(id);
                _selectedId = id;
                return ToSnapshot(panel);
            }
        }

        public PanelSnapshot Rename(int id, string name)
        {
            lock (_gate)
            {
                Panel panel = Require(id);
                panel.Name = name;
                return ToSnapshot(panel);
            }
        }

        public Task SendTextAsync(int id, string text, bool submit)
        {
            lock (_gate)
            {
                Panel panel = Require(id);
                LastInput = text;
                LastSubmit = submit;
                panel.Log.Append(Encoding.UTF8.GetBytes(text + "\n"));
            }

            return Task.CompletedTask;
        }

        public Task SendKeysAsync(int id, string keys)
        {
            _ = PanelInputEncoder.EncodeKeys(keys);
            lock (_gate)
                Require(id);
            return Task.CompletedTask;
        }

        public TextChunk ReadOutput(int id, long since, int maxChars)
        {
            lock (_gate)
                return Require(id).Log.Read(since, maxChars);
        }

        private Panel Require(int id)
        {
            Panel? panel = _panels.FirstOrDefault(item => item.Id == id);
            if (panel == null)
                throw new PanelApiException(404, "panel not found");
            return panel;
        }

        private PanelSnapshot ToSnapshot(Panel panel) =>
            new(
                panel.Id,
                _panels.IndexOf(panel),
                panel.Name,
                "active",
                true,
                "pwsh.exe",
                null,
                panel.Id == _selectedId,
                null,
                panel.Log.NextOffset);

        private sealed class Panel
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public TerminalTextLog Log { get; } = new();
        }
    }
}
