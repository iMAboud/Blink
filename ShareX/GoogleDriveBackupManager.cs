#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia.Threading;
using ShareX.AvaloniaUI;
using ShareX.HelpersLib;
using ShareX.Localization;
using ShareX.UploadersLib;
using ShareX.UploadersLib.FileUploaders;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ShareX;

public static class GoogleDriveBackupManager
{
    public const string BackupFileName = "Blink-backup.bkb";
    public const string LegacyBackupFileName = "ShareX-backup.bkb";

    public static event EventHandler? StateChanged;
    public static event EventHandler? SettingsRestored;

    public static bool IsBusy { get; private set; }
    public static string StatusText { get; private set; } = string.Empty;
    public static string LastBackupInfo { get; private set; } = string.Empty;

    public static bool IsConnected =>
        OAuth2Info.CheckOAuth(Program.UploadersConfig?.GoogleDriveOAuth2Info);

    public static string AccountDisplayName
    {
        get
        {
            OAuthUserInfo? user = Program.UploadersConfig?.GoogleDriveUserInfo;
            if (user != null)
            {
                if (!string.IsNullOrWhiteSpace(user.name) && !string.IsNullOrWhiteSpace(user.email))
                    return $"{user.name} ({user.email})";
                if (!string.IsNullOrWhiteSpace(user.name))
                    return user.name;
                if (!string.IsNullOrWhiteSpace(user.email))
                    return user.email;
            }

            return IsConnected ? "Connected" : "Not connected";
        }
    }

    public static async Task<bool> LoginAsync(Avalonia.Controls.Window? parent = null)
    {
        if (IsConnected)
        {
            return true;
        }

        OAuth2Info defaultGoogleAuth = UploaderOAuthClientFactory.CreateGoogle();
        string clientId = !string.IsNullOrWhiteSpace(defaultGoogleAuth.Client_ID)
            ? defaultGoogleAuth.Client_ID
            : Program.UploadersConfig.GoogleDriveCustomClientID;

        string clientSecret = !string.IsNullOrWhiteSpace(defaultGoogleAuth.Client_Secret)
            ? defaultGoogleAuth.Client_Secret
            : Program.UploadersConfig.GoogleDriveCustomClientSecret;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            StatusText = "Google Drive client configuration missing.";
            return false;
        }

        OAuth2Info oauthInfo = new(clientId, clientSecret);
        GoogleDrive drive = new(oauthInfo);

        OAuthListenerWindowResult? result = OAuthListenerWindowIntegration.Show(drive.OAuth2);
        if (result?.OAuth2Info != null && OAuth2Info.CheckOAuth(result.OAuth2Info))
        {
            Program.UploadersConfig.GoogleDriveOAuth2Info = result.OAuth2Info;
            Program.UploadersConfig.GoogleDriveUserInfo = result.UserInfo;
            SettingManager.SaveUploadersConfigAsync();

            SetStatus("Logged in successfully.");
            NotifyStateChanged();
            _ = RestoreAsync();
            return true;
        }

        SetStatus("Google Drive authorization cancelled or failed.");
        NotifyStateChanged();
        return false;
    }

    public static void Logout()
    {
        Program.UploadersConfig.GoogleDriveOAuth2Info = null;
        Program.UploadersConfig.GoogleDriveUserInfo = null;
        LastBackupInfo = string.Empty;
        SettingManager.SaveUploadersConfigAsync();
        SetStatus("Disconnected from Google Drive.");
        NotifyStateChanged();
    }

    public static async Task<GoogleDriveFile?> CheckRemoteBackupInfoAsync()
    {
        if (!IsConnected)
        {
            LastBackupInfo = string.Empty;
            NotifyStateChanged();
            return null;
        }

        try
        {
            GoogleDrive drive = new(Program.UploadersConfig.GoogleDriveOAuth2Info);
            GoogleDriveFile? file = await drive.FindFileAsync(BackupFileName).ConfigureAwait(false);
            if (file == null)
            {
                file = await drive.FindFileAsync(LegacyBackupFileName).ConfigureAwait(false);
            }

            if (file != null)
            {
                string size = file.size.HasValue ? file.size.Value.ToSizeString() : string.Empty;
                string date = file.modifiedTime.HasValue
                    ? file.modifiedTime.Value.ToLocalTime().ToString("g")
                    : string.Empty;
                LastBackupInfo = $"Backup found: {file.name} ({size}, {date})";
            }
            else
            {
                LastBackupInfo = "No backup found in Google Drive.";
            }

            NotifyStateChanged();
            return file;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
            LastBackupInfo = string.Empty;
            return null;
        }
    }

    public static async Task<bool> BackupAsync()
    {
        if (!IsConnected)
        {
            SetStatus("Not connected to Google Drive.");
            return false;
        }

        IsBusy = true;
        SetStatus("Preparing settings backup...");
        NotifyStateChanged();

        string tempBkb = Path.Combine(Path.GetTempPath(), $"Blink-backup-{Guid.NewGuid():N}.bkb");

        try
        {
            await Task.Run(() =>
            {
                SettingManager.SaveAllSettings();
                return SettingManager.Export(tempBkb, settings: true, history: false);
            }).ConfigureAwait(false);

            if (!File.Exists(tempBkb))
            {
                SetStatus("Failed to create local backup archive.");
                return false;
            }

            SetStatus("Uploading backup to Google Drive...");
            NotifyStateChanged();

            GoogleDrive drive = new(Program.UploadersConfig.GoogleDriveOAuth2Info);
            using (FileStream fs = File.OpenRead(tempBkb))
            {
                GoogleDriveFile? uploaded = await drive.UploadBackupFileAsync(fs, BackupFileName).ConfigureAwait(false);
                if (uploaded == null)
                {
                    SetStatus("Failed to upload backup to Google Drive.");
                    return false;
                }
            }

            SetStatus("Backup successfully uploaded to Google Drive!");
            await CheckRemoteBackupInfoAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
            SetStatus($"Backup failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(tempBkb))
            {
                try { File.Delete(tempBkb); } catch { }
            }

            IsBusy = false;
            NotifyStateChanged();
        }
    }

    public static async Task<bool> RestoreAsync()
    {
        if (!IsConnected)
        {
            SetStatus("Not connected to Google Drive.");
            return false;
        }

        IsBusy = true;
        SetStatus("Searching Google Drive for backup...");
        NotifyStateChanged();

        string tempBkb = Path.Combine(Path.GetTempPath(), $"Blink-restore-{Guid.NewGuid():N}.bkb");

        try
        {
            GoogleDrive drive = new(Program.UploadersConfig.GoogleDriveOAuth2Info);
            GoogleDriveFile? file = await drive.FindFileAsync(BackupFileName).ConfigureAwait(false);
            if (file == null)
            {
                file = await drive.FindFileAsync(LegacyBackupFileName).ConfigureAwait(false);
            }

            if (file == null || string.IsNullOrEmpty(file.id))
            {
                SetStatus("No backup file found in your Google Drive.");
                ShowMessageBox("No backup file (.bkb) was found in your Google Drive.", "Blink - Restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            string size = file.size.HasValue ? file.size.Value.ToSizeString() : string.Empty;
            string date = file.modifiedTime.HasValue ? file.modifiedTime.Value.ToLocalTime().ToString("g") : string.Empty;

            bool confirmed = ShowMessageBox(
                $"Found backup '{file.name}' ({size}, modified {date}).\n\nWould you like to restore this backup? All current settings will be replaced.",
                "Blink - Confirm Restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            if (!confirmed)
            {
                SetStatus("Restore cancelled.");
                return false;
            }

            SetStatus("Downloading backup from Google Drive...");
            NotifyStateChanged();

            bool downloaded = await drive.DownloadFileAsync(file.id, tempBkb).ConfigureAwait(false);
            if (!downloaded || !File.Exists(tempBkb))
            {
                SetStatus("Failed to download backup file.");
                return false;
            }

            SetStatus("Applying restored settings...");
            NotifyStateChanged();

            // Preserve current OAuth session across import — the backup may not contain credentials
            OAuth2Info? savedOAuth = Program.UploadersConfig?.GoogleDriveOAuth2Info;
            OAuthUserInfo? savedUser = Program.UploadersConfig?.GoogleDriveUserInfo;

            bool imported = await Task.Run(() =>
            {
                if (!SettingManager.Import(tempBkb))
                {
                    return false;
                }

                SettingManager.LoadAllSettings();
                return true;
            }).ConfigureAwait(false);

            // Re-inject OAuth credentials so the user stays logged in
            if (savedOAuth != null && OAuth2Info.CheckOAuth(savedOAuth) && Program.UploadersConfig != null)
            {
                Program.UploadersConfig.GoogleDriveOAuth2Info = savedOAuth;
                Program.UploadersConfig.GoogleDriveUserInfo = savedUser;
                SettingManager.SaveUploadersConfigAsync();
            }

            if (!imported)
            {
                SetStatus("Failed to import settings from downloaded backup.");
                return false;
            }

            LanguageHelper.ChangeLanguage(Program.Settings.Language);
            SetStatus("Settings restored successfully from Google Drive!");

            Dispatcher.UIThread.Post(() =>
            {
                SettingsRestored?.Invoke(null, EventArgs.Empty);
            });

            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
            SetStatus($"Restore failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(tempBkb))
            {
                try { File.Delete(tempBkb); } catch { }
            }

            IsBusy = false;
            NotifyStateChanged();
        }
    }

    private static void SetStatus(string text)
    {
        StatusText = text;
    }

    private static void NotifyStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            StateChanged?.Invoke(null, EventArgs.Empty);
        });
    }

    private static DialogResult ShowMessageBox(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return MessageBox.Show(text, title, buttons, icon);
        }

        return Dispatcher.UIThread.InvokeAsync(() => MessageBox.Show(text, title, buttons, icon)).GetAwaiter().GetResult();
    }
}
