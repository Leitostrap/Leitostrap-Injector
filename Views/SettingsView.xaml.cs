using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using LeitostrapV7.Core;


namespace LeitostrapV7.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly SettingsService _settings = SettingsService.Instance;
        private readonly ConsoleService _console = ConsoleService.Instance;


        private bool _dropdownOpen = false;
        private string _currentMethod = "memory";


        private readonly Dictionary<string, (string Label, string Desc)> _methods = new()
        {
            ["memory"] = ("Memory Offsets", "Recommended - uses offset database for direct memory writes"),
            ["cache"] = ("Cache Method", "Proxy only - modifies Roblox client settings"),
            ["combined"] = ("Combined", "Combines cache and memory methods"),
            ["offsetless"] = ("Memory Offsetless Pattern Scan", "Scans memory patterns - no offset database needed")
        };


        private readonly Dictionary<string, bool> _toggleSettings = new();


        public SettingsView()
        {
            InitializeComponent();
            LoadSettings();
        }


        private void LoadSettings()
        {
            _currentMethod = _settings.Get<string>("InjectionMethod", "memory");
            UpdateMethodDisplay();


            SetToggle("rpc_enabled", ToggleRpc, KnobRpc);
            SetToggle("tips_enabled", ToggleTips, KnobTips);
            SetToggle("notif_sound", ToggleNotifSound, KnobNotifSound);
            SetToggle("ui_sounds", ToggleUiSounds, KnobUiSounds);
            SetToggle("potato_mode", TogglePotato, KnobPotato);
        }


        private void SetToggle(string key, Border toggle, Border knob)
        {
            bool value = _settings.Get<bool>(key, false);
            _toggleSettings[key] = value;
            SetToggleVisual(toggle, knob, value, false);
        }


        private void SetToggleVisual(Border toggle, Border knob, bool value, bool animate = false)
        {
            double targetMarginLeft = value ? 21 : 3;
            if (animate)
            {
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                var anim = new System.Windows.Media.Animation.ThicknessAnimation(
                    new Thickness(targetMarginLeft, 0, 0, 0), TimeSpan.FromMilliseconds(200))
                { EasingFunction = ease };
                knob.BeginAnimation(MarginProperty, anim);
            }
            else
            {
                knob.BeginAnimation(MarginProperty, null);
                knob.Margin = new Thickness(targetMarginLeft, 0, 0, 0);
            }
        }


        private void ToggleSetting_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            string key = element.Tag?.ToString();
            if (string.IsNullOrEmpty(key)) return;


            bool newValue = !_toggleSettings.GetValueOrDefault(key, false);
            _toggleSettings[key] = newValue;
            _settings.Set(key, newValue);


            Border toggle = null;
            Border knob = null;


            switch (key)
            {
                case "rpc_enabled":
                    toggle = ToggleRpc; knob = KnobRpc;
                    DiscordService.Instance.SetEnabled(newValue);
                    break;
                case "tips_enabled":
                    toggle = ToggleTips; knob = KnobTips; break;
                case "notif_sound":
                    toggle = ToggleNotifSound; knob = KnobNotifSound; break;
                case "ui_sounds":
                    toggle = ToggleUiSounds; knob = KnobUiSounds; break;
                case "potato_mode":
                    toggle = TogglePotato; knob = KnobPotato; break;
            }


            if (toggle != null && knob != null)
                SetToggleVisual(toggle, knob, newValue, true);


            _console.Log($"Setting {key}: {(newValue ? "ON" : "OFF")}", "Info");
        }


        private void SpoofBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string newGuid = Guid.NewGuid().ToString("N");
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", true))
                {
                    key?.SetValue("MachineGuid", newGuid, RegistryValueKind.String);
                }
                HwidStatusText.Text = $"HWID spoofed! New GUID: {newGuid.Substring(0, 8)}...";
                _console.Log($"HWID spoofed: {newGuid.Substring(0, 8)}...", "Info");
            }
            catch (Exception ex)
            {
                HwidStatusText.Text = $"Error: {ex.Message}";
                _console.Log($"HWID spoof failed: {ex.Message}", "Error");
            }
        }


        private void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string newGuid = Guid.NewGuid().ToString();
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", true))
                {
                    key?.SetValue("MachineGuid", newGuid, RegistryValueKind.String);
                }
                HwidStatusText.Text = $"HWID restored! New GUID: {newGuid.Substring(0, 8)}...";
                _console.Log($"HWID restored: {newGuid.Substring(0, 8)}...", "Info");
            }
            catch (Exception ex)
            {
                HwidStatusText.Text = $"Error: {ex.Message}";
                _console.Log($"HWID restore failed: {ex.Message}", "Error");
            }
        }


        private void MethodDropdown_Click(object sender, MouseButtonEventArgs e)
        {
            _dropdownOpen = !_dropdownOpen;
            MethodOptionsPanel.Visibility = _dropdownOpen ? Visibility.Visible : Visibility.Collapsed;
        }


        private void Option_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            string method = element.Tag?.ToString();
            if (string.IsNullOrEmpty(method)) return;


            _currentMethod = method;
            _settings.Set("InjectionMethod", method);
            UpdateMethodDisplay();


            _dropdownOpen = false;
            MethodOptionsPanel.Visibility = Visibility.Collapsed;


            _console.Log($"Injection method: {_methods[method].Label}", "Info");
        }


        private void UpdateMethodDisplay()
        {
            if (_methods.TryGetValue(_currentMethod, out var info))
            {
                MethodLabel.Text = info.Label;
                MethodDesc.Text = info.Desc;
            }


            CheckCombined.Visibility = _currentMethod == "combined" ? Visibility.Visible : Visibility.Collapsed;
            CheckCache.Visibility = _currentMethod == "cache" ? Visibility.Visible : Visibility.Collapsed;
            CheckMemory.Visibility = _currentMethod == "memory" ? Visibility.Visible : Visibility.Collapsed;
            CheckOffsetless.Visibility = _currentMethod == "offsetless" ? Visibility.Visible : Visibility.Collapsed;
        }


        private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
        {
            HotkeyDisplay.Text = "Press a key...";
        }


        private void TestHotkey_Click(object sender, RoutedEventArgs e)
        {
            _console.Log($"Hotkey test: {_settings.Get<string>("overlay_hotkey", "Insert")}", "Info");
        }
    }
}
