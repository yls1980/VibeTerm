using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using VibeTerm.Api;
using Xunit;

namespace VibeTerm.Tests;

public sealed class McpPanelServerTests
{
    [Fact]
    public async Task StdioServerListsToolsAndCallsTheRunningApi()
    {
        string dll = FindServerDll();
        var panels = new RecordingPanels();
        using var api = new PanelApiServer(panels, token: "mcp-test-token");
        api.Start();

        string endpointPath = Path.Combine(Path.GetTempPath(), "vibeterm-mcp-" + Guid.NewGuid().ToString("N") + ".json");
        PanelApiEndpointFile.Write(endpointPath, api.BaseUrl!, api.Token);

        var stderr = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "\"" + dll + "\"",
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };
        process.StartInfo.Environment["VIBETERM_PANEL_API_FILE"] = endpointPath;
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
                stderr.AppendLine(args.Data);
        };

        try
        {
            Assert.True(process.Start());
            process.BeginErrorReadLine();

            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"vibeterm-test","version":"1.0"}}}""");
            string initialized = await ReadUntilIdAsync(process.StandardOutput, 1, stderr);
            Assert.Contains("serverInfo", initialized);

            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            string tools = await ReadUntilIdAsync(process.StandardOutput, 2, stderr);
            Assert.Contains("list_panels", tools);
            Assert.Contains("list_agents", tools);
            Assert.Contains("ask_agent", tools);
            Assert.Contains("ask_agents", tools);
            Assert.Contains("send_input", tools);
            Assert.Contains("read_output", tools);
            Assert.DoesNotContain("cancellationToken", tools);

            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_panels","arguments":{}}}""");
            string listed = await ReadUntilIdAsync(process.StandardOutput, 3, stderr);
            Assert.Contains("Build", listed);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            if (File.Exists(endpointPath))
                File.Delete(endpointPath);
        }
    }

    [Fact]
    public async Task AskAgentReturnsTextPrintedByTheRunningPanel()
    {
        string dll = FindServerDll();
        var panels = new TalkingPanels();
        using var api = new PanelApiServer(panels, token: "mcp-test-token");
        api.Start();

        string endpointPath = Path.Combine(Path.GetTempPath(), "vibeterm-mcp-" + Guid.NewGuid().ToString("N") + ".json");
        PanelApiEndpointFile.Write(endpointPath, api.BaseUrl!, api.Token);

        var stderr = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "\"" + dll + "\"",
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };
        process.StartInfo.Environment["VIBETERM_PANEL_API_FILE"] = endpointPath;
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
                stderr.AppendLine(args.Data);
        };

        try
        {
            Assert.True(process.Start());
            process.BeginErrorReadLine();

            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"vibeterm-test","version":"1.0"}}}""");
            await ReadUntilIdAsync(process.StandardOutput, 1, stderr);
            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await process.StandardInput.WriteLineAsync(
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"ask_agent","arguments":{"panelId":1,"prompt":"2+2","untilText":"ANSWER_OK","quietMs":5000,"timeoutMs":5000}}}""");
            string reply = await ReadUntilIdAsync(process.StandardOutput, 4, stderr);
            Assert.Contains("ANSWER_OK", reply);
            Assert.Contains("\\u0022settled\\u0022:true", reply);
            Assert.Equal("2+2", panels.LastPrompt);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            if (File.Exists(endpointPath))
                File.Delete(endpointPath);
        }
    }

    private static string FindServerDll()
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "VibeTerm.Mcp", "bin", configuration, "net7.0", "VibeTerm.Mcp.dll"));
        Assert.True(File.Exists(dll), "MCP server was not built: " + dll);
        return dll;
    }

    private static Task<string> ReadUntilIdAsync(StreamReader reader, int id, StringBuilder stderr) =>
        ReadUntilAsync(reader, line =>
            line.Contains("\"id\":" + id, StringComparison.Ordinal) ||
            line.Contains("\"id\": " + id, StringComparison.Ordinal), stderr, "id " + id);

    private static async Task<string> ReadUntilAsync(
        StreamReader reader,
        Func<string, bool> match,
        StringBuilder stderr,
        string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            Task<string?> read = reader.ReadLineAsync();
            Task finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(20)));
            if (finished != read)
                throw new TimeoutException("MCP server did not respond. stderr: " + stderr);

            string? line = await read;
            if (line == null)
                throw new InvalidOperationException("MCP server closed stdout. stderr: " + stderr);
            if (match(line))
                return line;
        }

        throw new TimeoutException("MCP server did not return " + description + ". stderr: " + stderr);
    }

    private sealed class RecordingPanels : IPanelController
    {
        public IReadOnlyList<PanelSnapshot> List() => new[]
        {
            new PanelSnapshot(1, 0, "Build", "active", true, "pwsh.exe", "C:\\work", true, null, 0)
        };

        public PanelSnapshot Get(int id) => List()[0];
        public PanelSnapshot Create(string? name, string? workingDirectory) => List()[0];
        public Task CloseAsync(int id) => Task.CompletedTask;
        public PanelSnapshot Select(int id) => List()[0];
        public PanelSnapshot Rename(int id, string name) => List()[0];
        public Task SendTextAsync(int id, string text, bool submit) => Task.CompletedTask;
        public Task SendKeysAsync(int id, string keys) => Task.CompletedTask;
        public TextChunk ReadOutput(int id, long since, int maxChars) => new(0, 0, string.Empty, false);
    }

    private sealed class TalkingPanels : IPanelController
    {
        private readonly object _gate = new();
        private readonly TerminalTextLog _log = new();

        public string? LastPrompt { get; private set; }

        public IReadOnlyList<PanelSnapshot> List()
        {
            lock (_gate)
                return new[] { Snapshot() };
        }

        public PanelSnapshot Get(int id) => List()[0];
        public PanelSnapshot Create(string? name, string? workingDirectory) => List()[0];
        public Task CloseAsync(int id) => Task.CompletedTask;
        public PanelSnapshot Select(int id) => List()[0];
        public PanelSnapshot Rename(int id, string name) => List()[0];

        public Task SendTextAsync(int id, string text, bool submit)
        {
            lock (_gate)
            {
                LastPrompt = text;
                _log.Append(Encoding.UTF8.GetBytes("prompt:" + text + "\r\nANSWER_OK\r\n"));
            }

            return Task.CompletedTask;
        }

        public Task SendKeysAsync(int id, string keys) => Task.CompletedTask;

        public TextChunk ReadOutput(int id, long since, int maxChars)
        {
            lock (_gate)
                return _log.Read(since, maxChars);
        }

        private PanelSnapshot Snapshot() =>
            new(1, 0, "Claude", "active", true, "pwsh.exe", "C:\\work", true, null, _log.NextOffset);
    }
}
