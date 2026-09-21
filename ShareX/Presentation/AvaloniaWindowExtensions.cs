#nullable enable

using Avalonia.Controls;
using ShareX.HelpersLib;
using System;

namespace ShareX;

public static class AvaloniaWindowExtensions
{
    public static void ApplyDarkTitleBar(this Window window, int captionColorRef = 0x00222222)
    {
        void Apply()
        {
            IntPtr handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                NativeMethods.ApplyDarkTitleBar(handle, captionColorRef);
            }
        }

        window.Opened += (_, _) => Apply();
        Apply();
    }
}
