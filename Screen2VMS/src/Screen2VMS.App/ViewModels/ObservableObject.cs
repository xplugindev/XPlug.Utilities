using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Screen2VMS.App.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulled from an MVVM toolkit: the UI is deliberately
/// tiny (spec 28) and this is the whole of what it needs.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
