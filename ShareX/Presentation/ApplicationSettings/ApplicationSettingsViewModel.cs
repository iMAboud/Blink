#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia.Platform;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Controls;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.Localization;
using ShareX.UploadersLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using AvaloniaColor = Avalonia.Media.Color;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;
using MessageBoxResult = ShareX.AvaloniaUI.DialogResult;

namespace ShareX;

public sealed class ApplicationSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private SettingsNavigationItem? _selectedNavigationItem;
    private ClipboardFormatItem? _selectedClipboardFormat;
    private string _personalFolderPath = string.Empty;
    private string _personalFolderPreview = string.Empty;
    private string _screenshotsFolderPreview = string.Empty;
    private bool _startWithWindows;
    private bool _startWithWindowsEnabled;
    private string _startWithWindowsText = string.Empty;
    private bool _shellContextMenu;
    private bool _editWithShareX;
    private bool _sendToMenu;
    private bool _chromeExtensionSupport;
    private bool _firefoxAddonSupport;
    private bool _steamShowInApp;
    private bool _exportSettings = true;
    private bool _personalPathDirty;
    private bool _isBusy;

    public event EventHandler? SettingsImported;
    private bool _restartRequired;
    private string _statusMessage = string.Empty;
    private bool _disposed;

    private ApplicationConfig Settings => Program.Settings;

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; private set; } = [];
    public ObservableCollection<ClipboardFormatItem> ClipboardFormats { get; private set; } = [];
    public ObservableCollection<TrayMenuItemModel> TrayMenuItems { get; private set; } = [];
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = CreateLanguageOptions();
    public IReadOnlyList<EnumOption<HotkeyType>> HotkeyTypeOptions { get; } = CreateHotkeyTypeOptions();
    public IReadOnlyList<EnumOption<UpdateChannel>> UpdateChannelOptions { get; } = CreateEnumOptions<UpdateChannel>();
    public IReadOnlyList<EnumOption<ThumbnailTitleLocation>> ThumbnailTitleLocationOptions { get; } = CreateEnumOptions<ThumbnailTitleLocation>();
    public IReadOnlyList<EnumOption<ThumbnailViewClickAction>> ThumbnailClickActionOptions { get; } =
        CreateEnumOptions<ThumbnailViewClickAction>().Where(x => x.Value != ThumbnailViewClickAction.EditImage).ToArray();
    public IReadOnlyList<EnumOption<ProxyMethod>> ProxyMethodOptions { get; } = CreateEnumOptions<ProxyMethod>();
    public IReadOnlyList<EnumOption<ContentAlignment>> DropAlignmentOptions { get; } =
    [
        new(ContentAlignment.TopLeft, Strings.ApplicationSettingsWindow_TopLeft),
        new(ContentAlignment.TopCenter, Strings.ApplicationSettingsWindow_TopCenter),
        new(ContentAlignment.TopRight, Strings.ApplicationSettingsWindow_TopRight),
        new(ContentAlignment.MiddleLeft, Strings.ApplicationSettingsWindow_MiddleLeft),
        new(ContentAlignment.MiddleCenter, Strings.ApplicationSettingsWindow_MiddleCenter),
        new(ContentAlignment.MiddleRight, Strings.ApplicationSettingsWindow_MiddleRight),
        new(ContentAlignment.BottomLeft, Strings.ApplicationSettingsWindow_BottomLeft),
        new(ContentAlignment.BottomCenter, Strings.ApplicationSettingsWindow_BottomCenter),
        new(ContentAlignment.BottomRight, Strings.ApplicationSettingsWindow_BottomRight)
    ];
    public IReadOnlyList<EnumOption<int>> BufferSizeOptions { get; private set; } = [];
    public IReadOnlyList<EnumOption<string>> ThemeOptions { get; } =
    [
        new("Dark", Strings.ApplicationSettingsWindow_Dark),
        new("Light", Strings.ApplicationSettingsWindow_Light)
    ];

    public SettingsNavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (!SetField(ref _selectedNavigationItem, value))
            {
                return;
            }

            OnPageChanged();

            if (value?.Id == "general")
            {
                RefreshStartWithWindows();
            }
        }
    }

    public bool IsGeneralPage => IsPage("general");
    public bool IsThemePage => IsPage("theme");
    public bool IsIntegrationPage => IsPage("integration");
    public bool IsPathsPage => IsPage("paths");
    public bool IsSettingsPage => IsPage("settings");
    public bool IsMainWindowPage => IsPage("main-window");
    public bool IsClipboardFormatsPage => IsPage("clipboard-formats");
    public bool IsUploadPage => IsPage("upload");
    public bool IsRecentTasksPage => IsPage("recent-tasks");
    public bool IsPrintPage => IsPage("print");
    public bool IsProxyPage => IsPage("proxy");
    public bool IsTrayPage => IsPage("tray");
    public bool IsAdvancedPage => IsPage("advanced");
    public bool IsUpdaterPage => IsPage("updater");

    public bool UpdatesVisible => true;

    public bool WindowsIntegrationVisible
    {
        get
        {
#if MicrosoftStore
            return false;
#else
            return true;
#endif
        }
    }

    public bool SteamIntegrationVisible
    {
        get
        {
#if STEAM
            return true;
#else
            return false;
#endif
        }
    }

    public LanguageOption? SelectedLanguage
    {
        get => Find(LanguageOptions, Settings.Language);
        set
        {
            if (value == null || Settings.Language == value.Value)
            {
                return;
            }

            Settings.Language = value.Value;

            if (LanguageHelper.ChangeLanguage(value.Value))
            {
                RestartRequired = true;
            }
        }
    }

    public bool ShowTray
    {
        get => Settings.ShowTray;
        set
        {
            if (SetSetting(Settings.ShowTray, value, x => Settings.ShowTray = x))
            {
                MainWindowIntegration.SetTrayVisible(value);
                OnPropertyChanged(nameof(SilentRunEnabled));
            }
        }
    }

    public bool SilentRunEnabled => ShowTray;
    public bool SilentRun { get => Settings.SilentRun; set => SetSetting(Settings.SilentRun, value, x => Settings.SilentRun = x); }
    public bool TrayIconProgressEnabled { get => Settings.TrayIconProgressEnabled; set => SetSetting(Settings.TrayIconProgressEnabled, value, x => Settings.TrayIconProgressEnabled = x); }

    public bool TaskbarProgressEnabled
    {
        get => Settings.TaskbarProgressEnabled;
        set
        {
            if (SetSetting(Settings.TaskbarProgressEnabled, value, x => Settings.TaskbarProgressEnabled = x))
            {
                TaskbarManager.Enabled = value;
            }
        }
    }

    public bool TaskbarProgressSupported => TaskbarManager.IsPlatformSupported;

    public bool UseWhiteShareXIcon
    {
        get => Settings.UseWhiteShareXIcon;
        set
        {
            if (SetSetting(Settings.UseWhiteShareXIcon, value, x => Settings.UseWhiteShareXIcon = x))
            {
                InvokeOnMainThread(Program.MainForm.UpdateTheme);
            }
        }
    }

    public bool RememberMainFormPosition { get => Settings.RememberMainFormPosition; set => SetSetting(Settings.RememberMainFormPosition, value, x => Settings.RememberMainFormPosition = x); }
    public bool RememberMainFormSize { get => Settings.RememberMainFormSize; set => SetSetting(Settings.RememberMainFormSize, value, x => Settings.RememberMainFormSize = x); }

    public bool UseSystemTheme
    {
        get => Settings.ThemeOptions.UseSystemTheme;
        set
        {
            if (SetSetting(Settings.ThemeOptions.UseSystemTheme, value, x => Settings.ThemeOptions.UseSystemTheme = x))
            {
                OnPropertyChanged(nameof(CanEditTheme));
            }
        }
    }

    public bool CanEditTheme => !UseSystemTheme;

    public EnumOption<string>? SelectedTheme
    {
        get => Find(ThemeOptions, NormalizeTheme(Settings.ThemeOptions.Theme));
        set
        {
            if (value != null)
            {
                SetSetting(Settings.ThemeOptions.Theme, value.Value, x => Settings.ThemeOptions.Theme = x);
            }
        }
    }

    public bool UseSystemAccentColor
    {
        get => Settings.ThemeOptions.UseSystemAccentColor;
        set
        {
            if (SetSetting(Settings.ThemeOptions.UseSystemAccentColor, value, x => Settings.ThemeOptions.UseSystemAccentColor = x))
            {
                OnPropertyChanged(nameof(CanEditAccentColor));
            }
        }
    }

    public bool CanEditAccentColor => !UseSystemAccentColor;

    public AvaloniaColor AccentColor
    {
        get => Settings.ThemeOptions.AccentColor;
        set => SetSetting(Settings.ThemeOptions.AccentColor, value, x => Settings.ThemeOptions.AccentColor = x);
    }

    public EnumOption<HotkeyType>? SelectedTrayLeftClickAction
    {
        get => Find(HotkeyTypeOptions, Settings.TrayLeftClickAction);
        set { if (value != null) SetSetting(Settings.TrayLeftClickAction, value.Value, x => Settings.TrayLeftClickAction = x); }
    }

    public EnumOption<HotkeyType>? SelectedTrayMiddleClickAction
    {
        get => Find(HotkeyTypeOptions, Settings.TrayMiddleClickAction);
        set { if (value != null) SetSetting(Settings.TrayMiddleClickAction, value.Value, x => Settings.TrayMiddleClickAction = x); }
    }

    public bool AutoCheckUpdate
    {
        get => Settings.AutoCheckUpdate;
        set
        {
            if (SetSetting(Settings.AutoCheckUpdate, value, x => Settings.AutoCheckUpdate = x))
            {
                OnPropertyChanged(nameof(UpdateChannelEnabled));
            }
        }
    }

    public bool UpdateChannelEnabled => AutoCheckUpdate;

    public EnumOption<UpdateChannel>? SelectedUpdateChannel
    {
        get => Find(UpdateChannelOptions, Settings.UpdateChannel);
        set { if (value != null) SetSetting(Settings.UpdateChannel, value.Value, x => Settings.UpdateChannel = x); }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!_startWithWindowsEnabled || _startWithWindows == value)
            {
                return;
            }

            try
            {
                InvokeOnMainThread(() => StartupManager.State = value ? StartupState.Enabled : StartupState.Disabled);
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
                StatusMessage = e.Message;
            }

            RefreshStartWithWindows();
        }
    }

    public bool StartWithWindowsEnabled { get => _startWithWindowsEnabled; private set => SetField(ref _startWithWindowsEnabled, value); }
    public string StartWithWindowsText { get => _startWithWindowsText; private set => SetField(ref _startWithWindowsText, value); }

    public bool ShellContextMenu
    {
        get => _shellContextMenu;
        set
        {
            if (SetField(ref _shellContextMenu, value))
            {
                InvokeOnMainThread(() => IntegrationHelpers.CreateShellContextMenuButton(value));
            }
        }
    }

    public bool EditWithShareX
    {
        get => _editWithShareX;
        set
        {
            if (SetField(ref _editWithShareX, value))
            {
                InvokeOnMainThread(() => IntegrationHelpers.CreateEditShellContextMenuButton(value));
            }
        }
    }

    public bool SendToMenu
    {
        get => _sendToMenu;
        set
        {
            if (SetField(ref _sendToMenu, value))
            {
                InvokeOnMainThread(() => IntegrationHelpers.CreateSendToMenuButton(value));
            }
        }
    }

    public bool ChromeExtensionSupport
    {
        get => _chromeExtensionSupport;
        set
        {
            if (SetField(ref _chromeExtensionSupport, value))
            {
                InvokeOnMainThread(() => IntegrationHelpers.CreateChromeExtensionSupport(value));
            }
        }
    }

    public bool FirefoxAddonSupport
    {
        get => _firefoxAddonSupport;
        set
        {
            if (SetField(ref _firefoxAddonSupport, value))
            {
                InvokeOnMainThread(() => IntegrationHelpers.CreateFirefoxAddonSupport(value));
            }
        }
    }

    public bool SteamShowInApp
    {
        get => _steamShowInApp;
        set
        {
            if (SetField(ref _steamShowInApp, value))
            {
                InvokeOnMainThread(() => IntegrationHelpers.SteamShowInApp(value));
            }
        }
    }

    public string PersonalFolderPath
    {
        get => _personalFolderPath;
        set
        {
            if (!SetField(ref _personalFolderPath, value))
            {
                return;
            }

            UpdatePersonalFolderPreview();
            _personalPathDirty = true;
        }
    }

    public string PersonalFolderPreview { get => _personalFolderPreview; private set => SetField(ref _personalFolderPreview, value); }

    public bool UseCustomScreenshotsPath
    {
        get => Settings.UseCustomScreenshotsPath;
        set
        {
            if (SetSetting(Settings.UseCustomScreenshotsPath, value, x => Settings.UseCustomScreenshotsPath = x))
            {
                UpdateScreenshotsFolderPreview();
            }
        }
    }

    public string CustomScreenshotsPath
    {
        get => Settings.CustomScreenshotsPath;
        set
        {
            string sanitized = FileHelpers.SanitizePath(value);
            if (SetSetting(Settings.CustomScreenshotsPath, sanitized, x => Settings.CustomScreenshotsPath = x))
            {
                UpdateScreenshotsFolderPreview();
            }
        }
    }

    public string SaveImageSubFolderPattern
    {
        get => Settings.SaveImageSubFolderPattern;
        set
        {
            string sanitized = FileHelpers.SanitizePath(value);
            if (SetSetting(Settings.SaveImageSubFolderPattern, sanitized, x => Settings.SaveImageSubFolderPattern = x))
            {
                UpdateScreenshotsFolderPreview();
            }
        }
    }

    public string SaveImageSubFolderPatternWindow
    {
        get => Settings.SaveImageSubFolderPatternWindow;
        set => SetSetting(Settings.SaveImageSubFolderPatternWindow, FileHelpers.SanitizePath(value), x => Settings.SaveImageSubFolderPatternWindow = x);
    }

    public string ScreenshotsFolderPreview { get => _screenshotsFolderPreview; private set => SetField(ref _screenshotsFolderPreview, value); }

    public bool ExportSettings { get => _exportSettings; set { if (SetField(ref _exportSettings, value)) OnPropertyChanged(nameof(CanExport)); } }
    public bool CanExport => !IsBusy && ExportSettings;

    public bool AutoCleanupBackupFiles { get => Settings.AutoCleanupBackupFiles; set => SetSetting(Settings.AutoCleanupBackupFiles, value, x => Settings.AutoCleanupBackupFiles = x); }
    public bool AutoCleanupLogFiles { get => Settings.AutoCleanupLogFiles; set => SetSetting(Settings.AutoCleanupLogFiles, value, x => Settings.AutoCleanupLogFiles = x); }
    public decimal CleanupKeepFileCount { get => Settings.CleanupKeepFileCount; set => SetSetting(Settings.CleanupKeepFileCount, decimal.ToInt32(value), x => Settings.CleanupKeepFileCount = x); }

    public bool ShowThumbnailTitle { get => Settings.ShowThumbnailTitle; set => SetSetting(Settings.ShowThumbnailTitle, value, x => Settings.ShowThumbnailTitle = x); }

    public EnumOption<ThumbnailTitleLocation>? SelectedThumbnailTitleLocation
    {
        get => Find(ThumbnailTitleLocationOptions, Settings.ThumbnailTitleLocation);
        set { if (value != null) SetSetting(Settings.ThumbnailTitleLocation, value.Value, x => Settings.ThumbnailTitleLocation = x); }
    }

    public decimal ThumbnailWidth
    {
        get => Settings.ThumbnailSize.Width;
        set => SetSetting(Settings.ThumbnailSize.Width, decimal.ToInt32(value), x => Settings.ThumbnailSize = new Size(x, Settings.ThumbnailSize.Height));
    }

    public decimal ThumbnailHeight
    {
        get => Settings.ThumbnailSize.Height;
        set => SetSetting(Settings.ThumbnailSize.Height, decimal.ToInt32(value), x => Settings.ThumbnailSize = new Size(Settings.ThumbnailSize.Width, x));
    }

    public EnumOption<ThumbnailViewClickAction>? SelectedThumbnailClickAction
    {
        get => Find(ThumbnailClickActionOptions, Settings.ThumbnailClickAction);
        set { if (value != null) SetSetting(Settings.ThumbnailClickAction, value.Value, x => Settings.ThumbnailClickAction = x); }
    }

    public ClipboardFormatItem? SelectedClipboardFormat
    {
        get => _selectedClipboardFormat;
        set
        {
            if (SetField(ref _selectedClipboardFormat, value))
            {
                OnPropertyChanged(nameof(HasSelectedClipboardFormat));
            }
        }
    }

    public bool HasSelectedClipboardFormat => SelectedClipboardFormat != null;

    public decimal UploadLimit { get => Settings.UploadLimit; set => SetSetting(Settings.UploadLimit, decimal.ToInt32(value), x => Settings.UploadLimit = x); }

    public EnumOption<int>? SelectedBufferSize
    {
        get => Find(BufferSizeOptions, Settings.BufferSizePower);
        set { if (value != null) SetSetting(Settings.BufferSizePower, value.Value, x => Settings.BufferSizePower = x); }
    }

    public decimal MaxUploadFailRetry { get => Settings.MaxUploadFailRetry; set => SetSetting(Settings.MaxUploadFailRetry, decimal.ToInt32(value), x => Settings.MaxUploadFailRetry = x); }

    public bool RecentTasksSave { get => Settings.RecentTasksSave; set => SetSetting(Settings.RecentTasksSave, value, x => Settings.RecentTasksSave = x); }
    public decimal RecentTasksMaxCount { get => Settings.RecentTasksMaxCount; set => SetSetting(Settings.RecentTasksMaxCount, decimal.ToInt32(value), x => Settings.RecentTasksMaxCount = x); }
    public bool RecentTasksShowInMainWindow { get => Settings.RecentTasksShowInMainWindow; set => SetSetting(Settings.RecentTasksShowInMainWindow, value, x => Settings.RecentTasksShowInMainWindow = x); }
    public bool RecentTasksShowInTrayMenu { get => Settings.RecentTasksShowInTrayMenu; set => SetSetting(Settings.RecentTasksShowInTrayMenu, value, x => Settings.RecentTasksShowInTrayMenu = x); }
    public bool RecentTasksTrayMenuMostRecentFirst { get => Settings.RecentTasksTrayMenuMostRecentFirst; set => SetSetting(Settings.RecentTasksTrayMenuMostRecentFirst, value, x => Settings.RecentTasksTrayMenuMostRecentFirst = x); }

    public bool DontShowPrintSettingsDialog { get => Settings.DontShowPrintSettingsDialog; set => SetSetting(Settings.DontShowPrintSettingsDialog, value, x => Settings.DontShowPrintSettingsDialog = x); }

    public bool DontShowWindowsPrintDialog
    {
        get => !Settings.PrintSettings.ShowPrintDialog;
        set
        {
            if (SetSetting(Settings.PrintSettings.ShowPrintDialog, !value, x => Settings.PrintSettings.ShowPrintDialog = x))
            {
                OnPropertyChanged(nameof(DefaultPrinterOverrideVisible));
            }
        }
    }

    public bool DefaultPrinterOverrideVisible => !Settings.PrintSettings.ShowPrintDialog;
    public string DefaultPrinterOverride { get => Settings.PrintSettings.DefaultPrinterOverride; set => SetSetting(Settings.PrintSettings.DefaultPrinterOverride, value, x => Settings.PrintSettings.DefaultPrinterOverride = x); }

    public EnumOption<ProxyMethod>? SelectedProxyMethod
    {
        get => Find(ProxyMethodOptions, Settings.ProxySettings.ProxyMethod);
        set
        {
            if (value == null || !SetSetting(Settings.ProxySettings.ProxyMethod, value.Value, x => Settings.ProxySettings.ProxyMethod = x))
            {
                return;
            }

            if (value.Value == ProxyMethod.Automatic)
            {
                Settings.ProxySettings.IsValidProxy();
                OnPropertyChanged(nameof(ProxyHost));
                OnPropertyChanged(nameof(ProxyPort));
            }

            OnPropertyChanged(nameof(ProxyCredentialsEnabled));
            OnPropertyChanged(nameof(ManualProxyEnabled));
        }
    }

    public bool ProxyCredentialsEnabled => Settings.ProxySettings.ProxyMethod != ProxyMethod.None;
    public bool ManualProxyEnabled => Settings.ProxySettings.ProxyMethod == ProxyMethod.Manual;
    public string ProxyUsername { get => Settings.ProxySettings.Username ?? string.Empty; set => SetSetting(Settings.ProxySettings.Username, value, x => Settings.ProxySettings.Username = x); }
    public string ProxyPassword { get => Settings.ProxySettings.Password ?? string.Empty; set => SetSetting(Settings.ProxySettings.Password, value, x => Settings.ProxySettings.Password = x); }
    public string ProxyHost { get => Settings.ProxySettings.Host ?? string.Empty; set => SetSetting(Settings.ProxySettings.Host, value, x => Settings.ProxySettings.Host = x); }
    public decimal ProxyPort { get => Settings.ProxySettings.Port; set => SetSetting(Settings.ProxySettings.Port, decimal.ToInt32(value), x => Settings.ProxySettings.Port = x); }

    public bool BinaryUnits
    {
        get => Settings.BinaryUnits;
        set
        {
            if (SetSetting(Settings.BinaryUnits, value, x => Settings.BinaryUnits = x))
            {
                RefreshBufferSizeOptions();
            }
        }
    }

    public bool ShowMostRecentTaskFirst { get => Settings.ShowMostRecentTaskFirst; set => SetSetting(Settings.ShowMostRecentTaskFirst, value, x => Settings.ShowMostRecentTaskFirst = x); }
    public bool WorkflowsOnlyShowEdited { get => Settings.WorkflowsOnlyShowEdited; set => SetSetting(Settings.WorkflowsOnlyShowEdited, value, x => Settings.WorkflowsOnlyShowEdited = x); }
    public bool TrayAutoExpandCaptureMenu { get => Settings.TrayAutoExpandCaptureMenu; set => SetSetting(Settings.TrayAutoExpandCaptureMenu, value, x => Settings.TrayAutoExpandCaptureMenu = x); }
    public string BrowserPath { get => Settings.BrowserPath ?? string.Empty; set => SetSetting(Settings.BrowserPath, value, x => Settings.BrowserPath = x); }
    public bool SaveSettingsAfterTaskCompleted { get => Settings.SaveSettingsAfterTaskCompleted; set => SetSetting(Settings.SaveSettingsAfterTaskCompleted, value, x => Settings.SaveSettingsAfterTaskCompleted = x); }
    public bool DevMode { get => Settings.DevMode; set => SetSetting(Settings.DevMode, value, x => Settings.DevMode = x); }

    public bool DisableHotkeys { get => Settings.DisableHotkeys; set => SetSetting(Settings.DisableHotkeys, value, x => Settings.DisableHotkeys = x); }
    public bool DisableHotkeysOnFullscreen { get => Settings.DisableHotkeysOnFullscreen; set => SetSetting(Settings.DisableHotkeysOnFullscreen, value, x => Settings.DisableHotkeysOnFullscreen = x); }
    public decimal HotkeyRepeatLimit { get => Settings.HotkeyRepeatLimit; set => SetSetting(Settings.HotkeyRepeatLimit, decimal.ToInt32(value), x => Settings.HotkeyRepeatLimit = x); }

    public bool ShowClipboardContentViewer { get => Settings.ShowClipboardContentViewer; set => SetSetting(Settings.ShowClipboardContentViewer, value, x => Settings.ShowClipboardContentViewer = x); }
    public bool DefaultClipboardCopyImageFillBackground { get => Settings.DefaultClipboardCopyImageFillBackground; set => SetSetting(Settings.DefaultClipboardCopyImageFillBackground, value, x => Settings.DefaultClipboardCopyImageFillBackground = x); }
    public bool UseAlternativeClipboardCopyImage { get => Settings.UseAlternativeClipboardCopyImage; set => SetSetting(Settings.UseAlternativeClipboardCopyImage, value, x => Settings.UseAlternativeClipboardCopyImage = x); }
    public bool UseAlternativeClipboardGetImage { get => Settings.UseAlternativeClipboardGetImage; set => SetSetting(Settings.UseAlternativeClipboardGetImage, value, x => Settings.UseAlternativeClipboardGetImage = x); }

    public bool RotateImageByExifOrientationData { get => Settings.RotateImageByExifOrientationData; set => SetSetting(Settings.RotateImageByExifOrientationData, value, x => Settings.RotateImageByExifOrientationData = x); }
    public bool PNGStripColorSpaceInformation { get => Settings.PNGStripColorSpaceInformation; set => SetSetting(Settings.PNGStripColorSpaceInformation, value, x => Settings.PNGStripColorSpaceInformation = x); }

    public bool DisableUpload { get => Settings.DisableUpload; set => SetSetting(Settings.DisableUpload, value, x => Settings.DisableUpload = x); }
    public bool URLEncodeIgnoreEmoji { get => Settings.URLEncodeIgnoreEmoji; set => SetSetting(Settings.URLEncodeIgnoreEmoji, value, x => Settings.URLEncodeIgnoreEmoji = x); }
    public bool ShowMultiUploadWarning { get => Settings.ShowMultiUploadWarning; set => SetSetting(Settings.ShowMultiUploadWarning, value, x => Settings.ShowMultiUploadWarning = x); }
    public decimal ShowLargeFileSizeWarning { get => Settings.ShowLargeFileSizeWarning; set => SetSetting(Settings.ShowLargeFileSizeWarning, decimal.ToInt32(value), x => Settings.ShowLargeFileSizeWarning = x); }

    public bool UseMachineSpecificUploadersConfig { get => Settings.UseMachineSpecificUploadersConfig; set => SetSetting(Settings.UseMachineSpecificUploadersConfig, value, x => Settings.UseMachineSpecificUploadersConfig = x); }
    public string CustomUploadersConfigPath { get => Settings.CustomUploadersConfigPath ?? string.Empty; set => SetSetting(Settings.CustomUploadersConfigPath, value, x => Settings.CustomUploadersConfigPath = x); }
    public string CustomHotkeysConfigPath { get => Settings.CustomHotkeysConfigPath ?? string.Empty; set => SetSetting(Settings.CustomHotkeysConfigPath, value, x => Settings.CustomHotkeysConfigPath = x); }
    public string CustomScreenshotsPath2 { get => Settings.CustomScreenshotsPath2 ?? string.Empty; set => SetSetting(Settings.CustomScreenshotsPath2, value, x => Settings.CustomScreenshotsPath2 = x); }

    public decimal DropSize { get => Settings.DropSize; set => SetSetting(Settings.DropSize, decimal.ToInt32(value), x => Settings.DropSize = x); }
    public decimal DropOffset { get => Settings.DropOffset; set => SetSetting(Settings.DropOffset, decimal.ToInt32(value), x => Settings.DropOffset = x); }

    public EnumOption<ContentAlignment>? SelectedDropAlignment
    {
        get => Find(DropAlignmentOptions, Settings.DropAlignment);
        set { if (value != null) SetSetting(Settings.DropAlignment, value.Value, x => Settings.DropAlignment = x); }
    }

    public decimal DropOpacity { get => Settings.DropOpacity; set => SetSetting(Settings.DropOpacity, decimal.ToInt32(value), x => Settings.DropOpacity = x); }
    public decimal DropHoverOpacity { get => Settings.DropHoverOpacity; set => SetSetting(Settings.DropHoverOpacity, decimal.ToInt32(value), x => Settings.DropHoverOpacity = x); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanExport));
            }
        }
    }

    public bool RestartRequired { get => _restartRequired; private set => SetField(ref _restartRequired, value); }
    public string StatusMessage { get => _statusMessage; private set { if (SetField(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); } }
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool IsGoogleDriveConnected => GoogleDriveBackupManager.IsConnected;
    public bool IsGoogleDriveBusy => GoogleDriveBackupManager.IsBusy;
    public string GoogleDriveAccountText => GoogleDriveBackupManager.AccountDisplayName;
    public string GoogleDriveStatusText => !string.IsNullOrEmpty(GoogleDriveBackupManager.StatusText)
        ? GoogleDriveBackupManager.StatusText
        : GoogleDriveBackupManager.LastBackupInfo;
    public bool HasGoogleDriveStatus => !string.IsNullOrWhiteSpace(GoogleDriveStatusText);

    public ApplicationSettingsViewModel()
    {
        Reload();
        GoogleDriveBackupManager.StateChanged += OnGoogleDriveStateChanged;
        GoogleDriveBackupManager.SettingsRestored += OnGoogleDriveSettingsRestored;
        if (GoogleDriveBackupManager.IsConnected)
        {
            _ = GoogleDriveBackupManager.CheckRemoteBackupInfoAsync();
        }
    }

    public async Task LoginGoogleDriveAsync(Avalonia.Controls.Window? parent = null)
    {
        await GoogleDriveBackupManager.LoginAsync(parent);
    }

    public async Task BackupGoogleDriveAsync()
    {
        await GoogleDriveBackupManager.BackupAsync();
    }

    public async Task RestoreGoogleDriveAsync()
    {
        await GoogleDriveBackupManager.RestoreAsync();
    }

    public void DisconnectGoogleDrive()
    {
        GoogleDriveBackupManager.Logout();
    }

    private void OnGoogleDriveStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsGoogleDriveConnected));
        OnPropertyChanged(nameof(IsGoogleDriveBusy));
        OnPropertyChanged(nameof(GoogleDriveAccountText));
        OnPropertyChanged(nameof(GoogleDriveStatusText));
        OnPropertyChanged(nameof(HasGoogleDriveStatus));
    }

    private async void OnGoogleDriveSettingsRestored(object? sender, EventArgs e)
    {
        LanguageHelper.ChangeLanguage(Settings.Language);
        Reload();
        await UpdateMainFormAsync();
        SettingsImported?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(string.Empty);
    }

    public void AddClipboardFormat(string description, string value)
    {
        ClipboardFormat format = new(description, value);
        Settings.ClipboardContentFormats.Add(format);
        ClipboardFormatItem item = new(format);
        ClipboardFormats.Add(item);
        SelectedClipboardFormat = item;
    }

    public void RemoveSelectedClipboardFormat()
    {
        if (SelectedClipboardFormat == null)
        {
            return;
        }

        int index = ClipboardFormats.IndexOf(SelectedClipboardFormat);
        Settings.ClipboardContentFormats.Remove(SelectedClipboardFormat.Model);
        ClipboardFormats.Remove(SelectedClipboardFormat);
        SelectedClipboardFormat = ClipboardFormats.Count == 0 ? null : ClipboardFormats[Math.Min(index, ClipboardFormats.Count - 1)];
    }

    public void ResetThumbnailSize()
    {
        Settings.ThumbnailSize = new Size(200, 150);
        OnPropertyChanged(nameof(ThumbnailWidth));
        OnPropertyChanged(nameof(ThumbnailHeight));
    }

    public void EditQuickTaskMenu() => QuickTaskMenuEditorIntegration.Show();

    public async Task CheckDevBuildAsync()
    {
        IsBusy = true;
        try
        {
            await TaskHelpers.DownloadDevBuild();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenChromeExtensionPage() => URLHelpers.OpenURL("https://chrome.google.com/webstore/detail/sharex/nlkoigbdolhchiicbonbihbphgamnaoc");
    public void OpenFirefoxAddonPage() => URLHelpers.OpenURL("https://addons.mozilla.org/en-US/firefox/addon/sharex/");
    public void OpenPersonalFolder() => FileHelpers.OpenFolder(PersonalFolderPreview);
    public void OpenScreenshotsFolder() => FileHelpers.OpenFolder(ScreenshotsFolderPreview);

    public void ShowImagePrintSettings(Avalonia.Controls.Window owner)
    {
        InvokeOnMainThread(() =>
        {
            using Image image = TaskHelpers.GetScreenshot().CaptureActiveMonitor();
            PrintWindowIntegration.Show(image, Settings.PrintSettings, true, owner);
        });
    }

    public async Task ExportAsync(string path)
    {
        IsBusy = true;
        StatusMessage = Strings.ApplicationSettingsWindow_ExportingBackup;

        try
        {
            bool exportSettings = ExportSettings;
            bool result = await Task.Run(() =>
            {
                SettingManager.SaveAllSettings();
                return SettingManager.Export(path, exportSettings, false);
            });
            StatusMessage = result
                ? string.Format(Strings.ApplicationSettingsWindow_BackupExportedTo, path)
                : Strings.ApplicationSettingsWindow_BackupExportFailed;
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
            StatusMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportAsync(string path)
    {
        IsBusy = true;
        StatusMessage = Strings.ApplicationSettingsWindow_ImportingBackup;

        try
        {
            // Preserve current OAuth session across import
            var savedOAuth = Program.UploadersConfig?.GoogleDriveOAuth2Info;
            var savedUser = Program.UploadersConfig?.GoogleDriveUserInfo;

            bool result = await Task.Run(() =>
            {
                if (!SettingManager.Import(path))
                {
                    return false;
                }

                SettingManager.LoadAllSettings();
                return true;
            });

            // Re-inject OAuth credentials so the user stays logged in
            if (savedOAuth != null && OAuth2Info.CheckOAuth(savedOAuth) && Program.UploadersConfig != null)
            {
                Program.UploadersConfig.GoogleDriveOAuth2Info = savedOAuth;
                Program.UploadersConfig.GoogleDriveUserInfo = savedUser;
                SettingManager.SaveUploadersConfigAsync();
            }

            if (result)
            {
                LanguageHelper.ChangeLanguage(Settings.Language);
                Reload();
                await UpdateMainFormAsync();
                SettingsImported?.Invoke(this, EventArgs.Empty);
                StatusMessage = string.Format(Strings.ApplicationSettingsWindow_BackupImportedFrom, path);
            }
            else
            {
                StatusMessage = Strings.ApplicationSettingsWindow_BackupImportFailed;
            }
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
            StatusMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResetAsync()
    {
        bool confirmed = InvokeOnMainThread(() => MessageBox.Show(
            Strings.ApplicationSettingsForm_btnResetSettings_Click_WouldYouLikeToResetShareXSettings,
            "Blink - " + Strings.Confirmation,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Exclamation) == MessageBoxResult.Yes);

        if (!confirmed)
        {
            return;
        }

        IsBusy = true;

        try
        {
            InvokeOnMainThread(() =>
            {
                SettingManager.ResetSettings();
                SettingManager.SaveAllSettings();
            });
            LanguageHelper.ChangeLanguage(Settings.Language);
            Reload();
            await UpdateMainFormAsync();
            StatusMessage = Strings.ApplicationSettingsWindow_SettingsReset;
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
            StatusMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Restart() => InvokeOnMainThread(() => Program.Restart());

    public void Reload()
    {
        _personalFolderPath = Program.ReadPersonalPathConfig();
        UpdatePersonalFolderPreview();
        UpdateScreenshotsFolderPreview();
        RefreshIntegrations();
        RefreshStartWithWindows();

        ClipboardFormats = new ObservableCollection<ClipboardFormatItem>(Settings.ClipboardContentFormats
            .Select(x => new ClipboardFormatItem(x)));
        SelectedClipboardFormat = ClipboardFormats.FirstOrDefault();

        RefreshBufferSizeOptions();
        RefreshTrayMenuItems();

        NavigationItems = CreateNavigationItems();
        SelectedNavigationItem = NavigationItems.FirstOrDefault();

        OnPropertyChanged(string.Empty);
    }

    private void RefreshTrayMenuItems()
    {
        TrayMenuItems.Clear();

        TrayMenuItems.Add(new TrayMenuItemModel("Settings", Strings.Settings_Settings, LucideIcons.settings, true, isMandatory: true));
        TrayMenuItems.Add(new TrayMenuItemModel("Exit", Strings.MainMenuBuilder_Exit, LucideIcons.log_out, true, isMandatory: true));

        if (Program.HotkeysConfig?.Hotkeys != null)
        {
            foreach (HotkeySettings hotkey in Program.HotkeysConfig.Hotkeys)
            {
                if (hotkey.TaskSettings.Job == HotkeyType.None) continue;
                string id = hotkey.TaskSettings.Job.ToString();

                string title = hotkey.TaskSettings.Job.GetLocalizedDescription();
                if (hotkey.HotkeyInfo.IsValidHotkey)
                {
                    title += $" ({hotkey.HotkeyInfo})";
                }

                string icon = TaskHelpers.FindMenuLucideIcon(hotkey.TaskSettings.Job);
                bool isVisible = !Settings.HiddenTrayMenuItems.Contains(id);

                TrayMenuItemModel item = new(id, title, icon, isVisible, isMandatory: false);
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(TrayMenuItemModel.IsVisible))
                    {
                        if (!item.IsVisible)
                        {
                            if (!Settings.HiddenTrayMenuItems.Contains(item.Id))
                            {
                                Settings.HiddenTrayMenuItems.Add(item.Id);
                            }
                        }
                        else
                        {
                            Settings.HiddenTrayMenuItems.Remove(item.Id);
                        }
                    }
                };
                TrayMenuItems.Add(item);
            }
        }
    }

    private ObservableCollection<SettingsNavigationItem> CreateNavigationItems()
    {
        return
        [
            Nav("general", Strings.ApplicationSettingsWindow_General, LucideIcons.settings),
            Nav("theme", Strings.ApplicationSettingsWindow_Theme, LucideIcons.palette),
            Nav("updater", "Updater", LucideIcons.download),
            Nav("paths", Strings.ApplicationSettingsWindow_Paths, LucideIcons.folder),
            Nav("settings", Strings.ApplicationSettingsWindow_Settings, LucideIcons.database_backup),
            Nav("tray", "System Tray", LucideIcons.panel_bottom),
            Nav("advanced", Strings.ApplicationSettingsWindow_Advanced, LucideIcons.sliders_horizontal)
        ];
    }

    private static SettingsNavigationItem Nav(string id, string title, string icon) => new(id, title, icon);

    private static string NormalizeTheme(string? theme)
    {
        return !string.IsNullOrWhiteSpace(theme) &&
            theme.Contains("Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
    }

    private void RefreshIntegrations()
    {
#if !MicrosoftStore
        _shellContextMenu = IntegrationHelpers.CheckShellContextMenuButton();
        _editWithShareX = IntegrationHelpers.CheckEditShellContextMenuButton();
        _sendToMenu = IntegrationHelpers.CheckSendToMenuButton();
        _chromeExtensionSupport = IntegrationHelpers.CheckChromeExtensionSupport();
        _firefoxAddonSupport = IntegrationHelpers.CheckFirefoxAddonSupport();
#endif
#if STEAM
        _steamShowInApp = IntegrationHelpers.CheckSteamShowInApp();
#endif
    }

    private void RefreshStartWithWindows()
    {
        StartWithWindowsText = Strings.ApplicationSettingsForm_cbStartWithWindows_Text;
        StartWithWindowsEnabled = false;

        try
        {
            StartupState state = InvokeOnMainThread(() => StartupManager.State);
            _startWithWindows = state == StartupState.Enabled || state == StartupState.EnabledByPolicy;
            OnPropertyChanged(nameof(StartWithWindows));

            if (state == StartupState.DisabledByUser)
            {
                StartWithWindowsText = Strings.ApplicationSettingsForm_cbStartWithWindows_DisabledByUser_Text;
            }
            else if (state == StartupState.DisabledByPolicy)
            {
                StartWithWindowsText = Strings.ApplicationSettingsForm_cbStartWithWindows_DisabledByPolicy_Text;
            }
            else if (state == StartupState.EnabledByPolicy)
            {
                StartWithWindowsText = Strings.ApplicationSettingsForm_cbStartWithWindows_EnabledByPolicy_Text;
            }
            else
            {
                StartWithWindowsEnabled = true;
            }
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
            StatusMessage = e.Message;
        }
    }

    private void UpdatePersonalFolderPreview()
    {
        try
        {
            string path = FileHelpers.SanitizePath(_personalFolderPath);
            if (string.IsNullOrEmpty(path))
            {
                path = Program.Portable ? Program.PortablePersonalFolder : Program.DefaultPersonalFolder;
            }
            else
            {
                path = FileHelpers.GetAbsolutePath(path);
            }

            PersonalFolderPreview = path;
        }
        catch (Exception e)
        {
            PersonalFolderPreview = Strings.ApplicationSettingsWindow_ErrorPrefix + " " + e.Message;
        }
    }

    private void UpdateScreenshotsFolderPreview()
    {
        try
        {
            ScreenshotsFolderPreview = TaskHelpers.GetScreenshotsFolder();
        }
        catch (Exception e)
        {
            ScreenshotsFolderPreview = Strings.ApplicationSettingsWindow_ErrorPrefix + " " + e.Message;
        }
    }

    private bool IsPage(string id) => SelectedNavigationItem?.Id == id;

    private void OnPageChanged()
    {
        OnPropertyChanged(nameof(IsGeneralPage));
        OnPropertyChanged(nameof(IsThemePage));
        OnPropertyChanged(nameof(IsIntegrationPage));
        OnPropertyChanged(nameof(IsPathsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsMainWindowPage));
        OnPropertyChanged(nameof(IsClipboardFormatsPage));
        OnPropertyChanged(nameof(IsUploadPage));
        OnPropertyChanged(nameof(IsRecentTasksPage));
        OnPropertyChanged(nameof(IsPrintPage));
        OnPropertyChanged(nameof(IsProxyPage));
        OnPropertyChanged(nameof(IsTrayPage));
        OnPropertyChanged(nameof(IsAdvancedPage));
        OnPropertyChanged(nameof(IsUpdaterPage));
    }

    private bool SetSetting<T>(T current, T value, Action<T> setter, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        setter(value);
        OnPropertyChanged(propertyName);
        return true;
    }

    private void RefreshBufferSizeOptions()
    {
        BufferSizeOptions = Enumerable.Range(0, 14)
            .Select(power => new EnumOption<int>(power, ((long)(Math.Pow(2, power) * 1024)).ToSizeString(Settings.BinaryUnits, 0)))
            .ToArray();
        OnPropertyChanged(nameof(BufferSizeOptions));
        OnPropertyChanged(nameof(SelectedBufferSize));
    }

    private void FlushPersonalPath()
    {
        if (!_personalPathDirty)
        {
            return;
        }

        _personalPathDirty = false;

        try
        {
            bool changed = InvokeOnMainThread(() => Program.WritePersonalPathConfig(FileHelpers.SanitizePath(_personalFolderPath)));
            if (changed)
            {
                RestartRequired = true;
            }
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
            StatusMessage = e.Message;
        }
    }

    private async Task UpdateMainFormAsync()
    {
        Task updateTask = InvokeOnMainThread(() => Program.MainForm.UpdateControls());
        await updateTask;
    }

    private static IReadOnlyList<EnumOption<T>> CreateEnumOptions<T>() where T : struct, Enum =>
        Helpers.GetEnums<T>().Select(x => new EnumOption<T>(x, x.GetLocalizedDescription())).ToArray();

    private static IReadOnlyList<EnumOption<HotkeyType>> CreateHotkeyTypeOptions()
    {
        HashSet<HotkeyType> excluded =
        [
            HotkeyType.ScreenRecorder,
            HotkeyType.ScreenRecorderActiveWindow,
            HotkeyType.ScreenRecorderCustomRegion,
            HotkeyType.ScreenRecorderGIFCustomRegion,
            HotkeyType.StartScreenRecorder,
            HotkeyType.ColorPicker,
            HotkeyType.ScreenColorPicker,
            HotkeyType.Ruler,
            HotkeyType.MouseHighlighter,
            HotkeyType.PinToScreenCloseAll,
            HotkeyType.ImageEditor,
            HotkeyType.ImageBeautifier,
            HotkeyType.ImageEffects,
            HotkeyType.ImageViewer,
            HotkeyType.BackgroundRemover,
            HotkeyType.ImageComparer,
            HotkeyType.IconConverter,
            HotkeyType.ImageCombiner,
            HotkeyType.ImageSplitter,
            HotkeyType.ImageResizer,
            HotkeyType.ImageConverter,
            HotkeyType.ImageWatermark,
            HotkeyType.ImageThumbnailer,
            HotkeyType.VideoConverter,
            HotkeyType.VideoTrimmer,
            HotkeyType.VideoThumbnailer,
            HotkeyType.AnalyzeImage,
            HotkeyType.QRCode,
            HotkeyType.QRCodeScanRegion,
            HotkeyType.HashCheck,
            HotkeyType.Metadata,
            HotkeyType.StripMetadata,
            HotkeyType.IndexFolder,
            HotkeyType.BorderlessWindow,
            HotkeyType.ActiveWindowBorderless,
            HotkeyType.ActiveWindowTopMost,
            HotkeyType.InspectWindow,
            HotkeyType.NetworkMonitor,
            HotkeyType.MonitorTest,
            HotkeyType.CustomWindow,
            HotkeyType.CustomRegion,
            HotkeyType.LastRegion,
            HotkeyType.AutoCapture,
            HotkeyType.StartAutoCapture,
            HotkeyType.StopAutoCapture,
            HotkeyType.FileUpload,
            HotkeyType.FolderUpload,
            HotkeyType.UploadText,
            HotkeyType.DragDropUpload,
            HotkeyType.ShortenURL,
            HotkeyType.StopUploads,
            HotkeyType.DisableHotkeys,
            HotkeyType.OpenMainWindow,
            HotkeyType.ToggleActionsToolbar,
            HotkeyType.ToggleTrayMenu,
            HotkeyType.ExitBlink
        ];

        return Helpers.GetEnums<HotkeyType>()
            .Where(x => !excluded.Contains(x))
            .Select(x => new EnumOption<HotkeyType>(x, x.GetLocalizedDescription()))
            .ToArray();
    }

    private static IReadOnlyList<LanguageOption> CreateLanguageOptions() =>
        Helpers.GetEnums<SupportedLanguage>()
            .Select(x => new LanguageOption(
                x,
                x.GetLocalizedDescription(),
                LoadLanguageFlag(x),
                x == SupportedLanguage.Automatic ? LucideIcons.languages : null))
            .ToArray();

    private static AvaloniaBitmap? LoadLanguageFlag(SupportedLanguage language)
    {
        if (language == SupportedLanguage.Automatic)
        {
            return null;
        }

        try
        {
            string resourceName = language == SupportedLanguage.Arabic ? "sa" : LanguageHelper.GetCultureName(language).Split('-')[1].ToLowerInvariant();
            string asm = typeof(ApplicationSettingsViewModel).Assembly.GetName().Name ?? "Blink";
            Uri uri = new Uri($"avares://{asm}/Resources/Flags/{resourceName}.png");
            if (AssetLoader.Exists(uri))
            {
                using Stream stream = AssetLoader.Open(uri);
                return new AvaloniaBitmap(stream);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
        }

        return null;
    }

    private static LanguageOption? Find(IReadOnlyList<LanguageOption> options, SupportedLanguage value) =>
        options.FirstOrDefault(x => x.Value == value);

    private static EnumOption<T>? Find<T>(IReadOnlyList<EnumOption<T>> options, T value) =>
        options.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, value));

    private static void InvokeOnMainThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }

    private static T InvokeOnMainThread<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        return Dispatcher.UIThread.Invoke(action);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GoogleDriveBackupManager.StateChanged -= OnGoogleDriveStateChanged;
        GoogleDriveBackupManager.SettingsRestored -= OnGoogleDriveSettingsRestored;
        foreach (LanguageOption language in LanguageOptions)
        {
            language.Flag?.Dispose();
        }

        if (!Program.IsClosing)
        {
            FlushPersonalPath();
            InvokeOnMainThread(Program.MainForm.ApplyApplicationSettings);
            SettingManager.SaveApplicationConfigAsync();
        }
    }

    private string _latestVersionText = "N/A";
    private string _updateStatusText = "Click 'Check for Updates' to check for new releases.";
    private double _updateProgress;
    private bool _isUpdating;
    private bool _isUpdateAvailable;

    public string CurrentVersionText => Program.VersionText;

    public string LatestVersionText
    {
        get => _latestVersionText;
        set => SetField(ref _latestVersionText, value);
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetField(ref _updateStatusText, value);
    }

    public double UpdateProgress
    {
        get => _updateProgress;
        set => SetField(ref _updateProgress, value);
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        set => SetField(ref _isUpdating, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => SetField(ref _isUpdateAvailable, value);
    }

    private GitHubUpdateChecker? _currentUpdateChecker;

    public async Task CheckForUpdatesAsync()
    {
        if (IsUpdating) return;

        IsUpdating = true;
        UpdateStatusText = "Checking for updates...";
        UpdateProgress = 0;

        try
        {
            _currentUpdateChecker = Program.UpdateManager.CreateUpdateChecker();
            await _currentUpdateChecker.CheckUpdateAsync();

            if (_currentUpdateChecker.Status == UpdateStatus.UpdateAvailable)
            {
                LatestVersionText = _currentUpdateChecker.LatestVersion?.ToString() ?? "New version";
                UpdateStatusText = $"An update is available: v{LatestVersionText}";
                IsUpdateAvailable = true;
            }
            else if (_currentUpdateChecker.Status == UpdateStatus.UpToDate)
            {
                LatestVersionText = CurrentVersionText;
                UpdateStatusText = "Blink is up to date.";
                IsUpdateAvailable = false;
            }
            else
            {
                UpdateStatusText = "Failed to check for updates. Please try again later.";
                IsUpdateAvailable = false;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = "Error checking for updates: " + ex.Message;
            IsUpdateAvailable = false;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    public async Task InstallUpdateAsync()
    {
        if (IsUpdating || _currentUpdateChecker == null || string.IsNullOrEmpty(_currentUpdateChecker.DownloadURL))
        {
            return;
        }

        IsUpdating = true;
        UpdateProgress = 0;
        UpdateStatusText = "Downloading update...";

        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Blink_Update");
            Directory.CreateDirectory(tempDir);
            string downloadPath = Path.Combine(tempDir, "Blink.exe");

            Progress<double> progress = new Progress<double>(p =>
            {
                UpdateProgress = p;
                UpdateStatusText = $"Downloading update... {p:0}%";
            });

            bool downloaded = await DownloadFileAsync(_currentUpdateChecker.DownloadURL, downloadPath, progress);

            if (downloaded && File.Exists(downloadPath))
            {
                UpdateStatusText = "Download complete. Starting updater...";

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string rootExe = Path.GetFullPath(Path.Combine(baseDir, "..", "Blink.exe"));
                if (!File.Exists(rootExe))
                {
                    rootExe = Environment.ProcessPath ?? Path.Combine(baseDir, "Blink.exe");
                }

                string updaterPath = Path.Combine(baseDir, "updater.exe");
                if (!File.Exists(updaterPath))
                {
                    updaterPath = Path.Combine(baseDir, "ShareX.Updater.exe");
                }

                if (File.Exists(updaterPath))
                {
                    int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = updaterPath,
                        UseShellExecute = false
                    };
                    psi.ArgumentList.Add("--pid");
                    psi.ArgumentList.Add(pid.ToString());
                    psi.ArgumentList.Add("--new-exe");
                    psi.ArgumentList.Add(downloadPath);
                    psi.ArgumentList.Add("--target-exe");
                    psi.ArgumentList.Add(rootExe);
                    psi.ArgumentList.Add("--launch");

                    System.Diagnostics.Process.Start(psi);
                    ShareX.AvaloniaUI.Integration.AvaloniaBootstrapper.Shutdown();
                }
                else
                {
                    UpdateStatusText = "Updater executable not found.";
                    IsUpdating = false;
                }
            }
            else
            {
                UpdateStatusText = "Failed to download update.";
                IsUpdating = false;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = "Error installing update: " + ex.Message;
            IsUpdating = false;
        }
    }

    private static async Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<double> progress)
    {
        try
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Blink-App");
                using (var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                progress.Report((double)totalRead / totalBytes.Value * 100.0);
                            }
                        }
                    }
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
            return false;
        }
    }

}
