using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FysZeRewardOverlay;

internal static class SteamCs2
{
    private static readonly Regex LibraryPath = new("^[ \\t]*\"(?:path|[0-9]+)\"[ \\t]+\"([^\r\n\"]+)\"", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex InstallDir = new("^[ \\t]*\"installdir\"[ \\t]+\"([^\r\n\"]+)\"", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    internal static string? FindConfigDirectory()
    {
        var runningDirectory = FindRunningConfigDirectory();
        if (runningDirectory is not null) return runningDirectory;

        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        var userPath = ReadSteamPath(Registry.CurrentUser, "SteamPath");
        var machinePath = ReadSteamPath(machine, "InstallPath");
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return FindConfigDirectory(new[] { userPath, machinePath, defaultPath }.OfType<string>());
    }

    internal static string? FindConfigDirectory(IEnumerable<string> steamRoots)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in steamRoots)
        {
            if (!Path.IsPathFullyQualified(root)) continue;
            libraries.Add(root);
            var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            foreach (Match match in LibraryPath.Matches(ReadSteamFile(file)))
            {
                var library = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Path.IsPathFullyQualified(library)) libraries.Add(library);
            }
        }

        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            var install = InstallDir.Match(ReadSteamFile(Path.Combine(apps, "appmanifest_730.acf"))).Groups[1].Value;
            if (install.Length == 0 || install is "." or ".." || install.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                continue;
            var config = VerifyGameDirectory(Path.Combine(apps, "common", install, "game"));
            if (config is not null) return config;
        }
        return null;
    }

    private static string? FindRunningConfigDirectory()
    {
        var processes = Process.GetProcessesByName("cs2");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var exe = process.MainModule?.FileName;
                    if (exe is null) continue;
                    var win64 = Path.GetDirectoryName(exe)!;
                    var game = Path.GetFullPath(Path.Combine(win64, "..", ".."));
                    var config = VerifyGameDirectory(game);
                    if (config is not null) return config;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
            return null;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static string? VerifyGameDirectory(string game)
    {
        var config = Path.Combine(game, "csgo", "cfg");
        return Directory.Exists(config) && File.Exists(Path.Combine(game, "bin", "win64", "cs2.exe"))
            ? Path.GetFullPath(config) : null;
    }

    private static string? ReadSteamPath(RegistryKey hive, string value)
    {
        try
        {
            using var key = hive.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue(value) as string;
        }
        catch (Exception ex) when (ex is IOException or SecurityException or UnauthorizedAccessException) { return null; }
    }

    private static string ReadSteamFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }
}
