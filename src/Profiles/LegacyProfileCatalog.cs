using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Translator;

internal sealed record SlicerPreset(string Name, JsonObject Settings);

internal sealed class LegacyProfileCatalog
{
    private readonly string installation;
    private readonly Dictionary<string, SlicerPreset> printers;
    private readonly Dictionary<string, SlicerPreset> processes;
    private readonly Dictionary<string, SlicerPreset> filaments;
    private static Task<LegacyProfileCatalog>? catalog;

    internal static Task<LegacyProfileCatalog> LoadAsync() =>
        catalog is null || catalog.IsFaulted || catalog.IsCanceled
            ? catalog = Task.Run(() => new LegacyProfileCatalog()) : catalog;

    private LegacyProfileCatalog()
    {
        installation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnycubicSlicerNext");
        if (!File.Exists(Path.Combine(installation, "AnycubicSlicerNext.exe")))
            throw new InvalidOperationException(T("The original slicer for this saved preset is unavailable. You can add a 3MF reference instead.",
                "Lo slicer originale di questo preset non è disponibile. Puoi aggiungere un 3MF di riferimento."));

        string bundled = Path.Combine(installation, "resources", "profiles", "Anycubic");
        string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnycubicSlicerNext");
        string updated = Path.Combine(roaming, "system", "Anycubic");
        string user = Path.Combine(roaming, "user");
        string[] locations = Directory.Exists(user) ? Directory.GetDirectories(user) : [];
        string[] roots = [bundled, updated, .. locations];
        printers = LoadPresets(roots, "machine");
        processes = LoadPresets(roots, "process");
        filaments = LoadPresets(roots, "filament");
    }

    private static bool SupportsBed(SlicerPreset printer, SlicerPreset filament, string bed) =>
        bed.Length == 0 || (Text(printer.Settings, "support_multi_bed_types") == "1" &&
            ReferenceConfiguration.PlateTemperatureKeys.TryGetValue(bed, out string? temperature) &&
            HasBedTemperature(filament.Settings, temperature) &&
            HasBedTemperature(filament.Settings, temperature + "_initial_layer"));

    private static bool HasBedTemperature(JsonObject filament, string key) =>
        filament[key] is JsonArray { Count: > 0 } values && ProcessSettings.StringValue(values[0]) is string value &&
        ProcessSettings.TryNumber(value, out double temperature) && temperature > 0;

    private static bool Compatible(SlicerPreset preset, SlicerPreset printer)
    {
        if (preset.Settings["compatible_printers"] is not JsonArray { Count: > 0 } compatible)
            return false;
        string? parent = Text(printer.Settings, "inherits");
        return compatible.Any(node => ProcessSettings.StringValue(node) is string name &&
            (name == printer.Name || name == parent));
    }

    private static string? Text(JsonObject profile, string key) => ProcessSettings.StringValue(profile[key]);

    private static bool TryHeight(string value, out decimal height) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out height) &&
        height is >= 0.01m and <= 1m;

    internal DestinationProfile Migrate(DestinationProfile profile)
    {
        if (!printers.TryGetValue(profile.Printer, out SlicerPreset? printer) ||
            !processes.TryGetValue(profile.Process, out SlicerPreset? process) || !Compatible(process, printer) ||
            Text(process.Settings, "layer_height") is not string layer || !TryHeight(layer, out _) ||
            !filaments.TryGetValue(profile.Filament, out SlicerPreset? filament) || !Compatible(filament, printer))
            throw new InvalidOperationException(T("A saved preset is no longer available for the selected printer.", "Uno dei preset salvati non è più disponibile per la stampante scelta."));
        if (!SupportsBed(printer, filament, profile.Bed))
            throw new InvalidOperationException(T("The selected build plate is unavailable for the selected filament.", "Il piatto scelto non è disponibile per il filamento selezionato."));
        if (profile.Slots is < 1 or > 4 || profile.LayerHeight is < 0.01m or > 1m)
            throw new InvalidOperationException(T("The destination profile has an invalid slot count or layer height.", "Il profilo di destinazione ha un numero di slot o un'altezza non validi."));
        if (printer.Settings["nozzle_diameter"] is not JsonArray { Count: 1 } nozzle ||
            !TryHeight(ProcessSettings.StringValue(nozzle[0]) ?? "", out decimal diameter) || profile.LayerHeight > diameter)
            throw new InvalidOperationException(T("The fallback layer height must be compatible with the printer nozzle.", "L'altezza di riserva deve essere compatibile con l'ugello della stampante."));

        DirectoryInfo scratch = ProfileStore.CreateMigrationFolder();
        try
        {
            string reference = GenerateReference(scratch.FullName, profile, printer, process, filament);
            return ProfileStore.ImportReference(profile.Name, reference, profile);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private string GenerateReference(string directory, DestinationProfile selection, SlicerPreset printer,
        SlicerPreset process, SlicerPreset filament)
    {
        string machineFile = WritePreset(directory, "machine", printer);
        string processFile = WritePreset(directory, "process", process);
        string filamentFile = WritePreset(directory, "filament", filament);
        string mesh = Path.Combine(installation, "resources", "handy_models", "XYZ 20mm Calibration Cube.stl");
        if (!File.Exists(mesh))
            throw new FileNotFoundException(T("The original slicer installation is missing its calibration model.", "Nell’installazione dello slicer originale manca il modello di calibrazione."));

        var start = new ProcessStartInfo(Path.Combine(installation, "AnycubicSlicerNext.exe"))
        {
            WorkingDirectory = directory, CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        string[] arguments =
        [
            "--debug", "2", "--load-settings", $"{machineFile};{processFile}",
            "--load-filaments", string.Join(';', Enumerable.Repeat(filamentFile, selection.Slots)),
            "--outputdir", directory, "--datadir", Path.Combine(directory, "slicer-data"),
            "--export-3mf", "reference.3mf", mesh
        ];
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process native = Process.Start(start) ?? throw new InvalidOperationException(T("Could not start the original slicer.", "Impossibile avviare lo slicer originale."));
        Task<string> standardOutput = native.StandardOutput.ReadToEndAsync();
        Task<string> standardError = native.StandardError.ReadToEndAsync();
        if (!native.WaitForExit(30_000))
        {
            native.Kill(entireProcessTree: true);
            native.WaitForExit();
            throw new InvalidOperationException(T("The original slicer took too long to prepare the profile.", "Lo slicer originale ha impiegato troppo tempo a preparare il profilo."));
        }

        string reference = Path.Combine(directory, "reference.3mf");
        if (native.ExitCode != 0 || !File.Exists(reference))
        {
            string detail = string.Join(Environment.NewLine,
                (standardError.GetAwaiter().GetResult() + Environment.NewLine + standardOutput.GetAwaiter().GetResult())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(6));
            throw new InvalidOperationException(T($"The original slicer could not generate the profile (exit code {native.ExitCode}). {detail}", $"Lo slicer originale non ha generato il profilo (codice {native.ExitCode}). {detail}"));
        }

        ConfigureReference(reference, selection);
        return reference;
    }

    private static string WritePreset(string directory, string kind, SlicerPreset preset)
    {
        string path = Path.Combine(directory, kind + ".json");
        JsonObject settings = (JsonObject)preset.Settings.DeepClone();
        settings["type"] = kind;
        settings["name"] = preset.Name;
        File.WriteAllText(path, settings.ToJsonString());
        return path;
    }

    private static void ConfigureReference(string reference, DestinationProfile selection)
    {
        JsonObject settings;
        using (var input = new ProjectArchive(reference))
        {
            input.Validate("generato");
            settings = input.ReadSettings();
        }
        if (Text(settings, "printer_settings_id") != selection.Printer || Text(settings, "print_settings_id") != selection.Process ||
            settings["filament_settings_id"] is not JsonArray ids || ids.Count != selection.Slots ||
            ids.Any(id => ProcessSettings.StringValue(id) != selection.Filament))
            throw new InvalidDataException(T("The original slicer returned different presets.", "Lo slicer originale ha restituito preset diversi."));

        settings["layer_height"] = selection.LayerHeight.ToString(CultureInfo.InvariantCulture);
        if (selection.Bed.Length > 0)
            settings["curr_bed_type"] = selection.Bed;
        if (settings["filament_colour"] is not JsonArray { Count: > 0 } colors ||
            ProcessSettings.StringValue(colors[0]) is not string firstColor)
            throw new InvalidDataException(T("The original slicer did not return a palette for the profile.", "Lo slicer originale non ha restituito una tavolozza per il profilo."));
        while (colors.Count < selection.Slots)
            colors.Add(firstColor);

        if (settings["flush_volumes_matrix"] is JsonArray matrix)
        {
            int dimension = (int)Math.Sqrt(matrix.Count);
            if (dimension * dimension == matrix.Count && dimension >= selection.Slots && dimension != selection.Slots)
            {
                var cropped = new JsonArray();
                for (int row = 0; row < selection.Slots; row++)
                    for (int column = 0; column < selection.Slots; column++)
                        cropped.Add(matrix[row * dimension + column]?.DeepClone());
                settings["flush_volumes_matrix"] = cropped;
            }
        }
        if (settings["flush_volumes_vector"] is JsonArray vector)
            while (vector.Count > selection.Slots * 2)
                vector.RemoveAt(vector.Count - 1);

        using var zip = ZipFile.Open(reference, ZipArchiveMode.Update);
        zip.GetEntry(ProjectArchive.Settings)!.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(ProjectArchive.Settings).Open());
        writer.Write(settings.ToJsonString());
    }

    private static Dictionary<string, SlicerPreset> LoadPresets(IEnumerable<string> roots, string kind)
    {
        var raw = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            string directory = Path.Combine(root, kind);
            if (!Directory.Exists(directory))
                continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    if (ReadPreset(File.ReadAllText(path)) is JsonObject settings && Text(settings, "name") is string name)
                        raw[name] = settings;
                }
                catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
                {
                    // An individual unreadable preset does not prevent browsing the others.
                }
            }
        }

        var result = new Dictionary<string, SlicerPreset>(StringComparer.Ordinal);
        foreach ((string name, JsonObject leaf) in raw)
        {
            if (Text(leaf, "instantiation") is "false" or "0")
                continue;
            var chain = new Stack<JsonObject>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? next = name;
            while (!string.IsNullOrEmpty(next) && visited.Add(next) && raw.TryGetValue(next, out JsonObject? current))
            {
                chain.Push(current);
                next = Text(current, "inherits");
            }
            if (!string.IsNullOrEmpty(next))
                continue;

            var merged = new JsonObject();
            foreach (JsonObject member in chain)
                foreach ((string key, JsonNode? value) in member)
                    merged[key] = value?.DeepClone();
            merged["inherits"] = leaf["inherits"]?.DeepClone() ?? JsonValue.Create("");
            if (kind == "machine" && Text(leaf, "printer_settings_id") is null)
                merged["printer_settings_id"] = name;
            if (kind == "process" && Text(leaf, "print_settings_id") is null)
                merged["print_settings_id"] = name;
            if (kind == "filament" && leaf["filament_settings_id"] is null)
                merged["filament_settings_id"] = new JsonArray(JsonValue.Create(name));
            result[name] = new SlicerPreset(name, merged);
        }
        return result;
    }

    private static JsonObject? ReadPreset(string input)
    {
        if (JsonNode.Parse(input) is not JsonObject settings)
            return null;
        try
        {
            _ = settings.Count;
            return settings;
        }
        catch (ArgumentException)
        {
            // Keep the last value when a preset repeats a top-level property.
            using JsonDocument document = JsonDocument.Parse(input);
            var normalized = new JsonObject();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                normalized[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            return normalized;
        }
    }
}
