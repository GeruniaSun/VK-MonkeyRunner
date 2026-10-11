using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WukongBenchmarkRunner.Services;
// запускает бенчмарк, нажимает кнопки, кликает, убивает бенчмарк, вобщем - смотрящий
public sealed class Launcher
{
    private const string SteamAppId = "3132990";
    private const int SW_RESTORE = 9;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const byte VK_RETURN = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private readonly RunnerSettings _settings;

    public string ScreenshotDirectory { get; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    public Launcher(RunnerSettings settings)
    {
        _settings = settings;
        ScreenshotDirectory = ResolveScreenshotDirectory();
    }

    private static string ResolveScreenshotDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MonkeyRunner.csproj")))
            {
                return Path.Combine(directory.FullName, "Images");
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Images");
    }

    public async Task<BenchmarkRun> LaunchAndAwaitAsync(string testName)
    {
        if (!TerminateRunningBenchmarkProcesses())
        {
            return SaveFailure(testName, "Не удалось завершить уже запущенный процесс бенчмарка.");
        }

        var existingProcessIds = GetProcessIds();
        if (!LaunchViaSteam())
        {
            return Failed(testName, "Не удалось запустить бенчмарк через Steam.");
        }

        var windowTimeout = TimeSpan.FromSeconds(_settings.WindowWaitTimeoutSeconds);
        using var benchmarkProcess = await WaitForActiveBenchmarkWindowAsync(existingProcessIds, windowTimeout);
        if (benchmarkProcess is null)
        {
            return SaveFailure(testName, $"Окно бенчмарка не появилось за {windowTimeout.TotalSeconds:F0} сек.");
        }

        try
        {
            return await StartAndCaptureAsync(benchmarkProcess, testName);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ошибка во время прогона {testName}: {exception.Message}");
            var run = SaveFailure(testName, exception.Message);
            TryTerminate(benchmarkProcess);
            return run;
        }
    }

    private async Task<BenchmarkRun> StartAndCaptureAsync(
        Process benchmarkProcess,
        string testName)
    {
        await Task.Delay(TimeSpan.FromSeconds(_settings.StartupDelaySeconds));

        benchmarkProcess.Refresh();
        var window = benchmarkProcess.MainWindowHandle;
        if (window == IntPtr.Zero)
        {
            var failedRun = SaveFailure(testName, "У процесса нет доступного главного окна.");
            TryTerminate(benchmarkProcess);
            return failedRun;
        }

        ShowWindow(window, SW_RESTORE);
        await FocusWindowAsync(window);
        Console.WriteLine($"{testName}: отправляю клавишу ввода для перехода к меню.");
        SendEnterKey(window);
        await Task.Delay(_settings.Click.AfterInitialEnterDelayMilliseconds);

        for (var click = 0; click < _settings.Click.RepeatCount; click++)
        {
            ClickAt(GetClickPoint(window, _settings.Click, click));
            if (click + 1 < _settings.Click.RepeatCount)
            {
                await Task.Delay(_settings.Click.RepeatIntervalMilliseconds);
            }
        }

        Console.WriteLine($"{testName}: жду {_settings.Click.ConfirmationDelaySeconds} с после клика перед подтверждением.");
        await Task.Delay(TimeSpan.FromSeconds(_settings.Click.ConfirmationDelaySeconds));
        await FocusWindowAsync(window);
        Console.WriteLine($"{testName}: отправляю клавишу ввода для подтверждения.");
        SendEnterKey(window);

        var captureDelay = TimeSpan.FromMinutes(_settings.ResultCaptureDelayMinutes);
        var captureDelayText = $"{captureDelay.TotalMinutes:F0} мин.";
        Console.WriteLine($"Ожидание результатов: {captureDelayText}");
        await Task.Delay(captureDelay);

        using var screenshot = CapturePrimaryScreen();
        var screenshotPath = SaveScreenshot(testName, screenshot);
        var completedNaturally = HasExited(benchmarkProcess);
        TryTerminate(benchmarkProcess);

        var status = completedNaturally
            ? $"Снимок сохранён после ожидания {captureDelayText}; процесс уже завершился."
            : $"Снимок сохранён после ожидания {captureDelayText}; процесс закрыт после снимка.";
        return new BenchmarkRun(testName, screenshotPath, completedNaturally, status);
    }

    private static Point GetClickPoint(IntPtr window, ClickSettings settings, int sequenceIndex)
    {
        if (!GetClientRect(window, out var clientRect))
        {
            throw new InvalidOperationException("Не удалось получить размер окна бенчмарка.");
        }

        var clientWidth = clientRect.Right - clientRect.Left;
        var clientHeight = clientRect.Bottom - clientRect.Top;
        if (clientWidth <= 0 || clientHeight <= 0)
        {
            throw new InvalidOperationException("Размер клиентской области бенчмарка некорректен.");
        }

        var xRatio = settings.X;
        var yRatio = settings.StartY + settings.StepY * sequenceIndex;
        var point = new NativePoint
        {
            X = Math.Clamp((int)Math.Round(xRatio * clientWidth), 0, clientWidth - 1),
            Y = Math.Clamp((int)Math.Round(yRatio * clientHeight), 0, clientHeight - 1)
        };
        if (!ClientToScreen(window, ref point))
        {
            throw new InvalidOperationException("Не удалось преобразовать точку клика в координаты экрана.");
        }

        return new Point(point.X, point.Y);
    }

    private static async Task<Process?> WaitForActiveBenchmarkWindowAsync(
        IReadOnlySet<int> existingProcessIds,
        TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            foreach (var process in Process.GetProcesses())
            {
                var keepProcess = false;
                try
                {
                    if (!existingProcessIds.Contains(process.Id) &&
                        IsBenchmarkProcess(process.ProcessName) &&
                        !process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        keepProcess = true;
                        return process;
                    }
                }
                catch
                {
                    // Процесс может завершиться, пока читается информация о его окне, и это ок
                }
                finally
                {
                    if (!keepProcess)
                    {
                        process.Dispose();
                    }
                }
            }

            await Task.Delay(1000);
        }

        return null;
    }

    private static HashSet<int> GetProcessIds()
    {
        var processIds = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                processIds.Add(process.Id);
            }
        }

        return processIds;
    }

    private static bool HasRunningBenchmarkProcess()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && IsBenchmarkProcess(process.ProcessName))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Игнорируем процессы, которые завершились во время перебора.
                }
            }
        }

        return false;
    }

    private static bool TerminateRunningBenchmarkProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (IsBenchmarkProcess(process.ProcessName) && !process.HasExited)
                    {
                        Console.WriteLine($"Завершаю ранее запущенный процесс бенчмарка (PID {process.Id}).");
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(10000) && !process.HasExited)
                        {
                            Console.Error.WriteLine($"Не удалось дождаться завершения процесса бенчмарка (PID {process.Id}).");
                            return false;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Не удалось завершить процесс бенчмарка: {exception.Message}");
                    return false;
                }
            }
        }

        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (!HasRunningBenchmarkProcess())
            {
                return true;
            }

            Thread.Sleep(250);
        }

        return !HasRunningBenchmarkProcess();
    }

    private static bool IsBenchmarkProcess(string processName) =>
        processName.StartsWith("b1", StringComparison.OrdinalIgnoreCase);

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось закрыть процесс бенчмарка: {exception.Message}");
        }
    }

    private static async Task FocusWindowAsync(IntPtr window)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            SetForegroundWindow(window);
            if (GetForegroundWindow() == window)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("Не удалось активировать окно бенчмарка для отправки клавиатурного ввода.");
    }

    private static void SendEnterKey(IntPtr window)
    {
        if (GetForegroundWindow() != window)
        {
            throw new InvalidOperationException("Окно бенчмарка потеряло фокус; клавиша ввода не отправлена.");
        }

        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void ClickAt(Point point)
    {
        SetCursorPos(point.X, point.Y);
        Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
    }

    private static bool LaunchViaSteam()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://rungameid/{SteamAppId}",
                UseShellExecute = true
            })?.Dispose();
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось запустить бенчмарк через Steam: {exception.Message}");
            return false;
        }
    }

    private static Bitmap CapturePrimaryScreen()
    {
        var screen = Screen.PrimaryScreen ?? throw new InvalidOperationException("Основной экран не найден.");
        var bounds = screen.Bounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        return bitmap;
    }

    private BenchmarkRun SaveFailure(string testName, string status)
    {
        try
        {
            using var screenshot = CapturePrimaryScreen();
            return new BenchmarkRun(testName, SaveScreenshot(testName, screenshot), false, status);
        }
        catch (Exception exception)
        {
            return Failed(testName, $"{status} Скриншот не сохранён: {exception.Message}");
        }
    }

    private static BenchmarkRun Failed(string testName, string status) =>
        new(testName, null, false, status);

    private string SaveScreenshot(string testName, Bitmap bitmap)
    {
        var screenName = string.IsNullOrWhiteSpace(testName) ? "benchmark" : testName.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            screenName = screenName.Replace(invalidCharacter, '_');
        }

        Directory.CreateDirectory(ScreenshotDirectory);
        var path = Path.Combine(ScreenshotDirectory, $"benchmark_{screenName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
