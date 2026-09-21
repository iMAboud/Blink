#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia.Threading;

namespace ShareX;

public static class ApplicationSettingsIntegration
{
    public static void Show() => SettingsIntegration.Show(SettingsTab.Application);
}
