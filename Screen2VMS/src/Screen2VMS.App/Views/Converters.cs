using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Screen2VMS.Core.Cameras;

namespace Screen2VMS.App.Views;

/// <summary>Inverts a boolean, for controls that are disabled while streaming.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;
}

/// <summary>Collapses an element when the bound boolean is true.</summary>
public sealed class BooleanToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Colours the status dot by camera state (spec 63).</summary>
public sealed class CameraStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is CameraState state
            ? state switch
            {
                CameraState.Running => "RunningBrush",
                CameraState.Starting => "FaultBrush",
                CameraState.Busy or CameraState.Disconnected or CameraState.Error => "FaultBrush",
                _ => "StoppedBrush",
            }
            : "StoppedBrush";

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
