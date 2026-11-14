using Avalonia;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Localization;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PhotoViewer.Common;

public partial class BHelper
{

    public static string AppName { get; } = "PhotoViewer";

    public static string AppDisplayName => Core.Lang[LangId._AppName];

    public static string AppExePath => Environment.ProcessPath ?? "";

    public static string AppRelaunchPath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage ? appImage : AppExePath;

    public static OSType OS { get; } = GetOS();
    private static OSType GetOS()
    {
        if (OperatingSystem.IsLinux()) return OSType.Linux;
        if (OperatingSystem.IsMacOS()) return OSType.Mac;
        if (OperatingSystem.IsWindows()) return OSType.Windows;
        return OSType.Unknown;
    }

    public static string ArchToken { get; } = RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "other",
    };

    public static bool IsWindows10 { get; } = Environment.OSVersion.Version.Major == 10
        && Environment.OSVersion.Version.Build < 22000;

    public static List<int> GenerateWrappedIndexes(int centerIndex, uint range, uint count, bool includeCenterIndex)
    {
        if (centerIndex < 0 || centerIndex > count - 1) return [];

        var unloadSet = new HashSet<int>();

        if (includeCenterIndex)
        {
            unloadSet.Add(centerIndex);
        }

        for (var i = 1; i <= range; i++)
        {
            var rightIndex = ComputeIndexInRange(centerIndex + i, count, true);
            var leftIndex = ComputeIndexInRange(centerIndex - i, count, true);

            unloadSet.Add(rightIndex);
            unloadSet.Add(leftIndex);
        }

        return unloadSet.ToList();
    }

    public static int ComputeIndexInRange(int index, uint count, bool loopIndex)
    {
        long newIndex;

        if (count <= 0)
        {
            newIndex = 0;
        }
        else if (loopIndex)
        {
            newIndex = (((index) % count) + count) % count;
        }
        else
        {
            newIndex = Math.Clamp(index, 0, count - 1);
        }

        return (int)newIndex;
    }

    public static Size ResizeRatio(Size size, double newSize)
    {
        if (size.Width >= size.Height)
        {
            return new Size(newSize, newSize * size.Height / size.Width);
        }

        return new Size(newSize * size.Width / size.Height, newSize);
    }

    public static bool? ConvertStringToBool(string? value)
    {
        var strValue = value ?? string.Empty;
        bool? boolValue = null;

        if (strValue.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            boolValue = true;
        }
        else if (strValue.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            boolValue = false;
        }

        return boolValue;
    }

    public static string GetExceptionDetails(Exception ex)
    {
        var osArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        var details = $"""
            Version: {BHelper.AppDisplayName} v{Core.BuildInfo.Version}
            Magick.NET: {MagickNET.Version}
            Runtime: .NET {Environment.Version}
            OS: {OS} {Environment.OSVersion.VersionString} {osArch}

                                  :>>>>>>>:
                                  | Error |
                                  :<<<<<<<:

            {ex.ToString()}

            """;

        return details;
    }

    public static (string DebugInfo, string Details) GetInAppError(Exception ex)
    {
        var osArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        var debugInfo = $"""
            {BHelper.AppDisplayName} {Core.BuildInfo.Version}
            {MagickNET.Version}
            {OS} {osArch} {Environment.OSVersion.Version}, .NET {Environment.Version}
            """;

        var errorLines = ex.StackTrace?.Split("\r\n", StringSplitOptions.RemoveEmptyEntries) ?? [];
        var errDetails = $"""
            {ex.Source} ▶ {ex.GetType().FullName} ▶ {ex.Message}

            ▶{string.Join("\r\n▶", errorLines)}
            """;

        return (debugInfo, errDetails);
    }

}
