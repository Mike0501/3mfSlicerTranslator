using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Translator.ProjectArchive;

namespace Translator;

internal sealed record ConversionResult(string Source, string Target, string Printer, int Plates, int Colors,
    int LayerChanges, string Process, string OutputProcess, string? LayerHeight, string? Support,
    IReadOnlyList<string> Settings, IReadOnlyList<string> RetainedSettings, IReadOnlyList<string> Warnings);

internal sealed record SourceProcess(string? Entry, string Label)
{
    public override string ToString() => Label;
}

internal static class ProjectConverter
{
    private const string TranslatedPreset = "Metadata/process_settings_1.config";
    private const string CrealityConfiguration = "Metadata/creality.config";
    private static readonly HashSet<string> GeometryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".model", ".rels", ".svg", ".png", ".jpeg", ".jpg"
    };

    private static readonly HashSet<string> ObjectMetadata = new(StringComparer.Ordinal)
    {
        "name", "matrix", "source_object_id", "source_volume_id", "source_offset_x", "source_offset_y",
        "source_offset_z", "source_in_inches", "source_in_meters", "mesh_shared", "volume_type", "part_type"
    };

    private static readonly HashSet<string> PlateMetadata = new(StringComparer.Ordinal)
    {
        "plater_id", "plater_name", "locked", "print_sequence", "first_layer_print_sequence",
        "other_layers_print_sequence", "other_layers_print_sequence_nums", "thumbnail_file",
        "thumbnail_no_light_file", "top_file", "pick_file"
    };

    internal static List<SourceProcess> GetSourceProcesses(string sourcePath)
    {
        using var source = new ProjectArchive(sourcePath);
        if (PrusaProject.Detect(source) is PrusaProject legacy)
        {
            source.ValidateStructure(T("source", "di origine"));
            return [new(null, T("Active settings · ", "Impostazioni attive · ") + ProcessName(legacy.ReadSettings(source)))];
        }
        source.Validate(T("source", "di origine"));
        JsonObject active = source.ReadSettings();
        var processes = new List<SourceProcess> { new(null, T("Active settings · ", "Impostazioni attive · ") + ProcessName(active)) };
        foreach (string entry in source.Names.Where(IsProcessPreset).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                processes.Add(new(entry, T("Embedded preset · ", "Preset incorporato · ") + ProcessName(source.ReadSettings(entry))));
            }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                // A damaged optional preset should not prevent using the active project settings.
            }
        }
        return processes;
    }

    private static string ProcessName(JsonObject profile) =>
        ProcessSettings.StringValue(profile["print_settings_id"]) ??
        ProcessSettings.StringValue(profile["name"]) ?? T("Unnamed profile", "Profilo senza nome");

    internal static string GetReferencePrinter(string path)
    {
        using var reference = new ProjectArchive(path);
        reference.Validate(T("reference", "modello"));
        JsonObject profile = reference.ReadSettings();
        string printer = ReadPrinterName(profile);
        _ = ReadDestinationNozzle(profile);
        return printer;
    }

    internal static ReferenceConfiguration ReadReferenceConfiguration(string path) =>
        ReadReferenceConfiguration(ReadReferenceSettings(path));

    internal static ReferenceConfiguration PrepareReferenceCopy(string path, ReferenceMachine? expectedMachine,
        string? buildPlate)
    {
        JsonObject settings = ReadReferenceSettings(path);
        ReferenceConfiguration configuration = ReadReferenceConfiguration(settings);
        if (expectedMachine is not null && expectedMachine != configuration.Machine)
            throw new InvalidDataException(T("The reference printer or nozzle has changed. Choose the reference again.",
                "La stampante o l’ugello del riferimento sono cambiati. Scegli di nuovo il riferimento."));
        if (buildPlate is null || buildPlate == configuration.CurrentPlate)
            return configuration;
        if (!configuration.Plates.Contains(buildPlate))
            throw new InvalidDataException(T("The selected build plate is unavailable for this reference.",
                "Il piatto scelto non è disponibile per questo riferimento."));

        settings["curr_bed_type"] = buildPlate;
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        archive.Entries.Single(entry => entry.FullName.Equals(Settings, StringComparison.OrdinalIgnoreCase)).Delete();
        WriteJson(archive, Settings, settings);
        return configuration with { CurrentPlate = buildPlate };
    }

    private static JsonObject ReadReferenceSettings(string path)
    {
        using var reference = new ProjectArchive(path);
        reference.Validate(T("reference", "modello"));
        return reference.ReadSettings();
    }

    private static ReferenceConfiguration ReadReferenceConfiguration(JsonObject settings) =>
        ReferenceConfiguration.Read(settings, ReadPrinterName(settings), ReadDestinationNozzle(settings));

    internal static (PrintPreset Values, LayerHeightRange Range) GetDestinationPreset(DestinationProfile profile)
    {
        if (profile.ReferencePath is not string path)
            throw new InvalidDataException(T("This preset needs a 3MF reference.", "Questo preset richiede un riferimento 3MF."));
        using var reference = new ProjectArchive(path);
        reference.Validate(T("reference", "modello"));
        JsonObject settings = reference.ReadSettings();
        if (ReadPrinterName(settings) != profile.Printer)
            throw new InvalidDataException(T("The reference printer has changed. Add the reference again.",
                "La stampante del riferimento è cambiata. Aggiungi di nuovo il riferimento."));
        _ = ReadDestinationNozzle(settings);
        var range = LayerHeightRange.Read(settings);
        profile.Preset?.Apply(settings, range);
        return (PrintPreset.Read(settings), range);
    }

    internal static ConversionResult Convert(string sourcePath, string templatePath, string outputPath,
        string? sourcePresetEntry = null, ReviewSettings? review = null, PrintPreset? destinationPreset = null,
        ReviewFilamentSlots? reviewSlots = null)
    {
        outputPath = Path.GetFullPath(outputPath);
        if (File.Exists(outputPath))
            throw new IOException(T("Choose a new name for the output file.", "Scegli un nome nuovo per il file di destinazione."));
        if (!Path.GetExtension(outputPath).Equals(".3mf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(T("The output file name must end in .3mf.", "Il nome del file di destinazione deve terminare con .3mf."));

        using var source = new ProjectArchive(sourcePath);
        using var template = new ProjectArchive(templatePath);
        PrusaProject? legacy = PrusaProject.Detect(source);
        if (legacy is null)
            source.Validate(T("source", "di origine"));
        else
            source.ValidateStructure(T("source", "di origine"));
        template.Validate(T("reference", "modello"));

        var warnings = new List<string>();
        JsonObject original = legacy?.ReadSettings(source, warnings) ?? source.ReadSettings();
        if (sourcePresetEntry is not null && (legacy is not null || !IsProcessPreset(sourcePresetEntry)))
            throw new InvalidDataException(T("Choose a process preset embedded in the source project.", "Scegli un preset di processo incorporato nel progetto di origine."));
        JsonObject? preset = sourcePresetEntry is null ? null : source.ReadSettings(sourcePresetEntry);
        JsonObject target = template.ReadSettings();
        int sourceNozzles = ValidatePrinters(original, target);
        IReadOnlyList<string> presetChanges = destinationPreset?.Apply(target, LayerHeightRange.Read(target)) ?? [];
        string printer = ProcessSettings.StringValue(target["printer_settings_id"])!;
        string[] targetColors = ReadPalette(target);
        string[] originalColors = legacy is not null && original["filament_colour"] is null ? [targetColors[0]] : ReadPalette(original);
        if (legacy is not null && original["filament_colour"] is null)
            warnings.Add(T("The source project has no filament color. The first destination color was used.", "Il progetto di origine non specifica un colore filamento. È stato usato il primo colore di destinazione."));
        XDocument model = source.ReadXml(Model);
        XElement modelRoot = ReadModelRoot(model);
        XElement targetModel = ReadModelRoot(template.ReadXml(Model));
        string sourceApp = ReadApplication(modelRoot);
        string targetApp = ReadApplication(targetModel);
        bool crealityDestination = template.Contains(CrealityConfiguration);
        modelRoot.Elements(Core + "metadata").Single(node => (string?)node.Attribute("name") == "Application").Value = targetApp;

        XDocument modelSettings = legacy?.TranslateModel(source, modelRoot, warnings) ?? source.ReadXml(ModelSettings);
        var remappedMeshes = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        int[] sourceSlots = SourceFilaments.Fit(source, model, modelSettings, original, preset,
            originalColors, targetColors.Length, warnings, reviewSlots, remappedMeshes);
        string[] colors = sourceSlots.Select((slot, index) => slot == 0 ? targetColors[index] : originalColors[slot - 1]).ToArray();
        var variantSlots = reviewSlots is not null && originalColors.Length > targetColors.Length
            ? Enumerable.Range(1, originalColors.Length).ToArray() : sourceSlots;
        var variant = SourceFilaments.ActiveVariant(original, modelSettings, variantSlots, originalColors.Length);
        if (sourceNozzles > 1)
            warnings.Add(T($"A project with {sourceNozzles} nozzles was adapted to one nozzle. Check filament changes in the preview.", $"Progetto da {sourceNozzles} ugelli adattato a un solo ugello. Verifica i cambi di filamento nell'anteprima."));
        CopyPalette(original, target, colors, targetColors, sourceSlots, warnings);
        var retainedSettings = new List<string>();
        List<string> settings = ProcessSettings.Transfer(original, preset, target, colors.Length, warnings, retainedSettings, variant, review, crealityDestination);
        JsonObject translatedPreset = BuildTranslatedPreset(target, template, outputPath, targetColors.Length, settings.Concat(presetChanges));

        int plates = SanitizeModelSettings(modelSettings, target, colors.Length, warnings, crealityDestination);
        (XDocument? events, int layerChanges) = FilterLayerEvents(source, plates, colors.Length, warnings);
        if (source.Contains("Metadata/layer_config_ranges.xml"))
            warnings.Add(T("Height range settings were skipped.", "Le impostazioni per intervalli di altezza non sono state trasferite."));
        if (source.Names.Any(name => name.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase)))
            warnings.Add(T("Previously generated G-code was removed. Slice the project again.", "Il G-code già elaborato è stato rimosso. Esegui di nuovo lo slicing."));
        if (!JsonNode.DeepEquals(original["printable_area"], target["printable_area"]) ||
            !JsonNode.DeepEquals(original["printable_height"], target["printable_height"]))
            warnings.Add(T("The build volume differs. Check object positions and the footprint on each plate in the slicer.", "Il volume di stampa è diverso. Controlla la posizione degli oggetti e l'ingombro di ogni piatto nello slicer."));

        var retained = source.Names.Where(KeepSourcePart).ToHashSet(StringComparer.OrdinalIgnoreCase);
        retained.UnionWith([Model, Settings, ModelSettings, TranslatedPreset]);
        if (crealityDestination)
            retained.Add(CrealityConfiguration);
        if (events is not null)
            retained.Add(LayerEvents);

        string temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".3mftranslator-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew))
            using (var output = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string name in source.Names.Where(KeepSourcePart))
                {
                    if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                        WriteXml(output, name, FilterRelationships(source.ReadXml(name), name, retained));
                    else if (name.Equals(ContentTypes, StringComparison.OrdinalIgnoreCase))
                        WriteXml(output, name, FilterContentTypes(source.ReadXml(name), retained, crealityDestination));
                    else if (remappedMeshes.TryGetValue(name, out XDocument? mesh))
                        WriteXml(output, name, mesh);
                    else
                        source.CopyTo(name, output);
                }

                foreach (string name in template.Names.Where(IsEmbeddedHardwarePreset))
                    template.CopyTo(name, output);
                if (crealityDestination)
                    template.CopyTo(CrealityConfiguration, output);

                WriteXml(output, Model, model);
                WriteXml(output, ModelSettings, modelSettings);
                if (events is not null)
                    WriteXml(output, LayerEvents, events);
                WriteJson(output, Settings, target);
                WriteJson(output, TranslatedPreset, translatedPreset);
            }

            File.Move(temporaryPath, outputPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        string? support = ProcessSettings.StringValue(target["enable_support"]);
        return new ConversionResult(sourceApp, targetApp, printer, plates, colors.Length, layerChanges,
            ProcessName(preset ?? original), ProcessName(target), ProcessSettings.StringValue(target["layer_height"]),
            support, settings, retainedSettings, warnings);
    }

    private static int ValidatePrinters(JsonObject source, JsonObject target)
    {
        foreach (JsonObject profile in new[] { source, target })
        {
            _ = ReadPrinterName(profile);
            if (profile["filament_is_mixed"] is JsonArray mixed &&
                mixed.Any(node => ProcessSettings.StringValue(node) is "1" or "true"))
                throw new InvalidDataException(T("Mixed virtual filaments are unsupported.", "I filamenti virtuali miscelati non sono supportati."));
        }

        double targetDiameter = ReadDestinationNozzle(target);
        if (source["nozzle_diameter"] is not JsonArray { Count: > 0 } sourceNozzles ||
            sourceNozzles.Any(nozzle => ProcessSettings.StringValue(nozzle) is not string value ||
                !ProcessSettings.TryNumber(value, out double diameter) || diameter != targetDiameter))
            throw new InvalidDataException(T("Every nozzle in the source project must match the destination nozzle diameter.", "Tutti gli ugelli del progetto di origine devono avere lo stesso diametro di quello di destinazione."));
        return sourceNozzles.Count;
    }

    private static string ReadPrinterName(JsonObject profile) =>
        ProcessSettings.StringValue(profile["printer_technology"]) == "FFF" &&
        ProcessSettings.StringValue(profile["printer_settings_id"]) is string name && !string.IsNullOrWhiteSpace(name)
            ? name : throw new InvalidDataException(T("The project must contain an FFF printer profile.", "Il progetto deve contenere un profilo stampante FFF."));

    private static double ReadDestinationNozzle(JsonObject target)
    {
        if (target["nozzle_diameter"] is not JsonArray { Count: 1 } nozzles)
            throw new InvalidDataException(T("Choose a destination profile with one physical nozzle.", "Scegli un profilo di destinazione con un solo ugello fisico."));
        if (ProcessSettings.StringValue(nozzles[0]) is not string value ||
            !ProcessSettings.TryNumber(value, out double diameter) || diameter <= 0)
            throw new InvalidDataException(T("The destination nozzle diameter is invalid.", "Il diametro dell’ugello di destinazione non è valido."));
        return diameter;
    }

    private static string[] ReadPalette(JsonObject profile)
    {
        if (profile["filament_colour"] is not JsonArray { Count: > 0 } palette)
            throw new InvalidDataException(T("The profile does not include filament slots.", "Il profilo non contiene gli slot dei filamenti."));

        return palette.Select(node => ProcessSettings.StringValue(node) is string color && IsColor(color)
            ? color
            : throw new InvalidDataException(T("The palette contains an invalid color.", "La tavolozza contiene un colore non valido."))).ToArray();
    }

    private static bool IsColor(string value) => Regex.IsMatch(value, @"\A#[0-9a-fA-F]{6}\z");

    private static void CopyPalette(JsonObject source, JsonObject target, string[] colors, string[] targetColors,
        int[] sourceSlots, List<string> warnings)
    {
        var palette = (JsonArray)target["filament_colour"]!;
        for (int i = 0; i < colors.Length; i++)
            palette[i] = colors[i];

        if (source["filament_type"] is JsonArray sourceTypes && target["filament_type"] is JsonArray targetTypes &&
            (sourceTypes.Count < sourceSlots.Max() || targetTypes.Count < colors.Length ||
             Enumerable.Range(0, colors.Length).Any(index => sourceSlots[index] > 0 &&
                 !JsonNode.DeepEquals(sourceTypes[sourceSlots[index] - 1], targetTypes[index]))))
            warnings.Add(T("Some materials differ. Filament profiles and temperatures come from the destination reference.", "Alcuni materiali differiscono. I profili filamento e le temperature provengono dal modello di destinazione."));
        if (colors.Length < targetColors.Length)
            warnings.Add(T("The extra destination slots are available in the slicer.", "Gli slot extra del modello restano disponibili nello slicer."));
        if (colors.Length > 1 && !colors.SequenceEqual(targetColors.Take(colors.Length), StringComparer.OrdinalIgnoreCase))
            warnings.Add(T("Purge volumes come from the destination reference. Check them for the imported colors.", "I volumi di spurgo provengono dal modello di destinazione. Verificali per i colori importati."));
    }

    private static JsonObject BuildTranslatedPreset(JsonObject target, ProjectArchive template, string outputPath,
        int slots, IEnumerable<string> settings)
    {
        string oldName = ProcessName(target);
        JsonArray inherits = GetProcessGroups(target, "inherits_group", slots);
        string? parent = ProcessSettings.StringValue(inherits[0]);
        if (string.IsNullOrEmpty(parent))
        {
            foreach (string entry in template.Names.Where(IsProcessPreset))
            {
                JsonObject embedded = template.ReadSettings(entry);
                if (ProcessName(embedded) == oldName)
                {
                    parent = ProcessSettings.StringValue(embedded["inherits"]);
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(parent))
            parent = oldName;
        inherits[0] = parent;

        string height = ProcessSettings.StringValue(target["layer_height"]) ?? "";
        string prefix = ProcessSettings.TryNumber(height, out double millimeters)
            ? millimeters.ToString("0.00#", CultureInfo.InvariantCulture) + "mm "
            : "";
        string stem = Path.GetFileNameWithoutExtension(outputPath);
        string name = $"{prefix}3MF Translator - {stem[..Math.Min(55, stem.Length)]}";
        target["print_settings_id"] = name;

        JsonArray differences = GetProcessGroups(target, "different_settings_to_system", slots);
        string existing = ProcessSettings.StringValue(differences[0]) ?? "";
        differences[0] = string.Join(';', existing.Split(';', StringSplitOptions.RemoveEmptyEntries).Union(settings));

        var preset = new JsonObject
        {
            ["name"] = name,
            ["print_settings_id"] = name,
            ["type"] = "process",
            ["from"] = "project",
            ["version"] = target["version"]?.DeepClone(),
            ["inherits"] = parent,
            ["compatible_printers"] = new JsonArray(target["printer_settings_id"]?.DeepClone())
        };
        foreach (string key in ProcessSettings.Keys)
        {
            if (target[key] is JsonNode node)
                preset[key] = node.DeepClone();
        }
        return preset;
    }

    private static JsonArray GetProcessGroups(JsonObject target, string key, int slots)
    {
        if (target[key] is not JsonArray groups)
        {
            groups = new JsonArray();
            target[key] = groups;
        }
        while (groups.Count < slots + 2)
            groups.Add("");
        return groups;
    }

    private static void WriteJson(ZipArchive archive, string name, JsonObject profile)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(profile.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }));
    }

    private static XElement ReadModelRoot(XDocument model) =>
        model.Root is XElement root && root.Name == Core + "model" ? root :
            throw new InvalidDataException(T("The document does not contain a valid 3MF model.", "Il documento non contiene un modello 3MF valido."));

    private static string ReadApplication(XElement root)
    {
        XElement[] tags = root.Elements(Core + "metadata")
            .Where(node => (string?)node.Attribute("name") == "Application").ToArray();
        if (tags.Length != 1 || string.IsNullOrWhiteSpace(tags[0].Value))
            throw new InvalidDataException(T("The project is missing its slicer identifier.", "Manca l'identificativo dello slicer nel progetto."));
        return tags[0].Value;
    }

    private static int SanitizeModelSettings(XDocument document, JsonObject target, int slots, List<string> warnings,
        bool crealityDestination)
    {
        if (document.Root is not XElement root || root.Name != "config")
            throw new InvalidDataException(T("The object settings file has an unsupported format.", "Il file delle impostazioni per oggetto ha un formato non supportato."));

        var skipped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (XElement item in root.Descendants("metadata").Where(node =>
                     node.Parent?.Name.LocalName is "object" or "part" or "volume").ToArray())
        {
            string? key = (string?)item.Attribute("key");
            string value = (string?)item.Attribute("value") ?? "";
            if (key == "extruder")
            {
                if (!int.TryParse(value, out int slot) || slot < 0 || slot > slots)
                    throw new InvalidDataException(T("An object uses a filament slot outside the source palette.", "Un oggetto usa uno slot filamento fuori dalla tavolozza di origine."));
            }
            else if (key == "source_file")
                item.Remove();
            else if (key is not null && !ObjectMetadata.Contains(key))
            {
                bool mapped = false;
                foreach (string translatedKey in ProcessSettings.TargetKeys(key, target))
                {
                    if (!ProcessSettings.TryTranslate(translatedKey, value, target, slots, out string translatedValue, crealityDestination))
                        continue;
                    mapped = true;
                    if (translatedKey != key && item.Parent!.Elements("metadata").Any(sibling =>
                            (string?)sibling.Attribute("key") == translatedKey))
                        continue;
                    var translated = new XElement(item);
                    translated.SetAttributeValue("key", translatedKey);
                    translated.SetAttributeValue("value", translatedValue);
                    item.AddBeforeSelf(translated);
                }
                if (!mapped)
                    skipped.Add(key);
                item.Remove();
            }
        }

        if (skipped.Count > 0)
            warnings.Add(T("Skipped object settings · ", "Impostazioni per oggetto escluse: ") + string.Join(", ", skipped) + ".");

        XElement[] plates = root.Elements("plate").ToArray();
        foreach (XElement item in plates.SelectMany(plate => plate.Elements("metadata")).ToArray())
        {
            if ((string?)item.Attribute("key") is string key && !PlateMetadata.Contains(key))
                item.Remove();
        }

        return Math.Max(1, plates.Length);
    }

    private static (XDocument? Document, int Count) FilterLayerEvents(ProjectArchive source, int plates, int slots,
        List<string> warnings)
    {
        if (!source.Contains(LayerEvents))
            return (null, 0);

        XDocument events = source.ReadXml(LayerEvents);
        if (events.Root?.Name != "custom_gcodes_per_layer")
        {
            warnings.Add(T("The layer event format is unsupported.", "Il formato degli eventi per layer non è supportato."));
            return (null, 0);
        }

        var output = new XElement("custom_gcodes_per_layer");
        XElement[] plateNodes = events.Root.Elements("plate").ToArray();
        int count = 0;
        int skipped = 0;
        foreach (XElement plate in plateNodes.Length == 0 ? [events.Root] : plateNodes)
        {
            var targetPlate = new XElement("plate");
            string? id = (string?)plate.Element("plate_info")?.Attribute("id");
            if (plate == events.Root)
                id = "1";
            if (!int.TryParse(id, out int plateId) || plateId < 1 || plateId > plates)
            {
                skipped += plate.Elements("layer").Count();
                continue;
            }

            targetPlate.Add(new XElement("plate_info", new XAttribute("id", plateId)));
            string? mode = (string?)plate.Element("mode")?.Attribute("value");
            if (mode is "SingleExtruder" or "MultiAsSingle" or "MultiExtruder")
                targetPlate.Add(new XElement("mode", new XAttribute("value", mode)));

            foreach (XElement layer in plate.Elements("layer"))
            {
                string? type = (string?)layer.Attribute("type");
                string? code = (string?)layer.Attribute("gcode");
                string? height = (string?)layer.Attribute("top_z");
                string? color = (string?)layer.Attribute("color");
                if (type is null)
                    type = code switch { "M600" => "0", "tool_change" => "2", _ => null };
                if (type is not ("0" or "2") || height is null || !ProcessSettings.TryNumber(height, out double z) ||
                    z <= 0 || !int.TryParse((string?)layer.Attribute("extruder"), out int slot) ||
                    slot < 1 || slot > slots || color is null || !IsColor(color))
                {
                    skipped++;
                    continue;
                }

                targetPlate.Add(new XElement("layer", new XAttribute("top_z", height),
                    new XAttribute("type", type), new XAttribute("extruder", slot), new XAttribute("color", color),
                    new XAttribute("gcode", type == "0" ? "M600" : "tool_change"), new XAttribute("extra", "")));
                count++;
            }

            if (targetPlate.Element("layer") is not null)
                output.Add(targetPlate);
        }

        if (skipped > 0)
            warnings.Add(T($"Skipped {skipped} layer events, including any pauses and custom G-code.", $"{skipped} eventi per layer esclusi, incluse eventuali pause e istruzioni G-code personalizzate."));
        return count == 0 ? (null, 0) : (new XDocument(output), count);
    }

    private static bool KeepSourcePart(string name)
    {
        if (name.Equals(Model, StringComparison.OrdinalIgnoreCase))
            return false;

        string lower = name.ToLowerInvariant();
        return lower is "[content_types].xml" or "_rels/.rels" or
                   "metadata/cut_information.xml" or "metadata/brim_ear_points.txt" or "metadata/layer_heights_profile.txt" ||
               (lower.StartsWith("3d/", StringComparison.Ordinal) && GeometryExtensions.Contains(Path.GetExtension(name))) ||
               (lower.StartsWith("metadata/", StringComparison.Ordinal) && lower.EndsWith(".png", StringComparison.Ordinal));
    }

    private static bool IsEmbeddedHardwarePreset(string name) =>
        Regex.IsMatch(name, @"\AMetadata/(?:machine_settings|filament_settings)_\d+\.config\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static XDocument FilterRelationships(XDocument document, string name, HashSet<string> retained)
    {
        const string origin = "https://3mf.invalid/";
        int relFolder = name.LastIndexOf("_rels/", StringComparison.OrdinalIgnoreCase);
        if (relFolder < 0)
            throw new InvalidDataException(T("Invalid 3MF relationships folder.", "Cartella delle relazioni 3MF non valida."));
        var baseUri = new Uri(origin + name[..relFolder]);
        foreach (XElement relationship in document.Root!.Elements().ToArray())
        {
            string? target = (string?)relationship.Attribute("Target");
            if (target is null || (string?)relationship.Attribute("TargetMode") == "External" ||
                !Uri.TryCreate(baseUri, target, out Uri? resolved) || resolved.Host != baseUri.Host ||
                !retained.Contains(Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'))))
                relationship.Remove();
        }

        return document;
    }

    private static XDocument FilterContentTypes(XDocument document, HashSet<string> retained, bool crealityDestination)
    {
        foreach (XElement item in document.Root!.Elements().Where(element => element.Name.LocalName == "Override").ToArray())
        {
            string name = Uri.UnescapeDataString(((string?)item.Attribute("PartName") ?? "").TrimStart('/'));
            if (!retained.Contains(name))
                item.Remove();
        }

        if (crealityDestination && !document.Root.Elements().Any(element => element.Name.LocalName == "Default" &&
                string.Equals((string?)element.Attribute("Extension"), "config", StringComparison.OrdinalIgnoreCase)))
        {
            XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
            document.Root.AddFirst(new XElement(contentTypes + "Default",
                new XAttribute("Extension", "config"),
                new XAttribute("ContentType", "application/octet-stream")));
        }

        return document;
    }

}
