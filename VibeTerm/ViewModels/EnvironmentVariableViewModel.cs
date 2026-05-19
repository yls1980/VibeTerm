using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VibeTerm.ViewModels;

public sealed class EnvironmentVariableViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _value = string.Empty;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
