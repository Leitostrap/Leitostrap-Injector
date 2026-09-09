using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LeitostrapV7.Core;


namespace LeitostrapV7.Views
{
    public partial class OffsetsView : UserControl
    {
        private readonly ConsoleService _console = ConsoleService.Instance;
        private List<OffsetItem> _allOffsets = new();
        private bool _isCache = true;


        public OffsetsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }


        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!OffsetService.Instance.IsFetched)
            {
                InfoText.Text = "Loading offsets...";
                await OffsetService.Instance.FetchOffsetsAsync();
            }


            if (SourceCombo.SelectedIndex == 0)
            {
                InfoText.Text = "Loading cache offsets...";
                await OffsetService.Instance.EnsureCacheOffsetsReadyAsync();
            }
            LoadOffsets(SourceCombo.SelectedIndex == 0);
        }


        private async void ReloadBtn_Click(object sender, RoutedEventArgs e)
        {
            ReloadBtn.IsEnabled = false;
            try
            {
                InfoText.Text = "Reloading offsets...";
                if (SourceCombo.SelectedIndex == 0)
                    await OffsetService.Instance.ReloadCacheOffsetsAsync();
                else
                    await OffsetService.Instance.FetchOffsetsAsync();
                LoadOffsets(SourceCombo.SelectedIndex == 0);
                _console.Log("Offsets reloaded", "Success");
            }
            catch { }
            ReloadBtn.IsEnabled = true;
        }


        private void LoadOffsets(bool isCache)
        {
            _isCache = isCache;
            var dict = isCache
                ? OffsetService.Instance.GetAllCacheOffsets()
                : OffsetService.Instance.GetAllMemoryOffsets();
            _allOffsets = dict.Select(kv => new OffsetItem { Name = kv.Key, Value = kv.Value, Type = isCache ? "Cache" : "Memory" }).ToList();
            RenderOffsets(_allOffsets);
            InfoText.Text = $"{_allOffsets.Count:N0} offsets loaded";
            VersionText.Text = $"Source: {(isCache ? "Cache" : "Memory")}";
        }


        private void RenderOffsets(List<OffsetItem> offsets)
        {
            OffsetList.ItemsSource = null;
            OffsetList.ItemsSource = offsets.Take(5000);
            InfoText.Text = $"{_allOffsets.Count:N0} offsets loaded" + (offsets.Count < _allOffsets.Count ? $" (showing first 5,000 of {offsets.Count:N0})" : "");
        }


        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            LoadOffsets(SourceCombo.SelectedIndex == 0);
        }


        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            string query = SearchBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(query)) RenderOffsets(_allOffsets);
            else
            {
                var filtered = _allOffsets.Where(o => o.Name.ToLower().Contains(query) || o.Value.ToLower().Contains(query)).ToList();
                RenderOffsets(filtered);
            }
        }
    }
}
