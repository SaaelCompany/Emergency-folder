namespace EmergencyFolder;

public static class WindowsPath
{
    public static string Combine(params string[] parts)
    {
        var builder = new System.Text.StringBuilder();
        var first = true;
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            if (first)
            {
                builder.Append(part.TrimEnd('\\', '/'));
                first = false;
                continue;
            }

            var trimmed = part.Trim('\\', '/');
            if (trimmed.Length == 0)
            {
                continue;
            }

            builder.Append('\\');
            builder.Append(trimmed);
        }

        return builder.ToString();
    }
}

public static class KnownPaths
{
    public const string ShortcutName = "Emergency folder.lnk";
    public const string RunValueName = "Emergency folder";
    public const string AppUserModelId = "ru.bzklinika.emergencyfolder";
    public const string TaskName = "EmergencyFolderLaunch";

    public static string PublicDesktop(string? publicRoot) =>
        WindowsPath.Combine(string.IsNullOrEmpty(publicRoot) ? @"C:\Users\Public" : publicRoot, "Desktop");

    public static string CommonStartup(string? programData) =>
        WindowsPath.Combine(
            string.IsNullOrEmpty(programData) ? @"C:\ProgramData" : programData,
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            "Startup");

    public static string LaunchLog(string? programData) =>
        WindowsPath.Combine(
            string.IsNullOrEmpty(programData) ? @"C:\ProgramData" : programData,
            "Emergency folder",
            "launch.log");
}

public static class SystemSession
{
    public static bool IsSystemContext(string? sessionName, string? userName, string? userProfile)
    {
        if (string.Equals(sessionName, "services", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(userName, "system", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return userProfile != null &&
               userProfile.Contains(@"\config\systemprofile", StringComparison.OrdinalIgnoreCase);
    }
}

public static class DcTiming
{
    public static readonly TimeSpan WhenAvailable = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WhenUnavailable = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(4);

    public static TimeSpan NextDelay(bool domainControllerAvailable) =>
        domainControllerAvailable ? WhenAvailable : WhenUnavailable;
}

public readonly record struct DcStatus(bool Ok, string Reason);

public static class DesktopOpenGate
{
    public const int DebounceMilliseconds = 800;

    public static bool ShouldOpen(long nowUnixMs, long lastUnixMs) =>
        nowUnixMs - lastUnixMs >= DebounceMilliseconds;
}

public enum ShortcutPresence
{
    Missing,
    MatchesTarget,
    DifferentTarget
}

public static class AutostartPolicy
{
    public static bool ShouldWriteCurrentUserRunKey(ShortcutPresence presence, bool systemContext) =>
        presence == ShortcutPresence.Missing && !systemContext;
}

public static class TaskXml
{
    public static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    public static string Build(string user, string exePath)
    {
        var userXml = Escape(user);
        var exeXml = Escape(exePath);
        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <TimeTrigger>
      <StartBoundary>2020-01-01T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{userXml}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{exeXml}</Command>
      <Arguments>--background</Arguments>
    </Exec>
  </Actions>
</Task>
""";
    }
}
