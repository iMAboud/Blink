#nullable enable

using SkiaSharp;
using System;

namespace ShareX.Tools;

public sealed class BackgroundRemovalModel
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }

    public string DisplayName => $"{FileName} ({FormatFileSize(FileSize)})";

    public override string ToString() => DisplayName;

    private static string FormatFileSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = size;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.#} {units[unitIndex]}";
    }
}

public enum BackgroundRemovalDevice
{
    Auto,
    GPU,
    CPU
}

public sealed record BackgroundRemovalResult(
    SKBitmap Image,
    bool IsSessionCached,
    string ExecutionDevice,
    long SessionSetupMilliseconds,
    long PreprocessingMilliseconds,
    long InferenceMilliseconds,
    long PostprocessingMilliseconds);

public sealed class BackgroundRemovalService : IDisposable
{
    public BackgroundRemovalResult RemoveBackground(SKBitmap source, BackgroundRemovalModel model, BackgroundRemovalDevice device)
    {
        return new BackgroundRemovalResult(source.Copy(), false, device.ToString(), 0, 0, 0, 0);
    }

    public void Dispose()
    {
    }
}
