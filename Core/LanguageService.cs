using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;


namespace LeitostrapV7.Core;


public sealed class LanguageService
{
    public static LanguageService Instance { get; } = new();


    public string Current { get; private set; } = "en";


    private Dictionary<string, string> _map = new(StringComparer.Ordinal);


    private LanguageService() { }


    public void Load(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) code = "en";
        if (code.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)) code = "zh-tw";
        Current = code;


        var raw = ReadResource($"Resources.Lang.{code}.json");
        if (string.IsNullOrWhiteSpace(raw) && code != "en")
        {
            raw = ReadResource("Resources.Lang.en.json");
            Current = "en";
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            _map = new Dictionary<string, string>(StringComparer.Ordinal);
            return;
        }


        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
            _map = dict ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            _map = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }


    private static string? ReadResource(string name)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var rn in asm.GetManifestResourceNames())
            {
                if (rn.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    rn.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                {
                    using var s = asm.GetManifestResourceStream(rn);
                    if (s == null) return null;
                    using var r = new StreamReader(s);
                    return r.ReadToEnd();
                }
            }
        }
        catch { }
        return null;
    }


    public string L(string text) => _map.TryGetValue(text, out var t) && !string.IsNullOrEmpty(t) ? t : text;
    public string T(string text) => L(text);


    public void Apply(DependencyObject root)
    {
        if (root == null || _map.Count == 0) return;
        var visited = new HashSet<int>();
        WalkVisual(root, visited);
    }


    private void WalkVisual(DependencyObject current, HashSet<int> visited)
    {
        int count = VisualTreeHelper.GetChildrenCount(current);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            int hash = child.GetHashCode();
            if (visited.Add(hash))
            {
                ApplyNode(child);
                WalkVisual(child, visited);
            }
        }
    }


    private string Tr(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return L(text);
    }


    private void ApplyNode(DependencyObject node)
    {
        if (node is TextBlock tb)
        {
            if (tb.Text != null && IsPlain(tb.Text)) tb.Text = Tr(tb.Text);
        }
        else if (node is Label lb)
        {
            if (lb.Content is string s && IsPlain(s)) lb.Content = Tr(s);
        }
        else if (node is Button btn)
        {
            if (btn.Content is string s) btn.Content = Tr(s);
            else if (btn.Content is TextBlock inner && inner.Text != null && IsPlain(inner.Text))
                inner.Text = Tr(inner.Text);
        }
        else if (node is RadioButton rb)
        {
            if (rb.Content is string s) rb.Content = Tr(s);
            else if (rb.Content is TextBlock inner && inner.Text != null && IsPlain(inner.Text))
                inner.Text = Tr(inner.Text);
        }
        else if (node is CheckBox cb)
        {
            if (cb.Content is string s) cb.Content = Tr(s);
            else if (cb.Content is TextBlock inner && inner.Text != null && IsPlain(inner.Text))
                inner.Text = Tr(inner.Text);
        }
        else if (node is ComboBox cmb)
        {
            if (cmb.ToolTip is string ts) cmb.ToolTip = Tr(ts);
            foreach (var item in cmb.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    if (cbi.Content is string s) cbi.Content = Tr(s);
                    else if (cbi.Content is TextBlock inner && inner.Text != null && IsPlain(inner.Text))
                        inner.Text = Tr(inner.Text);
                }
            }
        }
        else if (node is ContentControl cc)
        {
            if (cc.Content is string s)
            {
                cc.Content = Tr(s);
            }
            else if (cc.Content is TextBlock inner && inner.Text != null && IsPlain(inner.Text))
            {
                inner.Text = Tr(inner.Text);
            }
        }
        else if (node is HeaderedContentControl hcc)
        {
            if (hcc.Header is string hs) hcc.Header = Tr(hs);
        }
        else if (node is HeaderedItemsControl hic)
        {
            if (hic.Header is string hs) hic.Header = Tr(hs);
        }
        else if (node is GroupBox gb)
        {
            if (gb.Header is string hs) gb.Header = Tr(hs);
        }
        else if (node is TabItem ti)
        {
            if (ti.Header is string hs) ti.Header = Tr(hs);
            else if (ti.Header is TextBlock inner && inner.Text != null && IsPlain(inner.Text))
                inner.Text = Tr(inner.Text);
        }
        else if (node is ItemsControl ic && ic.Items.Count > 0)
        {
            for (int idx = 0; idx < ic.Items.Count; idx++)
            {
                var item = ic.Items[idx];
                if (item is ContentControl icc && icc.Content is string s)
                    icc.Content = Tr(s);
                else if (item is string s2)
                    ic.Items[idx] = Tr(s2);
            }
        }


        if (node is FrameworkElement fe && fe.ToolTip is string tss)
        {
            fe.ToolTip = Tr(tss);
        }
    }


    private static bool IsPlain(string s)
    {
        return !s.Contains('\r') && !s.Contains('\n');
    }
}
