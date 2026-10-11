using System.Management;

namespace WukongBenchmarkRunner.Services;
// настройки бенчмарка
public sealed record BenchmarkProfile(string Name, IReadOnlyDictionary<string, string> Settings);
// один прогон бенчмарка
public sealed record BenchmarkRun(string Name, string? ScreenshotPath, bool Completed, string Status);
// чтоб в конце вывести в консоль информацию о системе
public sealed class SystemInformation
{
    public string Cpu { get; init; } = "Неизвестно";
    public string Gpu { get; init; } = "Неизвестно";
    public double RamGb { get; init; }

    public static SystemInformation Read()
    {
        return new SystemInformation
        {
            Cpu = ReadWmiValue("Win32_Processor", "Name"),
            Gpu = ReadWmiValue("Win32_VideoController", "Name"),
            RamGb = ReadRamGb()
        };
    }

    private static string ReadWmiValue(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
            return searcher.Get().Cast<ManagementObject>()
                .Select(item => item[propertyName]?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Неизвестно";
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось прочитать {className} из WMI: {exception.Message}");
            return "Неизвестно";
        }
    }

    private static double ReadRamGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            using var modules = searcher.Get();
            ulong installedBytes = 0;
            foreach (ManagementObject module in modules)
            {
                using (module)
                {
                    if (module["Capacity"] is { } capacity)
                    {
                        installedBytes += Convert.ToUInt64(capacity);
                    }
                }
            }

            if (installedBytes > 0)
            {
                return installedBytes / 1024d / 1024 / 1024;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось прочитать ёмкость модулей памяти из WMI: {exception.Message}");
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            var bytes = Convert.ToDouble(searcher.Get().Cast<ManagementObject>().First()["TotalPhysicalMemory"]);
            return Math.Ceiling(bytes / 1024 / 1024 / 1024);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось прочитать объём оперативной памяти из WMI: {exception.Message}");
            return 0;
        }
    }
}
