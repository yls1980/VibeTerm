using System.Text;
using System.Text.Json;

namespace VibeTerm.Mcp;

internal static class AgentOrchestrator
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<string> ListAgentsAsync(CancellationToken cancellationToken)
    {
        string body = await VibeTermApiClient.GetAsync("/api/v1/panels", cancellationToken).ConfigureAwait(false);
        var list = JsonSerializer.Deserialize<PanelListDto>(body, Json) ?? new PanelListDto();
        var agents = new List<object>();
        foreach (PanelDto panel in list.Panels)
        {
            string tail = await ReadTailAsync(panel, cancellationToken).ConfigureAwait(false);
            agents.Add(Describe(panel, tail));
        }

        return JsonSerializer.Serialize(new { agents }, Json);
    }

    public static Task<string> AskAsync(
        int panelId,
        string prompt,
        int quietMs,
        int timeoutMs,
        string? untilText,
        CancellationToken cancellationToken) =>
        SerializeAskAsync(panelId, prompt, quietMs, timeoutMs, untilText, cancellationToken);

    public static async Task<string> AskManyAsync(
        IReadOnlyList<int>? panelIds,
        string prompt,
        int quietMs,
        int timeoutMs,
        string? untilText,
        CancellationToken cancellationToken)
    {
        if (panelIds == null || panelIds.Count == 0)
            throw new InvalidOperationException("panelIds is required");

        string[] replies = await Task.WhenAll(panelIds.Distinct().Select(async panelId =>
        {
            try
            {
                return await SerializeAskAsync(
                    panelId, prompt, quietMs, timeoutMs, untilText, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { panelId, error = ex.Message }, Json);
            }
        })).ConfigureAwait(false);

        return "[" + string.Join(",", replies) + "]";
    }

    private static async Task<string> SerializeAskAsync(
        int panelId,
        string prompt,
        int quietMs,
        int timeoutMs,
        string? untilText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("prompt is required");

        quietMs = Math.Clamp(quietMs, 300, 30_000);
        timeoutMs = Math.Clamp(timeoutMs, 1_000, 120_000);

        string panelJson = await VibeTermApiClient.GetAsync($"/api/v1/panels/{panelId}", cancellationToken)
            .ConfigureAwait(false);
        var panel = JsonSerializer.Deserialize<PanelDto>(panelJson, Json)
            ?? throw new InvalidOperationException("panel response is empty");
        if (!panel.Ready)
            throw new InvalidOperationException("panel is not ready");

        long since = panel.OutputOffset;
        await VibeTermApiClient.PostAsync(
            $"/api/v1/panels/{panelId}/input",
            new { text = prompt, submit = true },
            cancellationToken).ConfigureAwait(false);

        var buffer = new StringBuilder();
        long cursor = since;
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        DateTime lastChange = DateTime.UtcNow;
        bool sawOutput = false;
        bool matched = false;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutputDto chunk = await ReadOutputAsync(panelId, cursor, 100_000, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(chunk.Text))
            {
                buffer.Append(chunk.Text);
                cursor = chunk.NextOffset;
                lastChange = DateTime.UtcNow;
                sawOutput = true;
                if (!string.IsNullOrEmpty(untilText) &&
                    buffer.ToString().Contains(untilText, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            bool quiet = sawOutput && DateTime.UtcNow - lastChange >= TimeSpan.FromMilliseconds(quietMs);
            if (quiet)
                break;

            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        bool settled = matched || (sawOutput && DateTime.UtcNow - lastChange >= TimeSpan.FromMilliseconds(quietMs));
        return JsonSerializer.Serialize(new
        {
            panelId,
            name = panel.Name,
            agent = Classify(panel.Name, buffer.ToString()),
            since,
            nextOffset = cursor,
            settled,
            matched,
            text = buffer.ToString()
        }, Json);
    }

    private static async Task<string> ReadTailAsync(PanelDto panel, CancellationToken cancellationToken)
    {
        long since = Math.Max(0, panel.OutputOffset - 2000);
        OutputDto output = await ReadOutputAsync(panel.Id, since, 2000, cancellationToken).ConfigureAwait(false);
        string text = output.Text ?? string.Empty;
        return text.Length <= 800 ? text : text[^800..];
    }

    private static async Task<OutputDto> ReadOutputAsync(
        int panelId,
        long since,
        int maxChars,
        CancellationToken cancellationToken)
    {
        string body = await VibeTermApiClient.GetAsync(
            $"/api/v1/panels/{panelId}/output?since={since}&maxChars={maxChars}",
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<OutputDto>(body, Json) ?? new OutputDto();
    }

    private static object Describe(PanelDto panel, string tail) => new
    {
        panel.Id,
        panel.Index,
        panel.Name,
        panel.Status,
        panel.Ready,
        panel.WorkingDirectory,
        panel.OutputOffset,
        agent = Classify(panel.Name, tail),
        tail
    };

    internal static string Classify(string? name, string? tail)
    {
        string haystack = (name ?? string.Empty) + "\n" + (tail ?? string.Empty);
        if (haystack.Contains("codex", StringComparison.OrdinalIgnoreCase))
            return "codex";
        if (haystack.Contains("claude", StringComparison.OrdinalIgnoreCase))
            return "claude";
        return "shell";
    }

    private sealed class PanelListDto
    {
        public List<PanelDto> Panels { get; set; } = new();
    }

    private sealed class PanelDto
    {
        public int Id { get; set; }
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool Ready { get; set; }
        public string? WorkingDirectory { get; set; }
        public long OutputOffset { get; set; }
    }

    private sealed class OutputDto
    {
        public long Since { get; set; }
        public long NextOffset { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool Truncated { get; set; }
    }
}
