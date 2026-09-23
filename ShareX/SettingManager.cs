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

using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.Localization;
using ShareX.ScreenCaptureLib;
using ShareX.UploadersLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;

namespace ShareX
{
    internal static class SettingManager
    {
        internal const string ApplicationConfigFileName = "ApplicationConfig.json";

        private static string ApplicationConfigFilePath
        {
            get
            {
                if (Program.Sandbox) return null;

                return Path.Combine(Program.PersonalFolder, ApplicationConfigFileName);
            }
        }

        private const string UploadersConfigFileNamePrefix = "UploadersConfig";
        private const string UploadersConfigFileNameExtension = "json";
        private const string UploadersConfigFileName = UploadersConfigFileNamePrefix + "." + UploadersConfigFileNameExtension;

        private static string UploadersConfigFilePath
        {
            get
            {
                if (Program.Sandbox) return null;

                string uploadersConfigFolder;

                if (Settings != null && !string.IsNullOrEmpty(Settings.CustomUploadersConfigPath))
                {
                    uploadersConfigFolder = FileHelpers.ExpandFolderVariables(Settings.CustomUploadersConfigPath);
                }
                else
                {
                    uploadersConfigFolder = Program.PersonalFolder;
                }

                string uploadersConfigFileName = GetUploadersConfigFileName(uploadersConfigFolder);

                return Path.Combine(uploadersConfigFolder, uploadersConfigFileName);
            }
        }

        private const string HotkeysConfigFileName = "HotkeysConfig.json";

        private static string HotkeysConfigFilePath
        {
            get
            {
                if (Program.Sandbox) return null;

                string hotkeysConfigFolder;

                if (Settings != null && !string.IsNullOrEmpty(Settings.CustomHotkeysConfigPath))
                {
                    hotkeysConfigFolder = FileHelpers.ExpandFolderVariables(Settings.CustomHotkeysConfigPath);
                }
                else
                {
                    hotkeysConfigFolder = Program.PersonalFolder;
                }

                return Path.Combine(hotkeysConfigFolder, HotkeysConfigFileName);
            }
        }

        public static string BackupFolder => Path.Combine(Program.PersonalFolder, "Backup");

        private static ApplicationConfig Settings { get => Program.Settings; set => Program.Settings = value; }
        private static TaskSettings DefaultTaskSettings { get => Program.DefaultTaskSettings; set => Program.DefaultTaskSettings = value; }
        private static UploadersConfig UploadersConfig { get => Program.UploadersConfig; set => Program.UploadersConfig = value; }
        private static HotkeysConfig HotkeysConfig { get => Program.HotkeysConfig; set => Program.HotkeysConfig = value; }

        private static ManualResetEvent uploadersConfigResetEvent = new ManualResetEvent(false);
        private static ManualResetEvent hotkeysConfigResetEvent = new ManualResetEvent(false);

        public static void LoadInitialSettings()
        {
            MigrateLegacySettingsIfNeeded();
            LoadApplicationConfig();

            Task.Run(() =>
            {
                LoadUploadersConfig();
                uploadersConfigResetEvent.Set();

                LoadHotkeysConfig();
                hotkeysConfigResetEvent.Set();
            });
        }

        public static void WaitUploadersConfig()
        {
            if (UploadersConfig == null)
            {
                uploadersConfigResetEvent.WaitOne();
            }
        }

        public static void WaitHotkeysConfig()
        {
            if (HotkeysConfig == null)
            {
                hotkeysConfigResetEvent.WaitOne();
            }
        }

        public static T LoadDefaultSetting<T>(string resourceName) where T : class, new()
        {
            try
            {
                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string fullResourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(fullResourceName))
                {
                    using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
                    {
                        if (stream != null)
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            using (Newtonsoft.Json.JsonTextReader jsonReader = new Newtonsoft.Json.JsonTextReader(reader))
                            {
                                Newtonsoft.Json.JsonSerializer serializer = new Newtonsoft.Json.JsonSerializer();
                                serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                                serializer.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Local;
                                serializer.ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace;
                                return serializer.Deserialize<T>(jsonReader);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, $"Failed to load default setting: {resourceName}");
            }

            return null;
        }

        public static void LoadApplicationConfig(bool fallbackSupport = true)
        {
            if (!File.Exists(ApplicationConfigFilePath))
            {
                Settings = LoadDefaultSetting<ApplicationConfig>("DefaultApplicationConfig.json") ?? new ApplicationConfig();
                Settings.FilePath = ApplicationConfigFilePath;
                Settings.BackupFolder = BackupFolder;
                Settings.IsFirstTimeRun = true;
                Settings.FirstTimeRunDate = DateTime.Now;
                Settings.ShowStartScreen = true;
                Settings.Save(ApplicationConfigFilePath);
            }
            else
            {
                Settings = ApplicationConfig.Load(ApplicationConfigFilePath, BackupFolder, fallbackSupport);
            }

            Settings.BackupFolder = BackupFolder;
            Settings.CreateBackup = true;
            Settings.CreateWeeklyBackup = true;
            Settings.SettingsSaveFailed -= Settings_SettingsSaveFailed;
            Settings.SettingsSaveFailed += Settings_SettingsSaveFailed;
            DefaultTaskSettings = Settings.DefaultTaskSettings;
            ApplicationConfigBackwardCompatibilityTasks();
            Settings.ThemeOptions ??= new ApplicationThemeOptions();
            ThemeManager.Configure(Settings.ThemeOptions);
        }

        private static void Settings_SettingsSaveFailed(Exception e)
        {
            string message;

            if (e is UnauthorizedAccessException || e is FileNotFoundException)
            {
                message = Strings.YourAntiVirusSoftwareOrTheControlledFolderAccessFeatureInWindowsCouldBeBlockingShareX;
            }
            else
            {
                message = e.Message;
            }

            TaskHelpers.ShowNotificationTip(message, "Blink - " + Strings.FailedToSaveSettings, 5000);
        }

        public static void LoadUploadersConfig(bool fallbackSupport = true)
        {
            if (!File.Exists(UploadersConfigFilePath))
            {
                UploadersConfig = LoadDefaultSetting<UploadersConfig>("DefaultUploadersConfig.json") ?? new UploadersConfig();
                UploadersConfig.FilePath = UploadersConfigFilePath;
                UploadersConfig.BackupFolder = BackupFolder;
                UploadersConfig.Save(UploadersConfigFilePath);
            }
            else
            {
                UploadersConfig = UploadersConfig.Load(UploadersConfigFilePath, BackupFolder, fallbackSupport);
            }

            UploadersConfig.BackupFolder = BackupFolder;
            UploadersConfig.CreateBackup = true;
            UploadersConfig.CreateWeeklyBackup = true;
            UploadersConfig.SupportDPAPIEncryption = true;
            UploadersConfigBackwardCompatibilityTasks();
        }

        public static void LoadHotkeysConfig(bool fallbackSupport = true)
        {
            if (!File.Exists(HotkeysConfigFilePath))
            {
                HotkeysConfig = LoadDefaultSetting<HotkeysConfig>("DefaultHotkeysConfig.json") ?? new HotkeysConfig();
                HotkeysConfig.FilePath = HotkeysConfigFilePath;
                HotkeysConfig.BackupFolder = BackupFolder;
                if (HotkeysConfig.Hotkeys == null || HotkeysConfig.Hotkeys.Count == 0)
                {
                    HotkeysConfig.Hotkeys = HotkeyManager.GetDefaultHotkeyList();
                }
                HotkeysConfig.Save(HotkeysConfigFilePath);
            }
            else
            {
                HotkeysConfig = HotkeysConfig.Load(HotkeysConfigFilePath, BackupFolder, fallbackSupport);
                if (HotkeysConfig.Hotkeys == null || HotkeysConfig.Hotkeys.Count == 0)
                {
                    HotkeysConfig.Hotkeys = HotkeyManager.GetDefaultHotkeyList();
                    HotkeysConfig.Save(HotkeysConfigFilePath);
                }
            }

            HotkeysConfig.BackupFolder = BackupFolder;
            HotkeysConfig.CreateBackup = true;
            HotkeysConfig.CreateWeeklyBackup = true;
            HotkeysConfigBackwardCompatibilityTasks();
        }

        public static void LoadAllSettings()
        {
            LoadApplicationConfig();
            LoadUploadersConfig();
            LoadHotkeysConfig();
        }

        private static string GetUploadersConfigFileName(string destinationFolder)
        {
            if (string.IsNullOrEmpty(destinationFolder))
            {
                return UploadersConfigFileName;
            }

            if (Settings != null && Settings.UseMachineSpecificUploadersConfig)
            {
                string sanitizedMachineName = FileHelpers.SanitizeFileName(Environment.MachineName.ToLowerInvariant());

                if (!string.IsNullOrEmpty(sanitizedMachineName))
                {
                    string machineSpecificFileName = $"{UploadersConfigFileNamePrefix}-{sanitizedMachineName}.{UploadersConfigFileNameExtension}";
                    string machineSpecificPath = Path.Combine(destinationFolder, machineSpecificFileName);

                    if (!File.Exists(machineSpecificPath))
                    {
                        string defaultFilePath = Path.Combine(destinationFolder, UploadersConfigFileName);

                        if (File.Exists(defaultFilePath))
                        {
                            try
                            {
                                File.Copy(defaultFilePath, machineSpecificPath, false);
                            }
                            catch (IOException)
                            {
                                // Ignore copy issues; file may have been created in the meantime.
                            }
                        }
                    }

                    return machineSpecificFileName;
                }
            }

            return UploadersConfigFileName;
        }

        private static void ApplicationConfigBackwardCompatibilityTasks()
        {
            if (!Settings.IsFirstTimeRun || (Settings.FirstTimeRunDate != default && Settings.FirstTimeRunDate < DateTime.Now.AddMinutes(-1)))
            {
                Settings.ShowStartScreen = false;
            }

            if (SystemOptions.DisableUpload)
            {
                DefaultTaskSettings.AfterCaptureJob = DefaultTaskSettings.AfterCaptureJob.Remove(AfterCaptureTasks.UploadImageToHost);
            }

            if (Settings.IsUpgradeFrom("14.1.2"))
            {
                if (!Environment.Is64BitOperatingSystem && !string.IsNullOrEmpty(DefaultTaskSettings.CaptureSettings.FFmpegOptions.CLIPath))
                {
                    DefaultTaskSettings.CaptureSettings.FFmpegOptions.OverrideCLIPath = true;
                }
            }

            if (Settings.IsUpgradeFrom("15.0.1"))
            {
                DefaultTaskSettings.CaptureSettings.ScrollingCaptureOptions = new ScrollingCaptureOptions();
                DefaultTaskSettings.CaptureSettings.FFmpegOptions.FixSources();
            }

            if (Settings.IsUpgradeFrom("16.0.2"))
            {
                if (Settings.CheckPreReleaseUpdates)
                {
                    Settings.UpdateChannel = UpdateChannel.PreRelease;
                }
            }

            NotificationActionButton.EnsureButton(DefaultTaskSettings.GeneralSettings.ToastWindowButtons, ToastClickAction.OCR);
        }

        public static void HistoryConnect()
        {
        }

        public static void HistoryClose()
        {
        }

        private static void UploadersConfigBackwardCompatibilityTasks()
        {
            if (UploadersConfig.CustomUploadersList != null)
            {
                foreach (CustomUploaderItem cui in UploadersConfig.CustomUploadersList)
                {
                    try
                    {
                        cui.CheckBackwardCompatibility();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void HotkeysConfigBackwardCompatibilityTasks()
        {
            if (SystemOptions.DisableUpload)
            {
                foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
                {
                    if (taskSettings != null)
                    {
                        taskSettings.AfterCaptureJob = taskSettings.AfterCaptureJob.Remove(AfterCaptureTasks.UploadImageToHost);
                    }
                }
            }

            if (Settings.IsUpgradeFrom("15.0.1"))
            {
                foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
                {
                    if (taskSettings != null && taskSettings.CaptureSettings != null)
                    {
                        taskSettings.CaptureSettings.ScrollingCaptureOptions = new ScrollingCaptureOptions();
                        taskSettings.CaptureSettings.FFmpegOptions.FixSources();
                    }
                }
            }

            foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
            {
                if (taskSettings?.GeneralSettings?.ToastWindowButtons != null)
                {
                    NotificationActionButton.EnsureButton(taskSettings.GeneralSettings.ToastWindowButtons, ToastClickAction.OCR);
                }
            }
        }

        public static void CleanupHotkeysConfig()
        {
            foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
            {
                taskSettings.Cleanup();
            }
        }

        public static void SaveAllSettings()
        {
            if (Settings != null)
            {
                Settings.Save(ApplicationConfigFilePath);
            }

            if (UploadersConfig != null)
            {
                UploadersConfig.Save(UploadersConfigFilePath);
            }

            if (HotkeysConfig != null)
            {
                CleanupHotkeysConfig();
                HotkeysConfig.Save(HotkeysConfigFilePath);
            }
        }

        public static void SaveApplicationConfig()
        {
            if (Settings != null)
            {
                Settings.Save(ApplicationConfigFilePath);
            }
        }

        public static void SaveApplicationConfigAsync()
        {
            if (Settings != null)
            {
                Settings.SaveAsync(ApplicationConfigFilePath);
            }
        }

        public static void SaveUploadersConfigAsync()
        {
            if (UploadersConfig != null)
            {
                UploadersConfig.SaveAsync(UploadersConfigFilePath);
            }
        }

        public static void SaveHotkeysConfigAsync()
        {
            if (HotkeysConfig != null)
            {
                CleanupHotkeysConfig();
                HotkeysConfig.SaveAsync(HotkeysConfigFilePath);
            }
        }

        public static void SaveAllSettingsAsync()
        {
            SaveApplicationConfigAsync();
            SaveUploadersConfigAsync();
            SaveHotkeysConfigAsync();
        }

        public static void ResetSettings()
        {
            if (File.Exists(ApplicationConfigFilePath)) File.Delete(ApplicationConfigFilePath);
            LoadApplicationConfig(false);

            if (File.Exists(UploadersConfigFilePath)) File.Delete(UploadersConfigFilePath);
            LoadUploadersConfig(false);

            if (File.Exists(HotkeysConfigFilePath)) File.Delete(HotkeysConfigFilePath);
            LoadHotkeysConfig(false);
        }

        public static bool Export(string archivePath, bool settings, bool history)
        {
            MemoryStream msApplicationConfig = null, msUploadersConfig = null, msHotkeysConfig = null;

            try
            {
                List<ZipEntryInfo> entries = new List<ZipEntryInfo>();

                if (settings)
                {
                    msApplicationConfig = Settings.SaveToMemoryStream(false);
                    entries.Add(new ZipEntryInfo(msApplicationConfig, ApplicationConfigFileName));

                    msUploadersConfig = UploadersConfig.SaveToMemoryStream(false);
                    entries.Add(new ZipEntryInfo(msUploadersConfig, UploadersConfigFileName));

                    msHotkeysConfig = HotkeysConfig.SaveToMemoryStream(false);
                    entries.Add(new ZipEntryInfo(msHotkeysConfig, HotkeysConfigFileName));
                }



                ZipManager.Compress(archivePath, entries);
                return true;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
                MessageBox.Show(string.Format(Strings.SettingManager_ExportBackupError, e), Strings.SettingManager_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                msApplicationConfig?.Dispose();
                msUploadersConfig?.Dispose();
                msHotkeysConfig?.Dispose();

                if (history)
                {
                    HistoryConnect();
                }
            }

            return false;
        }

        public static bool Import(string archivePath)
        {
            try
            {
                HistoryClose();

                ZipManager.Extract(archivePath, Program.PersonalFolder, false, entry =>
                {
                    return FileHelpers.CheckExtension(entry.Name, new string[] { "json", "xml" });
                }, 1_000_000_000);

                TransformImportedSettings(Program.PersonalFolder);

                return true;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
                MessageBox.Show(string.Format(Strings.SettingManager_ImportBackupError, e), Strings.SettingManager_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                HistoryConnect();
            }

            return false;
        }

        public static void MigrateLegacySettingsIfNeeded()
        {
            if (File.Exists(ApplicationConfigFilePath))
            {
                return;
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string parentDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
                string grandParentDir = Path.GetFullPath(Path.Combine(parentDir, ".."));

                string currentDir = Directory.GetCurrentDirectory();
                string[] searchDirs = [parentDir, baseDir, grandParentDir, currentDir];
                foreach (string dir in searchDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        string[] backupFiles = Directory.GetFiles(dir, "*.sxb")
                            .Concat(Directory.GetFiles(dir, "*.bkb"))
                            .ToArray();

                        if (backupFiles.Length > 0)
                        {
                            string backupToImport = backupFiles.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
                            if (Import(backupToImport))
                            {
                                return;
                            }
                        }
                    }
                }

                List<string> candidateLegacyFolders =
                [
                    Path.Combine(parentDir, "ShareX"),
                    Path.Combine(baseDir, "ShareX"),
                    Path.Combine(Path.GetDirectoryName(Program.PersonalFolder) ?? "", "ShareX"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShareX")
                ];

                string appBlink = Path.Combine(baseDir, Program.AppName);
                if (!string.Equals(appBlink, Program.PersonalFolder, StringComparison.OrdinalIgnoreCase))
                {
                    candidateLegacyFolders.Insert(0, appBlink);
                }

                foreach (string legacyFolder in candidateLegacyFolders)
                {
                    if (Directory.Exists(legacyFolder))
                    {
                        string legacyAppConfig = Path.Combine(legacyFolder, ApplicationConfigFileName);
                        if (File.Exists(legacyAppConfig))
                        {
                            foreach (string file in Directory.GetFiles(legacyFolder, "*", SearchOption.AllDirectories))
                            {
                                string rel = Path.GetRelativePath(legacyFolder, file);
                                string dest = Path.Combine(Program.PersonalFolder, rel);
                                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                                File.Copy(file, dest, true);
                            }

                            TransformImportedSettings(Program.PersonalFolder);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "MigrateLegacySettingsIfNeeded failed");
            }
        }

        private static void TransformImportedSettings(string folder)
        {
            try
            {
                string appConfig = Path.Combine(folder, ApplicationConfigFileName);
                if (File.Exists(appConfig))
                {
                    string text = File.ReadAllText(appConfig);
                    text = text.Replace("\"ShareX - ", "\"Blink - ")
                               .Replace("\"WindowTitle\": \"ShareX", "\"WindowTitle\": \"Blink")
                               .Replace("ShareX/", "Blink/")
                               .Replace("\"ShareX\"", "\"Blink\"")
                               .Replace("\"ShowStartScreen\": true", "\"ShowStartScreen\": false");
                    if (!text.Contains("\"ShowStartScreen\""))
                    {
                        text = text.Replace("\"FirstTimeRunDate\"", "\"ShowStartScreen\": false,\r\n  \"FirstTimeRunDate\"");
                    }
                    File.WriteAllText(appConfig, text);
                }

                string uploadersConfig = Path.Combine(folder, UploadersConfigFileName);
                if (File.Exists(uploadersConfig))
                {
                    string text = File.ReadAllText(uploadersConfig);
                    text = text.Replace("\"ShareX/", "\"Blink/")
                               .Replace("ShareX/%y/%mo", "Blink/%y/%mo")
                               .Replace("Sending email from ShareX", "Sending email from Blink")
                               .Replace("\"ShareX\"", "\"Blink\"");
                    File.WriteAllText(uploadersConfig, text);
                }

                string hotkeysConfig = Path.Combine(folder, HotkeysConfigFileName);
                if (File.Exists(hotkeysConfig))
                {
                    string text = File.ReadAllText(hotkeysConfig);
                    text = text.Replace("\"ShareX - ", "\"Blink - ")
                               .Replace("\"ExitShareX\"", "\"ExitBlink\"");
                    File.WriteAllText(hotkeysConfig, text);
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "TransformImportedSettings failed");
            }
        }
    }
}
