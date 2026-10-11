using System.Text.Json;

namespace WukongBenchmarkRunner.Services;
// читает json из папки Settings, парсит настройки и раннера, и бенчмарка
public sealed record BenchmarkConfiguration(RunnerSettings Runner, IReadOnlyList<BenchmarkProfile> Profiles)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static BenchmarkConfiguration Load(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Settings");
        var runnerPath = Path.Combine(directory, "runner.json");
        var runner = ReadJson<RunnerSettings>(runnerPath);
        runner.Validate();

        var profiles = runner.Runs
            .Select(run =>
            {
                var path = Path.IsPathRooted(run.SettingsFile)
                    ? run.SettingsFile
                    : Path.Combine(directory, run.SettingsFile);
                var profile = ReadJson<ProfileFile>(path);
                if (profile.Settings is null || profile.Settings.Count == 0)
                {
                    throw new InvalidDataException($"Файл профиля не содержит настроек: {path}");
                }

                return new BenchmarkProfile(run.Name, profile.Settings);
            })
            .ToArray();

        return new BenchmarkConfiguration(runner, profiles);
    }

    private static T ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Обязательный файл настроек не найден: {path}", path);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Файл настроек пуст: {path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Некорректный JSON в файле настроек: {path}", exception);
        }
    }

    private sealed class ProfileFile
    {
        public Dictionary<string, string>? Settings { get; init; }
    }
}

public sealed class RunnerSettings
{
    public int StartupDelaySeconds { get; init; } = 120;
    public int ResultCaptureDelayMinutes { get; init; } = 5;
    public int WindowWaitTimeoutSeconds { get; init; } = 180;
    public List<BenchmarkRunSettings> Runs { get; init; } = [];
    public ClickSettings Click { get; init; } = new();

    public void Validate()
    {
        if (StartupDelaySeconds is < 0 or > 3600 ||
            ResultCaptureDelayMinutes is < 1 or > 60 ||
            WindowWaitTimeoutSeconds is < 1 or > 1800)
        {
            throw new InvalidDataException("Настройки задержек запуска вне поддерживаемого диапазона.");
        }

        if (Runs is null || Runs.Count is < 1 or > 6 ||
            Runs.Any(run => run is null || string.IsNullOrWhiteSpace(run.Name) || string.IsNullOrWhiteSpace(run.SettingsFile)) ||
            Runs.Select(run => run.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Runs.Count)
        {
            throw new InvalidDataException("Укажите от одного до шести прогонов с уникальными именами и файлами настроек.");
        }

        if (Click is null)
        {
            throw new InvalidDataException("В настройках запуска должен быть раздел click.");
        }

        Click.Validate();
    }
}

public sealed class BenchmarkRunSettings
{
    public string Name { get; init; } = string.Empty;
    public string SettingsFile { get; init; } = string.Empty;
}

public sealed class ClickSettings
{
    public double X { get; init; } = 0.105;
    public double StartY { get; init; } = 0.60;
    public double StepY { get; init; } = 0.025;
    public int RepeatCount { get; init; } = 3;
    public int RepeatIntervalMilliseconds { get; init; } = 150;
    public int AfterInitialEnterDelayMilliseconds { get; init; } = 1000;
    public int ConfirmationDelaySeconds { get; init; } = 5;

    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(StartY) || !double.IsFinite(StepY) ||
            StepY < 0 || X < 0 || X > 1 || StartY < 0 ||
            StartY + StepY * (RepeatCount - 1) > 1 ||
            RepeatCount is < 1 or > 20 ||
            RepeatIntervalMilliseconds is < 0 or > 5000 ||
            AfterInitialEnterDelayMilliseconds is < 0 or > 10000 ||
            ConfirmationDelaySeconds is < 0 or > 60)
        {
            throw new InvalidDataException("Настройки кликов вне поддерживаемого диапазона.");
        }
    }
}
