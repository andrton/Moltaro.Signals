using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Moltaro.Signals.Measurements;

// What the figures were measured on: the processor, the operating system, .NET and the build. Never the name of the
// machine.
internal static class Machine
{
#if DEBUG
    private const string Build = "Debug: its figures do not count, run with -c Release";
#else
    private const string Build = "Release";
#endif

    public static string Describe()
    {
        return $"""
            Processor: {ProcessorName()}, {Environment.ProcessorCount} logical processors, {RuntimeInformation.ProcessArchitecture}
            OS:        {RuntimeInformation.OSDescription}
            .NET:      {RuntimeInformation.FrameworkDescription}
            Build:     {Build}
            """;
    }

    // The name of the processor where the system tells it without a tool: the registry on Windows, /proc/cpuinfo on
    // Linux.
    private static string ProcessorName()
    {
        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? processor = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

            if (processor?.GetValue("ProcessorNameString") is string name)
            {
                return name.Trim();
            }
        }

        if (OperatingSystem.IsLinux())
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal))
                {
                    return line[(line.IndexOf(':') + 1)..].Trim();
                }
            }
        }

        return "unknown";
    }
}
