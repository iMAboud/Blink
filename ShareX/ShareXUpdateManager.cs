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

using ShareX.HelpersLib;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ShareX
{
    internal class ShareXUpdateManager : GitHubUpdateManager
    {
        public UpdateChannel UpdateChannel { get; set; }

        public ShareXUpdateManager()
        {
            GitHubOwner = "iMAboud";
            GitHubRepo = "Blink";
        }

        public override GitHubUpdateChecker CreateUpdateChecker()
        {
            string owner = string.IsNullOrEmpty(GitHubOwner) ? "iMAboud" : GitHubOwner;
            string repo = string.IsNullOrEmpty(GitHubRepo) ? "Blink" : GitHubRepo;

            return new GitHubUpdateChecker(owner, repo)
            {
                IsPortable = Program.Portable,
                IncludePreRelease = UpdateChannel == UpdateChannel.PreRelease,
                IgnoreRevision = true
            };
        }

        protected override async Task CheckUpdate()
        {
            if (!AutoUpdateEnabled) return;

            GitHubUpdateChecker updateChecker = CreateUpdateChecker();
            await updateChecker.CheckUpdateAsync();

            if (updateChecker.Status == UpdateStatus.UpdateAvailable)
            {
                string versionText = updateChecker.LatestVersion?.ToString() ?? "New version";
                TaskHelpers.ShowNotificationTip($"An update (v{versionText}) is available.", "Blink Update");

                bool isWindowOpen = MainWindowIntegration.IsVisible || SettingsIntegration.IsVisible;

                if (!isWindowOpen && !string.IsNullOrEmpty(updateChecker.DownloadURL))
                {
                    _ = PerformBackgroundUpdateAsync(updateChecker.DownloadURL);
                }
            }
        }

        private async Task PerformBackgroundUpdateAsync(string downloadUrl)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "Blink_Update");
                Directory.CreateDirectory(tempDir);
                string downloadPath = Path.Combine(tempDir, "Blink.exe");

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Blink-App");
                    using (Stream stream = await client.GetStreamAsync(downloadUrl))
                    using (FileStream fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }

                if (File.Exists(downloadPath))
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string rootExe = Path.GetFullPath(Path.Combine(baseDir, "..", "Blink.exe"));
                    if (!File.Exists(rootExe))
                    {
                        rootExe = Environment.ProcessPath ?? Path.Combine(baseDir, "Blink.exe");
                    }

                    string updaterPath = Path.Combine(baseDir, "updater.exe");
                    if (!File.Exists(updaterPath))
                    {
                        updaterPath = Path.Combine(baseDir, "ShareX.Updater.exe");
                    }

                    if (File.Exists(updaterPath))
                    {
                        int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = updaterPath,
                            UseShellExecute = false
                        };
                        psi.ArgumentList.Add("--pid");
                        psi.ArgumentList.Add(pid.ToString());
                        psi.ArgumentList.Add("--new-exe");
                        psi.ArgumentList.Add(downloadPath);
                        psi.ArgumentList.Add("--target-exe");
                        psi.ArgumentList.Add(rootExe);
                        psi.ArgumentList.Add("--launch");

                        System.Diagnostics.Process.Start(psi);
                        ShareX.AvaloniaUI.Integration.AvaloniaBootstrapper.Shutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Silent background update failed.");
            }
        }
    }
}