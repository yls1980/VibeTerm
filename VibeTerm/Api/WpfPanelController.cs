using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using VibeTerm.Terminal;
using VibeTerm.ViewModels;

namespace VibeTerm.Api;

/// <summary>
/// Runs panel operations on the WPF dispatcher, because the terminal list
/// is bound to the window.
/// </summary>
public sealed class WpfPanelController : IPanelController
{
    private readonly MainViewModel _viewModel;
    private readonly Dispatcher _dispatcher;

    public WpfPanelController(MainViewModel viewModel, Dispatcher dispatcher)
    {
        _viewModel = viewModel;
        _dispatcher = dispatcher;
    }

    public IReadOnlyList<PanelSnapshot> List() =>
        OnUi(() => _viewModel.Terminals.Select(ToSnapshot).ToList());

    public PanelSnapshot Get(int id) =>
        OnUi(() => ToSnapshot(Require(id)));

    public PanelSnapshot Create(string? name, string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
            throw new PanelApiException(400, "working directory does not exist");

        string? displayName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (displayName != null && displayName.Length > 80)
            throw new PanelApiException(400, "name is too long");

        return OnUi(() => ToSnapshot(_viewModel.AddTerminal(displayName, workingDirectory)));
    }

    public Task CloseAsync(int id)
    {
        TerminalViewModel panel = OnUi(() => Require(id));
        return _viewModel.CloseTerminalAsync(panel);
    }

    public PanelSnapshot Select(int id) =>
        OnUi(() =>
        {
            TerminalViewModel panel = Require(id);
            _viewModel.SelectedTerminal = panel;
            return ToSnapshot(panel);
        });

    public PanelSnapshot Rename(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new PanelApiException(400, "name is required");

        name = name.Trim();
        if (name.Length > 80)
            throw new PanelApiException(400, "name is too long");

        return OnUi(() =>
        {
            TerminalViewModel panel = Require(id);
            panel.Rename(name);
            return ToSnapshot(panel);
        });
    }

    public async Task SendTextAsync(int id, string text, bool submit)
    {
        if (text.Length > 100_000)
            throw new PanelApiException(400, "text is too long");

        TerminalSession session = OnUi(() => RequireReady(id));
        await session.SendInputAsync(PanelInputEncoder.EncodeText(text, submit)).ConfigureAwait(false);
    }

    public async Task SendKeysAsync(int id, string keys)
    {
        byte[] bytes = PanelInputEncoder.EncodeKeys(keys);
        TerminalSession session = OnUi(() => RequireReady(id));
        await session.SendInputAsync(bytes).ConfigureAwait(false);
    }

    public TextChunk ReadOutput(int id, long since, int maxChars) =>
        OnUi(() => Require(id).Session.ReadText(since, maxChars));

    private TerminalViewModel Require(int id)
    {
        TerminalViewModel? panel = _viewModel.Terminals.FirstOrDefault(item => item.Id == id);
        if (panel == null)
            throw new PanelApiException(404, "panel not found");

        return panel;
    }

    private TerminalSession RequireReady(int id)
    {
        TerminalSession session = Require(id).Session;
        if (session.Status is TerminalStatus.Exited or TerminalStatus.Error)
            throw new PanelApiException(409, "panel is not running");
        if (session.Status == TerminalStatus.Starting)
            throw new PanelApiException(409, "panel is not ready");

        return session;
    }

    private PanelSnapshot ToSnapshot(TerminalViewModel panel)
    {
        TerminalSession session = panel.Session;
        uint? exitCode = session.Status == TerminalStatus.Exited ? session.ExitCode : null;
        return new PanelSnapshot(
            session.Id,
            _viewModel.Terminals.IndexOf(panel),
            session.DisplayName,
            panel.StatusText,
            session.Status is TerminalStatus.Active or TerminalStatus.Running,
            session.ShellPath,
            session.WorkingDirectory,
            ReferenceEquals(_viewModel.SelectedTerminal, panel),
            exitCode,
            session.TextOffset);
    }

    private T OnUi<T>(Func<T> action)
    {
        if (_dispatcher.CheckAccess())
            return action();

        return _dispatcher.Invoke(action);
    }
}
