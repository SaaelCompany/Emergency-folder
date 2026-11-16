using Microsoft.Win32;

namespace EmergencyFolder;

internal sealed class TrayAppContext : ApplicationContext
{
    readonly LaunchLog _log;
    readonly DesktopOpener _desktop;
    readonly FabForm _fab;
    readonly NotifyIcon _tray;
    readonly EventWaitHandle _openEvent;
    readonly Thread _listener;
    int _generation;
    CancellationTokenSource? _checks;
    volatile bool _stopListening;

    public TrayAppContext(LaunchLog log, string exePath)
    {
        _log = log;
        _desktop = new DesktopOpener(log);
        _openEvent = Program.CreateOpenEvent();
        if (_openEvent.WaitOne(0))
        {
            _desktop.Open();
        }

        _listener = new Thread(ListenForAnotherLaunch)
        {
            IsBackground = true,
            Name = "Emergency folder second instance"
        };
        _listener.Start();

        new AutostartService(log).Ensure(exePath, systemContext: false);

        _fab = new FabForm(OnFabClicked);
        _ = _fab.Handle;
        _tray = new NotifyIcon
        {
            Icon = TrayIcon.Load(),
            Text = "Открыть общий рабочий стол",
            Visible = true
        };
        _tray.MouseClick += (_, _) => _desktop.Open();
        _tray.MouseDoubleClick += (_, _) => _desktop.Open();
        _log.Write("TRAY", "Клик по иконке открывает " + KnownPaths.PublicDesktop(Environment.GetEnvironmentVariable("PUBLIC")));

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        StartChecks();
    }

    void ListenForAnotherLaunch()
    {
        while (!_stopListening)
        {
            _openEvent.WaitOne();
            if (_stopListening)
            {
                break;
            }

            _desktop.Open();
        }
    }

    void OnFabClicked()
    {
        _desktop.Open();
        StartChecks();
    }

    void StartChecks()
    {
        var generation = Interlocked.Increment(ref _generation);
        var previous = Interlocked.Exchange(ref _checks, new CancellationTokenSource());
        previous?.Cancel();
        var token = _checks.Token;
        _ = Task.Run(() => CheckLoop(generation, token), token);
    }

    async Task CheckLoop(int generation, CancellationToken token)
    {
        while (!token.IsCancellationRequested && generation == Volatile.Read(ref _generation))
        {
            DcStatus status;
            try
            {
                status = await DcProbe.CheckAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                status = new DcStatus(false, ex.Message);
            }

            if (token.IsCancellationRequested || generation != Volatile.Read(ref _generation))
            {
                break;
            }

            var reason = string.IsNullOrEmpty(status.Reason) ? "Сеть в порядке" : status.Reason;
            _log.Write("DC", (status.Ok ? "OK" : "FAIL") + " (" + reason + ")");
            Apply(status);
            try
            {
                await Task.Delay(DcTiming.NextDelay(status.Ok), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    void Apply(DcStatus status)
    {
        if (_fab.IsDisposed)
        {
            return;
        }

        if (_fab.InvokeRequired)
        {
            _fab.BeginInvoke(() => Apply(status));
            return;
        }

        if (status.Ok)
        {
            _fab.Dismiss();
        }
        else
        {
            _fab.Present();
        }
    }

    void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (_fab.IsDisposed || !_fab.Visible)
        {
            return;
        }

        if (_fab.InvokeRequired)
        {
            _fab.BeginInvoke(new Action(() => OnDisplayChanged(sender, e)));
            return;
        }

        _fab.PlaceBottomRight();
        _fab.Present();
    }

    void OnSessionEnding(object sender, SessionEndingEventArgs e) => ExitThread();

    protected override void ExitThreadCore()
    {
        _stopListening = true;
        _checks?.Cancel();
        try
        {
            _openEvent.Set();
        }
        catch
        {
            // Событие уже закрыто.
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _tray.Visible = false;
        _tray.Dispose();
        _fab.Dispose();
        _openEvent.Dispose();
        base.ExitThreadCore();
    }
}
