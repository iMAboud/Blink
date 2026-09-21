#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Theming;
using System;

namespace ShareX;

public partial class SettingsWindow : Window
{
    private SettingsTab _currentTab = SettingsTab.Application;

    public SettingsWindow()
    {
        InitializeComponent();
        this.ApplyDarkTitleBar();
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
        Opened += (_, _) => Activate();
        ApplicationSettingsView.SettingsImported += OnSettingsImported;
    }

    private void OnThemeChanged(object? sender, Avalonia.Styling.ThemeVariant theme) =>
        Dispatcher.UIThread.Post(() => RequestedThemeVariant = theme);

    private void OnClosed(object? sender, EventArgs e)
    {
        ApplicationSettingsView.SettingsImported -= OnSettingsImported;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        ApplicationSettingsView.Dispose();
        HotkeySettingsView.Dispose();
    }

    private void OnSettingsImported(object? sender, EventArgs e)
    {
        HotkeySettingsView.Reload();
        TaskSettingsView.Reload();
    }

    public void SelectTab(SettingsTab tab)
    {
        _currentTab = tab;

        ApplicationSettingsView.IsVisible = tab == SettingsTab.Application;
        HotkeySettingsView.IsVisible = tab == SettingsTab.Hotkeys;
        TaskSettingsView.IsVisible = tab == SettingsTab.Tasks;

        UpdateTabClasses(TabApplication, tab == SettingsTab.Application);
        UpdateTabClasses(TabHotkeys, tab == SettingsTab.Hotkeys);
        UpdateTabClasses(TabTasks, tab == SettingsTab.Tasks);
    }

    private static void UpdateTabClasses(Button button, bool isActive)
    {
        if (isActive)
        {
            if (!button.Classes.Contains("active"))
            {
                button.Classes.Add("active");
            }
        }
        else
        {
            button.Classes.Remove("active");
        }
    }

    private void OnApplicationTabClick(object? sender, RoutedEventArgs e) => SelectTab(SettingsTab.Application);
    private void OnHotkeysTabClick(object? sender, RoutedEventArgs e) => SelectTab(SettingsTab.Hotkeys);
    private void OnTasksTabClick(object? sender, RoutedEventArgs e) => SelectTab(SettingsTab.Tasks);
}
