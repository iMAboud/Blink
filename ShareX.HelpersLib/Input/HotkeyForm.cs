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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX.HelpersLib
{
    public class HotkeyForm : Form
    {
        public delegate void HotkeyEventHandler(ushort id, Keys key, Modifiers modifier);

        public event HotkeyEventHandler HotkeyPress;
        public event Action<Keys, Modifiers> CapturingKeyPressed;

        public int HotkeyRepeatLimit { get; set; }
        public bool IgnoreHotkeys { get; set; }

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int LLKHF_ALTDOWN = 0x20;
        private const int WM_SETTINGCHANGE = 0x001A;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        private Stopwatch repeatLimitTimer;
        private readonly List<HotkeyInfo> registeredHotkeys = new List<HotkeyInfo>();
        private HookProc keyboardHookProc;
        private IntPtr keyboardHookHandle = IntPtr.Zero;
        private WinEventDelegate winEventProc;
        private IntPtr winEventHookHandle = IntPtr.Zero;
        private bool printScreenHandledOnDown;
        private bool printScreenCapturedOnDown;
        private ushort nextFallbackId = 0xB000;

        public HotkeyForm()
        {
            HotkeyRepeatLimit = 1000;
            repeatLimitTimer = Stopwatch.StartNew();

            DisableWindowsPrintScreenSnipping();
            InstallHooks();
        }

        public static void DisableWindowsPrintScreenSnipping()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Keyboard"))
                {
                    if (key != null)
                    {
                        object current = key.GetValue("PrintScreenKeyForSnippingEnabled");
                        if (current == null || !(current is int val) || val != 0)
                        {
                            key.SetValue("PrintScreenKeyForSnippingEnabled", 0, RegistryValueKind.DWord);
                            NativeMethods.SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero,
                                "Control Panel\\Keyboard", SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 200, out _);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
            }
        }

        private void InstallHooks()
        {
            try
            {
                keyboardHookProc = KeyboardHookCallback;
                ReinstallKeyboardHook();

                winEventProc = WinEventCallback;
                winEventHookHandle = NativeMethods.SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    winEventProc,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT);
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
            }
        }

        private void ReinstallKeyboardHook()
        {
            try
            {
                if (keyboardHookHandle != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(keyboardHookHandle);
                    keyboardHookHandle = IntPtr.Zero;
                }

                using (Process currentProcess = Process.GetCurrentProcess())
                using (ProcessModule currentModule = currentProcess.MainModule)
                {
                    IntPtr moduleHandle = NativeMethods.GetModuleHandle(currentModule.ModuleName);
                    keyboardHookHandle = NativeMethods.SetWindowsHookEx(NativeConstants.WH_KEYBOARD_LL, keyboardHookProc, moduleHandle, 0);
                }
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
            }
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND && registeredHotkeys.Count > 0)
            {
                ReinstallKeyboardHook();
            }
        }

        public void RegisterHotkey(HotkeyInfo hotkeyInfo)
        {
            if (hotkeyInfo != null && hotkeyInfo.Status != HotkeyStatus.Registered)
            {
                if (!hotkeyInfo.IsValidHotkey)
                {
                    hotkeyInfo.Status = HotkeyStatus.NotConfigured;
                    return;
                }

                if (hotkeyInfo.KeyCode == Keys.PrintScreen)
                {
                    DisableWindowsPrintScreenSnipping();
                }

                if (hotkeyInfo.ID == 0)
                {
                    string uniqueID = Helpers.GetUniqueID();
                    hotkeyInfo.ID = NativeMethods.GlobalAddAtom(uniqueID);

                    if (hotkeyInfo.ID == 0)
                    {
                        hotkeyInfo.ID = nextFallbackId++;
                    }
                }

                bool nativeRegistered = NativeMethods.RegisterHotKey(Handle, hotkeyInfo.ID, (uint)hotkeyInfo.ModifiersEnum, (uint)hotkeyInfo.KeyCode);

                if (!nativeRegistered && keyboardHookHandle == IntPtr.Zero)
                {
                    ReinstallKeyboardHook();
                }

                if (!nativeRegistered && keyboardHookHandle == IntPtr.Zero)
                {
                    NativeMethods.GlobalDeleteAtom(hotkeyInfo.ID);
                    DebugHelper.WriteLine("Unable to register hotkey: " + hotkeyInfo);
                    hotkeyInfo.ID = 0;
                    hotkeyInfo.Status = HotkeyStatus.Failed;
                    return;
                }

                if (!nativeRegistered)
                {
                    DebugHelper.WriteLine("Native RegisterHotKey conflict, using low-level hook for: " + hotkeyInfo);
                }

                if (!registeredHotkeys.Contains(hotkeyInfo))
                {
                    registeredHotkeys.Add(hotkeyInfo);
                }

                hotkeyInfo.Status = HotkeyStatus.Registered;
            }
        }

        public bool UnregisterHotkey(HotkeyInfo hotkeyInfo)
        {
            if (hotkeyInfo != null)
            {
                registeredHotkeys.Remove(hotkeyInfo);

                if (hotkeyInfo.ID > 0)
                {
                    NativeMethods.UnregisterHotKey(Handle, hotkeyInfo.ID);
                    NativeMethods.GlobalDeleteAtom(hotkeyInfo.ID);
                    hotkeyInfo.ID = 0;
                    hotkeyInfo.Status = HotkeyStatus.NotConfigured;
                    return true;
                }

                hotkeyInfo.Status = HotkeyStatus.NotConfigured;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KeyEvent msg = (KeyEvent)(int)wParam;
                bool isKeyDown = msg == KeyEvent.WM_KEYDOWN || msg == KeyEvent.WM_SYSKEYDOWN;
                bool isKeyUp = msg == KeyEvent.WM_KEYUP || msg == KeyEvent.WM_SYSKEYUP;

                if (isKeyDown || isKeyUp)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    int flags = Marshal.ReadInt32(lParam, 8);
                    Keys keyCode = (Keys)vkCode;

                    if (IgnoreHotkeys)
                    {
                        if (keyCode == Keys.PrintScreen)
                        {
                            Modifiers mods = GetCurrentModifiers(flags);
                            if (isKeyDown)
                            {
                                printScreenCapturedOnDown = true;
                                TriggerCapturingKey(keyCode, mods);
                            }
                            else if (printScreenCapturedOnDown)
                            {
                                printScreenCapturedOnDown = false;
                            }
                            else
                            {
                                TriggerCapturingKey(keyCode, mods);
                            }

                            return (IntPtr)1;
                        }
                    }
                    else if (registeredHotkeys.Count > 0)
                    {
                        Modifiers currentModifiers = GetCurrentModifiers(flags);
                        HotkeyInfo matched = FindMatchingHotkey(keyCode, currentModifiers);

                        if (isKeyDown && matched != null)
                        {
                            if (keyCode == Keys.PrintScreen)
                            {
                                printScreenHandledOnDown = true;
                            }

                            if (CheckRepeatLimitTime())
                            {
                                TriggerHotkey(matched.ID, matched.KeyCode, matched.ModifiersEnum);
                            }

                            return (IntPtr)1;
                        }
                        else if (isKeyUp && keyCode == Keys.PrintScreen)
                        {
                            if (printScreenHandledOnDown)
                            {
                                printScreenHandledOnDown = false;
                                return (IntPtr)1;
                            }

                            if (matched != null)
                            {
                                if (CheckRepeatLimitTime())
                                {
                                    TriggerHotkey(matched.ID, matched.KeyCode, matched.ModifiersEnum);
                                }

                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }

            return NativeMethods.CallNextHookEx(keyboardHookHandle, nCode, wParam, lParam);
        }

        private HotkeyInfo FindMatchingHotkey(Keys keyCode, Modifiers modifiers)
        {
            for (int i = 0; i < registeredHotkeys.Count; i++)
            {
                HotkeyInfo info = registeredHotkeys[i];
                if (info.Status == HotkeyStatus.Registered && info.KeyCode == keyCode && info.ModifiersEnum == modifiers)
                {
                    return info;
                }
            }

            return null;
        }

        private static Modifiers GetCurrentModifiers(int hookFlags)
        {
            Modifiers modifiers = Modifiers.None;

            if ((hookFlags & LLKHF_ALTDOWN) != 0 || IsKeyDown(Keys.Menu) || IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu))
            {
                modifiers |= Modifiers.Alt;
            }

            if (IsKeyDown(Keys.ControlKey) || IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey))
            {
                modifiers |= Modifiers.Control;
            }

            if (IsKeyDown(Keys.ShiftKey) || IsKeyDown(Keys.LShiftKey) || IsKeyDown(Keys.RShiftKey))
            {
                modifiers |= Modifiers.Shift;
            }

            if (IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin))
            {
                modifiers |= Modifiers.Win;
            }

            return modifiers;
        }

        private static bool IsKeyDown(Keys key)
        {
            return (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        private void TriggerHotkey(ushort id, Keys key, Modifiers modifier)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() => OnKeyPressed(id, key, modifier)));
            }
            else
            {
                OnKeyPressed(id, key, modifier);
            }
        }

        private void TriggerCapturingKey(Keys key, Modifiers modifier)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() => CapturingKeyPressed?.Invoke(key, modifier)));
            }
            else
            {
                CapturingKeyPressed?.Invoke(key, modifier);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)WindowsMessages.HOTKEY && CheckRepeatLimitTime())
            {
                ushort id = (ushort)m.WParam;
                Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
                Modifiers modifier = (Modifiers)((int)m.LParam & 0xFFFF);
                OnKeyPressed(id, key, modifier);
                return;
            }

            base.WndProc(ref m);
        }

        protected void OnKeyPressed(ushort id, Keys key, Modifiers modifier)
        {
            HotkeyPress?.Invoke(id, key, modifier);
        }

        private bool CheckRepeatLimitTime()
        {
            if (HotkeyRepeatLimit > 0)
            {
                if (repeatLimitTimer.ElapsedMilliseconds >= HotkeyRepeatLimit)
                {
                    repeatLimitTimer.Reset();
                    repeatLimitTimer.Start();
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (winEventHookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(winEventHookHandle);
                winEventHookHandle = IntPtr.Zero;
            }

            if (keyboardHookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(keyboardHookHandle);
                keyboardHookHandle = IntPtr.Zero;
            }

            base.Dispose(disposing);
        }
    }
}
