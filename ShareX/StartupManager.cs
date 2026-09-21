#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Microsoft.Win32;
using ShareX.HelpersLib;
using System;
using System.Windows.Forms;

#if MicrosoftStore
using Windows.ApplicationModel;
#endif

namespace ShareX
{
    public static class StartupManager
    {
#if MicrosoftStore
        private const int StartupTargetIndex = 0;
        private static readonly StartupTask packageTask = StartupTask.GetForCurrentPackageAsync().GetAwaiter().GetResult()[StartupTargetIndex];
#endif

        public static string StartupTargetPath
        {
            get
            {
                string rootLauncher = FileHelpers.GetAbsolutePath("../Blink.exe");
                if (System.IO.File.Exists(rootLauncher))
                {
                    return rootLauncher;
                }
                return Application.ExecutablePath;
            }
        }

        private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
        private static readonly byte[] EnabledRegistryBytes = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        public static void EnsureStartup()
        {
#if !MicrosoftStore
            try
            {
                ShortcutHelpers.SetShortcut(false, Environment.SpecialFolder.Startup, "ShareX");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, true))
                {
                    key?.DeleteValue("ShareX.lnk", false);
                }

                if (State != StartupState.DisabledByUser)
                {
                    ShortcutHelpers.SetShortcut(true, Environment.SpecialFolder.Startup, "Blink", StartupTargetPath, "-silent");
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupApprovedKey))
                    {
                        key?.SetValue("Blink.lnk", EnabledRegistryBytes, RegistryValueKind.Binary);
                    }
                }
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
            }
#endif
        }

        public static StartupState State
        {
            get
            {
#if MicrosoftStore
                return (StartupState)packageTask.State;
#else
                if (ShortcutHelpers.CheckShortcut(Environment.SpecialFolder.Startup, "Blink", StartupTargetPath))
                {
                    if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
                        "Blink.lnk", null) is byte[] status && status.Length > 0 && status[0] == 3)
                    {
                        return StartupState.DisabledByUser;
                    }
                    else
                    {
                        return StartupState.Enabled;
                    }
                }
                else
                {
                    return StartupState.Disabled;
                }
#endif
            }
            set
            {
#if MicrosoftStore
                if (value == StartupState.Enabled)
                {
                    packageTask.RequestEnableAsync().GetAwaiter().GetResult();
                }
                else if (value == StartupState.Disabled)
                {
                    packageTask.Disable();
                }
                else
                {
                    throw new NotSupportedException();
                }
#else
                if (value == StartupState.Enabled)
                {
                    ShortcutHelpers.SetShortcut(true, Environment.SpecialFolder.Startup, "Blink", StartupTargetPath, "-silent");
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupApprovedKey))
                        {
                            key?.SetValue("Blink.lnk", EnabledRegistryBytes, RegistryValueKind.Binary);
                        }
                    }
                    catch (Exception e)
                    {
                        DebugHelper.WriteException(e);
                    }
                }
                else if (value == StartupState.Disabled)
                {
                    ShortcutHelpers.SetShortcut(false, Environment.SpecialFolder.Startup, "Blink", StartupTargetPath, "-silent");
                }
                else
                {
                    throw new NotSupportedException();
                }
#endif
            }
        }
    }
}