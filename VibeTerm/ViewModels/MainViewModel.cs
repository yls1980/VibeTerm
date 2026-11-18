using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using VibeTerm.Logging;
using VibeTerm.Settings;
using VibeTerm.Terminal;

namespace VibeTerm.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TerminalSessionManager _manager;
    private readonly TerminalLoggingService _logger;
    private TerminalViewModel?             _selectedTerminal;

    public ObservableCollection<TerminalViewModel> Terminals { get; } = new();
    public ObservableCollection<EnvironmentVariableViewModel> EnvironmentVariables { get; } = new();

    public TerminalViewModel? SelectedTerminal
    {
        get => _selectedTerminal;
        set
        {
            if (ReferenceEquals(_selectedTerminal, value))
                return;

            _selectedTerminal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTerminals));
        }
    }

    public bool HasTerminals => Terminals.Count > 0;

    public AppSettings Settings { get; }

    public ICommand AddTerminalCommand   { get; }
    public ICommand CloseTerminalCommand { get; }
    public ICommand NextTerminalCommand  { get; }
    public ICommand PrevTerminalCommand  { get; }
    public ICommand OpenSettingsCommand  { get; }
    public ICommand AddEnvironmentVariableCommand { get; }
    public ICommand RemoveEnvironmentVariableCommand { get; }
    public ICommand ApplyEnvironmentVariablesCommand { get; }

    public event EventHandler<TerminalViewModel>? TerminalAdded;
    public event EventHandler<TerminalViewModel>? TerminalRemoved;
    public event EventHandler?                    OpenSettingsRequested;

    public MainViewModel()
    {
        Settings = App.Settings;
        LoadEnvironmentVariables();

        _logger  = new TerminalLoggingService(Settings);
        _manager = new TerminalSessionManager(_logger);

        AddTerminalCommand = new RelayCommand(() => AddTerminal());
        CloseTerminalCommand = new RelayCommand(
            p => _ = CloseTerminalAsync(p as TerminalViewModel ?? SelectedTerminal),
            p => (p as TerminalViewModel ?? SelectedTerminal) != null);
        NextTerminalCommand = new RelayCommand(SelectNext,
            () => Terminals.Count > 1);
        PrevTerminalCommand = new RelayCommand(SelectPrev,
            () => Terminals.Count > 1);
        OpenSettingsCommand = new RelayCommand(
            () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        AddEnvironmentVariableCommand = new RelayCommand(AddEnvironmentVariable);
        RemoveEnvironmentVariableCommand = new RelayCommand(
            p => RemoveEnvironmentVariable(p as EnvironmentVariableViewModel),
            p => p is EnvironmentVariableViewModel);
        ApplyEnvironmentVariablesCommand = new RelayCommand(
            () => _ = ApplyEnvironmentVariablesAsync());
    }

    public TerminalViewModel AddTerminal(string? displayName = null, string? workingDirectory = null)
    {
        SaveEnvironmentVariables();

        var session = _manager.CreateSession(Settings, displayName, workingDirectory);
        var vm      = new TerminalViewModel(session);
        vm.CloseRequested += (_, _) => _ = CloseTerminalAsync(vm);

        Terminals.Add(vm);
        OnPropertyChanged(nameof(HasTerminals));

        TerminalAdded?.Invoke(this, vm);
        SelectedTerminal = vm;
        return vm;
    }

    public async Task CloseTerminalAsync(TerminalViewModel? vm)
    {
        if (vm == null) return;

        await _manager.CloseSessionAsync(vm.Session).ConfigureAwait(false);

        // Must update UI on dispatcher
        App.Current.Dispatcher.Invoke(() =>
        {
            int idx = Terminals.IndexOf(vm);
            Terminals.Remove(vm);
            OnPropertyChanged(nameof(HasTerminals));
            TerminalRemoved?.Invoke(this, vm);

            if (Terminals.Count > 0)
                SelectedTerminal = Terminals[Math.Max(0, idx - 1)];
            else
                SelectedTerminal = null;
        });
    }

    public bool HasActiveSessions() => _manager.HasActiveSessions();

    public Dictionary<string, string> GetEnvironmentOverrides() =>
        EnvironmentVariables
            .Select(v => new { Name = v.Name.Trim(), v.Value })
            .Where(v => IsValidEnvironmentVariableName(v.Name))
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

    private void LoadEnvironmentVariables()
    {
        foreach (var pair in Settings.EnvironmentVariables.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            EnvironmentVariables.Add(new EnvironmentVariableViewModel
            {
                Name = pair.Key,
                Value = pair.Value
            });
        }
    }

    private void AddEnvironmentVariable()
    {
        EnvironmentVariables.Add(new EnvironmentVariableViewModel
        {
            Name = "NEW_VARIABLE",
            Value = string.Empty
        });
    }

    private void RemoveEnvironmentVariable(EnvironmentVariableViewModel? variable)
    {
        if (variable == null) return;
        EnvironmentVariables.Remove(variable);
    }

    public async Task ApplyEnvironmentVariablesAsync()
    {
        SaveEnvironmentVariables();

        foreach (var terminal in Terminals)
            await terminal.Session.ApplyEnvironmentAsync(Settings.EnvironmentVariables).ConfigureAwait(false);
    }

    public void SaveEnvironmentVariables()
    {
        Settings.EnvironmentVariables = GetEnvironmentOverrides();
        App.SettingsService.Save(Settings);
    }

    private static bool IsValidEnvironmentVariableName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.Contains('=') &&
        !name.Contains('\0');

    public void SelectNext()
    {
        if (Terminals.Count < 2 || SelectedTerminal == null) return;
        int idx = Terminals.IndexOf(SelectedTerminal);
        SelectedTerminal = Terminals[(idx + 1) % Terminals.Count];
    }

    public void SelectPrev()
    {
        if (Terminals.Count < 2 || SelectedTerminal == null) return;
        int idx = Terminals.IndexOf(SelectedTerminal);
        SelectedTerminal = Terminals[(idx - 1 + Terminals.Count) % Terminals.Count];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        SaveEnvironmentVariables();
        _manager.Dispose();
        _logger.Dispose();
    }
}
