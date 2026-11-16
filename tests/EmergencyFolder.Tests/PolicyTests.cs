using Xunit;

namespace EmergencyFolder.Tests;

public class PolicyTests
{
    [Theory]
    [InlineData("services", "user", @"C:\Users\user", true)]
    [InlineData("Console", "SYSTEM", @"C:\Users\user", true)]
    [InlineData("Console", "user", @"C:\Windows\system32\config\systemprofile", true)]
    [InlineData("Console", "clinic", @"C:\Users\clinic", false)]
    public void Detects_system_session(string session, string user, string profile, bool expected)
    {
        Assert.Equal(expected, SystemSession.IsSystemContext(session, user, profile));
    }

    [Fact]
    public void Domain_check_waits_longer_when_the_controller_answers()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), DcTiming.NextDelay(true));
        Assert.Equal(TimeSpan.FromSeconds(5), DcTiming.NextDelay(false));
        Assert.Equal(TimeSpan.FromSeconds(4), DcTiming.CheckTimeout);
    }

    [Fact]
    public void Desktop_opens_are_debounced_for_800_milliseconds()
    {
        Assert.True(DesktopOpenGate.ShouldOpen(10_000, 0));
        Assert.False(DesktopOpenGate.ShouldOpen(10_799, 10_000));
        Assert.True(DesktopOpenGate.ShouldOpen(10_800, 10_000));
    }

    [Theory]
    [InlineData(ShortcutPresence.MatchesTarget, false, false)]
    [InlineData(ShortcutPresence.DifferentTarget, false, false)]
    [InlineData(ShortcutPresence.Missing, true, false)]
    [InlineData(ShortcutPresence.Missing, false, true)]
    public void Current_user_autostart_is_only_a_fallback(ShortcutPresence presence, bool system, bool expected)
    {
        Assert.Equal(expected, AutostartPolicy.ShouldWriteCurrentUserRunKey(presence, system));
    }

    [Fact]
    public void Paths_use_public_and_programdata()
    {
        Assert.Equal(@"C:\Users\Public\Desktop", KnownPaths.PublicDesktop(null));
        Assert.Equal(@"D:\Public\Desktop", KnownPaths.PublicDesktop(@"D:\Public"));
        Assert.Equal(
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup",
            KnownPaths.CommonStartup(null));
        Assert.Equal(
            @"C:\ProgramData\Emergency folder\launch.log",
            KnownPaths.LaunchLog(null));
    }

    [Fact]
    public void Scheduled_task_runs_the_exe_in_the_user_session()
    {
        var xml = TaskXml.Build(@"CLINIC\A&B", @"C:\Program Files\Emergency folder\Emergency folder.exe");
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml, StringComparison.Ordinal);
        Assert.Contains("<UserId>CLINIC\\A&amp;B</UserId>", xml, StringComparison.Ordinal);
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml, StringComparison.Ordinal);
        Assert.Contains(@"<Command>C:\Program Files\Emergency folder\Emergency folder.exe</Command>", xml, StringComparison.Ordinal);
        Assert.Contains("<Arguments>--background</Arguments>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("NT AUTHORITY\\SYSTEM", xml, StringComparison.Ordinal);
    }
}
