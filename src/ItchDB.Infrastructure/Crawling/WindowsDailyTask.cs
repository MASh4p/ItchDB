using System.Diagnostics;
using System.Security;
using System.Text;

namespace ItchDB.Infrastructure.Crawling;

public enum DailyTaskResult
{
    Unchanged,
    Installed,
    Removed,
}

/// <summary>
/// Keeps a Windows Task Scheduler job that starts the app once a day.
/// The job is installed only while automatic daily collection is enabled.
/// </summary>
public static class WindowsDailyTask
{
    public const string Name = "ItchDB Daily collection";
    public static readonly TimeOnly LocalTime = new(4, 0);
    private static readonly Lock Gate = new();

    public static DailyTaskResult Apply(bool enabled, string contentRoot, string dataRoot, string? hostPath)
    {
        if (!OperatingSystem.IsWindows()) return DailyTaskResult.Unchanged;
        lock (Gate)
        {
            var scriptPath = Path.Combine(dataRoot, "daily-launch.ps1");
            if (!enabled)
            {
                var removed = TryDelete();
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                return removed ? DailyTaskResult.Removed : DailyTaskResult.Unchanged;
            }

            Directory.CreateDirectory(dataRoot);
            var script = BuildPowerShell(contentRoot, string.IsNullOrWhiteSpace(hostPath) ? "dotnet" : hostPath);
            var scriptSame = File.Exists(scriptPath) && File.ReadAllText(scriptPath) == script;
            if (scriptSame && Exists()) return DailyTaskResult.Unchanged;

            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var xmlPath = Path.Combine(dataRoot, "daily-task.xml");
            File.WriteAllText(xmlPath, BuildTaskXml(scriptPath, NextRun(DateTime.Now)), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            Run($"/Create /F /TN \"{Name}\" /XML \"{xmlPath}\"");
            return DailyTaskResult.Installed;
        }
    }

    /// <summary>The next 04:00 that is still ahead, so registering the task does not fire today's missed slot.</summary>
    public static DateTime NextRun(DateTime localNow)
    {
        var next = localNow.Date.Add(LocalTime.ToTimeSpan());
        if (next <= localNow) next = next.AddDays(1);
        return next;
    }

    internal static string BuildPowerShell(string contentRoot, string hostPath, string? projectFile = null)
    {
        projectFile ??= Directory.Exists(contentRoot)
            ? Directory.EnumerateFiles(contentRoot, "*.csproj").FirstOrDefault()
            : null;
        var root = Ps(contentRoot);
        string file;
        string arguments;
        if (projectFile is not null)
        {
            // dotnet run keeps the project directory as the content root, so the scheduled
            // process uses the same data folder as a launch from the IDE.
            file = Ps(ResolveDotnet(hostPath));
            arguments = $"@('run','--project',{Ps(projectFile)},'--no-launch-profile','--','--daily')";
        }
        else
        {
            file = Ps(hostPath);
            arguments = "@('--daily')";
        }
        return string.Join('\n',
            "$ErrorActionPreference = 'Stop'",
            "$env:ASPNETCORE_URLS = 'http://localhost:5257'",
            $"$p = Start-Process -FilePath {file} -ArgumentList {arguments} -WorkingDirectory {root} -WindowStyle Hidden -Wait -PassThru",
            "exit $p.ExitCode",
            "");
    }

    private static string Ps(string value) => "'" + value.Replace("'", "''") + "'";

    private static string ResolveDotnet(string hostPath)
    {
        var name = Path.GetFileName(hostPath);
        if (name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return hostPath;
        var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        return File.Exists(candidate) ? candidate : "dotnet";
    }

    internal static string BuildTaskXml(string scriptPath, DateTime startLocal)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{Xml("Ежедневный сбор ItchDB. После завершения сбора приложение закрывается.")}</Description>
              </RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{startLocal:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay>
                    <DaysInterval>1</DaysInterval>
                  </ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT18H</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Xml(powershell)}</Command>
                  <Arguments>{Xml(arguments)}</Arguments>
                  <WorkingDirectory>{Xml(Path.GetDirectoryName(scriptPath) ?? "")}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Xml(string value) => SecurityElement.Escape(value) ?? "";

    private static bool Exists()
    {
        var code = Run($"/Query /TN \"{Name}\"", throwOnError: false);
        return code == 0;
    }

    private static bool TryDelete()
    {
        if (!Exists()) return false;
        Run($"/Delete /TN \"{Name}\" /F");
        return true;
    }

    private static int Run(string arguments, bool throwOnError = true)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("schtasks.exe не запустился");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException($"schtasks {arguments} завершился с кодом {process.ExitCode}. {(stderr + " " + stdout).Trim()}");
        return process.ExitCode;
    }
}
