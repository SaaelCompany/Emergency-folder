using System.Reflection;
using Microsoft.Win32;

namespace EmergencyFolder;

internal sealed class AutostartService
{
    readonly LaunchLog _log;

    public AutostartService(LaunchLog log) => _log = log;

    public void Ensure(string exePath, bool systemContext)
    {
        var lnkPath = WindowsPath.Combine(
            KnownPaths.CommonStartup(Environment.GetEnvironmentVariable("PROGRAMDATA")),
            KnownPaths.ShortcutName);
        var presence = Inspect(lnkPath, exePath);
        if (presence == ShortcutPresence.MatchesTarget)
        {
            _log.Write("AUTOSTART", "Ярлык уже в общей автозагрузке");
            return;
        }

        if (presence == ShortcutPresence.DifferentTarget)
        {
            if (TryWriteShortcut(lnkPath, exePath))
            {
                _log.Write("AUTOSTART", "Ярлык общей автозагрузки обновлён");
            }
            else
            {
                _log.Write("AUTOSTART", "Ярлык общей автозагрузки уже существует");
            }

            return;
        }

        if (TryWriteShortcut(lnkPath, exePath))
        {
            _log.Write("AUTOSTART", "Ярлык записан в общую автозагрузку: " + lnkPath);
            return;
        }

        if (!AutostartPolicy.ShouldWriteCurrentUserRunKey(ShortcutPresence.Missing, systemContext))
        {
            _log.Write("AUTOSTART", "Общая автозагрузка не записана из служебного сеанса");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                ?? throw new InvalidOperationException("Нет ключа автозагрузки пользователя");
            key.SetValue(KnownPaths.RunValueName, exePath);
            _log.Write("AUTOSTART", "Включена автозагрузка текущего пользователя");
        }
        catch (Exception ex)
        {
            _log.Write("AUTOSTART", "Не удалось настроить автозагрузку: " + ex.Message);
        }
    }

    ShortcutPresence Inspect(string lnkPath, string exePath)
    {
        if (!File.Exists(lnkPath))
        {
            return ShortcutPresence.Missing;
        }

        try
        {
            var target = ReadTarget(lnkPath);
            if (string.Equals(Normalize(target), Normalize(exePath), StringComparison.OrdinalIgnoreCase))
            {
                return ShortcutPresence.MatchesTarget;
            }
        }
        catch (Exception ex)
        {
            _log.Write("AUTOSTART", "Не удалось прочитать ярлык: " + ex.Message);
        }

        return ShortcutPresence.DifferentTarget;
    }

    static string Normalize(string path) => path.Trim().TrimEnd('\\').ToLowerInvariant();

    bool TryWriteShortcut(string lnkPath, string exePath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
            {
                return false;
            }

            var directory = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            shell = Activator.CreateInstance(type);
            if (shell == null)
            {
                return false;
            }

            shortcut = type.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { lnkPath });
            if (shortcut == null)
            {
                return false;
            }

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
            shortcutType.InvokeMember(
                "WorkingDirectory",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { Path.GetDirectoryName(exePath) ?? "" });
            shortcutType.InvokeMember(
                "Description",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { "Общий рабочий стол" });
            shortcutType.InvokeMember(
                "IconLocation",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { exePath + ",0" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            return File.Exists(lnkPath);
        }
        catch (Exception ex)
        {
            _log.Write("AUTOSTART", "Не удалось записать ярлык: " + ex.Message);
            return false;
        }
        finally
        {
            Release(shortcut);
            Release(shell);
        }
    }

    static string ReadTarget(string lnkPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
            {
                throw new InvalidOperationException("WScript.Shell недоступен");
            }

            shell = Activator.CreateInstance(type) ?? throw new InvalidOperationException("WScript.Shell недоступен");
            shortcut = type.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { lnkPath }) ?? throw new InvalidOperationException("Ярлык не открылся");
            var target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            return target as string ?? "";
        }
        finally
        {
            Release(shortcut);
            Release(shell);
        }
    }

    static void Release(object? com)
    {
        if (com == null)
        {
            return;
        }

        try
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(com);
        }
        catch
        {
            // Объект уже освобождён.
        }
    }
}
