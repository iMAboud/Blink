#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace ShareX.Updater;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            int pid = 0;
            string? newExe = null;
            string? targetExe = null;
            string? updateDir = null;
            bool launch = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], out pid);
                }
                else if (arg.Equals("--new-exe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    newExe = args[++i];
                }
                else if (arg.Equals("--target-exe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    targetExe = args[++i];
                }
                else if (arg.Equals("--update-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    updateDir = args[++i];
                }
                else if (arg.Equals("--launch", StringComparison.OrdinalIgnoreCase))
                {
                    launch = true;
                }
            }

            if (string.IsNullOrEmpty(targetExe))
            {
                return;
            }

            string currentProcPath = Environment.ProcessPath ?? string.Empty;
            string tempUpdaterDir = Path.Combine(Path.GetTempPath(), "Blink_Updater_Stage");

            // Self-copy to Temp directory to avoid locking files in App folder
            if (!currentProcPath.StartsWith(tempUpdaterDir, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(tempUpdaterDir);
                string tempUpdaterExe = Path.Combine(tempUpdaterDir, "updater.exe");

                try
                {
                    File.Copy(currentProcPath, tempUpdaterExe, true);
                    ProcessStartInfo psiTemp = new ProcessStartInfo
                    {
                        FileName = tempUpdaterExe,
                        UseShellExecute = false
                    };
                    foreach (string a in args)
                    {
                        psiTemp.ArgumentList.Add(a);
                    }
                    Process.Start(psiTemp);
                    return;
                }
                catch
                {
                    // If self-copy fails, continue in place
                }
            }

            // Wait for parent process to exit
            if (pid > 0)
            {
                try
                {
                    Process targetProc = Process.GetProcessById(pid);
                    if (!targetProc.HasExited)
                    {
                        if (!targetProc.WaitForExit(10000))
                        {
                            targetProc.Kill();
                        }
                    }
                }
                catch { }
            }

            Thread.Sleep(1000);

            string? targetDir = Path.GetDirectoryName(targetExe);
            string appDir = !string.IsNullOrEmpty(targetDir) ? Path.Combine(targetDir, "App") : string.Empty;

            // Differential update: If an update directory with new files is supplied, compare SHA-256 hashes and update only changed files
            if (!string.IsNullOrEmpty(updateDir) && Directory.Exists(updateDir) && !string.IsNullOrEmpty(appDir))
            {
                Directory.CreateDirectory(appDir);
                string[] newFiles = Directory.GetFiles(updateDir, "*", SearchOption.AllDirectories);

                foreach (string srcFile in newFiles)
                {
                    string relPath = Path.GetRelativePath(updateDir, srcFile);
                    string destFile = Path.Combine(appDir, relPath);
                    string? destFolder = Path.GetDirectoryName(destFile);

                    if (!string.IsNullOrEmpty(destFolder) && !Directory.Exists(destFolder))
                    {
                        Directory.CreateDirectory(destFolder);
                    }

                    if (File.Exists(destFile))
                    {
                        string srcHash = GetFileHash(srcFile);
                        string destHash = GetFileHash(destFile);
                        if (srcHash.Equals(destHash, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Skip identical file - differential update optimization!
                        }
                    }

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            File.Copy(srcFile, destFile, true);
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(300);
                        }
                    }
                }

                // Cleanup update staging directory
                try { Directory.Delete(updateDir, true); } catch { }
            }

            // Copy single executable if provided
            if (!string.IsNullOrEmpty(newExe) && File.Exists(newExe))
            {
                bool copied = false;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        File.Copy(newExe, targetExe, true);
                        copied = true;
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(500);
                    }
                }

                if (!copied)
                {
                    MessageBox.Show("Failed to apply update file. Please make sure Blink is closed.", "Blink Updater",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try { File.Delete(newExe); } catch { }
            }

            // Relaunch app
            if (launch && File.Exists(targetExe))
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = targetDir ?? string.Empty,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Updater encountered an error: " + ex.Message, "Blink Updater",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string GetFileHash(string filepath)
    {
        try
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(filepath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}
