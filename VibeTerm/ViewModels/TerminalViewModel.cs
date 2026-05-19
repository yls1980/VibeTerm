using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VibeTerm.Terminal;

namespace VibeTerm.ViewModels;

public sealed class TerminalViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TerminalSession _session;
    private bool _isEditing;
    private string _editName = string.Empty;

    public TerminalSession Session => _session;

    public int    Id          => _session.Id;
    public string DisplayName => _session.DisplayName;

    public TerminalStatus Status => _session.Status;

    public string StatusText => _session.Status switch
    {
        TerminalStatus.Starting => "starting",
        TerminalStatus.Active   => "active",
        TerminalStatus.Running  => "running",
        TerminalStatus.Exited   => "exited",
        TerminalStatus.Error    => "error",
        _                       => "unknown"
    };

    public string StatusColor => _session.Status switch
    {
        TerminalStatus.Active   => "#6A9955",
        TerminalStatus.Running  => "#DCDCAA",
        TerminalStatus.Exited   => "#858585",
        TerminalStatus.Error    => "#F44747",
        _                       => "#858585"
    };

    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); }
    }

    public string EditName
    {
        get => _editName;
        set { _editName = value; OnPropertyChanged(); }
    }

    public ICommand BeginRenameCommand { get; }
    public ICommand CommitRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }

    public event EventHandler? CloseRequested;

    public TerminalViewModel(TerminalSession session)
    {
        _session = session;
        _session.StatusChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        };

        BeginRenameCommand = new RelayCommand(() =>
        {
            EditName  = DisplayName;
            IsEditing = true;
        });

        CommitRenameCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(EditName))
            {
                _session.Rename(EditName.Trim());
                OnPropertyChanged(nameof(DisplayName));
            }
            IsEditing = false;
        });

        CancelRenameCommand = new RelayCommand(() => IsEditing = false);
    }

    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose() => _session.Dispose();
}
