#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia.Platform.Storage;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;

namespace ShareX;

public interface ITaskSettingsHost
{
    string? Title { get; }
    IStorageProvider? StorageProvider { get; }
    void ShowNotificationButtonsEditor(List<NotificationActionButton> buttons, Action<List<NotificationActionButton>> onSave);
    void ShowActionEditor(ExternalProgram? action, Action<ExternalProgram> onSave);
}
