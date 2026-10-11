using Microsoft.Win32;

namespace WukongBenchmarkRunner.Services;
// читает и правит .ini файл бенчмарка с настройками
public sealed class ConfigManager : IDisposable
{
    private const string UserSettingsSection = "[/Script/GSGameSettings.GSGameUserSettings]";
    private const string ScalabilitySection = "[ScalabilityGroups]";
    private const string RayTracingSection = "[RayTracing]";
    private const string RenderSection = "[GSRenderSetting]";

    private readonly string _configFile;
    private readonly byte[] _originalConfig;
    private bool _disposed;

    public ConfigManager(string? configFile = null)
    {
        _configFile = ResolveConfigFile(configFile);
        if (!File.Exists(_configFile))
        {
            throw new FileNotFoundException(
                $"Файл настроек не найден: {_configFile}. Можно задать полный путь через WUKONG_BENCHMARK_CONFIG.");
        }

        _originalConfig = File.ReadAllBytes(_configFile);
    }

    public void Apply(BenchmarkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        WriteSettings(profile.Settings);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        File.WriteAllBytes(_configFile, _originalConfig);
        _disposed = true;
    }

    private static string ResolveConfigFile(string? configFile)
    {
        var configuredPath = configFile ?? Environment.GetEnvironmentVariable("WUKONG_BENCHMARK_CONFIG");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var locatedConfigFile = SteamLibraryLocator.FindBenchmarkConfigFile();
        if (locatedConfigFile is not null)
        {
            return locatedConfigFile;
        }

        var steamPath = Registry.CurrentUser
            .OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString();

        var candidates = new[]
        {
            steamPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(
                path!, "steamapps", "common", "Black Myth Wukong Benchmark Tool", "b1", "Saved",
                "Config", "Windows", "GameUserSettings.ini"));

        return candidates.FirstOrDefault(File.Exists)
            ?? candidates.First();
    }

    private void WriteSettings(IReadOnlyDictionary<string, string> settings)
    {
        var lines = File.ReadAllLines(_configFile).ToList();
        foreach (var (key, value) in settings)
        {
            SetIniValue(lines, key, value, GetSection(key));
        }

        File.WriteAllLines(_configFile, lines);
    }

    private static void SetIniValue(List<string> lines, string key, string value, string section)
    {
        var sectionStart = FindSection(lines, section);
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add(section);
            lines.Add($"{key}={value}");
            return;
        }

        var sectionEnd = sectionStart + 1;
        while (sectionEnd < lines.Count && !IsSectionHeader(lines[sectionEnd]))
        {
            sectionEnd++;
        }

        for (var index = sectionStart + 1; index < sectionEnd; index++)
        {
            var separator = lines[index].IndexOf('=');
            if (separator >= 0 && string.Equals(lines[index][..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = $"{key}={value}";
                return;
            }
        }

        lines.Insert(sectionEnd, $"{key}={value}");
    }

    private static int FindSection(IReadOnlyList<string> lines, string section)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (string.Equals(lines[index].Trim(), section, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSectionHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('[') && trimmed.EndsWith(']');
    }

    private static string GetSection(string key)
    {
        if (key.StartsWith("sg.", StringComparison.OrdinalIgnoreCase))
        {
            return ScalabilitySection;
        }

        if (key.StartsWith("r.", StringComparison.OrdinalIgnoreCase))
        {
            return RayTracingSection;
        }

        return key.Equals("GSStreamingPoolSize", StringComparison.OrdinalIgnoreCase)
            ? RenderSection
            : UserSettingsSection;
    }
}

