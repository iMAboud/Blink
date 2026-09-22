#nullable enable

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX.Launcher;

static class Program
{
    private static Form? splashForm;
    private static ProgressBar? progressBar;
    private static Label? statusLabel;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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

            if (!needsExtract)
            {
                LaunchTarget(targetExe, appDir, args);
                return;
            }

            // Create smooth, non-intrusive setup splash UI
            CreateSplashForm();

            if (splashForm != null)
            {
                splashForm.Shown += (s, e) =>
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            ExtractPayload(appDir, (percent, status) =>
                            {
                                if (splashForm != null && !splashForm.IsDisposed && splashForm.IsHandleCreated)
                                {
                                    splashForm.BeginInvoke(new Action(() =>
                                    {
                                        if (progressBar != null) progressBar.Value = percent;
                                        if (statusLabel != null) statusLabel.Text = status;
                                    }));
                                }
                            });

                            if (splashForm != null && !splashForm.IsDisposed && splashForm.IsHandleCreated)
                            {
                                splashForm.BeginInvoke(new Action(() =>
                                {
                                    splashForm.Close();
                                    LaunchTarget(targetExe, appDir, args);
                                }));
                            }
                        }
                        catch (Exception ex)
                        {
                            if (splashForm != null && !splashForm.IsDisposed && splashForm.IsHandleCreated)
                            {
                                splashForm.BeginInvoke(new Action(() =>
                                {
                                    splashForm.Close();
                                    MessageBox.Show("Failed to set up Blink portable environment:\n" + ex.Message,
                                        "Blink Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }));
                            }
                        }
                    });
                };

                Application.Run(splashForm);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to launch Blink:\n" + ex.Message, "Blink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void LaunchTarget(string targetExe, string appDir, string[] args)
    {
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
            MessageBox.Show("Could not find Blink executable after setup.", "Blink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void CreateSplashForm()
    {
        splashForm = new Form
        {
            Text = "Blink - Setting up environment",
            Width = 420,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ControlBox = false,
            ShowInTaskbar = true
        };

        statusLabel = new Label
        {
            Text = "Setting up Blink environment silently...",
            Left = 20,
            Top = 20,
            Width = 360,
            Height = 25,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular)
        };

        progressBar = new ProgressBar
        {
            Left = 20,
            Top = 50,
            Width = 360,
            Height = 25,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        splashForm.Controls.Add(statusLabel);
        splashForm.Controls.Add(progressBar);
    }

    private static void ExtractPayload(string appDir, Action<int, string> progressReport)
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

        if (stream == null) return;

        using (stream)
        using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            const int BufferSize = 128 * 1024;
            byte[] buffer = new byte[BufferSize];
            string fullAppDir = Path.GetFullPath(appDir);
            int totalEntries = archive.Entries.Count;
            int currentEntry = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                currentEntry++;
                int percent = (int)((currentEntry / (double)totalEntries) * 100);
                progressReport(percent, $"Setting up component ({currentEntry}/{totalEntries})...");

                string fullName = entry.FullName;
                if (string.IsNullOrEmpty(entry.Name) && (fullName.EndsWith('/') || fullName.EndsWith('\\')))
                {
                    Directory.CreateDirectory(Path.Combine(appDir, fullName));
                    continue;
                }

                string destPath = Path.GetFullPath(Path.Combine(appDir, fullName));
                if (!destPath.StartsWith(fullAppDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                using (Stream entryStream = entry.Open())
                using (FileStream fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                {
                    int bytesRead;
                    while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fileStream.Write(buffer, 0, bytesRead);
                    }
                }
            }
        }
    }
}
