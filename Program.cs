using System.Text;

namespace Translator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["convert", string source, string template, string destination, string report, ..] &&
            args.Length <= 6)
        {
            try
            {
                using var reportFile = new StreamWriter(new FileStream(report, FileMode.CreateNew));
                try
                {
                    string? preset = args.Length == 6 ? args[5] : null;
                    reportFile.WriteLine(FormatReport(ProjectConverter.Convert(source, template, destination, preset)));
                    return 0;
                }
                catch (Exception error)
                {
                    reportFile.WriteLine(error.Message);
                    return 1;
                }
            }
            catch (IOException)
            {
                return 1;
            }
        }
        if (args.Length != 0)
            return 2;

        ApplicationConfiguration.Initialize();
        AppTheme.ApplyNativeTheme();
        TranslatorSelection? selection = null;
        do
        {
            using var form = new TranslatorForm(selection);
            Application.Run(form);
            selection = form.RestartSelection;
        } while (selection is not null);
        return 0;
    }

    internal static string FormatReport(ConversionResult result)
    {
        var report = new StringBuilder()
            .AppendLine(T($"Source slicer · {result.Source}", $"Slicer di origine: {result.Source}"))
            .AppendLine(T($"Destination slicer · {result.Target}", $"Slicer del modello: {result.Target}"))
            .AppendLine(T($"Printer profile · {result.Printer}", $"Profilo stampante: {result.Printer}"))
            .AppendLine(T($"Source process settings · {result.Process}", $"Impostazioni prese da: {result.Process}"))
            .AppendLine(T($"Translated project process · {result.OutputProcess}", $"Processo nel progetto tradotto: {result.OutputProcess}"))
            .AppendLine(T($"Project layer height · {result.LayerHeight ?? "unspecified"} mm", $"Altezza strato nel progetto: {result.LayerHeight ?? "non specificata"} mm"))
            .AppendLine(T($"Plates · {result.Plates}   Imported slots · {result.Colors}   Layer changes · {result.LayerChanges}", $"Piatti: {result.Plates}   Slot importati: {result.Colors}   Cambi per layer: {result.LayerChanges}"))
            .AppendLine(result.Support switch
            {
                "1" => T("Global supports are enabled in the translated project.", "Supporti globali attivi nel processo importato."),
                "0" => T("Global supports are disabled in the translated project. Per-object settings apply separately.", "Supporti globali disattivati nel processo importato. Eventuali impostazioni per oggetto si applicano separatamente."),
                _ => T("The global support toggle comes from the destination profile.", "L'attivazione globale dei supporti proviene dal modello di destinazione.")
            })
            .AppendLine()
            .AppendLine(T($"Transferred settings ({result.Settings.Count})", $"Impostazioni trasferite ({result.Settings.Count})"))
            .AppendLine(string.Join(", ", result.Settings))
            .AppendLine()
            .AppendLine(T("Printer, machine limits, temperatures and filament profiles come from the destination profile.", "Stampante, limiti macchina, temperature e profili filamento provengono dal modello di destinazione."))
            .AppendLine(T("Open the result as a project and check the preview after slicing.", "Apri il risultato come progetto e verifica l'anteprima dopo lo slicing."));

        if (result.RetainedSettings.Count > 0)
        {
            report.AppendLine().AppendLine(T($"Destination settings kept by choice ({result.RetainedSettings.Count})",
                $"Impostazioni di destinazione mantenute per scelta ({result.RetainedSettings.Count})"));
            report.AppendLine(string.Join(", ", result.RetainedSettings));
        }

        if (result.Warnings.Count > 0)
        {
            report.AppendLine().AppendLine(T("To review", "Da verificare"));
            foreach (string warning in result.Warnings)
                report.AppendLine("• " + warning);
        }

        return report.ToString();
    }
}
