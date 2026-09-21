#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia.Threading;

namespace ShareX;

public enum SettingsTab
{
    Application,
    Hotkeys,
    Tasks
}

public static class SettingsIntegration
{
    private static SettingsWindow? _window;
    public static bool IsVisible => _window?.IsVisible == true;

    public static void Show(SettingsTab tab = SettingsTab.Application)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _window.SelectTab(tab);
                if (!_window.IsVisible)
                {
                    _window.Show();
                }
                _window.Activate();
                return;
            }

            _window = new SettingsWindow();
            _window.SelectTab(tab);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _window.Activate();
        });
    }
}
