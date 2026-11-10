using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Translator;

internal sealed record SlicerPreset(string Name, JsonObject Settings);

internal sealed record CatalogMachine(LegacyProfileCatalog Catalog, string Brand, string Model, decimal Nozzle,
    string Preset, string Process, string Filament, decimal LayerHeight, string? DefaultPlate, IReadOnlyList<string> Plates)
{
    public override string ToString() => $"{Nozzle} mm · {Preset} ({Catalog.SlicerName})";
}

internal sealed class LegacyProfileCatalog
{
    private readonly string executable;
    private readonly Dictionary<string, SlicerPreset> printers;
    private readonly Dictionary<string, SlicerPreset> processes;
    private readonly Dictionary<string, SlicerPreset> filaments;
    private string SlicerExecutable => Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
    internal string SlicerName => SlicerExecutable switch
    {
        "orca-slicer" => "OrcaSlicer",
        "bambu-studio" => "Bambu Studio",
        _ => "Anycubic Slicer Next"
    };
    private static Task<LegacyProfileCatalog>? catalog;

    internal static Task<LegacyProfileCatalog> LoadAsync() =>
        catalog is null || catalog.IsFaulted || catalog.IsCanceled
            ? catalog = Task.Run(LoadLegacy) : catalog;

    private static LegacyProfileCatalog LoadLegacy()
    {
        string installation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnycubicSlicerNext");
        string executable = Path.Combine(installation, "AnycubicSlicerNext.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException(T("The original slicer for this saved preset is unavailable. You can add a 3MF reference instead.",
                "Lo slicer originale di questo preset non è disponibile. Puoi aggiungere un 3MF di riferimento."));

        string bundled = Path.Combine(installation, "resources", "profiles", "Anycubic");
        string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnycubicSlicerNext");
        string updated = Path.Combine(roaming, "system", "Anycubic");
        string user = Path.Combine(roaming, "user");
        string[] locations = Directory.Exists(user) ? Directory.GetDirectories(user) : [];
        return new LegacyProfileCatalog(executable, [bundled, updated, .. locations]);
    }

    private LegacyProfileCatalog(string executable, string[] roots)
    {
        this.executable = executable;
        printers = LoadPresets(roots, "machine");
        processes = LoadPresets(roots, "process");
        var sharedFilaments = roots.Select(root => Path.Combine(Path.GetDirectoryName(root)!, "OrcaFilamentLibrary"));
        filaments = LoadPresets(sharedFilaments.Concat(roots).Distinct(StringComparer.OrdinalIgnoreCase), "filament");
    }

    internal static IEnumerable<string> InstalledSlicers()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        string[] paths = ["AnycubicSlicerNext/AnycubicSlicerNext.exe", "OrcaSlicer/orca-slicer.exe", "Bambu Studio/bambu-studio.exe"];
        return new[] { programFiles, localPrograms }.SelectMany(root => paths.Select(path => Path.Combine(root, path)))
            .Concat(paths.Select(path => ReadSlicerLocation(Path.GetFileName(path))))
            .Concat(WorkspaceOrcaLocations())
            .OfType<string>().Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> WorkspaceOrcaLocations()
    {
        // Release builds launched from this checkout can use its portable tools.
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "3MFTranslator.csproj")))
                continue;
            string tools = Path.Combine(directory.FullName, ".local", "slicers");
            if (Directory.Exists(tools))
                foreach (string location in Directory.EnumerateDirectories(tools, "OrcaSlicer*"))
                    yield return Path.Combine(location, "orca-slicer.exe");
            yield break;
        }
    }

    private static string SlicerLocationFile(string executable) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3MFTranslator", Path.GetFileName(executable) + ".location");

    private static string? ReadSlicerLocation(string executable)
    {
        try { return File.ReadAllText(SlicerLocationFile(executable)).Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static void RememberSlicerLocation(string executable)
    {
        try
        {
            string path = SlicerLocationFile(executable);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Path.GetFullPath(executable));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // This location is still available for the current dialog.
        }
    }

    internal static CatalogMachine[] ReadMachines(string executable)
    {
        string app = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant() switch
        {
            "orca-slicer" => "OrcaSlicer",
            "bambu-studio" => "BambuStudio",
            "anycubicslicernext" => "AnycubicSlicerNext",
            _ => throw new InvalidDataException(T("Choose a supported slicer executable.", "Scegli l’eseguibile di uno slicer supportato."))
        };
        string resources = Path.Combine(Path.GetDirectoryName(executable)!, "resources", "profiles");
        string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app, "system");
        string[] vendors = new[] { resources, system }.Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories).Select(path => Path.GetFileName(path)!)
            .Where(name => !name.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return vendors.SelectMany(vendor =>
        {
            string[] roots = [Path.Combine(resources, vendor), Path.Combine(system, vendor)];
            var source = new LegacyProfileCatalog(executable, roots);
            return source.GetMachines(vendor, roots);
        }).OrderBy(machine => machine.Brand).ThenBy(machine => machine.Model).ThenBy(machine => machine.Nozzle).ToArray();
    }

    private IEnumerable<CatalogMachine> GetMachines(string vendor, string[] roots)
    {
        string brand = vendor == "BBL" ? "Bambu Lab" : vendor;
        foreach (SlicerPreset printer in printers.Values)
        {
            JsonObject settings = printer.Settings;
            if (Text(settings, "printer_technology") != "FFF" || Text(settings, "printer_model") is not { Length: > 0 } model ||
                settings["nozzle_diameter"] is not JsonArray { Count: 1 } diameters ||
                !TryHeight(ProcessSettings.StringValue(diameters[0]) ?? "", out decimal nozzle))
                continue;
            string? defaultProcess = Text(settings, "default_print_profile");
            string? defaultFilament = (settings["default_filament_profile"] as JsonArray)?.Select(ProcessSettings.StringValue).FirstOrDefault();
            var process = SelectPreset(processes, printer, defaultProcess, preset =>
                TryHeight(Text(preset.Settings, "layer_height") ?? "", out decimal height) && height <= nozzle);
            var filament = SelectPreset(filaments, printer, defaultFilament, _ => true);
            if (process is null || filament is null || !TryHeight(Text(process.Settings, "layer_height") ?? "", out decimal layer))
                continue;
            string[] plates = ReferenceConfiguration.PlateTemperatureKeys.Keys
                .Where(bed => SupportsBed(printer, filament, bed, vendor == "BBL")).ToArray();
            string? defaultPlate = Text(settings, "default_bed_type");
            if (string.IsNullOrWhiteSpace(defaultPlate) &&
                model.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !model.EndsWith('.'))
            {
                foreach (string root in roots.Reverse())
                {
                    string path = Path.Combine(root, "machine", model + ".json");
                    if (File.Exists(path) && ReadPreset(File.ReadAllText(path)) is JsonObject modelSettings &&
                        Text(modelSettings, "default_bed_type") is { Length: > 0 } found)
                    {
                        defaultPlate = found;
                        break;
                    }
                }
            }
            yield return new(this, brand, model, nozzle, printer.Name, process.Name, filament.Name, layer, defaultPlate, plates);
        }
    }

    private static SlicerPreset? SelectPreset(Dictionary<string, SlicerPreset> presets, SlicerPreset printer,
        string? preferred, Func<SlicerPreset, bool> valid) =>
        preferred is not null && presets.TryGetValue(preferred, out SlicerPreset? preset) && valid(preset)
            ? preset : presets.Values.FirstOrDefault(candidate => Compatible(candidate, printer) && valid(candidate));

    internal DestinationProfile Create(CatalogMachine machine, string name, string plate, int slots)
    {
        if (machine.Catalog != this || slots is < 1 or > 4 || (plate.Length > 0 && !machine.Plates.Contains(plate)))
            throw new InvalidDataException(T("Choose a valid printer, plate and slot count.", "Scegli stampante, piatto e numero di slot validi."));
        string effectivePlate = plate.Length == 0 ? machine.DefaultPlate ?? "" : plate;
        var profile = new DestinationProfile(name, machine.Preset, machine.Process, machine.Filament, effectivePlate, machine.LayerHeight, slots);
        return SaveReference(profile, printers[machine.Preset], processes[machine.Process], filaments[machine.Filament], previous: null);
    }

    private static bool SupportsBed(SlicerPreset printer, SlicerPreset filament, string bed, bool isBambu = false) =>
        bed.Length == 0 || (ReferenceConfiguration.SupportsMultiplePlates(printer.Settings, isBambu) &&
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

        return SaveReference(profile, printer, process, filament, profile);
    }

    private DestinationProfile SaveReference(DestinationProfile profile, SlicerPreset printer, SlicerPreset process,
        SlicerPreset filament, DestinationProfile? previous)
    {
        DirectoryInfo scratch = ProfileStore.CreateMigrationFolder();
        try
        {
            string reference = GenerateReference(scratch.FullName, profile, printer, process, filament);
            return ProfileStore.ImportReference(profile.Name, reference, previous);
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
        string mesh = Path.Combine(Path.GetDirectoryName(executable)!, "resources", "handy_models", "XYZ 20mm Calibration Cube.stl");
        if (!File.Exists(mesh))
            mesh = WriteCalibrationMesh(directory);

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        bool exportSettings = SlicerExecutable == "orca-slicer";
        string settingsFile = Path.Combine(directory, "project.json");
        string reference = Path.Combine(directory, "reference.3mf");
        string[] arguments =
        [
            "--debug", "2", "--load-settings", $"{machineFile};{processFile}",
            "--load-filaments", string.Join(';', Enumerable.Repeat(filamentFile, selection.Slots)),
            "--outputdir", directory, "--datadir", Path.Combine(directory, "slicer-data"),
            .. (exportSettings ? new[] { "--export-settings", settingsFile } : ["--export-3mf", "reference.3mf"]),
            mesh
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

        if (native.ExitCode != 0 || !File.Exists(exportSettings ? settingsFile : reference))
        {
            string detail = string.Join(Environment.NewLine,
                (standardError.GetAwaiter().GetResult() + Environment.NewLine + standardOutput.GetAwaiter().GetResult())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(6));
            throw new InvalidOperationException(T($"The original slicer could not generate the profile (exit code {native.ExitCode}). {detail}", $"Lo slicer originale non ha generato il profilo (codice {native.ExitCode}). {detail}"));
        }

        if (exportSettings)
            WriteOrcaReference(reference, File.ReadAllText(settingsFile));
        ConfigureReference(reference, selection);
        return reference;
    }

    private static void WriteOrcaReference(string path, string settings)
    {
        // A destination needs effective project settings and a valid model package.
        // Export settings separately so Orca does not render thumbnails with OpenGL.
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(ProjectArchive.Settings, settings);
        Write(ProjectArchive.ContentTypes, """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>
              <Default Extension="config" ContentType="application/octet-stream"/>
            </Types>
            """);
        Write(ProjectArchive.RootRelationships, """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>
            </Relationships>
            """);
        string version = Text(JsonNode.Parse(settings)!.AsObject(), "version") ?? "";
        var core = ProjectArchive.Core;
        var model = System.Xml.Linq.XDocument.Parse("""
            <model unit="millimeter" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
              <metadata name="Application"/>
              <metadata name="BambuStudio:3mfVersion">1</metadata>
              <resources>
                <object id="1" type="model" name="Reference">
                  <mesh>
                    <vertices>
                      <vertex x="0" y="0" z="0"/><vertex x="10" y="0" z="0"/>
                      <vertex x="0" y="10" z="0"/><vertex x="0" y="0" z="10"/>
                    </vertices>
                    <triangles>
                      <triangle v1="0" v2="2" v3="1"/><triangle v1="0" v2="1" v3="3"/>
                      <triangle v1="0" v2="3" v3="2"/><triangle v1="1" v2="2" v3="3"/>
                    </triangles>
                  </mesh>
                </object>
              </resources>
              <build><item objectid="1"/></build>
            </model>
            """);
        model.Root!.Element(core + "metadata")!.Value = "OrcaSlicer-" + version;
        ProjectArchive.WriteXml(zip, ProjectArchive.Model, model);
        Write(ProjectArchive.ModelSettings, """
            <config>
              <object id="1"><metadata key="name" value="Reference"/><metadata key="extruder" value="1"/></object>
              <plate><metadata key="plater_id" value="1"/></plate>
            </config>
            """);

        void Write(string name, string content)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private static string WriteCalibrationMesh(string directory)
    {
        string path = Path.Combine(directory, "reference.stl");
        string[] vertices = ["0 0 0", "10 0 0", "0 10 0", "0 0 10"];
        int[][] faces = [[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]];
        using var writer = File.CreateText(path);
        writer.WriteLine("solid reference");
        foreach (int[] face in faces)
        {
            writer.WriteLine("facet normal 0 0 0\nouter loop");
            foreach (int vertex in face)
                writer.WriteLine("vertex " + vertices[vertex]);
            writer.WriteLine("endloop\nendfacet");
        }
        writer.WriteLine("endsolid reference");
        return path;
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
