using System.Windows;
using System.Windows.Controls;
using Screen2VMS.Core.Diagnostics;

namespace Screen2VMS.App.Views;

/// <summary>
/// One line of the status panel: a coloured dot, a component name and its
/// current detail (spec 63).
/// </summary>
public partial class StatusRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(StatusRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State),
            typeof(ComponentState),
            typeof(StatusRow),
            new PropertyMetadata(ComponentState.Stopped));

    public StatusRow()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public ComponentState State
    {
        get => (ComponentState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }
}
