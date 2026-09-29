using System.Drawing;
using System.Windows.Forms;
using MhwModManager.Updater;

namespace MhwModManager.Updater.Helper;

internal sealed class UpdaterProgressHost : IDisposable
{
    private readonly ManualResetEventSlim ready = new(false);
    private readonly Thread uiThread;
    private UpdaterProgressForm? form;
    private Exception? startupFailure;
    private bool terminalSignaled;
    private bool failureSignaled;
    private bool disposed;

    public UpdaterProgressHost(string targetVersion)
    {
        uiThread = new Thread(() => RunUi(targetVersion))
        {
            Name = "MHW Mod Manager updater progress UI",
            IsBackground = true
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Timed out starting the updater progress window.");
        if (startupFailure is not null)
            throw new InvalidOperationException(
                "Could not start the updater progress window.",
                startupFailure);
    }

    public void ReportStep(int step, string title, string detail)
    {
        Post(window => window.ReportStep(step, title, detail));
    }

    public void ReportRestartStage(UpdateRestartStage stage)
    {
        switch (stage)
        {
            case UpdateRestartStage.LaunchingUpdatedApplication:
                ReportStep(
                    3,
                    "Starting the updated app",
                    "Launching the new MHW Manual Mod Manager build...");
                break;
            case UpdateRestartStage.VerifyingUpdatedApplication:
                ReportStep(
                    4,
                    "Verifying the update",
                    "Waiting for the updated app to report a healthy startup...");
                break;
            case UpdateRestartStage.ConfirmingUpdate:
                ReportStep(
                    4,
                    "Finalizing the update",
                    "The new build started successfully. Finalizing the installation...");
                break;
            case UpdateRestartStage.RecoveringPreviousApplication:
                ReportStep(
                    4,
                    "Recovering safely",
                    "The new build did not start correctly. Restoring the previous version...");
                break;
            case UpdateRestartStage.RestoredPreviousApplication:
                ReportStep(
                    4,
                    "Previous version restored",
                    "The previous MHW Manual Mod Manager build was restored safely.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
        }
    }

    public void Succeed(string detail)
    {
        terminalSignaled = true;
        Post(window => window.ShowSuccess(detail));
    }

    public void Fail(string title, string detail)
    {
        terminalSignaled = true;
        failureSignaled = true;
        Post(window => window.ShowFailure(title, detail));
    }

    private void RunUi(string targetVersion)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            using var window = new UpdaterProgressForm(targetVersion);
            Volatile.Write(ref form, window);
            window.Shown += (_, _) => ready.Set();
            Application.Run(window);
        }
        catch (Exception ex)
        {
            startupFailure = ex;
            ready.Set();
        }
        finally
        {
            ready.Set();
            Volatile.Write(ref form, null);
        }
    }

    private void Post(Action<UpdaterProgressForm> action)
    {
        var window = Volatile.Read(ref form);
        if (window is null || window.IsDisposed) return;

        try
        {
            if (window.InvokeRequired)
                window.BeginInvoke(new Action(() => action(window)));
            else
                action(window);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (!terminalSignaled)
            Post(window => window.CloseFromHost());

        if (uiThread.IsAlive && Thread.CurrentThread != uiThread)
        {
            var timeout = failureSignaled
                ? TimeSpan.FromSeconds(11)
                : TimeSpan.FromSeconds(2);
            uiThread.Join(timeout);
        }

        ready.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class UpdaterProgressForm : Form
{
    private const int TotalSteps = 4;

    private readonly Label titleLabel;
    private readonly Label detailLabel;
    private readonly Label stepLabel;
    private readonly ProgressBar activityBar;
    private readonly Button closeButton;
    private readonly System.Windows.Forms.Timer terminalCloseTimer;
    private bool allowClose;

    public UpdaterProgressForm(string targetVersion)
    {
        Text = "MHW Mod Manager Updater";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ClientSize = new Size(540, 250);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.White;

        var versionLabel = new Label
        {
            AutoSize = false,
            Location = new Point(28, 22),
            Size = new Size(484, 24),
            Text = string.IsNullOrWhiteSpace(targetVersion)
                ? "MHW Manual Mod Manager update"
                : $"Updating MHW Manual Mod Manager to v{targetVersion}",
            ForeColor = Color.FromArgb(185, 185, 195)
        };

        titleLabel = new Label
        {
            AutoSize = false,
            Location = new Point(28, 56),
            Size = new Size(484, 34),
            Text = "Preparing update",
            ForeColor = Color.White
        };

        detailLabel = new Label
        {
            AutoSize = false,
            Location = new Point(28, 96),
            Size = new Size(484, 48),
            Text = "The updater is preparing the verified update package.",
            ForeColor = Color.FromArgb(215, 215, 222)
        };

        activityBar = new ProgressBar
        {
            Location = new Point(28, 158),
            Size = new Size(484, 18),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            AccessibleName = "Update activity"
        };

        stepLabel = new Label
        {
            AutoSize = false,
            Location = new Point(28, 185),
            Size = new Size(330, 24),
            Text = $"Step 1 of {TotalSteps}",
            ForeColor = Color.FromArgb(165, 165, 176)
        };

        closeButton = new Button
        {
            Location = new Point(402, 184),
            Size = new Size(110, 32),
            Text = "Close",
            Visible = false
        };
        closeButton.Click += (_, _) =>
        {
            allowClose = true;
            Close();
        };

        terminalCloseTimer = new System.Windows.Forms.Timer();
        terminalCloseTimer.Tick += (_, _) =>
        {
            terminalCloseTimer.Stop();
            allowClose = true;
            Close();
        };

        Controls.Add(versionLabel);
        Controls.Add(titleLabel);
        Controls.Add(detailLabel);
        Controls.Add(activityBar);
        Controls.Add(stepLabel);
        Controls.Add(closeButton);
    }

    public void ReportStep(int step, string title, string detail)
    {
        var boundedStep = Math.Clamp(step, 1, TotalSteps);
        titleLabel.Text = title;
        detailLabel.Text = detail;
        stepLabel.Text = $"Step {boundedStep} of {TotalSteps}";
        activityBar.Style = ProgressBarStyle.Marquee;
        activityBar.MarqueeAnimationSpeed = 24;
        closeButton.Visible = false;
    }

    public void ShowSuccess(string detail)
    {
        titleLabel.Text = "Update complete";
        detailLabel.Text = detail;
        stepLabel.Text = $"Step {TotalSteps} of {TotalSteps}";
        activityBar.MarqueeAnimationSpeed = 0;
        activityBar.Style = ProgressBarStyle.Blocks;
        activityBar.Value = 100;
        closeButton.Visible = false;
        terminalCloseTimer.Interval = 900;
        terminalCloseTimer.Start();
    }

    public void ShowFailure(string title, string detail)
    {
        titleLabel.Text = title;
        detailLabel.Text = detail;
        stepLabel.Text = "Update stopped";
        activityBar.MarqueeAnimationSpeed = 0;
        activityBar.Style = ProgressBarStyle.Blocks;
        activityBar.Value = 0;
        closeButton.Visible = true;
        TopMost = true;
        Activate();
        terminalCloseTimer.Interval = 10000;
        terminalCloseTimer.Start();
    }

    public void CloseFromHost()
    {
        allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            terminalCloseTimer.Dispose();

        base.Dispose(disposing);
    }
}
