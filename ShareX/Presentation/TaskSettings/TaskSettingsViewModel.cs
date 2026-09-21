#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using ShareX.AvaloniaUI.Controls;
using ShareX.AvaloniaUI.Theming;
using ShareX.Localization;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ShareX;

public sealed class TaskSettingsViewModel : INotifyPropertyChanged
{
    private readonly bool _isDefault;
    private SettingsNavigationItem? _selectedNavigationItem;

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; } = [];

    public SettingsNavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (ReferenceEquals(_selectedNavigationItem, value))
            {
                return;
            }

            _selectedNavigationItem = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNavigationItem)));
            SelectedPageChanged?.Invoke(value?.Id);
        }
    }

    public event Action<string?>? SelectedPageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public TaskSettingsViewModel(bool isDefault)
    {
        _isDefault = isDefault;

        if (!isDefault)
        {
            NavigationItems.Add(new SettingsNavigationItem("task", Strings.TaskSettingsWindow_Task, LucideIcons.keyboard));
        }

        NavigationItems.Add(new SettingsNavigationItem("general", Strings.TaskSettingsWindow_General, LucideIcons.settings));
        NavigationItems.Add(new SettingsNavigationItem("capture", Strings.TaskSettingsWindow_Capture, LucideIcons.camera));
        NavigationItems.Add(new SettingsNavigationItem("capture-region", Strings.TaskSettingsWindow_RegionCapture, LucideIcons.crop));
        NavigationItems.Add(new SettingsNavigationItem("capture-screen-recorder", "GIF Recorder", LucideIcons.film));
        NavigationItems.Add(new SettingsNavigationItem("capture-ocr", Strings.TaskSettingsWindow_OCR, LucideIcons.scan_text));
        NavigationItems.Add(new SettingsNavigationItem("actions", Strings.TaskSettingsWindow_Actions, LucideIcons.zap));

        SelectedNavigationItem = NavigationItems[0];
    }

    public void SelectPage(string id)
    {
        SelectedNavigationItem = NavigationItems.FirstOrDefault(item => item.Id == id) ?? NavigationItems.FirstOrDefault();
    }
}
