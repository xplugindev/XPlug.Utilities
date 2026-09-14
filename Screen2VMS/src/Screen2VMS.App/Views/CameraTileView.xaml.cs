using System.Windows.Controls;

namespace Screen2VMS.App.Views;

/// <summary>
/// One camera in the multi-camera grid: its own preview, controls and ONVIF
/// connection details (spec override, see CLAUDE.md "multi-camera").
/// </summary>
public partial class CameraTileView : UserControl
{
    public CameraTileView()
    {
        InitializeComponent();
    }
}
