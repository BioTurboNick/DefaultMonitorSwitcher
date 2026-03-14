namespace DefaultMonitorSwitcher.UI.Settings;

public partial class SettingsWindow : System.Windows.Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += (_, _) => Close();
    }
}
