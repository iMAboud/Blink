#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ShareX.AvaloniaUI.Controls;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.ImageEditor.Integration;
using ShareX.Localization;
using ShareX.ScreenCaptureLib;
using ShareX.Tools;
using ShareX.UploadersLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using WinForms = System.Windows.Forms;

namespace ShareX;

internal sealed class TaskSettingsPageBuilder
{
    private readonly ITaskSettingsHost _host;
    private readonly TaskSettings _settings;
    private readonly bool _isDefault;
    private readonly TaskSettingsGeneral _generalSettings;
    private readonly TaskSettingsCapture _captureSettings;
    private readonly List<ExternalProgram> _externalPrograms;
    private readonly BoundValue<bool> _generalOverride;
    private readonly BoundValue<bool> _captureOverride;
    private readonly BoundValue<bool> _actionsOverride;
    private ContextMenu? _activeCodeMenu;

    public TaskSettingsPageBuilder(ITaskSettingsHost host, TaskSettings settings, bool isDefault)
    {
        _host = host;
        _settings = settings;
        _isDefault = isDefault;

        _generalSettings = settings.GeneralSettings ?? new TaskSettingsGeneral();
        _captureSettings = settings.CaptureSettings ?? new TaskSettingsCapture();
        _externalPrograms = settings.ExternalPrograms ?? [];

        _generalOverride = OverrideValue(() => !_settings.UseDefaultGeneralSettings, value =>
        {
            _settings.UseDefaultGeneralSettings = !value;
            if (value) _settings.GeneralSettings = _generalSettings;
        });
        _captureOverride = OverrideValue(() => !_settings.UseDefaultCaptureSettings, value =>
        {
            _settings.UseDefaultCaptureSettings = !value;
            if (value) _settings.CaptureSettings = _captureSettings;
        });
        _actionsOverride = OverrideValue(() => !_settings.UseDefaultActions, value =>
        {
            _settings.UseDefaultActions = !value;
            if (value) _settings.ExternalPrograms = _externalPrograms;
        });
    }

    public IReadOnlyDictionary<string, Control> BuildPages()
    {
        Dictionary<string, Control> pages = [];

        if (!_isDefault)
        {
            pages.Add("task", BuildTaskPage());
        }

        pages.Add("general", BuildGeneralPage());
        pages.Add("capture", BuildCapturePage());
        pages.Add("capture-region", BuildRegionCapturePage());
        pages.Add("capture-screen-recorder", BuildScreenRecorderPage());
        pages.Add("capture-ocr", BuildOcrPage());
        pages.Add("actions", BuildActionsPage());

        return pages;
    }

    private Control BuildTaskPage()
    {
        BoundValue<bool> afterCaptureOverride = OverrideValue(
            () => !_settings.UseDefaultAfterCaptureJob,
            value => _settings.UseDefaultAfterCaptureJob = !value);
        BoundValue<bool> afterUploadOverride = OverrideValue(
            () => !_settings.UseDefaultAfterUploadJob,
            value => _settings.UseDefaultAfterUploadJob = !value);
        BoundValue<bool> destinationsOverride = OverrideValue(
            () => !_settings.UseDefaultDestinations,
            value => _settings.UseDefaultDestinations = !value);

        Control afterCapture = TaskFlagsMenu(
            () => _settings.AfterCaptureJob,
            value => _settings.AfterCaptureJob = value,
            MainMenuBuilder.GetAfterCaptureTaskMenuOptions(),
            LucideIcons.image_up);
        BindEnabled(afterCapture, afterCaptureOverride);

        Control afterUpload = TaskFlagsMenu(
            () => _settings.AfterUploadJob,
            value => _settings.AfterUploadJob = value,
            MainMenuBuilder.GetAfterUploadTaskMenuOptions(),
            LucideIcons.cloud_upload);
        BindEnabled(afterUpload, afterUploadOverride);

        Control destinations = DestinationMenu(_settings);
        BindEnabled(destinations, destinationsOverride);

        List<Control> accountControls = [];

        if (Program.UploadersConfig?.FTPAccountList.Count > 0)
        {
            BoundValue<bool> ftpOverride = new(_settings.OverrideFTP, value => _settings.OverrideFTP = value);
            ComboBox ftp = ObjectCombo(
                Program.UploadersConfig.FTPAccountList,
                () => Program.UploadersConfig.FTPAccountList[_settings.FTPIndex.BetweenOrDefault(0, Program.UploadersConfig.FTPAccountList.Count - 1)],
                value => _settings.FTPIndex = Program.UploadersConfig.FTPAccountList.IndexOf(value));
            BindEnabled(ftp, ftpOverride);
            accountControls.Add(Check(Strings.TaskSettingsWindow_OverrideDefaultFTPAccount, ftpOverride));
            accountControls.Add(Row(Strings.TaskSettingsWindow_FTPAccount, ftp));
        }

        if (Program.UploadersConfig?.CustomUploadersList.Count > 0)
        {
            BoundValue<bool> customOverride = new(_settings.OverrideCustomUploader, value => _settings.OverrideCustomUploader = value);
            ComboBox custom = ObjectCombo(
                Program.UploadersConfig.CustomUploadersList,
                () => Program.UploadersConfig.CustomUploadersList[_settings.CustomUploaderIndex.BetweenOrDefault(0, Program.UploadersConfig.CustomUploadersList.Count - 1)],
                value => _settings.CustomUploaderIndex = Program.UploadersConfig.CustomUploadersList.IndexOf(value));
            BindEnabled(custom, customOverride);
            accountControls.Add(Check(Strings.TaskSettingsWindow_OverrideDefaultCustomUploader, customOverride));
            accountControls.Add(Row(Strings.TaskSettingsWindow_CustomUploader, custom));
        }

        BoundValue<bool> folderOverride = new(_settings.OverrideScreenshotsFolder, value => _settings.OverrideScreenshotsFolder = value);
        TextBox folderText = Text(() => _settings.ScreenshotsFolder, value => _settings.ScreenshotsFolder = value);
        Button browseFolder = Button(Strings.TaskSettingsWindow_BrowseWithEllipsis, async () =>
        {
            string? path = await PickFolderAsync(Strings.TaskSettingsWindow_ChooseScreenshotsFolder);
            if (!string.IsNullOrEmpty(path))
            {
                ((BoundValue<string>)folderText.DataContext!).Value = path;
            }
        });
        BindEnabled(folderText, folderOverride);
        BindEnabled(browseFolder, folderOverride);
        Grid folderRow = InputWithButton(folderText, browseFolder);

        return Page("task", Strings.TaskSettingsWindow_Task, LucideIcons.keyboard,
            Card(Strings.TaskSettingsWindow_Task, Row(Strings.TaskSettingsWindow_TaskLabel, TaskMenu(() => _settings.Job, value => _settings.Job = value)),
                Row(Strings.TaskSettingsWindow_Description, Text(() => _settings.Description, value => _settings.Description = value))),
            Card(Strings.TaskSettingsWindow_AfterCaptureTasks, Check(Strings.TaskSettingsWindow_OverrideAfterCaptureTasks, afterCaptureOverride), afterCapture),
            Card(Strings.TaskSettingsWindow_AfterUploadTasks, Check(Strings.TaskSettingsWindow_OverrideAfterUploadTasks, afterUploadOverride), afterUpload),
            Card(Strings.TaskSettingsWindow_Destinations, Check(Strings.TaskSettingsWindow_OverrideDestinations, destinationsOverride), destinations),
            Card(Strings.TaskSettingsWindow_UploaderAccounts, accountControls.ToArray()),
            Card(Strings.TaskSettingsWindow_ScreenshotsFolder, Check(Strings.TaskSettingsWindow_OverrideScreenshotsFolder, folderOverride), folderRow));
    }

    private Control BuildGeneralPage()
    {
        TaskSettingsGeneral general = _settings.GeneralSettings;
        BoundValue<bool> showToast = new(general.ShowToastNotificationAfterTaskCompleted, value => general.ShowToastNotificationAfterTaskCompleted = value);

        StackPanel toastOptions = new() { Spacing = 4 };
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_DurationSeconds, Number(() => (decimal)general.ToastWindowDuration, value => general.ToastWindowDuration = (float)value, 0, 60, 0.1m)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_FadeDurationSeconds, Number(() => (decimal)general.ToastWindowFadeDuration, value => general.ToastWindowFadeDuration = (float)value, 0, 10, 0.1m)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_Placement, EnumCombo(() => general.ToastWindowPlacement, value => general.ToastWindowPlacement = value)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_Width, Number(() => general.ToastWindowSize.Width, value => general.ToastWindowSize = new DrawingSize((int)value, general.ToastWindowSize.Height), 100, 2000)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_Height, Number(() => general.ToastWindowSize.Height, value => general.ToastWindowSize = new DrawingSize(general.ToastWindowSize.Width, (int)value), 50, 2000)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_LeftClickAction, EnumCombo(() => general.ToastWindowLeftClickAction, value => general.ToastWindowLeftClickAction = value)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_RightClickAction, EnumCombo(() => general.ToastWindowRightClickAction, value => general.ToastWindowRightClickAction = value)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_MiddleClickAction, EnumCombo(() => general.ToastWindowMiddleClickAction, value => general.ToastWindowMiddleClickAction = value)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_NotificationButtonSize, Number(() => general.ToastWindowButtonSize,
            value => general.ToastWindowButtonSize = (int)value, 16, 128)));
        toastOptions.Children.Add(Row(Strings.TaskSettingsWindow_NotificationButtonsLabel, Button(Strings.TaskSettingsWindow_ConfigureWithEllipsis, () =>
            _host.ShowNotificationButtonsEditor(general.ToastWindowButtons, buttons => general.ToastWindowButtons = buttons))));
        toastOptions.Children.Add(Check(Strings.TaskSettingsWindow_AutomaticallyHideOnScreenCapture, () => general.ToastWindowAutoHide, value => general.ToastWindowAutoHide = value));
        toastOptions.Children.Add(Check(Strings.TaskSettingsWindow_DisableToastNotificationsOnFullscreen, () => general.DisableNotificationsOnFullscreen, value => general.DisableNotificationsOnFullscreen = value));
        BindVisible(toastOptions, showToast);

        return Page("general", Strings.TaskSettingsWindow_General, LucideIcons.settings_2,
            OverrideCard(_generalOverride, Strings.TaskSettingsWindow_OverrideGeneralSettings),
            EnabledCard(_generalOverride, Strings.TaskSettingsWindow_Sounds,
                BuildSoundOptionRow(
                    Strings.TaskSettingsWindow_PlaySoundAfterCaptureIsMade,
                    () => general.PlaySoundAfterCapture, value => general.PlaySoundAfterCapture = value,
                    () => general.UseCustomCaptureSound, value => general.UseCustomCaptureSound = value,
                    () => general.CustomCaptureSoundPath, value => general.CustomCaptureSoundPath = value,
                    () => Properties.Resources.CaptureSound),
                BuildSoundOptionRow(
                    Strings.TaskSettingsWindow_PlaySoundAfterTaskIsCompleted,
                    () => general.PlaySoundAfterUpload, value => general.PlaySoundAfterUpload = value,
                    () => general.UseCustomTaskCompletedSound, value => general.UseCustomTaskCompletedSound = value,
                    () => general.CustomTaskCompletedSoundPath, value => general.CustomTaskCompletedSoundPath = value,
                    () => Properties.Resources.TaskCompletedSound),
                BuildSoundOptionRow(
                    Strings.TaskSettingsWindow_PlaySoundAfterActionIsCompleted,
                    () => general.PlaySoundAfterAction, value => general.PlaySoundAfterAction = value,
                    () => general.UseCustomActionCompletedSound, value => general.UseCustomActionCompletedSound = value,
                    () => general.CustomActionCompletedSoundPath, value => general.CustomActionCompletedSoundPath = value,
                    () => Properties.Resources.ActionCompletedSound)),
            EnabledCard(_generalOverride, Strings.TaskSettingsWindow_ToastNotification,
                Check(Strings.TaskSettingsWindow_ShowToastNotificationAfterTaskIsCompleted, showToast),
                toastOptions));
    }

    private Control BuildCapturePage()
    {
        TaskSettingsCapture capture = _settings.CaptureSettings;

        return Page("capture", Strings.TaskSettingsWindow_Capture, LucideIcons.camera,
            OverrideCard(_captureOverride, Strings.TaskSettingsWindow_OverrideCaptureSettings),
            EnabledCard(_captureOverride, Strings.TaskSettingsWindow_Screenshots,
                Check(Strings.TaskSettingsWindow_ShowCursorInScreenshots, () => capture.ShowCursor, value => capture.ShowCursor = value),
                Check(Strings.TaskSettingsWindow_HDRScreenshotColorCorrector, () => capture.HDRScreenshotColorCorrection, value => capture.HDRScreenshotColorCorrection = value)));
    }

    private Control BuildRegionCapturePage()
    {
        RegionCaptureOptions options = _settings.CaptureSettings.RegionCaptureOptions;
        BoundValue<bool> detectWindows = new(options.DetectWindows, value => options.DetectWindows = value);
        CheckBox detectControls = Check(Strings.TaskSettingsWindow_AlsoDetectControlsInsideWindows, () => options.DetectControls, value => options.DetectControls = value);
        BindEnabled(detectControls, detectWindows);

        return Page("capture-region", Strings.TaskSettingsWindow_RegionCapture, LucideIcons.crop,
            EnabledCard(_captureOverride, Strings.TaskSettingsWindow_Selection,
                Check(Strings.TaskSettingsWindow_QuickCapture, () => options.QuickCapture, value => options.QuickCapture = value),
                Check(Strings.TaskSettingsWindow_DetectWindowRegions, detectWindows), detectControls,
                Check(Strings.TaskSettingsWindow_RestrictCaptureAndCursorToTheActiveMonitor, () => options.ActiveMonitorMode, value => options.ActiveMonitorMode = value)),
            EnabledCard(_captureOverride, Strings.TaskSettingsWindow_MouseActions,
                Row(Strings.TaskSettingsWindow_RightClick, EnumCombo(() => options.RegionCaptureActionRightClick, value => options.RegionCaptureActionRightClick = value)),
                Row(Strings.TaskSettingsWindow_MiddleClick, EnumCombo(() => options.RegionCaptureActionMiddleClick, value => options.RegionCaptureActionMiddleClick = value)),
                Row(Strings.TaskSettingsWindow_Mouse4Click, EnumCombo(() => options.RegionCaptureActionX1Click, value => options.RegionCaptureActionX1Click = value)),
                Row(Strings.TaskSettingsWindow_Mouse5Click, EnumCombo(() => options.RegionCaptureActionX2Click, value => options.RegionCaptureActionX2Click = value))),
            EnabledCard(_captureOverride, Strings.TaskSettingsWindow_InformationAndMagnifier,
                Check(Strings.TaskSettingsWindow_ShowPositionAndSizeInfo, () => options.ShowInfo, value => options.ShowInfo = value),
                Check(Strings.TaskSettingsWindow_ShowCenterCrosshair, () => options.ShowCenterCrosshair, value => options.ShowCenterCrosshair = value)));
    }

    private Control BuildScreenRecorderPage()
    {
        TaskSettingsCapture capture = _settings.CaptureSettings;
        BoundValue<bool> fixedDuration = new(capture.ScreenRecordFixedDuration, value => capture.ScreenRecordFixedDuration = value);
        NumericUpDown duration = Number(() => (decimal)capture.ScreenRecordDuration, value => capture.ScreenRecordDuration = (float)value, 0, 86400, 0.1m);
        BindEnabled(duration, fixedDuration);

        return Page("capture-screen-recorder", "GIF Recorder", LucideIcons.film,
            EnabledCard(_captureOverride, Strings.TaskSettingsWindow_Recording,
                Row(Strings.TaskSettingsWindow_GIFFPS, Number(() => capture.GIFFPS, value => capture.GIFFPS = (int)value, 1, HelpersOptions.DevMode ? 60 : 30)),
                Check(Strings.TaskSettingsWindow_ShowCursorInRecording, () => capture.ScreenRecordShowCursor, value => capture.ScreenRecordShowCursor = value),
                Check(Strings.TaskSettingsWindow_ShowRecordingTimer, () => capture.ScreenRecordShowTimer, value => capture.ScreenRecordShowTimer = value),
                Check(Strings.TaskSettingsWindow_ShowRecordingButtonLabels, () => capture.ScreenRecordShowButtonLabels, value => capture.ScreenRecordShowButtonLabels = value),
                Check(Strings.TaskSettingsWindow_UseFixedDuration, fixedDuration), Row(Strings.TaskSettingsWindow_DurationSeconds, duration)));
    }

    private Control BuildOcrPage()
    {
        OCROptions options = _settings.CaptureSettings.OCROptions;
        ComboBox language;

        try
        {
            OCRLanguageOption[] languages = OCRHelper.AvailableLanguages.OrderBy(x => x.DisplayName).ToArray();
            OCRLanguageOption selected = languages.FirstOrDefault(x => x.LanguageTag.Equals(options.Language, StringComparison.OrdinalIgnoreCase)) ?? languages.First();
            options.Language = selected.LanguageTag;
            language = ObjectCombo(languages, () => selected, value => options.Language = value.LanguageTag, value => value.DisplayName);
        }
        catch
        {
            language = new ComboBox { IsEnabled = false, PlaceholderText = Strings.TaskSettingsWindow_OCRLanguagesAreUnavailable };
            language.Classes.Add("form-control");
        }

        BoundValue<bool> silent = new(options.Silent, value => options.Silent = value);

        return Page("capture-ocr", Strings.TaskSettingsWindow_OCR, LucideIcons.scan_text,
            EnabledCard(_captureOverride, Strings.TaskSettingsWindow_OpticalCharacterRecognition,
                Row(Strings.TaskSettingsWindow_DefaultLanguage, language),
                Check(Strings.TaskSettingsWindow_ProcessOCRSilently, silent)));
    }

    private Control BuildActionsPage()
    {
        _settings.ExternalPrograms = _externalPrograms;
        TaskHelpers.AddDefaultExternalPrograms(_settings);

        ListBox list = new() { MinHeight = 230 };
        list.Classes.Add("settings-list");
        list.Classes.Add("action-list");
        Dictionary<Control, ExternalProgram> entries = [];

        void Refresh(ExternalProgram? selected = null)
        {
            list.Items.Clear();
            entries.Clear();
            foreach (ExternalProgram action in _externalPrograms)
            {
                CheckBox enabled = Check(string.Empty, () => action.IsActive, value => action.IsActive = value);
                enabled.Content = null;
                enabled.Classes.Remove("setting");
                enabled.Classes.Add("action-list-toggle");
                ToolTip.SetTip(enabled, Strings.TaskSettingsWindow_EnableAction);

                Grid item = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
                    ColumnSpacing = 6
                };
                item.Classes.Add("action-list-item");
                item.Children.Add(enabled);

                TextBlock name = new()
                {
                    Text = action.Name,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(name, 1);
                item.Children.Add(name);

                TextBlock path = new()
                {
                    Text = $"— {action.Path}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                path.Classes.Add("action-list-path");
                Grid.SetColumn(path, 2);
                item.Children.Add(path);

                item.PointerPressed += (_, _) => list.SelectedItem = item;
                enabled.Click += (_, _) => list.SelectedItem = item;
                entries.Add(item, action);
                list.Items.Add(item);
                if (ReferenceEquals(action, selected))
                {
                    list.SelectedItem = item;
                }
            }
        }

        ExternalProgram? Selected() => list.SelectedItem is Control item && entries.TryGetValue(item, out ExternalProgram? action) ? action : null;
        Refresh();

        Button add = Button(Strings.TaskSettingsWindow_AddWithEllipsis, () =>
        {
            _host.ShowActionEditor(null, action =>
            {
                _externalPrograms.Add(action);
                Refresh(action);
            });
        });
        Button edit = Button(Strings.TaskSettingsWindow_EditWithEllipsis, () =>
        {
            if (Selected() is { } action)
            {
                _host.ShowActionEditor(action, editedAction =>
                {
                    Refresh(editedAction);
                });
            }
        });
        Button duplicate = Button(Strings.TaskSettingsWindow_Duplicate, () =>
        {
            if (Selected() is { } action)
            {
                ExternalProgram copy = action.Copy();
                _externalPrograms.Add(copy);
                Refresh(copy);
            }
        });
        Button remove = Button(Strings.TaskSettingsWindow_Remove, () =>
        {
            if (Selected() is { } action)
            {
                _externalPrograms.Remove(action);
                Refresh();
            }
        });

        return Page("actions", Strings.TaskSettingsWindow_Actions, LucideIcons.zap,
            OverrideCard(_actionsOverride, Strings.TaskSettingsWindow_OverrideActions),
            EnabledCard(_actionsOverride, Strings.TaskSettingsWindow_Actions, list, ButtonRow(add, edit, duplicate, remove),
                Hint(Strings.TaskSettingsWindow_YouCanEnableOrDisableActionsFromAfterCaptureTasksPerformActions)));
    }

    private ScrollViewer Page(string id, string title, string icon, params Control[] controls)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(28, 24, 28, 32),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = 0
        };

        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 9
        };
        header.Classes.Add("page-title");
        SettingsSearch.SetIsPageTitle(header, true);

        TextBlock iconBlock = new() { Text = icon };
        iconBlock.Classes.Add("icon");
        iconBlock.Classes.Add("page-title-icon");
        TextBlock titleBlock = new() { Text = title };
        titleBlock.Classes.Add("page-title-text");
        Grid.SetColumn(titleBlock, 1);
        header.Children.Add(iconBlock);
        header.Children.Add(titleBlock);
        content.Children.Add(header);

        foreach (Control control in controls)
        {
            content.Children.Add(control);
        }

        ScrollViewer page = new()
        {
            Content = content,
            IsVisible = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        SettingsSearch.SetPageId(page, id);
        return page;
    }

    private Border Card(string title, params Control[] controls)
    {
        StackPanel panel = new() { Spacing = 4 };
        if (!string.IsNullOrWhiteSpace(title))
        {
            TextBlock heading = new() { Text = title };
            heading.Classes.Add("section-title");
            panel.Children.Add(heading);
        }

        foreach (Control control in controls)
        {
            panel.Children.Add(control);
        }

        Border card = new() { Child = panel };
        card.Classes.Add("section-card");
        SettingsSearch.SetIsPanel(card, true);
        return card;
    }

    private Border EnabledCard(BoundValue<bool> enabled, string title, params Control[] controls)
    {
        Border card = Card(title, controls);
        BindEnabled(card, enabled);
        return card;
    }

    private Border OverrideCard(BoundValue<bool> value, string text)
    {
        Border card = new()
        {
            Child = Check(text, value)
        };
        card.Classes.Add("override-card");
        SettingsSearch.SetIsPanel(card, true);

        Border availability = new()
        {
            Child = card,
            IsVisible = !_isDefault
        };
        SettingsSearch.SetIsAvailabilityContainer(availability, true);
        return availability;
    }

    private BoundValue<bool> OverrideValue(Func<bool> getter, Action<bool> setter) =>
        _isDefault ? new BoundValue<bool>(true, _ => { }) : new BoundValue<bool>(getter(), setter);

    private static Grid Row(string label, Control editor)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("210,*"),
            ColumnSpacing = 8
        };
        row.Children.Add(Label(label));
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        return row;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Hint(string text)
    {
        TextBlock hint = new() { Text = text };
        hint.Classes.Add("hint");
        return hint;
    }

    private static CheckBox Check(string text, Func<bool> getter, Action<bool> setter) =>
        Check(text, new BoundValue<bool>(getter(), setter));

    private static CheckBox Check(string text, BoundValue<bool> value)
    {
        CheckBox checkBox = new() { Content = text, DataContext = value };
        checkBox.Classes.Add("setting");
        checkBox.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(BoundValue<bool>.Value))
        {
            Source = value,
            Mode = BindingMode.TwoWay
        });
        return checkBox;
    }

    private static TextBox Text(Func<string> getter, Action<string> setter) => Text(new BoundValue<string>(getter(), setter));

    private static TextBox Text(BoundValue<string> value)
    {
        TextBox textBox = new() { DataContext = value };
        textBox.Classes.Add("form-control");
        textBox.Bind(TextBox.TextProperty, new Binding(nameof(BoundValue<string>.Value))
        {
            Source = value,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        return textBox;
    }

    private TextBox NamePatternText(BoundValue<string> value, params CodeMenuEntryFilename[] ignoredEntries)
        => CodeMenuText(value, true, ignoredEntries);



    private TextBox CodeMenuText<T>(BoundValue<string> value, bool useCategories, params T[] ignoredEntries) where T : CodeMenuEntry
    {
        TextBox textBox = Text(value);
        HashSet<T> ignored = ignoredEntries.ToHashSet();
        List<T> entries = typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(T))
            .Select(field => field.GetValue(null))
            .OfType<T>()
            .Where(entry => !ignored.Contains(entry))
            .ToList();

        List<MenuItem> rootItems = [];

        MenuItem CreateItem(T entry)
        {
            string pattern = entry.ToPrefixString();
            MenuItem item = new()
            {
                Header = $"{pattern} - {entry.Description}",
                Focusable = false
            };
            item.Click += (_, _) => InsertText(textBox, pattern);
            return item;
        }

        if (useCategories)
        {
            foreach (IGrouping<string?, T> group in entries.GroupBy(entry => entry.Category))
            {
                List<MenuItem> items = group.Select(CreateItem).ToList();

                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    rootItems.AddRange(items);
                }
                else
                {
                    rootItems.Add(new MenuItem { Header = group.Key, ItemsSource = items, Focusable = false });
                }
            }
        }
        else
        {
            rootItems.AddRange(entries.Select(CreateItem));
        }

        ContextMenu menu = new()
        {
            Focusable = false,
            Placement = PlacementMode.RightEdgeAlignedTop,
            PlacementTarget = textBox,
            ItemsSource = rootItems
        };
        textBox.ContextMenu = menu;

        void OpenMenu()
        {
            if (_activeCodeMenu != null && !ReferenceEquals(_activeCodeMenu, menu))
            {
                _activeCodeMenu.Close();
            }

            if (!menu.IsOpen)
            {
                _activeCodeMenu = menu;
                menu.Open(textBox);
            }
        }

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeCodeMenu, menu))
            {
                _activeCodeMenu = null;
            }
        };
        textBox.GotFocus += (_, _) => OpenMenu();
        textBox.PointerReleased += (_, _) => OpenMenu();

        return textBox;
    }

    private static void InsertText(TextBox textBox, string textToInsert)
    {
        string text = textBox.Text ?? string.Empty;
        int start = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, text.Length);
        textBox.Text = text.Insert(start, textToInsert);
        textBox.CaretIndex = start + textToInsert.Length;
        textBox.Focus();
    }

    private static NumericUpDown Number(Func<decimal> getter, Action<decimal> setter, decimal minimum, decimal maximum, decimal increment = 1) =>
        Number(NumericValue(getter(), value => setter(value ?? 0)), minimum, maximum, increment);

    private static NumericUpDown Number(Func<int> getter, Action<decimal> setter, decimal minimum, decimal maximum, decimal increment = 1) =>
        Number(NumericValue(getter(), value => setter(value ?? 0)), minimum, maximum, increment);

    private static NumericUpDown Number(BoundValue<decimal?> value, decimal minimum, decimal maximum, decimal increment = 1)
    {
        NumericUpDown number = new()
        {
            DataContext = value,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment
        };
        number.Classes.Add("form-control");
        number.Bind(NumericUpDown.ValueProperty, new Binding(nameof(BoundValue<decimal?>.Value))
        {
            Source = value,
            Mode = BindingMode.TwoWay
        });
        return number;
    }

    private static BoundValue<decimal?> NumericValue(decimal initial, Action<decimal?> setter) => new(initial, setter);

    private static ComboBox EnumCombo<T>(Func<T> getter, Action<T> setter) where T : struct, Enum =>
        ObjectCombo(Enum.GetValues<T>(), getter, setter, value => ((Enum)(object)value).GetLocalizedDescription());

    private static Button TaskMenu(Func<HotkeyType> getter, Action<HotkeyType> setter)
    {
        TextBlock selectedIcon = CreateTaskMenuIcon(getter());
        TextBlock selectedTitle = new()
        {
            FontWeight = FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock chevron = new()
        {
            Text = LucideIcons.chevron_down,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        chevron.Classes.Add("icon");

        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 7
        };
        Grid.SetColumn(selectedTitle, 1);
        Grid.SetColumn(chevron, 2);
        content.Children.Add(selectedIcon);
        content.Children.Add(selectedTitle);
        content.Children.Add(chevron);

        Button button = new()
        {
            Content = content,
            MinWidth = 250,
            MinHeight = 32,
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        void UpdateSelectedTask(HotkeyType task)
        {
            selectedIcon.Text = TaskHelpers.FindMenuLucideIcon(task);
            selectedTitle.Text = task.GetLocalizedDescription();
        }

        void SelectTask(HotkeyType task)
        {
            setter(task);
            UpdateSelectedTask(task);
        }

        UpdateSelectedTask(getter());

        button.Click += (_, _) =>
        {
            List<HotkeyType> tasks = Helpers.GetEnums<HotkeyType>().ToList();
            List<MenuItem> rootItems = [CreateTaskMenuItem(HotkeyType.None, getter(), SelectTask)];

            foreach (IGrouping<string, HotkeyType> category in tasks
                .Where(task => task != HotkeyType.None)
                .Select(task => (Task: task, Category: new EnumInfo(task).Category ?? string.Empty))
                .Where(item => !string.IsNullOrWhiteSpace(item.Category))
                .GroupBy(item => item.Category, item => item.Task))
            {
                rootItems.Add(new MenuItem
                {
                    Header = category.Key,
                    ItemsSource = category.Select(task => CreateTaskMenuItem(task, getter(), SelectTask)).ToList()
                });
            }

            ContextMenu menu = new()
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                PlacementTarget = button,
                ItemsSource = rootItems
            };
            menu.Open(button);
        };

        return button;
    }

    private static Button TaskFlagsMenu<T>(
        Func<T> getter,
        Action<T> setter,
        IReadOnlyList<(T Task, string Header, string Icon)> options,
        string emptyIcon) where T : struct, Enum
    {
        IReadOnlyDictionary<T, (string Header, string Icon)> taskOptions = options
            .ToDictionary(option => option.Task, option => (option.Header, option.Icon));
        TextBlock selectedIcon = CreateAccentMenuIcon(emptyIcon);
        T lastSelectedTask = taskOptions.Keys.LastOrDefault(task => HasFlag(getter(), task));
        TextBlock selectedTasks = new()
        {
            FontWeight = FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock chevron = new()
        {
            Text = LucideIcons.chevron_down,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        chevron.Classes.Add("icon");

        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 7
        };
        Grid.SetColumn(selectedTasks, 1);
        Grid.SetColumn(chevron, 2);
        content.Children.Add(selectedIcon);
        content.Children.Add(selectedTasks);
        content.Children.Add(chevron);

        Button button = new()
        {
            Content = content,
            MinWidth = 300,
            MinHeight = 32,
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        void UpdateSelectedTasks()
        {
            T selected = getter();
            if (!EqualityComparer<T>.Default.Equals(lastSelectedTask, default) && !HasFlag(selected, lastSelectedTask))
            {
                lastSelectedTask = taskOptions.Keys.LastOrDefault(task => HasFlag(selected, task));
            }

            selectedIcon.Text = EqualityComparer<T>.Default.Equals(lastSelectedTask, default)
                ? emptyIcon
                : taskOptions[lastSelectedTask].Icon;

            string[] titles = taskOptions
                .Where(option => HasFlag(selected, option.Key))
                .Select(option => option.Value.Header)
                .ToArray();
            selectedTasks.Text = titles.Length > 0
                ? string.Join(", ", titles)
                : ((Enum)(object)default(T)).GetLocalizedDescription();
        }

        UpdateSelectedTasks();

        button.Click += (_, _) =>
        {
            T selected = getter();
            List<MenuItem> items = [];

            foreach ((T task, (string header, string icon)) in taskOptions)
            {
                MenuItem item = new()
                {
                    Header = header,
                    Icon = CreateAccentMenuIcon(icon),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = HasFlag(selected, task)
                };
                item.Classes.Add("compact-menu-item");
                item.Click += (_, _) =>
                {
                    T updated = ToggleFlag(getter(), task);
                    setter(updated);
                    if (HasFlag(updated, task))
                    {
                        lastSelectedTask = task;
                    }
                    UpdateSelectedTasks();
                };
                items.Add(item);
            }

            ContextMenu menu = new()
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                PlacementTarget = button,
                ItemsSource = items
            };
            menu.Open(button);
        };

        return button;
    }

    private static Button DestinationMenu(TaskSettings settings)
    {
        TextBlock icon = CreateAccentMenuIcon(LucideIcons.server);
        TextBlock title = new()
        {
            Text = Strings.TaskSettingsWindow_Destinations + "...",
            FontWeight = FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock chevron = new()
        {
            Text = LucideIcons.chevron_down,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        chevron.Classes.Add("icon");

        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 7
        };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(chevron, 2);
        content.Children.Add(icon);
        content.Children.Add(title);
        content.Children.Add(chevron);

        Button button = new()
        {
            Content = content,
            MinWidth = 300,
            MinHeight = 32,
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        button.Click += (_, _) =>
        {
            ContextMenu menu = new()
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                PlacementTarget = button,
                ItemsSource = MainMenuBuilder.BuildDestinationsMenu(settings)
                    .Select(CreateDestinationMenuItem)
                    .ToList()
            };
            menu.Open(button);
        };

        return button;
    }

    private static MenuItem CreateDestinationMenuItem(MainMenuEntry entry)
    {
        MenuItem item = new()
        {
            Header = entry.Header,
            IsChecked = entry.IsChecked,
            ToggleType = entry.ToggleType switch
            {
                MainMenuToggleType.CheckBox => MenuItemToggleType.CheckBox,
                MainMenuToggleType.Radio => MenuItemToggleType.Radio,
                _ => MenuItemToggleType.None
            }
        };
        item.Classes.Add("compact-menu-item");

        if (!string.IsNullOrEmpty(entry.Icon))
        {
            item.Icon = CreateAccentMenuIcon(entry.Icon);
        }

        if (entry.CreateChildren != null)
        {
            item.ItemsSource = entry.CreateChildren().Select(CreateDestinationMenuItem).ToList();
        }

        if (entry.ExecuteAsync != null)
        {
            item.Click += async (_, _) => await entry.ExecuteAsync();
        }

        return item;
    }

    private static bool HasFlag<T>(T value, T flag) where T : struct, Enum =>
        (Convert.ToUInt64(value) & Convert.ToUInt64(flag)) == Convert.ToUInt64(flag);

    private static T ToggleFlag<T>(T value, T flag) where T : struct, Enum =>
        (T)Enum.ToObject(typeof(T), Convert.ToUInt64(value) ^ Convert.ToUInt64(flag));

    private static MenuItem CreateTaskMenuItem(HotkeyType task, HotkeyType selectedTask, Action<HotkeyType> selectTask)
    {
        MenuItem item = new()
        {
            Header = task.GetLocalizedDescription(),
            Icon = CreateTaskMenuIcon(task),
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = task == selectedTask
        };
        item.Click += (_, _) => selectTask(task);
        return item;
    }

    private static TextBlock CreateTaskMenuIcon(HotkeyType task)
        => CreateAccentMenuIcon(TaskHelpers.FindMenuLucideIcon(task));

    private static TextBlock CreateAccentMenuIcon(string glyph)
    {
        TextBlock icon = new()
        {
            Text = glyph,
            FontSize = 16,
            Width = 18,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.Classes.Add("icon");
        icon.Classes.Add("accent-menu-icon");
        return icon;
    }

    private static ComboBox ObjectCombo<T>(IEnumerable<T> values, Func<T> getter, Action<T> setter, Func<T, string>? title = null)
    {
        ChoiceOption<T>[] options = values.Select(value => new ChoiceOption<T>(value, title?.Invoke(value) ?? value?.ToString() ?? string.Empty)).ToArray();
        T current = getter();
        ChoiceOption<T> selected = options.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, current)) ?? options.First();
        BoundValue<ChoiceOption<T>> binding = new(selected, value => setter(value.Value));
        ComboBox comboBox = new()
        {
            ItemsSource = options,
            DataContext = binding
        };
        comboBox.Classes.Add("form-control");
        comboBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(BoundValue<ChoiceOption<T>>.Value))
        {
            Source = binding,
            Mode = BindingMode.TwoWay
        });
        return comboBox;
    }



    private static Grid InputWithButton(Control input, Button button)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6
        };
        grid.Children.Add(input);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private static StackPanel ButtonRow(params Button[] buttons)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (Button button in buttons)
        {
            row.Children.Add(button);
        }
        return row;
    }

    private static Button Button(string text, Action action)
    {
        Button button = new() { Content = text };
        button.Classes.Add("compact");
        button.Click += (_, _) => action();
        return button;
    }

    private static Button Button(string text, Func<Task> action)
    {
        Button button = new() { Content = text };
        button.Classes.Add("compact");
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void BindEnabled(Control control, BoundValue<bool> value, bool invert = false)
    {
        control.Bind(Control.IsEnabledProperty, new Binding(nameof(BoundValue<bool>.Value))
        {
            Source = value,
            Converter = invert ? InverseBooleanConverter.Instance : null
        });
    }

    private static void BindVisible(Control control, BoundValue<bool> value, bool invert = false)
    {
        control.Bind(Visual.IsVisibleProperty, new Binding(nameof(BoundValue<bool>.Value))
        {
            Source = value,
            Converter = invert ? InverseBooleanConverter.Instance : null
        });
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        if (_host.StorageProvider == null) return null;
        IReadOnlyList<IStorageFolder> folders = await _host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickFileAsync(string title, string typeName, string pattern)
    {
        if (_host.StorageProvider == null) return null;
        IReadOnlyList<IStorageFile> files = await _host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = [pattern] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private static List<ChoiceOption<string>>? _availableSoundOptions;

    private static List<ChoiceOption<string>> GetAvailableSoundOptions()
    {
        if (_availableSoundOptions != null) return _availableSoundOptions;

        List<ChoiceOption<string>> list =
        [
            new ChoiceOption<string>("", "Default"),
            new ChoiceOption<string>("builtin:capture", "Blink - Capture"),
            new ChoiceOption<string>("builtin:task_completed", "Blink - Task completed"),
            new ChoiceOption<string>("builtin:action_completed", "Blink - Action completed"),
            new ChoiceOption<string>("builtin:error", "Blink - Error")
        ];

        try
        {
            string windowsMedia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
            if (Directory.Exists(windowsMedia))
            {
                DirectoryInfo di = new(windowsMedia);
                foreach (FileInfo fi in di.EnumerateFiles("*.wav").OrderBy(f => f.Name))
                {
                    string displayName = Path.GetFileNameWithoutExtension(fi.Name);
                    list.Add(new ChoiceOption<string>(fi.FullName, "Windows: " + displayName));
                }
            }
        }
        catch
        {
        }

        _availableSoundOptions = list;
        return list;
    }

    private Control BuildSoundOptionRow(
        string checkLabel,
        Func<bool> checkGetter,
        Action<bool> checkSetter,
        Func<bool> useCustomGetter,
        Action<bool> useCustomSetter,
        Func<string> pathGetter,
        Action<string> pathSetter,
        Func<Stream> defaultSoundGetter)
    {
        CheckBox checkBox = Check(checkLabel, checkGetter, checkSetter);
        checkBox.VerticalAlignment = VerticalAlignment.Center;

        List<ChoiceOption<string>> options = GetAvailableSoundOptions().ToList();
        string currentPath = useCustomGetter() ? (pathGetter() ?? "") : "";
        if (!string.IsNullOrEmpty(currentPath) && !options.Any(o => string.Equals(o.Value, currentPath, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new ChoiceOption<string>(currentPath, Path.GetFileName(currentPath)));
        }
        options.Add(new ChoiceOption<string>("__browse__", Strings.TaskSettingsWindow_BrowseWithEllipsis));

        ChoiceOption<string> initialSelected = options.FirstOrDefault(o => string.Equals(o.Value, currentPath, StringComparison.OrdinalIgnoreCase)) ?? options[0];

        ComboBox comboBox = new()
        {
            ItemsSource = options,
            SelectedItem = initialSelected,
            MinWidth = 200,
            MaxWidth = 260,
            VerticalAlignment = VerticalAlignment.Center
        };
        comboBox.Classes.Add("form-control");

        bool updatingSelection = false;
        comboBox.SelectionChanged += async (_, _) =>
        {
            if (updatingSelection || comboBox.SelectedItem is not ChoiceOption<string> selected) return;

            if (selected.Value == "__browse__")
            {
                updatingSelection = true;
                string? pickedFile = await PickFileAsync("Select Sound File", "Wave Audio (*.wav)", "*.wav");
                if (!string.IsNullOrEmpty(pickedFile) && File.Exists(pickedFile))
                {
                    useCustomSetter(true);
                    pathSetter(pickedFile);
                    ChoiceOption<string> customItem = new(pickedFile, Path.GetFileName(pickedFile));
                    options.Insert(options.Count - 1, customItem);
                    comboBox.ItemsSource = null;
                    comboBox.ItemsSource = options;
                    comboBox.SelectedItem = customItem;
                }
                else
                {
                    string prevKey = useCustomGetter() ? (pathGetter() ?? "") : "";
                    comboBox.SelectedItem = options.FirstOrDefault(o => string.Equals(o.Value, prevKey, StringComparison.OrdinalIgnoreCase)) ?? options[0];
                }
                updatingSelection = false;
                return;
            }

            if (string.IsNullOrEmpty(selected.Value))
            {
                useCustomSetter(false);
                pathSetter("");
            }
            else
            {
                useCustomSetter(true);
                pathSetter(selected.Value);
            }
        };

        TextBlock icon = new()
        {
            Text = LucideIcons.volume_2,
            FontSize = 16,
            Classes = { "icon" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button previewBtn = new()
        {
            Content = icon,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        previewBtn.Classes.Add("compact");
        ToolTip.SetTip(previewBtn, "Play sound");

        previewBtn.Click += (_, _) =>
        {
            ChoiceOption<string>? sel = comboBox.SelectedItem as ChoiceOption<string>;
            string key = sel != null && sel.Value != "__browse__" ? sel.Value : (useCustomGetter() ? (pathGetter() ?? "") : "");
            TaskHelpers.PlaySound(key, defaultSoundGetter());
        };

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 2)
        };
        Grid.SetColumn(checkBox, 0);
        Grid.SetColumn(comboBox, 1);
        Grid.SetColumn(previewBtn, 2);

        row.Children.Add(checkBox);
        row.Children.Add(comboBox);
        row.Children.Add(previewBtn);

        return row;
    }
}

internal sealed class BoundValue<T> : INotifyPropertyChanged
{
    private T _value;
    private readonly Action<T> _setter;

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
            {
                return;
            }

            _value = value;
            _setter(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BoundValue(T value, Action<T> setter)
    {
        _value = value;
        _setter = setter;
    }
}

internal sealed record ChoiceOption<T>(T Value, string Title)
{
    public override string ToString() => Title;
}

internal sealed class InverseBooleanConverter : Avalonia.Data.Converters.IValueConverter
{
    public static InverseBooleanConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is bool boolean && !boolean;
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is bool boolean && !boolean;
}
