using System.ComponentModel;
using ModelContextProtocol.Server;

namespace VibeTerm.Mcp;

[McpServerToolType]
public static class PanelTools
{
    [McpServerTool(Name = "list_panels"), Description("Lists terminal panels in the running VibeTerm window.")]
    public static Task<string> ListPanels(CancellationToken cancellationToken = default) =>
        VibeTermApiClient.GetAsync("/api/v1/panels", cancellationToken);

    [McpServerTool(Name = "get_panel"), Description("Returns one terminal panel by id, including status, directory, and the current output offset.")]
    public static Task<string> GetPanel(
        [Description("Panel id from list_panels.")] int panelId,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.GetAsync($"/api/v1/panels/{panelId}", cancellationToken);

    [McpServerTool(Name = "create_panel"), Description("Opens a new terminal panel. The shell may still be starting; wait until ready is true before send_input.")]
    public static Task<string> CreatePanel(
        [Description("Sidebar name. Omit to use the default PowerShell name.")] string? name = null,
        [Description("Existing directory for the new shell. Omit to use the configured startup directory.")] string? workingDirectory = null,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.PostAsync("/api/v1/panels", new { name, workingDirectory }, cancellationToken);

    [McpServerTool(Name = "close_panel"), Description("Closes a terminal panel and stops its shell.")]
    public static Task<string> ClosePanel(
        [Description("Panel id from list_panels.")] int panelId,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.DeleteAsync($"/api/v1/panels/{panelId}", cancellationToken);

    [McpServerTool(Name = "select_panel"), Description("Selects a panel and moves keyboard focus to it.")]
    public static Task<string> SelectPanel(
        [Description("Panel id from list_panels.")] int panelId,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.PostAsync($"/api/v1/panels/{panelId}/select", new { }, cancellationToken);

    [McpServerTool(Name = "rename_panel"), Description("Renames a panel in the sidebar.")]
    public static Task<string> RenamePanel(
        [Description("Panel id from list_panels.")] int panelId,
        [Description("New sidebar name.")] string name,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.PostAsync($"/api/v1/panels/{panelId}/rename", new { name }, cancellationToken);

    [McpServerTool(Name = "send_input"), Description("Types text into one panel. When submit is true, Enter is pressed after the text. The panel must already be ready.")]
    public static Task<string> SendInput(
        [Description("Panel id from list_panels.")] int panelId,
        [Description("Text to type. Newlines become Enter when submit is true.")] string text,
        [Description("Press Enter after the text. Defaults to true.")] bool submit = true,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.PostAsync($"/api/v1/panels/{panelId}/input", new { text, submit }, cancellationToken);

    [McpServerTool(Name = "send_keys"), Description("Sends one key to a panel: enter, tab, escape, backspace, delete, up, down, left, right, home, end, pageup, pagedown, space, ctrl+c, ctrl+d, ctrl+z, ctrl+l.")]
    public static Task<string> SendKeys(
        [Description("Panel id from list_panels.")] int panelId,
        [Description("Key name, for example ctrl+c or enter.")] string keys,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.PostAsync($"/api/v1/panels/{panelId}/keys", new { keys }, cancellationToken);

    [McpServerTool(Name = "read_output"), Description("Reads plain text from a panel. Pass the previous nextOffset as since. Color codes are removed. This is the current process output, not a pixel copy of the screen.")]
    public static Task<string> ReadOutput(
        [Description("Panel id from list_panels.")] int panelId,
        [Description("Offset returned by the previous read. Use 0 to start at the oldest kept text.")] long since = 0,
        [Description("Maximum number of characters to return, from 1 to 100000. Defaults to 16000.")] int maxChars = 16000,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.GetAsync($"/api/v1/panels/{panelId}/output?since={since}&maxChars={maxChars}", cancellationToken);

    [McpServerTool(Name = "wait_for_text"), Description("Waits until the panel output since the given offset contains the exact text, or until timeoutMs elapses. timeoutMs is capped at 30000. The search looks at the first 100000 characters after since.")]
    public static Task<string> WaitForText(
        [Description("Panel id from list_panels.")] int panelId,
        [Description("Exact text to wait for.")] string text,
        [Description("Output offset to start from.")] long since = 0,
        [Description("How long to wait, in milliseconds. Defaults to 5000 and cannot exceed 30000.")] int timeoutMs = 5000,
        CancellationToken cancellationToken = default) =>
        VibeTermApiClient.PostAsync(
            $"/api/v1/panels/{panelId}/wait",
            new { text, since, timeoutMs },
            cancellationToken);
}
