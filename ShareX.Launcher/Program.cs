#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace ShareX.Launcher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            string baseDir = !string.IsNullOrEmpty(processPath) ? Path.GetDirectoryName(processPath)! : AppDomain.CurrentDomain.BaseDirectory;
            string appDir = Path.Combine(baseDir, "App");
            string targetExe = Path.Combine(appDir, "Blink.exe");

            bool needsExtract = !File.Exists(targetExe);
            if (!needsExtract && !string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(processPath) > File.GetLastWriteTimeUtc(targetExe))
                    {
                        needsExtract = true;
                    }
                }
                catch { }
            }

            if (needsExtract)
            {
                Directory.CreateDirectory(appDir);
                Assembly assembly = typeof(Program).Assembly;
                Stream? stream = null;

                foreach (string resName in assembly.GetManifestResourceNames())
                {
                    if (resName.EndsWith("Blink_Payload.zip", StringComparison.OrdinalIgnoreCase) ||
                        resName.EndsWith("ShareX_Payload.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        stream = assembly.GetManifestResourceStream(resName);
                        break;
                    }
                }

                if (stream != null)
                {
                    using (stream)
                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        archive.ExtractToDirectory(appDir, overwriteFiles: true);
                    }
                }
            }

            if (File.Exists(targetExe))
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = appDir,
                    UseShellExecute = false
                };
                foreach (string arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }
                Process.Start(psi);
            }
            else
            {
                MessageBox.Show("Could not find or extract Blink executable.", "Blink",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            string? procPath = Environment.ProcessPath;
            string dir = !string.IsNullOrEmpty(procPath) ? Path.GetDirectoryName(procPath)! : AppDomain.CurrentDomain.BaseDirectory;
            try { File.WriteAllText(Path.Combine(dir, "launcher_error.txt"), ex.ToString()); } catch { }
            MessageBox.Show("Failed to launch Blink:\n" + ex.Message, "Blink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
