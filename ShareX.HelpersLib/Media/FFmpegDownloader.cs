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

using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX.HelpersLib
{
    public static class FFmpegDownloader
    {
        public const string DefaultFFmpegDownloadUrl = "https://github.com/iMAboud/iMA-Menu-Plugins/releases/download/1.0.2/ffmpeg.exe";

        public static async Task<bool> DownloadFFmpegAsync(string targetFilePath)
        {
            try
            {
                string targetDir = System.IO.Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrEmpty(targetDir) && !System.IO.Directory.Exists(targetDir))
                {
                    System.IO.Directory.CreateDirectory(targetDir);
                }

                DownloaderWindowResult result = await DownloaderWindow.ShowAsync(
                    DefaultFFmpegDownloadUrl,
                    "ffmpeg.exe",
                    window =>
                    {
                        window.Heading = "Downloading FFmpeg";
                        window.Description = "FFmpeg is required for recording and will be placed in the app folder.";
                        window.InstallType = InstallType.Event;
                        window.RunInstallerInBackground = false;
                        window.AutoStartInstall = true;
                        window.InstallRequested += downloadedFile =>
                        {
                            try
                            {
                                if (System.IO.File.Exists(downloadedFile))
                                {
                                    System.IO.File.Copy(downloadedFile, targetFilePath, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugHelper.WriteException(ex);
                            }
                        };
                    });

                return System.IO.File.Exists(targetFilePath);
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
                return false;
            }
        }

        public static async Task<DialogResult> DownloadFFmpeg(bool async, DownloaderWindow.DownloaderInstallEventHandler installRequested)
        {
            DownloaderWindowResult result = await DownloaderWindow.ShowAsync(DefaultFFmpegDownloadUrl, "ffmpeg.exe", window =>
            {
                window.InstallType = InstallType.Event;
                window.RunInstallerInBackground = async;
                window.InstallRequested += installRequested;
            });
            return result.DialogResult;
        }

        public static bool ExtractFFmpeg(string archivePath, string extractPath)
        {
            try
            {
                ZipManager.Extract(archivePath, extractPath, false,
                    entry => entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase), 1_000_000_000);
                return true;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
            }

            return false;
        }
    }
}
