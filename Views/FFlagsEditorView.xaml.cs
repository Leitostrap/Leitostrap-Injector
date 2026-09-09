using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LeitostrapV7.Core;
using Microsoft.Win32;


namespace LeitostrapV7.Views
{
    public partial class FFlagsEditorView : UserControl
    {
        private readonly FFlagService _flagService = FFlagService.Instance;
        private readonly ProcessService _processService = ProcessService.Instance;
        private readonly ConsoleService _console = ConsoleService.Instance;
        private readonly SettingsService _settings = SettingsService.Instance;


        private ObservableCollection<FFlagEntry> _activeFlags;
        private string _activeTabName = "Default";
        private readonly Dictionary<string, ObservableCollection<FFlagEntry>> _tabFlags = new();
        private readonly List<string> _tabOrder = new() { "Default" };
        private System.Timers.Timer _statusTimer;
        private bool _applyOverlayVisible;
        private bool _explorerExpanded = true;
        private bool _suppressSync;


    public FFlagsEditorView()
    {
        InitializeComponent();


        _tabFlags["Default"] = _flagService.CurrentFlags;
        _activeFlags = _tabFlags["Default"];
        RestorePersistedTabs();
        FlagDataGrid.ItemsSource = _activeFlags;


            _statusTimer = new System.Timers.Timer(2000);
            _statusTimer.Elapsed += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateStatus));
            _statusTimer.AutoReset = true;
            _statusTimer.Start();


            UpdateStatus();
            UpdateEmptyState();


            SearchBox.TextChanged += SearchBox_TextChanged;


            _flagService.CurrentFlags.CollectionChanged += (s, e) =>
            {
                if (_suppressSync) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_activeTabName == "Default")
                        RefreshDefaultFromService();
                    FFlagsLoadedText.Text = _activeFlags.Count.ToString();
                    DefaultFlagCount.Text = $"   {_activeFlags.Count} flags";
                }));
            };


            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible && _activeTabName == "Default")
                    RefreshDefaultFromService();
            };


            string method = _settings.Get<string>("InjectionMethod", "memory");
            MethodLabel.Text = method switch
            {
                "cache" => "Proxy Method",
                "memory" => "Memory Offsets",
                "offsetless" => "Memory Offsetless",
                _ => "Combined"
            };
        }


        private void UpdateStatus()
        {
            try
            {
                bool running = _processService.IsRunning();
                RobloxStatusText.Text = running ? "Detected" : "Not Running";
                RobloxStatusText.Foreground = running ?
                    new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)) :
                    new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));


                if (running)
                {


                    int pid = -1;
                    try
                    {
                        var procs = System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta");
                        if (procs.Length > 0) pid = procs[0].Id;
                    }
                    catch { }
                    PidText.Text = pid > 0 ? pid.ToString() : "-";
                }
                else PidText.Text = "-";


                CacheOffsetsText.Text = OffsetService.Instance.GetAllCacheOffsets().Count.ToString();
                MemoryOffsetsText.Text = OffsetService.Instance.GetAllMemoryOffsets().Count.ToString();
                FFlagsLoadedText.Text = _activeFlags.Count.ToString();
                DefaultFlagCount.Text = $"   {_activeFlags.Count} flags";
            }
            catch { }
        }


        private void UpdateEmptyState()
        {
            bool hasFlags = _activeFlags.Count > 0;
            FlagTableBorder.Visibility = hasFlags ? Visibility.Visible : Visibility.Collapsed;
            WelcomePage.Visibility = hasFlags ? Visibility.Collapsed : Visibility.Visible;
        }


        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(query))
            {
                FlagDataGrid.ItemsSource = _activeFlags;
            }
            else
            {
                var view = new System.Windows.Data.ListCollectionView(_activeFlags);
                view.Filter = item =>
                {
                    if (item is FFlagEntry f)
                        return f.Name?.ToLower().Contains(query) == true || f.Value?.ToLower().Contains(query) == true;
                    return false;
                };
                FlagDataGrid.ItemsSource = view;
            }
        }


        private void TableSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TableSearchBox == null) return;
            string query = TableSearchBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(query))
            {
                FlagDataGrid.ItemsSource = _activeFlags;
            }
            else
            {
                var view = new System.Windows.Data.ListCollectionView(_activeFlags);
                view.Filter = item =>
                {
                    if (item is FFlagEntry f)
                        return f.Name?.ToLower().Contains(query) == true || f.Value?.ToLower().Contains(query) == true;
                    return false;
                };
                FlagDataGrid.ItemsSource = view;
            }
        }


        private void SyncCurrentFlags()
        {
            if (_suppressSync) return;


            if (_activeTabName != "Default")
            {
                RefreshSavedExplorer();
                FFlagsLoadedText.Text = _activeFlags.Count.ToString();
                DefaultFlagCount.Text = $"   {_activeFlags.Count} flags";
            }
        }


        private void RefreshDefaultFromService()
        {


            if (_activeFlags != _tabFlags["Default"])
                _activeFlags = _tabFlags["Default"];
            FlagDataGrid.ItemsSource = _activeFlags;
            UpdateEmptyState();
            FFlagsLoadedText.Text = _activeFlags.Count.ToString();
            DefaultFlagCount.Text = $"   {_activeFlags.Count} flags";
        }


        private void FFlagsSection_Click(object sender, MouseButtonEventArgs e)
        {
            FFlagsFileTree.Visibility = FFlagsFileTree.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            FFlagsToggleIcon.Symbol = FFlagsFileTree.Visibility == Visibility.Visible
                ? Wpf.Ui.Controls.SymbolRegular.ChevronDown20
                : Wpf.Ui.Controls.SymbolRegular.ChevronRight20;
        }


        private void ExplorerPanel_Click(object sender, MouseButtonEventArgs e)
        {
            _explorerExpanded = !_explorerExpanded;
            MainGrid.ColumnDefinitions[0].Width = _explorerExpanded ? new GridLength(200) : new GridLength(0);
            ExplorerBorder.Visibility = _explorerExpanded ? Visibility.Visible : Visibility.Collapsed;
            ExplorerToggleIcon.Symbol = _explorerExpanded
                ? Wpf.Ui.Controls.SymbolRegular.ChevronLeft20
                : Wpf.Ui.Controls.SymbolRegular.ChevronRight20;
            if (ExpandExplorerBtn != null)
                ExpandExplorerBtn.Visibility = _explorerExpanded ? Visibility.Collapsed : Visibility.Visible;
        }


        private void ExpandExplorerBtn_Click(object sender, RoutedEventArgs e)
        {
            _explorerExpanded = true;
            MainGrid.ColumnDefinitions[0].Width = new GridLength(200);
            ExplorerBorder.Visibility = Visibility.Visible;
            ExpandExplorerBtn.Visibility = Visibility.Collapsed;
            ExplorerToggleIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronLeft20;
        }


        private void AddTabBtn_Click(object sender, RoutedEventArgs e)
        {
            string tabName = $"Tab {_tabOrder.Count + 1}";
            int suffix = _tabOrder.Count + 1;
            while (_tabFlags.ContainsKey(tabName)) { suffix++; tabName = $"Tab {suffix}"; }
            _flagService.AddTab(tabName);
            var newFlags = _flagService.Tabs[tabName];
            _tabFlags[tabName] = newFlags;
            _tabOrder.Add(tabName);
            RefreshTabBar();
            RefreshSavedExplorer();
            SwitchToTab(tabName);
            PersistTabs();
        }


        private void RestorePersistedTabs()
        {


            _tabOrder.Clear();
            _tabOrder.Add("Default");
            var persisted = _flagService.LoadTabs();
            foreach (var name in persisted)
            {
                if (name == "Default") continue;
                if (_tabFlags.TryGetValue(name, out _)) continue;
                _tabFlags[name] = _flagService.Tabs[name];
                _tabOrder.Add(name);
            }
            RefreshTabBar();
            RefreshSavedExplorer();
        }


        private void PersistTabs() => _flagService.SaveTabs(_tabOrder);


        private void RefreshTabBar()
        {
            TabBarPanel.Children.Clear();
            foreach (var tabName in _tabOrder)
            {
                bool isActive = tabName == _activeTabName;
                var tab = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                    BorderThickness = isActive ? new Thickness(0, 0, 0, 2) : new Thickness(0),
                    Padding = new Thickness(10, 5, 6, 5),
                    Margin = new Thickness(2, 0, 2, 0),
                    Cursor = Cursors.Hand
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = Wpf.Ui.Controls.SymbolRegular.Document20,
                    FontSize = 10,
                    Foreground = isActive ? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)) : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                var text = new TextBlock
                {
                    Text = tabName, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isActive ? Brushes.White
                        : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
                };
                sp.Children.Add(text);
                if (tabName != "Default")
                {
                    var closeBtn = new Button
                    {
                        Content = "✕",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(4, 0, 2, 0),
                        Margin = new Thickness(5, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = Cursors.Hand,
                        Focusable = false,
                        Tag = tabName
                    };
                    closeBtn.Click += (s, ev) => RemoveTabByName(tabName);
                    sp.Children.Add(closeBtn);
                }
                tab.Child = sp;
                tab.MouseLeftButtonDown += (s, ev) => SwitchToTab(tabName);
                TabBarPanel.Children.Add(tab);
            }
        }


        private void RemoveTabByName(string tabName)
        {
            if (tabName == "Default") return;
            var result = MessageBox.Show($"Remove tab \"{tabName}\"?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _flagService.RemoveTab(tabName);
            _tabFlags.Remove(tabName);
            _tabOrder.Remove(tabName);
            if (_activeTabName == tabName) SwitchToTab("Default");
            else { RefreshTabBar(); }
            RefreshSavedExplorer();
            PersistTabs();
        }


        private void SwitchToTab(string tabName)
        {
            _activeTabName = tabName;
            _activeFlags = _tabFlags[tabName];


            if (tabName == "Default")
            {


                RefreshDefaultFromService();
            }
            else
            {


                FlagDataGrid.ItemsSource = _activeFlags;
            }


            RefreshTabBar();
            UpdateEmptyState();
            FFlagsLoadedText.Text = _activeFlags.Count.ToString();
        }


        private void SyncTabToCurrent(string tabName)
        {
            _suppressSync = true;
            try
            {
                _flagService.CurrentFlags.Clear();
                foreach (var f in _tabFlags[tabName])
                    _flagService.CurrentFlags.Add(f);
            }
            finally
            {
                _suppressSync = false;
            }
        }


        private void AddFlagBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddFlagDialog();
            if (dialog.ShowDialog() == true)
            {
                var entry = new FFlagEntry { Name = dialog.FlagName, Value = dialog.FlagValue, Type = dialog.FlagType };
                _activeFlags.Add(entry);
                SyncCurrentFlags();
                UpdateEmptyState();
            }
        }


        private void DeleteFlagBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FFlagEntry flag) { _activeFlags.Remove(flag); SyncCurrentFlags(); UpdateEmptyState(); }
        }


        private void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = FlagDataGrid.SelectedItems.Cast<FFlagEntry>().ToList();
            foreach (var item in selected) _activeFlags.Remove(item);
            SyncCurrentFlags();
            UpdateEmptyState();
        }


        private void DeleteAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFlags.Count == 0) return;
            var result = MessageBox.Show("Delete all flags in this tab?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _suppressSync = true;
                try
                {
                    FlagDataGrid.ItemsSource = null;
                    _activeFlags.Clear();
                    if (_activeTabName == "Default")
                    {
                        _flagService.CurrentFlags.Clear();
                        try
                        {
                            string fflagsDir = System.IO.Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Leitostrap Injector", "FFlags");
                            string fflagsPath = System.IO.Path.Combine(fflagsDir, "current_fflags.json");
                            if (System.IO.File.Exists(fflagsPath))
                                System.IO.File.Delete(fflagsPath);
                        }
                        catch { }
                    }
                }
                finally
                {
                    _suppressSync = false;
                }
                FlagDataGrid.ItemsSource = _activeFlags;
                UpdateEmptyState();
                DefaultFlagCount.Text = $"   0 flags";
                FFlagsLoadedText.Text = "0";
            }
        }


        private void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", Title = "Import FFlags" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    int before = _activeFlags.Count;
                    ImportJsonContent(json);
                    int imported = _activeFlags.Count - before;
                    SyncCurrentFlags();
                    DiscordService.Instance.UpdateFlagCount(_activeFlags.Count);
                    NotificationService.Instance.Show($"Imported {imported} flags", NotificationType.Success);
                    _console.Log($"Imported {imported} flags from {Path.GetFileName(dialog.FileName)}", "Success");
                }
                catch (Exception ex) { _console.Log($"Import failed: {ex.Message}", "Error"); NotificationService.Instance.Show($"Import failed: {ex.Message}", NotificationType.Error); }
                UpdateEmptyState();
            }
        }


        private void ImportJsonContent(string json)
        {
            json = json.Trim();
            using var doc = System.Text.Json.JsonDocument.Parse(json);


            var batch = new List<FFlagEntry>();


            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("name", out var nameProp))
                    {
                        string name = nameProp.GetString() ?? "";
                        string value = elem.TryGetProperty("value", out var valProp) ? (valProp.GetString() ?? "") : "";
                        string type = elem.TryGetProperty("type", out var typeProp) ? (typeProp.GetString() ?? "FFlag") : "FFlag";
                        if (!string.IsNullOrEmpty(name))
                            batch.Add(new FFlagEntry { Name = name, Value = value, Type = type });
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string name = prop.Name;
                    string value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
                    if (!string.IsNullOrEmpty(name))
                        batch.Add(new FFlagEntry { Name = name, Value = value, Type = "FFlag" });
                }
            }


            if (batch.Count == 0) return;


            FlagDataGrid.ItemsSource = null;
            _activeFlags.Clear();
            foreach (var entry in batch)
                _activeFlags.Add(entry);
            FlagDataGrid.ItemsSource = _activeFlags;
        }


        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFlags.Count == 0) { _console.Log("No flags to export", "Warning"); return; }
            var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = "fflags.json" };
            if (dialog.ShowDialog() == true)
            {
                try
                {


                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(new List<FFlagEntry>(_activeFlags), options);
                    File.WriteAllText(dialog.FileName, json);
                    _console.Log($"Exported {_activeFlags.Count} flags", "Success");
                    NotificationService.Instance.Show($"Exported {_activeFlags.Count} flags", NotificationType.Success);
                }
                catch (Exception ex) { _console.Log($"Export failed: {ex.Message}", "Error"); }
            }
        }


        private void CopyFlags_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFlags.Count == 0) { _console.Log("No flags to copy", "Warning"); return; }
            try
            {
                var dict = new Dictionary<string, string>();
                foreach (var f in _activeFlags)
                    if (!string.IsNullOrWhiteSpace(f.Value)) dict[f.Name] = f.Value;
                string json = System.Text.Json.JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                Clipboard.SetText(json);
                NotificationService.Instance.Show("Flags copied to clipboard", NotificationType.Success);
                _console.Log($"Copied {_activeFlags.Count} flags to clipboard", "Info");
            }
            catch (Exception ex) { _console.Log($"Copy failed: {ex.Message}", "Error"); }
        }


        private void ClearFlags_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFlags.Count == 0) return;
            var result = MessageBox.Show("Clear all flags in this tab?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _suppressSync = true;
                try
                {
                    FlagDataGrid.ItemsSource = null;
                    _activeFlags.Clear();
                    if (_activeTabName == "Default")
                    {
                        _flagService.CurrentFlags.Clear();
                        try
                        {
                            string fflagsDir = System.IO.Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Leitostrap Injector", "FFlags");
                            string fflagsPath = System.IO.Path.Combine(fflagsDir, "current_fflags.json");
                            if (System.IO.File.Exists(fflagsPath))
                                System.IO.File.Delete(fflagsPath);
                        }
                        catch { }
                    }
                }
                finally
                {
                    _suppressSync = false;
                }
                FlagDataGrid.ItemsSource = _activeFlags;
                UpdateEmptyState();
                DefaultFlagCount.Text = $"   0 flags";
                FFlagsLoadedText.Text = "0";
                _console.Log("Flags cleared", "Info");
            }
        }


        private void OpenClients_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.NavigateTo(5);
        }


        private void OpenThemes_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.NavigateTo(7);
        }


        private void LaunchRoblox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessService.Instance.LaunchRoblox();
                _console.Log("Launching Roblox...", "Info");
                NotificationService.Instance.Show("Launching Roblox...", NotificationType.Info, 2000);
            }
            catch (Exception ex) { _console.Log($"Failed to launch Roblox: {ex.Message}", "Error"); }
        }


        private void JoinDiscord_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/leitostrap", UseShellExecute = true }); }
            catch { }
        }


        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            _console.Log($"Saved {_activeFlags.Count} flags in tab \"{_activeTabName}\"", "Success");
            NotificationService.Instance.Show("Flags saved", NotificationType.Success, 2000);
        }


        private void UserConsoleBtn_Click(object sender, RoutedEventArgs e)
        {
            string tab = (sender as FrameworkElement)?.Tag?.ToString() ?? "terminal";
            if (tab == "log")
            {


                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.NavigateTo(4);
                    MainWindow.Instance.OpenConsoleLog();
                }
                return;
            }


            SetBottomTab(tab);
        }


        private void BottomTab_Click(object sender, RoutedEventArgs e)
        {
            string tab = (sender as FrameworkElement)?.Tag?.ToString() ?? "terminal";
            SetBottomTab(tab);
        }


        private void CloseBottomPanel_Click(object sender, RoutedEventArgs e)
        {
            BottomPanel.Visibility = Visibility.Collapsed;
        }


        private void SetBottomTab(string tab)
        {
            BottomPanel.Visibility = Visibility.Visible;
            string title = tab switch
            {
                "problems" => "Problems",
                "roblox" => "Roblox Output",
                _ => "Terminal"
            };
            MiniLogBox.Text = BuildMiniLog(tab);


            ProblemsTabBtn.Foreground = Brushes.White;
            RobloxOutputTabBtn.Foreground = Brushes.White;
            TerminalTabBtn.Foreground = Brushes.White;
            switch (tab)
            {
                case "problems": ProblemsTabBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)); break;
                case "roblox": RobloxOutputTabBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)); break;
                default: TerminalTabBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)); break;
            }
        }


        private string BuildMiniLog(string tab)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var entry in _console.Entries)
            {
                bool keep = tab switch
                {
                    "problems" => entry.Level?.ToUpperInvariant() is "WARNING" or "ERROR",
                    "roblox" => entry.Message.IndexOf("roblox", StringComparison.OrdinalIgnoreCase) >= 0
                                || entry.Message.IndexOf("inject", StringComparison.OrdinalIgnoreCase) >= 0
                                || entry.Message.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0,
                    _ => true
                };
                if (!keep) continue;
                sb.Append($"[{entry.Time:HH:mm:ss}] [{entry.Level}] {entry.Message}");
                sb.AppendLine();
            }
            if (sb.Length == 0)
                sb.Append("(no entries)");
            return sb.ToString();
        }


        private void SaveAsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFlags.Count == 0)
            {
                _console.Log("No flags to save", "Warning");
                NotificationService.Instance.Show("No flags to save", NotificationType.Warning);
                return;
            }


            string suggested = _activeTabName == "Default" ? "My FFlags" : _activeTabName;
            var dialog = new SaveNameDialog(suggested);
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Name))
                return;


            string name = dialog.Name.Trim();
            if (name == "Default") name = "My FFlags";


            if (!_tabFlags.ContainsKey(name))
            {


                _flagService.AddTab(name);
                _tabFlags[name] = _flagService.Tabs[name];
                _tabOrder.Add(name);
            }


            var target = _tabFlags[name];
            target.Clear();
            foreach (var f in _activeFlags)
                target.Add(new FFlagEntry { Name = f.Name, Value = f.Value, Type = f.Type });


            RefreshTabBar();
            RefreshSavedExplorer();
            PersistTabs();
            SwitchToTab(name);


            _console.Log($"Saved {target.Count} flags as \"{name}\"", "Success");
            NotificationService.Instance.Show($"Saved {target.Count} flags as \"{name}\"", NotificationType.Success, 2000);
        }


        private void SavedSection_Click(object sender, MouseButtonEventArgs e)
        {
            bool visible = SavedFileTree.Visibility == Visibility.Visible;
            SavedFileTree.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            SavedToggleIcon.Symbol = visible
                ? Wpf.Ui.Controls.SymbolRegular.ChevronRight20
                : Wpf.Ui.Controls.SymbolRegular.ChevronDown20;
        }


        private void RefreshSavedExplorer()
        {
            if (SavedFileTree == null) return;
            SavedFileTree.Children.Clear();
            var saved = new List<string>();
            foreach (var name in _tabOrder)
                if (name != "Default") saved.Add(name);


            if (saved.Count == 0)
            {
                SavedFileTree.Children.Add(new TextBlock
                {
                    Text = "   Nothing saved yet", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }


            foreach (var name in saved)
            {
                var count = _tabFlags.TryGetValue(name, out var flags) ? flags.Count : 0;
                var item = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = name, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"   {count} flags", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                item.Child = sp;
                string openName = name;
                item.MouseLeftButtonDown += (s, ev) =>
                {
                    if (_tabFlags.TryGetValue(openName, out var savedFlags))
                    {
                        foreach (var flag in savedFlags)
                            _activeFlags.Add(new FFlagEntry { Name = flag.Name, Value = flag.Value, Type = flag.Type });
                        SyncCurrentFlags();
                        UpdateEmptyState();
                        NotificationService.Instance.Show($"Added {savedFlags.Count} flags from \"{openName}\"", NotificationType.Success);
                        _console.Log($"Added {savedFlags.Count} flags from saved set \"{openName}\" to \"{_activeTabName}\"", "Success");
                    }
                };
                SavedFileTree.Children.Add(item);
            }
        }


        private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFlags.Count == 0) { _console.Log("No flags to apply", "Warning"); NotificationService.Instance.Show("No flags to apply", NotificationType.Warning); return; }


            var flatDict = new Dictionary<string, string>();
            foreach (var f in _activeFlags)
                if (!string.IsNullOrWhiteSpace(f.Value))
                    flatDict[f.Name] = f.Value;


            if (flatDict.Count == 0) { _console.Log("No flags with values", "Warning"); NotificationService.Instance.Show("No flags with values", NotificationType.Warning); return; }


            string methodStr = _settings.Get<string>("InjectionMethod", "memory");
            var method = methodStr switch
            {
                "cache" => Combined.InjectionMethod.CacheMethod,
                "memory" => Combined.InjectionMethod.MemoryOffsets,
                "offsetless" => Combined.InjectionMethod.MemoryOffsetless,
                _ => Combined.InjectionMethod.Combined
            };


            bool needsAdmin = method != Combined.InjectionMethod.CacheMethod;
            if (needsAdmin && !CacheMethod.IsAdmin())
            {
                _console.Log("Admin required — relaunching as admin...", "Warning");
                NotificationService.Instance.Show("Relaunching as Administrator...", NotificationType.Warning, 3000);
                await Task.Delay(1000);
                CacheMethod.ElevateAndRelaunch();
                return;
            }


            string methodLabel = method switch
            {
                Combined.InjectionMethod.CacheMethod => "Proxy Method",
                Combined.InjectionMethod.MemoryOffsets => "Memory Offsets",
                Combined.InjectionMethod.MemoryOffsetless => "Memory Offsetless",
                _ => "Combined"
            };


            ShowApplyOverlay(methodLabel);
            SetApplyPhase("waiting");
            SetApplyState("Waiting for Roblox...", "Searching for RobloxPlayerBeta...", 0);


            Combined.Instance.OnStatusUpdate = (statusMsg, phase) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetApplyPhase(phase);
                    SetApplyState(phase switch
                    {
                        "waiting" => "Waiting for Roblox...",
                        "detecting" => "Detecting right moment...",
                        "injecting" => "Injecting flags...",
                        "done" => "Done!",
                        "error" => "Failed",
                        _ => "Preparing..."
                    }, statusMsg, phase == "done" || phase == "error" ? 1.0 : 0.3);
                }));
            };


            _console.Log($"Applying {flatDict.Count} flags via {methodLabel}...", "Info");


            var result = await Combined.Instance.InjectAsync(flatDict, method, (done, total) =>
            {
                if (total > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        double fraction = (double)done / total;
                        SetApplyState($"Injecting flags... ({done}/{total})", $"{done} of {total} flags written", 0.3 + (fraction * 0.7));
                    }));
                }
            });


            if (result.ok)
            {
                SetApplyPhase("done");
                SetApplyState("Flags Injected!", result.msg, 1.0);


                ApplyResultBorder.Visibility = Visibility.Visible;
                ApplyResultBorder.Background = new SolidColorBrush(Color.FromArgb(20, 0xFF, 0xFF, 0xFF));
                ApplyResultBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xFF, 0xFF));
                ApplyResultBorder.BorderThickness = new Thickness(1);
                ApplyResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
                ApplyResultIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                ApplyResultText.Text = $"{flatDict.Count} FastFlags applied successfully";


                InjectStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                InjectStatusText.Text = "Injected";
                InjectStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));


                _console.Log($"Applied {flatDict.Count} FastFlags ({result.msg})", "Success");
            }
            else
            {
                SetApplyPhase("error");
                SetApplyState("Injection Failed", result.msg, 1.0);


                ApplyResultBorder.Visibility = Visibility.Visible;
                ApplyResultBorder.Background = new SolidColorBrush(Color.FromArgb(20, 0x44, 0x44, 0x44));
                ApplyResultBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0x44, 0x44, 0x44));
                ApplyResultBorder.BorderThickness = new Thickness(1);
                ApplyResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle20;
                ApplyResultIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                ApplyResultText.Text = result.msg;


                InjectStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                InjectStatusText.Text = "Not Injected";
                InjectStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));


                _console.Log($"Apply failed: {result.msg}", "Error");
            }


            await Task.Delay(2200);
            HideApplyOverlay();
            Combined.Instance.OnStatusUpdate = null;
        }


        private void ShowApplyOverlay(string method)
        {
            if (_applyOverlayVisible) return;
            _applyOverlayVisible = true;


            ApplyResultBorder.Visibility = Visibility.Collapsed;
            ApplyStatusRing.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            ApplyStatusDot.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            ApplyStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));
            ApplyStatusRing.Fill = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));


            SetApplyState("Preparing...", "", 0);
            ApplyMethodText.Text = method;
            ApplyProgressFill.Width = 0;


            ApplyOverlay.Visibility = Visibility.Visible;
            ApplyCardScale.ScaleX = 0.92;
            ApplyCardScale.ScaleY = 0.92;
            ApplyCardSlide.Y = 20;


            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ApplyCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
            ApplyCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
            ApplyCardSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });


            StartApplyPulse();
        }


        private void HideApplyOverlay()
        {
            _applyOverlayVisible = false;
            StopApplyPulse();
            ApplyOverlay.Visibility = Visibility.Collapsed;
            ApplyOverlay.BeginAnimation(OpacityProperty, null);
            ApplyOverlay.Opacity = 1;
        }


        private DispatcherTimer? _applyPulseTimer;


        private void StartApplyPulse()
        {
            StopApplyPulse();
            bool toggled = false;
            _applyPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _applyPulseTimer.Tick += (_, _) =>
            {
                toggled = !toggled;
                var ring = toggled ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                ApplyStatusRing.Stroke.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(ring.Color, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            };
            _applyPulseTimer.Start();
        }


        private void StopApplyPulse()
        {
            _applyPulseTimer?.Stop();
            _applyPulseTimer = null;
        }


        private void SetApplyState(string title, string status, double progressFraction)
        {
            ApplyTitleText.Text = title;
            ApplyStatusText.Text = status;
            if (progressFraction >= 0 && progressFraction <= 1)
            {
                const double trackWidth = 340;
                double target = Math.Round(progressFraction * trackWidth);
                ApplyProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }


        private void SetApplyPhase(string phase)
        {
            var whiteBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            var darkBrush = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));


            switch (phase)
            {
                case "waiting":
                    ApplyStatusRing.Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                    ApplyStatusDot.Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                    ApplyStatusDot.Fill = darkBrush;
                    ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Clock20;
                    ApplyStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                    break;
                case "detecting":
                    ApplyStatusRing.Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    ApplyStatusDot.Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    ApplyStatusDot.Fill = darkBrush;
                    ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Search20;
                    ApplyStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    break;
                case "injecting":
                    ApplyStatusRing.Stroke = whiteBrush;
                    ApplyStatusDot.Stroke = whiteBrush;
                    ApplyStatusDot.Fill = darkBrush;
                    ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowSync20;
                    ApplyStatusIcon.Foreground = whiteBrush;
                    break;
                case "done":
                    StopApplyPulse();
                    ApplyStatusRing.Stroke = whiteBrush;
                    ApplyStatusDot.Stroke = whiteBrush;
                    ApplyStatusDot.Fill = darkBrush;
                    ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
                    ApplyStatusIcon.Foreground = whiteBrush;
                    break;
                case "error":
                    StopApplyPulse();
                    var grayBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                    ApplyStatusRing.Stroke = grayBrush;
                    ApplyStatusDot.Stroke = grayBrush;
                    ApplyStatusDot.Fill = darkBrush;
                    ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle20;
                    ApplyStatusIcon.Foreground = grayBrush;
                    break;
            }
        }


        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string json = File.ReadAllText(file);
                            int before = _activeFlags.Count;
                            ImportJsonContent(json);
                            int imported = _activeFlags.Count - before;
                            SyncCurrentFlags();
                            NotificationService.Instance.Show($"Imported {imported} flags from {Path.GetFileName(file)}", NotificationType.Success);
                            _console.Log($"Imported {imported} flags from {Path.GetFileName(file)}", "Success");
                        }
                        catch (Exception ex) { _console.Log($"Failed to import {Path.GetFileName(file)}: {ex.Message}", "Error"); }
                    }
                }
                UpdateEmptyState();
            }
        }
    }


    public class AddFlagDialog : Window
    {
        public string FlagName { get; private set; } = "";
        public string FlagValue { get; private set; } = "";
        public string FlagType { get; private set; } = "String";
        private TextBox NameBox, ValueBox;
        private ComboBox TypeCombo;


        public AddFlagDialog()
        {
            Title = "Add FFlag"; Width = 350; Height = 220;
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.ToolWindow;


            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });


            NameBox = CreateInput("Flag name...");
            ValueBox = CreateInput("Value...");
            TypeCombo = new ComboBox { ItemsSource = new[] { "String", "Int", "Bool", "Flag" }, SelectedIndex = 0,
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                Height = 30, FontSize = 11 };


            var addBtn = new Button { Content = "Add",
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Height = 30, Cursor = Cursors.Hand, FontSize = 11 };
            addBtn.Click += (s, e) =>
            {
                FlagName = NameBox.Text; FlagValue = ValueBox.Text;
                FlagType = TypeCombo.SelectedItem?.ToString() ?? "String";
                DialogResult = true;
            };


            Grid.SetRow(NameBox, 0); Grid.SetRow(ValueBox, 2); Grid.SetRow(TypeCombo, 4); Grid.SetRow(addBtn, 6);
            grid.Children.Add(NameBox); grid.Children.Add(ValueBox); grid.Children.Add(TypeCombo); grid.Children.Add(addBtn);
            Content = grid;
        }


        private TextBox CreateInput(string placeholder)
        {
            return new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                BorderThickness = new Thickness(1), Height = 30, FontSize = 11,
                Padding = new Thickness(8, 0, 8, 0), CaretBrush = Brushes.White
            };
        }
    }


    public class SaveNameDialog : Window
    {
        public string Name { get; private set; } = "";


        public SaveNameDialog(string suggested)
        {
            Title = "Save FFlags"; Width = 320; Height = 150;
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.ToolWindow;


            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });


            var textBox = new TextBox
            {
                Text = suggested,
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                BorderThickness = new Thickness(1), Height = 30, FontSize = 12,
                Padding = new Thickness(8, 0, 8, 0), CaretBrush = Brushes.White,
                VerticalContentAlignment = VerticalAlignment.Center
            };


            var saveBtn = new Button
            {
                Content = "Save",
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Height = 30, Cursor = Cursors.Hand, FontSize = 12
            };
            saveBtn.Click += (s, e) =>
            {
                Name = textBox.Text;
                DialogResult = true;
            };
            textBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Name = textBox.Text; DialogResult = true; } };


            Grid.SetRow(textBox, 0); Grid.SetRow(saveBtn, 2);
            grid.Children.Add(textBox); grid.Children.Add(saveBtn);
            Content = grid;
        }
    }
}
