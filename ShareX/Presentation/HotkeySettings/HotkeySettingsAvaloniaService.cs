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

using Avalonia.Threading;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ShareX;

internal sealed class HotkeySettingsAvaloniaService : IHotkeySettingsService
{
    private readonly HotkeyManager _manager;
    private readonly Action _closed;
    private readonly IReadOnlyList<HotkeyTaskOption> _taskOptions;
    private bool _disposed;

    public event EventHandler StateChanged;

    public bool AreHotkeysDisabled => InvokeOnMainThread(() => Program.Settings.DisableHotkeys);

    public HotkeySettingsAvaloniaService(HotkeyManager manager, Action closed)
    {
        _manager = manager;
        _closed = closed;
        HashSet<HotkeyType> excludedTypes =
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
            HotkeyType.OpenImageHistory,
            HotkeyType.ToggleActionsToolbar,
            HotkeyType.ToggleTrayMenu,
            HotkeyType.ExitBlink
        ];

        _taskOptions = Helpers.GetEnums<HotkeyType>()
            .Where(value => !excludedTypes.Contains(value))
            .Select(value => new EnumInfo(value))
            .Select(info => new HotkeyTaskOption(
                Convert.ToInt32(info.Value),
                info.Description,
                info.Category ?? string.Empty,
                TaskHelpers.FindMenuLucideIcon((HotkeyType)info.Value)))
            .ToArray();

        InvokeOnMainThread(() =>
        {
            _manager.HotkeysToggledTrigger += OnHotkeysToggled;
            _manager.RegisterFailedHotkeys();
        });
    }

    public IReadOnlyList<HotkeySettingsItem> GetItems()
    {
        HotkeySettings[] settings = InvokeOnMainThread(() => _manager.Hotkeys.ToArray());
        return settings.Select(CreateItem).ToArray();
    }

    private void PersistHotkeys()
    {
        SettingManager.SaveHotkeysConfigAsync();
        MainWindowIntegration.RefreshMenus();
    }

    public HotkeySettingsItem Add()
    {
        HotkeySettings settings = InvokeOnMainThread(() =>
        {
            HotkeySettings newSettings = new()
            {
                TaskSettings = TaskSettings.GetDefaultTaskSettings()
            };
            _manager.Hotkeys.Add(newSettings);
            PersistHotkeys();
            return newSettings;
        });

        return CreateItem(settings);
    }

    public HotkeySettingsItem Duplicate(HotkeySettingsItem item)
    {
        HotkeySettings source = GetSettings(item);
        HotkeySettings duplicate = InvokeOnMainThread(() =>
        {
            HotkeySettings newSettings = new()
            {
                TaskSettings = source.TaskSettings.Copy()
            };
            newSettings.TaskSettings.WatchFolderEnabled = false;
            newSettings.TaskSettings.WatchFolderList = [];
            _manager.Hotkeys.Add(newSettings);
            PersistHotkeys();
            return newSettings;
        });

        return CreateItem(duplicate);
    }

    public void Remove(HotkeySettingsItem item)
    {
        HotkeySettings settings = GetSettings(item);
        InvokeOnMainThread(() =>
        {
            _manager.UnregisterHotkey(settings, true);
            PersistHotkeys();
        });
    }

    public void Move(HotkeySettingsItem item, int offset)
    {
        HotkeySettings settings = GetSettings(item);
        InvokeOnMainThread(() =>
        {
            int oldIndex = _manager.Hotkeys.IndexOf(settings);
            if (oldIndex < 0 || _manager.Hotkeys.Count < 2)
            {
                return;
            }

            int newIndex = (oldIndex + offset + _manager.Hotkeys.Count) % _manager.Hotkeys.Count;
            _manager.Hotkeys.RemoveAt(oldIndex);
            _manager.Hotkeys.Insert(newIndex, settings);
            PersistHotkeys();
        });
    }

    public void SetTask(HotkeySettingsItem item, int taskValue)
    {
        HotkeySettings settings = GetSettings(item);
        InvokeOnMainThread(() =>
        {
            settings.TaskSettings.Job = (HotkeyType)taskValue;
            PersistHotkeys();
        });
    }

    public void EditTask(HotkeySettingsItem item)
    {
        HotkeySettings settings = GetSettings(item);
        InvokeOnMainThread(() =>
        {
            TaskSettingsIntegration.Show(settings.TaskSettings, false, () =>
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                SettingManager.SaveHotkeysConfigAsync();
            });
        });
    }

    public bool SetHotkey(HotkeySettingsItem item, HotkeyGesture gesture, bool finalize)
    {
        HotkeySettings settings = GetSettings(item);
        return InvokeOnMainThread(() =>
        {
            settings.HotkeyInfo.Hotkey = ConvertGesture(gesture);
            settings.HotkeyInfo.Win = gesture.Win;

            if (finalize && settings.HotkeyInfo.IsOnlyModifiers)
            {
                settings.HotkeyInfo.Hotkey = Keys.None;
                settings.HotkeyInfo.Win = false;
            }

            RegisterAndRetry(settings);
            if (finalize)
            {
                PersistHotkeys();
            }
            return settings.HotkeyInfo.IsValidHotkey;
        });
    }

    public void FinalizeHotkey(HotkeySettingsItem item)
    {
        HotkeySettings settings = GetSettings(item);
        InvokeOnMainThread(() =>
        {
            if (settings.HotkeyInfo.IsOnlyModifiers)
            {
                settings.HotkeyInfo.Hotkey = Keys.None;
                settings.HotkeyInfo.Win = false;
            }

            RegisterAndRetry(settings);
            PersistHotkeys();
        });
    }

    public void SetCaptureMode(bool isCapturing)
    {
        InvokeOnMainThread(() => _manager.IgnoreHotkeys = isCapturing);
    }

    public void Refresh(HotkeySettingsItem item)
    {
        HotkeySettings settings = GetSettings(item);
        HotkeyItemSnapshot snapshot = InvokeOnMainThread(() => new HotkeyItemSnapshot(
            Convert.ToInt32(settings.TaskSettings.Job),
            settings.HotkeyInfo.ToString(),
            settings.HotkeyInfo.Status switch
            {
                HotkeyStatus.Registered => HotkeyRegistrationState.Registered,
                HotkeyStatus.Failed => HotkeyRegistrationState.Failed,
                _ => HotkeyRegistrationState.NotConfigured
            },
            !settings.TaskSettings.IsUsingDefaultSettings));

        item.SelectedTask = _taskOptions.FirstOrDefault(x => x.Value == snapshot.TaskValue);
        item.HotkeyText = snapshot.HotkeyText;
        item.RegistrationState = snapshot.State;
        item.IsCustomized = snapshot.IsCustomized;
    }

    public void Reset()
    {
        InvokeOnMainThread(() =>
        {
            _manager.ResetHotkeys();
            PersistHotkeys();
        });
    }

    public void EnableHotkeys()
    {
        InvokeOnMainThread(() => TaskHelpers.ToggleHotkeys(false));
    }

    private HotkeySettingsItem CreateItem(HotkeySettings settings)
    {
        HotkeySettingsItem item = new(settings, _taskOptions);
        Refresh(item);
        return item;
    }

    private void RegisterAndRetry(HotkeySettings settings)
    {
        _manager.RegisterHotkey(settings);
        _manager.RegisterFailedHotkeys();
    }

    private static HotkeySettings GetSettings(HotkeySettingsItem item)
    {
        return (HotkeySettings)item.Source;
    }

    private static Keys ConvertGesture(HotkeyGesture gesture)
    {
        Keys key = gesture.KeyName switch
        {
            nameof(Avalonia.Input.Key.LeftCtrl) => Keys.ControlKey,
            nameof(Avalonia.Input.Key.RightCtrl) => Keys.ControlKey,
            nameof(Avalonia.Input.Key.LeftShift) => Keys.ShiftKey,
            nameof(Avalonia.Input.Key.RightShift) => Keys.ShiftKey,
            nameof(Avalonia.Input.Key.LeftAlt) => Keys.Menu,
            nameof(Avalonia.Input.Key.RightAlt) => Keys.Menu,
            nameof(Avalonia.Input.Key.Enter) => Keys.Enter,
            nameof(Avalonia.Input.Key.CapsLock) => Keys.CapsLock,
            nameof(Avalonia.Input.Key.PageUp) => Keys.PageUp,
            nameof(Avalonia.Input.Key.PageDown) => Keys.PageDown,
            nameof(Avalonia.Input.Key.PrintScreen) => Keys.PrintScreen,
            _ when Enum.TryParse(gesture.KeyName, true, out Keys parsed) => parsed,
            _ => Keys.None
        };

        if (gesture.Control)
        {
            key |= Keys.Control;
        }
        if (gesture.Shift)
        {
            key |= Keys.Shift;
        }
        if (gesture.Alt)
        {
            key |= Keys.Alt;
        }

        return key;
    }

    private void OnHotkeysToggled(bool hotkeysDisabled)
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeOnMainThread(Action action)
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

    private T InvokeOnMainThread<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        return Dispatcher.UIThread.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        InvokeOnMainThread(() =>
        {
            _manager.IgnoreHotkeys = false;
            _manager.HotkeysToggledTrigger -= OnHotkeysToggled;
            _closed();
        });
    }

    private sealed record HotkeyItemSnapshot(
        int TaskValue,
        string HotkeyText,
        HotkeyRegistrationState State,
        bool IsCustomized);
}
