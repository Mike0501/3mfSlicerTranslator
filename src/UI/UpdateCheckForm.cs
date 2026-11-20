using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using static Translator.AppTheme;

namespace Translator;

internal sealed class UpdateCheckForm : Form
{
    private readonly Label status = new()
    {
        AutoSize = true, MinimumSize = new Size(350, 0), MaximumSize = new Size(460, 0),
        Margin = new Padding(0, 0, 0, 15)
    };

    internal UpdateCheckForm()
    {
        Text = T("Application updates", "Aggiornamenti dell’app");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(18) };
        layout.Controls.Add(new Label
        {
            Text = T($"Installed version {AppUpdates.InstalledVersion}", $"Versione installata {AppUpdates.InstalledVersion}"),
            AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), Margin = new Padding(0, 0, 0, 10)
        });
        status.Text = T("Checking GitHub for updates…", "Ricerca aggiornamenti su GitHub…");
        layout.Controls.Add(status);
        var close = new RoundedButton { Text = T("Close", "Chiudi"), DialogResult = DialogResult.Cancel };
        var open = new RoundedButton(primary: true) { Text = T("Open release page", "Apri pagina della release") };
        open.Click += (_, _) =>
        {
            try
            {
                using Process? browser = Process.Start(new ProcessStartInfo(AppUpdates.ReleasesPage.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            {
                status.Text = T("Could not open the release page in the browser.", "Impossibile aprire la pagina della release nel browser.");
            }
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        actions.Controls.AddRange([close, open]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = close;
        ActiveControl = close;
        Shown += async (_, _) => await CheckForUpdates();
    }

    private async Task CheckForUpdates()
    {
        using var lifetime = new CancellationTokenSource();
        FormClosedEventHandler cancel = (_, _) => lifetime.Cancel();
        FormClosed += cancel;
        try
        {
            Version latest = await AppUpdates.CheckLatestVersion(lifetime.Token);
            if (lifetime.IsCancellationRequested)
                return;

            status.Text = latest > AppUpdates.InstalledVersion
                ? T($"Version {latest} is available.", $"La versione {latest} è disponibile.")
                : T("You have the latest release.", "Hai già la versione più recente.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            if (!lifetime.IsCancellationRequested)
                status.Text = T("Could not check for updates. You can still open the release page.",
                    "Impossibile verificare gli aggiornamenti. Puoi comunque aprire la pagina della release.");
        }
        finally
        {
            FormClosed -= cancel;
        }
    }
}
