using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace WukongBenchmarkRunner.Services;
// танцы с бубном, чтоб запустить игру без запуска стима
internal static class SteamLibraryLocator
{
    internal const string BenchmarkAppId = "3132990";

    private const string BenchmarkFolderName = "Black Myth Wukong Benchmark Tool";
    private static readonly Regex LibraryPathPattern = new(
        "\"path\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LegacyLibraryPathPattern = new(
        "\"\\d+\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InstallDirectoryPattern = new(
        "\"installdir\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string? FindBenchmarkConfigFile()
    {
        var candidates = GetBenchmarkInstallDirectories()
            .Select(directory => Path.Combine(
                directory, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini"))
            .ToArray();

        return candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault();
    }

    private static IEnumerable<string> GetBenchmarkInstallDirectories()
    {
        var libraries = GetSteamLibraries().ToArray();
        var manifestDirectories = new List<string>();

        foreach (var library in libraries)
        {
            var manifestPath = Path.Combine(
                library, "steamapps", $"appmanifest_{BenchmarkAppId}.acf");
            foreach (var installDirectory in ReadVdfValues(manifestPath, InstallDirectoryPattern))
            {
                if (!string.IsNullOrWhiteSpace(installDirectory) &&
                    !Path.IsPathRooted(installDirectory) &&
                    installDirectory.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0)
                {
                    manifestDirectories.Add(Path.Combine(
                        library, "steamapps", "common", installDirectory));
                }
            }
        }

        var knownDirectories = libraries.Select(library => Path.Combine(
            library, "steamapps", "common", BenchmarkFolderName));

        return manifestDirectories
            .Concat(knownDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetSteamLibraries()
    {
        var libraries = new List<string>();
        foreach (var steamRoot in GetSteamRoots())
        {
            AddExistingDirectory(libraries, steamRoot);

            var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            foreach (var path in ReadVdfValues(libraryFoldersPath, LibraryPathPattern)
                         .Concat(ReadVdfValues(libraryFoldersPath, LegacyLibraryPathPattern)))
            {
                if (Path.IsPathRooted(path))
                {
                    AddExistingDirectory(libraries, path);
                }
            }
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var roots = new List<string>();
        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steamKey?.GetValue("SteamPath") is string steamPath)
            {
                AddExistingDirectory(roots, steamPath);
            }

            if (steamKey?.GetValue("SteamExe") is string steamExecutable)
            {
                var executableDirectory = Path.GetDirectoryName(steamExecutable);
                if (!string.IsNullOrWhiteSpace(executableDirectory))
                {
                    AddExistingDirectory(roots, executableDirectory);
                }
            }
        }
        catch (Exception)
        {
            // При недоступном реестре проверяем стандартные папки Steam.
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            AddExistingDirectory(roots, Path.Combine(programFilesX86, "Steam"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            AddExistingDirectory(roots, Path.Combine(programFiles, "Steam"));
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddExistingDirectory(List<string> directories, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!directories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            directories.Add(fullPath);
        }
    }

    private static IEnumerable<string> ReadVdfValues(string path, Regex pattern)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string contents;
        try
        {
            contents = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return pattern.Matches(contents)
            .Select(match => DecodeVdfString(match.Groups["value"].Value))
            .ToArray();
    }

    private static string DecodeVdfString(string value) =>
        value.Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);
}
