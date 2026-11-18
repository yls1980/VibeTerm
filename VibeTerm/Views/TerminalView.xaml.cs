using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using VibeTerm.Services;
using VibeTerm.Terminal;
using VibeTerm.ViewModels;

namespace VibeTerm.Views;

/// <summary>
/// Hosts a WebView2 running xterm.js and bridges it to a TerminalSession.
/// </summary>
public partial class TerminalView : UserControl
{
    private TerminalViewModel? _vm;
    private TerminalSession?   _session;
    private bool               _terminalReady;
    private bool               _disposed;
    private byte[]?            _pendingSnapshot;
    private double             _initialFontSize;

    private static readonly Lazy<Task<CoreWebView2Environment>> SharedEnvironment = new(CreateSharedEnvironmentAsync);

    public event EventHandler<string>? HotkeyRequested;

    public TerminalView()
    {
        InitializeComponent();
        WebView.PreviewKeyDown += OnWebViewPreviewKeyDown;
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void SetSnapshot(byte[] data)    => _pendingSnapshot  = data;
    public void SetInitialFontSize(double size) => _initialFontSize = size;

    public void Attach(TerminalViewModel vm)
    {
        _vm      = vm;
        _session = vm.Session;

        _session.OutputReceived += OnOutputReceived;
        _session.StatusChanged  += OnSessionStatusChanged;
        _session.Exited         += OnSessionExited;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitWebViewAsync().ConfigureAwait(false);
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            var env = await SharedEnvironment.Value.ConfigureAwait(false);

            await Dispatcher.InvokeAsync(async () =>
            {
                await WebView.EnsureCoreWebView2Async(env);
                ConfigureWebView();

                // Serve terminal.html and xterm.js assets from the local Resources
                // folder via a virtual HTTPS hostname.  terminal.html will try local
                // files first and fall back to CDN if they haven't been downloaded yet.
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.local",
                    XtermAssets.ResourcesDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                WebView.CoreWebView2.Navigate("https://app.local/terminal.html");
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                var tb = (TextBlock)LoadingOverlay.Child;
                if (tb != null) tb.Text = $"WebView2 error: {ex.Message}";
            });
        }
    }

    private static Task<CoreWebView2Environment> CreateSharedEnvironmentAsync()
    {
        // Shared user data folder and environment for all WebView2 instances
        // in this process.
        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VibeTerm", "WebView2Cache");

        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder);
    }

    private void ConfigureWebView()
    {
        var core = WebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled      = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled    = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += (_, _) => { /* terminal.html sends 'ready' when xterm is ready */ };
        core.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.ClipboardRead &&
                Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) &&
                uri.Host.Equals("app.local", StringComparison.OrdinalIgnoreCase))
            {
                args.State = CoreWebView2PermissionState.Allow;
                args.Handled = true;
            }
        };
    }

    private void OnWebViewPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ctrl && !shift && key == Key.V)
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                    _ = _session?.SendInputAsync(Encoding.UTF8.GetBytes(text));
            }
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && key == Key.A)
        {
            PostTerminalCommand("selectAll");
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && key == Key.C)
        {
            PostTerminalCommand("copyOrInterrupt");
            e.Handled = true;
            return;
        }

        string? command = key switch
        {
            Key.T when ctrl && shift => "newTerminal",
            Key.W when ctrl && !shift => "closeTerminal",
            Key.Tab when ctrl => shift ? "prevTerminal" : "nextTerminal",
            Key.OemPlus when ctrl => "increaseFont",
            Key.Add when ctrl => "increaseFont",
            Key.OemMinus when ctrl => "decreaseFont",
            Key.Subtract when ctrl => "decreaseFont",
            Key.D0 when ctrl => "resetFont",
            Key.NumPad0 when ctrl => "resetFont",
            _ => null
        };

        if (command == null)
            return;

        e.Handled = true;
        HotkeyRequested?.Invoke(this, command);
    }

    private void PostTerminalCommand(string type)
    {
        try
        {
            string msg = JsonSerializer.Serialize(new { type });
            WebView.CoreWebView2?.PostWebMessageAsString(msg);
        }
        catch { }
    }

    private void PostRaw(string json)
    {
        try { WebView.CoreWebView2?.PostWebMessageAsString(json); }
        catch { }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";

            switch (type)
            {
                case "ready":
                    _terminalReady = true;
                    short cols = (short)(root.TryGetProperty("cols", out var c) ? c.GetInt32() : 80);
                    short rows = (short)(root.TryGetProperty("rows", out var r) ? r.GetInt32() : 24);
                    Dispatcher.InvokeAsync(() => LoadingOverlay.Visibility = Visibility.Collapsed);
                    // Restore font size for this instance (no-op if default 14)
                    if (_initialFontSize > 0)
                        PostRaw(JsonSerializer.Serialize(new { type = "setFontSize", size = _initialFontSize }));
                    // Replay previous session output so the user sees their history
                    if (_pendingSnapshot is { Length: > 0 })
                    {
                        PostRaw(JsonSerializer.Serialize(new { type = "replay", data = Convert.ToBase64String(_pendingSnapshot) }));
                        _pendingSnapshot = null;
                    }
                    StartSession(cols, rows);
                    break;

                case "input":
                    if (root.TryGetProperty("data", out var dataEl))
                    {
                        string b64 = dataEl.GetString() ?? "";
                        byte[] bytes = Convert.FromBase64String(b64);
                        _ = _session?.SendInputAsync(bytes);
                    }
                    break;

                case "resize":
                    short newCols = (short)(root.TryGetProperty("cols", out var nc) ? nc.GetInt32() : 80);
                    short newRows = (short)(root.TryGetProperty("rows", out var nr) ? nr.GetInt32() : 24);
                    _session?.Resize(newCols, newRows);
                    break;

                case "cwd":
                    if (root.TryGetProperty("path", out var pathEl))
                    {
                        string? path = pathEl.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                            _session?.UpdateWorkingDirectory(path);
                    }
                    break;

                case "hotkey":
                    if (root.TryGetProperty("command", out var commandEl))
                    {
                        string? command = commandEl.GetString();
                        if (!string.IsNullOrWhiteSpace(command))
                            HotkeyRequested?.Invoke(this, command);
                    }
                    break;
            }
        }
        catch { /* malformed message — ignore */ }
    }

    private void StartSession(short cols, short rows)
    {
        if (_session == null) return;
        try
        {
            _session.Start(cols, rows);
        }
        catch (Exception ex)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ExitedText.Text = $"Failed to start shell:\n{ex.Message}";
                ExitedOverlay.Visibility = Visibility.Visible;
            });
        }
    }

    private void OnOutputReceived(object? sender, byte[] data)
    {
        if (!_terminalReady) return;
        string b64  = Convert.ToBase64String(data);
        string json = JsonSerializer.Serialize(new { type = "output", data = b64 });
        Dispatcher.InvokeAsync(() =>
        {
            try { WebView.CoreWebView2?.PostWebMessageAsString(json); }
            catch { }
        });
    }

    // TerminalViewModel subscribes to StatusChanged itself and raises INPC — nothing extra needed here
    private void OnSessionStatusChanged(object? sender, EventArgs e) { }

    private void OnSessionExited(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            uint code = _session?.ExitCode ?? 0;
            ExitedText.Text = code == 0
                ? "Process completed (exit code 0)"
                : $"Process exited with code {code}";
            ExitedOverlay.Visibility = Visibility.Visible;
        });
    }

    public void FocusTerminal()
    {
        try { WebView.CoreWebView2?.PostWebMessageAsString("{\"type\":\"focus\"}"); }
        catch { }
    }

    public void SetFontSize(double size)
    {
        try
        {
            string msg = JsonSerializer.Serialize(new { type = "setFontSize", size });
            WebView.CoreWebView2?.PostWebMessageAsString(msg);
        }
        catch { }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Detach()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session != null)
        {
            _session.OutputReceived -= OnOutputReceived;
            _session.StatusChanged  -= OnSessionStatusChanged;
            _session.Exited         -= OnSessionExited;
        }
    }

}
