using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace EmergencyFolder;

internal static class Program
{
    const string MutexName = @"Local\ru.bzklinika.emergencyfolder";
    const string OpenEventName = @"Local\ru.bzklinika.emergencyfolder.open";

    [STAThread]
    static void Main(string[] args)
    {
        var system = IsSystemSession();
        var log = new LaunchLog(system);
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            log.Write("APP", "Не найден путь к программе");
            return;
        }

        if (system)
        {
            log.Write("SCCM", "Служебный сеанс. Настраиваю автозагрузку и запускаю программу у вошедшего пользователя.");
            new AutostartService(log).Ensure(exePath, systemContext: true);
            new UserSessionLauncher(log).Launch(exePath);
            return;
        }

        SetAppUserModelId(KnownPaths.AppUserModelId);
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            if (!HasBackgroundArg(args))
            {
                log.Write("APP", "Повторный запуск — открываю общий рабочий стол");
                SignalOpen();
            }

            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext(log, exePath));
    }

    internal static bool HasBackgroundArg(string[] args) =>
        args.Any(arg => string.Equals(arg, "--background", StringComparison.Ordinal));

    static bool IsSystemSession()
    {
        var fromEnvironment = SystemSession.IsSystemContext(
            Environment.GetEnvironmentVariable("SESSIONNAME"),
            Environment.GetEnvironmentVariable("USERNAME"),
            Environment.GetEnvironmentVariable("USERPROFILE"));
        if (fromEnvironment)
        {
            return true;
        }

        try
        {
            return WindowsIdentity.GetCurrent().IsSystem;
        }
        catch
        {
            return false;
        }
    }

    static void SetAppUserModelId(string appId)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(appId);
        }
        catch
        {
            // Идентификатор нужен только для группировки ярлыка. Без него программа всё равно работает.
        }
    }

    static void SignalOpen()
    {
        try
        {
            using var openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName);
            openEvent.Set();
        }
        catch
        {
            // Первая копия откроет папку по клику в трее.
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    internal static EventWaitHandle CreateOpenEvent() =>
        new(false, EventResetMode.AutoReset, OpenEventName);
}

internal sealed class LaunchLog
{
    readonly bool _system;
    readonly object _gate = new();

    public LaunchLog(bool system) => _system = system;

    public void Write(string tag, string message)
    {
        var line = $"[{tag}] {message}";
        Trace.WriteLine(line);
        if (!_system)
        {
            return;
        }

        try
        {
            var path = KnownPaths.LaunchLog(Environment.GetEnvironmentVariable("PROGRAMDATA"));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (_gate)
            {
                File.AppendAllText(path, $"{DateTime.UtcNow:o} {line}\r\n");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[LOG] Не удалось записать журнал: " + ex.Message);
        }
    }
}

internal sealed class DesktopOpener
{
    readonly LaunchLog _log;
    readonly object _gate = new();
    long _lastOpenUnixMs;

    public DesktopOpener(LaunchLog log) => _log = log;

    public void Open()
    {
        var path = KnownPaths.PublicDesktop(Environment.GetEnvironmentVariable("PUBLIC"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            if (!DesktopOpenGate.ShouldOpen(now, _lastOpenUnixMs))
            {
                return;
            }

            _lastOpenUnixMs = now;
        }

        _log.Write("DESKTOP", "Открываю " + path);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Write("DESKTOP", "Не удалось открыть папку: " + ex.Message);
        }
    }
}

internal static class TrayIcon
{
    static MemoryStream? _iconBytes;

    public static Icon Load()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                using var extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted != null)
                {
                    return new Icon(extracted, new Size(32, 32));
                }
            }
        }
        catch
        {
            // Ниже запасной значок из ресурсов.
        }

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico");
        if (resource == null)
        {
            return SystemIcons.Application;
        }

        _iconBytes = new MemoryStream();
        resource.CopyTo(_iconBytes);
        _iconBytes.Position = 0;
        return new Icon(_iconBytes, new Size(32, 32));
    }
}
