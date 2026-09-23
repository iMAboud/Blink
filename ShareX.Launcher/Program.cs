#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ShareX.Launcher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            string processPath = GetProcessPath();
            string baseDir = !string.IsNullOrEmpty(processPath) ? Path.GetDirectoryName(processPath) : AppDomain.CurrentDomain.BaseDirectory;
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
                        const int BufferSize = 128 * 1024;
                        byte[] buffer = new byte[BufferSize];
                        string fullAppDir = Path.GetFullPath(appDir);

                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string fullName = entry.FullName;
                            if (string.IsNullOrEmpty(entry.Name) && (fullName.EndsWith("/", StringComparison.Ordinal) || fullName.EndsWith("\\", StringComparison.Ordinal)))
                            {
                                Directory.CreateDirectory(Path.Combine(appDir, fullName));
                                continue;
                            }

                            string destPath = Path.GetFullPath(Path.Combine(appDir, fullName));
                            if (!destPath.StartsWith(fullAppDir, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string destDir = Path.GetDirectoryName(destPath);
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

            if (File.Exists(targetExe))
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = appDir,
                    UseShellExecute = false
                };
                if (args != null && args.Length > 0)
                {
                    psi.Arguments = string.Join(" ", args);
                }
                Process.Start(psi);
            }
            else
            {
                NativeMessageBox("Could not find or extract Blink executable.", "Blink");
            }
        }
        catch (Exception ex)
        {
            string procPath = GetProcessPath();
            string dir = !string.IsNullOrEmpty(procPath) ? Path.GetDirectoryName(procPath) : AppDomain.CurrentDomain.BaseDirectory;
            try { File.WriteAllText(Path.Combine(dir, "launcher_error.txt"), ex.ToString()); } catch { }
            NativeMessageBox("Failed to launch Blink:\n" + ex.Message, "Blink");
        }
    }

    const uint MB_OK = 0x00000000;
    const uint MB_ICONERROR = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    static void NativeMessageBox(string text, string caption)
    {
        MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONERROR);
    }

    static string GetProcessPath()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
        catch
        {
            return Assembly.GetEntryAssembly()?.Location ?? "";
        }
    }
}
