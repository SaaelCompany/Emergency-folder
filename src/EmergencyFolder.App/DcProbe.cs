using System.Diagnostics;

namespace EmergencyFolder;

internal static class DcProbe
{
    public static async Task<DcStatus> CheckAsync(CancellationToken cancellationToken)
    {
        var domain = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
        if (string.IsNullOrWhiteSpace(domain))
        {
            return new DcStatus(false, "Компьютер не в домене");
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "nltest",
            Arguments = "/dsgetdc:" + domain,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            if (!process.Start())
            {
                return new DcStatus(false, "КД недоступен");
            }
        }
        catch
        {
            return new DcStatus(false, "КД недоступен");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DcTiming.CheckTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new DcStatus(false, "Таймаут проверки КД");
        }

        _ = stdout;
        _ = stderr;
        if (!process.HasExited)
        {
            TryKill(process);
            return new DcStatus(false, "Таймаут проверки КД");
        }

        return process.ExitCode == 0
            ? new DcStatus(true, domain)
            : new DcStatus(false, "КД недоступен");
    }

    static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // nltest уже завершился.
        }
    }
}
