using System.ComponentModel;
using ModelContextProtocol.Server;

namespace VibeTerm.Mcp;

[McpServerToolType]
public static class AgentTools
{
    [McpServerTool(Name = "list_agents"), Description("Lists panels in the already running VibeTerm window and guesses which CLI agent is in each one: claude, codex, or shell. Does not start the app or any agent. The guess uses the panel name and the tail of its current output.")]
    public static Task<string> ListAgents(CancellationToken cancellationToken = default) =>
        AgentOrchestrator.ListAgentsAsync(cancellationToken);

    [McpServerTool(Name = "ask_agent"), Description("Sends one prompt into a panel where a CLI agent is already running, then returns the new output. Does not launch claude or codex. Pass untilText when the agent is expected to print a known ending. Otherwise the call returns after the output stays quiet for quietMs, or when timeoutMs elapses. If settled is false, call read_output with nextOffset to collect the rest.")]
    public static Task<string> AskAgent(
        [Description("Panel id from list_agents.")] int panelId,
        [Description("Text to type. Enter is pressed after it.")] string prompt,
        [Description("Optional exact text that ends the wait as soon as it appears in the new output.")] string? untilText = null,
        [Description("How long new output must stay unchanged before the reply is returned, in milliseconds. Defaults to 4000.")] int quietMs = 4000,
        [Description("Maximum wait, in milliseconds. Defaults to 60000 and cannot exceed 120000.")] int timeoutMs = 60000,
        CancellationToken cancellationToken = default) =>
        AgentOrchestrator.AskAsync(panelId, prompt, quietMs, timeoutMs, untilText, cancellationToken);

    [McpServerTool(Name = "ask_agents"), Description("Sends the same prompt to several already running panels at the same time and returns each reply. Use this to orchestrate models that are already open in different panels. One failing panel does not cancel the others.")]
    public static Task<string> AskAgents(
        [Description("Panel ids from list_agents. Each id should be a different panel.")] int[] panelIds,
        [Description("Text to type into every selected panel.")] string prompt,
        [Description("Optional exact text that ends each wait.")] string? untilText = null,
        [Description("Quiet period per panel, in milliseconds. Defaults to 4000.")] int quietMs = 4000,
        [Description("Maximum wait per panel, in milliseconds. Defaults to 60000.")] int timeoutMs = 60000,
        CancellationToken cancellationToken = default) =>
        AgentOrchestrator.AskManyAsync(panelIds, prompt, quietMs, timeoutMs, untilText, cancellationToken);
}
