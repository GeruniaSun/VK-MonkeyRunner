using WukongBenchmarkRunner.Services;
using WukongBenchmarkRunner.UI;

internal static class Program
{
    private static async Task Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        BenchmarkConfiguration configuration;
        try
        {
            configuration = BenchmarkConfiguration.Load();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось загрузить настройки: {exception.Message}");
            return;
        }

        var profiles = configuration.Profiles;
        var runs = new List<BenchmarkRun>();
        var steamLauncher = new Launcher(configuration.Runner);

        try
        {
            using (var configManager = new ConfigManager())
            {
                foreach (var profile in profiles)
                {
                    Console.WriteLine($"Тест {profile.Name} начат.");
                    BenchmarkRun run;
                    try
                    {
                        configManager.Apply(profile);
                        run = await steamLauncher.LaunchAndAwaitAsync(profile.Name);
                    }
                    catch (Exception exception)
                    {
                        run = new BenchmarkRun(profile.Name, null, false, exception.Message);
                    }

                    runs.Add(run);
                    Console.WriteLine($"{profile.Name}: {run.Status}");
                    if (run.ScreenshotPath is not null)
                    {
                        Console.WriteLine($"Снимок: {run.ScreenshotPath}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ошибка подготовки запусков: {exception.Message}");
        }

        foreach (var profile in profiles.Skip(runs.Count))
        {
            runs.Add(new BenchmarkRun(profile.Name, null, false, "Прогон не был запущен."));
        }

        var system = WukongBenchmarkRunner.Services.SystemInformation.Read();
        Console.WriteLine();
        Console.WriteLine($"Процессор: {system.Cpu}");
        Console.WriteLine($"Видеокарта: {system.Gpu}");
        Console.WriteLine($"Установленная оперативная память: {system.RamGb:F0} ГБ");

        ShowComparison(runs, steamLauncher.ScreenshotDirectory);
    }

    private static void ShowComparison(IReadOnlyList<BenchmarkRun> runs, string screenshotDirectory)
    {
        Exception? displayError = null;
        var uiThread = new Thread(() =>
        {
            try
            {
                Application.Run(new BenchmarkComparisonForm(runs, screenshotDirectory));
            }
            catch (Exception exception)
            {
                displayError = exception;
            }
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();

        if (displayError is not null)
        {
            Console.Error.WriteLine($"Не удалось открыть окно снимков: {displayError.Message}");
        }
    }
}
