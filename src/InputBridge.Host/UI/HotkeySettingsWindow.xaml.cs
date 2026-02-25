using System.Windows;
using InputBridge.Core.Configuration;

namespace InputBridge.Host.UI;

public partial class HotkeySettingsWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly AppSettings _currentSettings;

    public HotkeySettingsWindow(SettingsManager settingsManager, AppSettings currentSettings)
    {
        InitializeComponent();
        _settingsManager = settingsManager;
        _currentSettings = currentSettings;

        TxtHostHotkey.Text = _currentSettings.Hotkeys.SwitchToHost;
        TxtClientHotkey.Text = _currentSettings.Hotkeys.SwitchToClient1;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _currentSettings.Hotkeys.SwitchToHost = TxtHostHotkey.Text;
        _currentSettings.Hotkeys.SwitchToClient1 = TxtClientHotkey.Text;

        _settingsManager.Save(_currentSettings);
        DialogResult = true;
        Close();
    }
}
