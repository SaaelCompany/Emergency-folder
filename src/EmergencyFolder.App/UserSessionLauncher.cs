using System.Diagnostics;
using System.Text;

namespace EmergencyFolder;

internal sealed class UserSessionLauncher
{
    const string ListUsersScript = """
$ErrorActionPreference = 'Continue'
$users = @()
try {
  $users = @(Get-Process -Name explorer -IncludeUserName -ErrorAction SilentlyContinue |
    ForEach-Object { $_.UserName } |
    Where-Object { $_ -and ($_ -notmatch '(?i)NT AUTHORITY\\SYSTEM$') } |
    Select-Object -Unique)
} catch {}
if (-not $users -or $users.Count -eq 0) {
  try {
    $owners = @{}
    Get-CimInstance Win32_Process -Filter "Name = 'explorer.exe'" -ErrorAction SilentlyContinue | ForEach-Object {
      $owner = Invoke-CimMethod -InputObject $_ -MethodName GetOwner
      if ($owner.ReturnValue -eq 0 -and $owner.User) {
        $name = if ($owner.Domain) { "$($owner.Domain)\$($owner.User)" } else { $owner.User }
        if ($name -notmatch '(?i)^NT AUTHORITY\\SYSTEM$') { $owners[$name] = $true }
      }
    }
    $users = @($owners.Keys)
  } catch {}
}
if (-not $users -or $users.Count -eq 0) { Write-Output 'NO_USER'; exit 0 }
$users | ForEach-Object { Write-Output $_ }
""";

    readonly LaunchLog _log;

    public UserSessionLauncher(LaunchLog log) => _log = log;

    public void Launch(string exePath)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        string output;
        try
        {
            output = ListUsers(deadline);
        }
        catch (Exception ex)
        {
            _log.Write("SCCM", "Не удалось запустить сессию пользователя: " + ex.Message);
            return;
        }

        var users = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (users.Length == 0)
        {
            _log.Write("SCCM", "Скрипт запуска не вернул результат");
            return;
        }

        if (users.Length == 1 && string.Equals(users[0], "NO_USER", StringComparison.Ordinal))
        {
            _log.Write("SCCM", "NO_USER");
            return;
        }

        foreach (var user in users)
        {
            if (DateTime.UtcNow >= deadline)
            {
                _log.Write("SCCM", "Истекло время запуска сессий пользователей");
                break;
            }

            LaunchOne(user, exePath);
        }
    }

    string ListUsers(DateTime deadline)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "emergency-folder-users-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(scriptPath, ListUsersScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var result = Run(
                PowerShellPath(),
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
                Remaining(deadline));
            return result.Stdout;
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    void LaunchOne(string user, string exePath)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), "emergency-folder-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(xmlPath, TaskXml.Build(user, exePath), Encoding.Unicode);
            var create = Run(
                SchtasksPath(),
                new[] { "/Create", "/TN", KnownPaths.TaskName, "/XML", xmlPath, "/F" },
                20000);
            if (create.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(create.Stderr) ? create.Stdout : create.Stderr;
                _log.Write("SCCM", "FAILED " + user + (string.IsNullOrWhiteSpace(detail) ? "" : " " + detail.Trim()));
                return;
            }

            Run(SchtasksPath(), new[] { "/Run", "/TN", KnownPaths.TaskName }, 20000);
            Thread.Sleep(TimeSpan.FromSeconds(3));
            Run(SchtasksPath(), new[] { "/Delete", "/TN", KnownPaths.TaskName, "/F" }, 20000);
            _log.Write("SCCM", "STARTED " + user);
        }
        catch (Exception ex)
        {
            _log.Write("SCCM", "FAILED " + user + " " + ex.Message);
        }
        finally
        {
            TryDelete(xmlPath);
        }
    }

    static string PowerShellPath()
    {
        var full = WindowsPath.Combine(
            Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        return File.Exists(full) ? full : "powershell.exe";
    }

    static string SchtasksPath()
    {
        var full = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        return File.Exists(full) ? full : "schtasks.exe";
    }

    static int Remaining(DateTime deadline)
    {
        var ms = (int)Math.Clamp((deadline - DateTime.UtcNow).TotalMilliseconds, 1000, 60000);
        return ms;
    }

    static ProcessResult Run(string file, IReadOnlyList<string> args, int timeoutMs)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException("Не удалось запустить " + file);
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Процесс уже завершился.
            }

            throw new TimeoutException(file);
        }

        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Временный файл уберёт система.
        }
    }

    readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);
}
