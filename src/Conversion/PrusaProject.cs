using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Translator.ProjectArchive;

namespace Translator;

internal sealed record PrusaProject(string SettingsPath, string ModelSettingsPath)
{
    private static readonly XNamespace Production = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private static readonly string[] TriangleIndexes = ["v1", "v2", "v3"];
    private static readonly Dictionary<string, string> KeyMap = new(StringComparer.Ordinal)
    {
        ["perimeters"] = "wall_loops", ["top_solid_layers"] = "top_shell_layers",
        ["bottom_solid_layers"] = "bottom_shell_layers", ["top_solid_min_thickness"] = "top_shell_thickness",
        ["bottom_solid_min_thickness"] = "bottom_shell_thickness", ["fill_density"] = "sparse_infill_density",
        ["fill_pattern"] = "sparse_infill_pattern", ["top_fill_pattern"] = "top_surface_pattern",
        ["bottom_fill_pattern"] = "bottom_surface_pattern", ["fill_angle"] = "infill_direction",
        ["perimeter_generator"] = "wall_generator", ["brim_separation"] = "brim_object_gap",
        ["perimeter_speed"] = "inner_wall_speed", ["external_perimeter_speed"] = "outer_wall_speed",
        ["infill_speed"] = "sparse_infill_speed", ["solid_infill_speed"] = "internal_solid_infill_speed",
        ["top_solid_infill_speed"] = "top_surface_speed", ["first_layer_speed"] = "initial_layer_speed",
        ["support_material"] = "enable_support", ["support_material_angle"] = "support_angle",
        ["support_material_threshold"] = "support_threshold_angle",
        ["support_material_buildplate_only"] = "support_on_build_plate_only",
        ["support_material_contact_distance"] = "support_top_z_distance",
        ["support_material_bottom_contact_distance"] = "support_bottom_z_distance",
        ["support_material_xy_spacing"] = "support_object_xy_distance",
        ["support_material_pattern"] = "support_base_pattern", ["support_material_spacing"] = "support_base_pattern_spacing",
        ["support_material_interface_pattern"] = "support_interface_pattern",
        ["support_material_interface_layers"] = "support_interface_top_layers",
        ["support_material_bottom_interface_layers"] = "support_interface_bottom_layers",
        ["support_material_interface_spacing"] = "support_interface_spacing",
        ["support_material_interface_contact_loops"] = "support_interface_loop_pattern",
        ["support_material_speed"] = "support_speed", ["support_material_interface_speed"] = "support_interface_speed",
        ["support_material_enforce_layers"] = "enforce_support_layers", ["spiral_vase"] = "spiral_mode"
    };

    internal static PrusaProject? Detect(ProjectArchive archive)
    {
        if (archive.Contains(Settings) && archive.Contains(ModelSettings))
            return null;
        if (archive.Contains("Metadata/SuperSlicer.config"))
            return new("Metadata/SuperSlicer.config", archive.Contains("Metadata/SuperSlicer_model.config")
                ? "Metadata/SuperSlicer_model.config" : "Metadata/Slic3r_PE_model.config");
        return archive.Contains("Metadata/Slic3r_PE.config")
            ? new("Metadata/Slic3r_PE.config", "Metadata/Slic3r_PE_model.config") : null;
    }

    internal JsonObject ReadSettings(ProjectArchive archive, List<string>? warnings = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(archive.ReadText(SettingsPath));
        while (reader.ReadLine() is string line)
        {
            line = line.Trim().TrimStart(';').TrimStart();
            int separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        string technology = values.GetValueOrDefault("printer_technology") ?? "FFF";
        string[] nozzles = SplitList(values.GetValueOrDefault("nozzle_diameter"));
        if (technology != "FFF" || nozzles.Length != 1 || !ProcessSettings.TryNumber(nozzles[0], out _))
            throw new InvalidDataException(T("Importing from PrusaSlicer or SuperSlicer requires an FFF project with one nozzle slot.", "L'importazione PrusaSlicer/SuperSlicer richiede un progetto FFF con un solo slot ugello."));

        var settings = new JsonObject
        {
            ["printer_technology"] = technology,
            ["nozzle_diameter"] = new JsonArray(JsonValue.Create(nozzles[0])),
            ["printer_settings_id"] = values.GetValueOrDefault("printer_settings_id") ?? T("Source printer", "Stampante di origine"),
            ["print_settings_id"] = values.GetValueOrDefault("print_settings_id") ?? T("Source process", "Processo di origine")
        };
        if (SplitList(values.GetValueOrDefault("filament_type")) is [string material])
            settings["filament_type"] = new JsonArray(JsonValue.Create(material.Trim('"')));
        string[] colors = SplitList(values.GetValueOrDefault("extruder_colour")).Concat(SplitList(values.GetValueOrDefault("filament_colour")))
            .Select(item => item.Trim('"')).ToArray();
        // Keep an invalid supplied value so the shared palette recovery can ask the user to repair it.
        string? color = colors.FirstOrDefault(item => item.Length == 7 && item[0] == '#' && item[1..].All(Uri.IsHexDigit))
            ?? colors.FirstOrDefault()
            ?? (values.ContainsKey("extruder_colour") || values.ContainsKey("filament_colour") ? "" : null);
        if (color is not null)
            settings["filament_colour"] = new JsonArray(JsonValue.Create(color));
        if (values.TryGetValue("bed_shape", out string? bed))
            settings["printable_area"] = new JsonArray(SplitList(bed).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());
        if (values.TryGetValue("max_print_height", out string? maxHeight))
            settings["printable_height"] = maxHeight;

        Dictionary<string, string> mapped = MapSettings(values, warnings, global: true);
        foreach (string key in ProcessSettings.Keys)
            if (mapped.TryGetValue(key, out string? value))
                settings[key] = value;
        return settings;
    }

    private static string[] SplitList(string? value) =>
        value?.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static Dictionary<string, string> MapSettings(Dictionary<string, string> source, List<string>? warnings,
        bool global = false)
    {
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in source)
            mapped[KeyMap.GetValueOrDefault(key, key)] = key == "seam_position" && value == "rear" ? "back" : value;

        if (source.TryGetValue("first_layer_height", out string? first))
        {
            if (!first.EndsWith('%'))
                mapped["initial_layer_print_height"] = first;
            else if (ProcessSettings.TryNumber(first[..^1], out double percent) &&
                source.TryGetValue("layer_height", out string? height) && ProcessSettings.TryNumber(height, out double baseHeight))
                mapped["initial_layer_print_height"] = (baseHeight * percent / 100).ToString("G", CultureInfo.InvariantCulture);
        }
        if (source.ContainsKey("support_material_style") || (global && source.ContainsKey("support_material")))
        {
            string? style = source.GetValueOrDefault("support_material_style");
            string mode = source.GetValueOrDefault("support_material_auto") == "0" ? "manual" : "auto";
            mapped["support_type"] = (style == "organic" ? "tree" : "normal") + $"({mode})";
            if (style is "grid" or "snug" or "organic")
                mapped["support_style"] = style == "organic" ? "tree_organic" : style;
        }
        if (source.GetValueOrDefault("ironing") == "0")
            mapped["ironing_type"] = "no ironing";
        if (mapped.GetValueOrDefault("support_bottom_z_distance") == "0" &&
            mapped.TryGetValue("support_top_z_distance", out string? topDistance))
            mapped["support_bottom_z_distance"] = topDistance;
        if (mapped.TryGetValue("support_object_xy_distance", out string? spacing) && spacing.EndsWith('%'))
        {
            mapped.Remove("support_object_xy_distance");
            warnings?.Add(T("The support XY distance is a percentage. The destination profile value was used.", "La distanza XY dei supporti espressa in percentuale usa il valore del profilo di destinazione."));
        }
        foreach ((string key, string? relativeTo) in new[]
        {
            ("outer_wall_speed", "inner_wall_speed"), ("internal_solid_infill_speed", "sparse_infill_speed"),
            ("top_surface_speed", "internal_solid_infill_speed"), ("support_interface_speed", "support_speed"),
            ("initial_layer_speed", (string?)null)
        })
        {
            if (!mapped.TryGetValue(key, out string? speed) || !speed.EndsWith('%'))
                continue;
            if (relativeTo is not null && mapped.TryGetValue(relativeTo, out string? baseline) &&
                ProcessSettings.TryNumber(baseline, out double absolute) && ProcessSettings.TryNumber(speed[..^1], out double ratio))
                mapped[key] = (absolute * ratio / 100).ToString("G", CultureInfo.InvariantCulture);
            else
            {
                mapped.Remove(key);
                warnings?.Add(T($"Relative speed {key} uses the destination profile value.", $"La velocità relativa {key} usa il valore del profilo di destinazione."));
            }
        }
        return mapped;
    }

    internal XDocument TranslateModel(ProjectArchive archive, XElement model, List<string> warnings)
    {
        if ((string?)model.Attribute("unit") is string unit && unit != "millimeter")
            throw new InvalidDataException(T("The PrusaSlicer or SuperSlicer project must use millimeters.", "Il progetto PrusaSlicer/SuperSlicer deve usare millimetri."));
        const string wipeTower = "Metadata/Prusa_Slicer_wipe_tower_information.xml";
        if (archive.Contains(wipeTower) &&
            System.Text.RegularExpressions.Regex.IsMatch(archive.ReadText(wipeTower), """\bbed_idx\s*=\s*["'][1-9]"""))
            throw new InvalidDataException(T("Importing from PrusaSlicer or SuperSlicer supports projects with one plate.", "L'importazione PrusaSlicer/SuperSlicer supporta progetti con un solo piatto."));

        XElement resources = model.Element(Core + "resources") ?? throw InvalidModel();
        XElement build = model.Element(Core + "build") ?? throw InvalidModel();
        XElement[] items = build.Elements(Core + "item").ToArray();
        if (items.Length == 0)
            throw InvalidModel();
        Dictionary<int, XElement> objects = resources.Elements(Core + "object").ToDictionary(item => Id(item, "id"));
        XElement? original = archive.Contains(ModelSettingsPath) ? archive.ReadXml(ModelSettingsPath).Root : null;
        var metadata = original?.Elements("object").ToDictionary(item => Id(item, "id")) ?? [];
        if (original is null)
            warnings.Add(T("The source project has no object settings.", "Il progetto di origine non contiene le impostazioni per oggetto."));
        int nextId = objects.Keys.DefaultIfEmpty(0).Max();
        var translated = new XElement("config");
        var plate = new XElement("plate", Metadata("plater_id", "1"));
        var instances = new Dictionary<int, int>();
        var used = new HashSet<int>();
        var newResources = new List<XElement>();
        bool hasFuzzyPaint = false;

        foreach (XElement item in items)
        {
            int objectId = Id(item, "objectid");
            if (!objects.TryGetValue(objectId, out XElement? sourceObject))
                throw InvalidModel();
            if (sourceObject.Element(Core + "mesh") is null)
            {
                XElement[] components = sourceObject.Element(Core + "components")?.Elements(Core + "component").ToArray() ?? [];
                if (components.Length != 1 || components[0].Attribute("transform") is not null ||
                    !objects.TryGetValue(Id(components[0], "objectid"), out sourceObject))
                    throw new InvalidDataException(T("The PrusaSlicer or SuperSlicer project uses an unsupported component structure.", "La struttura dei componenti del progetto PrusaSlicer/SuperSlicer non è supportata."));
                objectId = Id(sourceObject, "id");
            }

            if (used.Add(objectId))
            {
                XElement mesh = sourceObject.Element(Core + "mesh") ?? throw InvalidModel();
                XElement[] vertices = mesh.Element(Core + "vertices")?.Elements(Core + "vertex").ToArray() ?? [];
                XElement[] triangles = mesh.Element(Core + "triangles")?.Elements(Core + "triangle").ToArray() ?? [];
                if (vertices.Length == 0 || triangles.Length == 0 || sourceObject.Attribute("pid") is not null)
                    throw InvalidModel();
                metadata.TryGetValue(objectId, out XElement? objectMetadata);
                var configObject = new XElement("object", new XAttribute("id", objectId));
                AddSettings(configObject, objectMetadata, warnings);
                var parts = new XElement(Core + "components");
                XElement[] volumes = objectMetadata?.Elements("volume").ToArray() ?? [];
                if (volumes.Length == 0)
                    volumes = [new XElement("volume", new XAttribute("firstid", 0), new XAttribute("lastid", triangles.Length - 1))];
                int expectedFirst = 0;
                foreach (XElement volume in volumes.OrderBy(volume => Id(volume, "firstid")))
                {
                    int first = Id(volume, "firstid");
                    int last = Id(volume, "lastid");
                    if (first != expectedFirst || last < first || last >= triangles.Length)
                        throw InvalidModel();
                    expectedFirst = last + 1;
                    int partId = checked(++nextId);
                    var part = new XElement("part", new XAttribute("id", partId),
                        new XAttribute("subtype", VolumeType(volume)));
                    AddSettings(part, volume, warnings);
                    configObject.Add(part);
                    XElement partMesh = CreateMesh(vertices, triangles.AsSpan(first, last - first + 1), ref hasFuzzyPaint);
                    newResources.Add(new XElement(Core + "object", new XAttribute("id", partId),
                        new XAttribute("type", "model"), Uuid(), partMesh));
                    parts.Add(new XElement(Core + "component", new XAttribute("objectid", partId), Uuid()));
                }
                if (expectedFirst != triangles.Length)
                    throw InvalidModel();
                newResources.Add(new XElement(Core + "object", new XAttribute("id", objectId),
                    new XAttribute("type", "model"), Uuid(), parts));
                translated.Add(configObject);
            }

            int instance = instances.GetValueOrDefault(objectId);
            instances[objectId] = instance + 1;
            item.SetAttributeValue("objectid", objectId);
            item.SetAttributeValue(Production + "UUID", Guid.NewGuid());
            plate.Add(new XElement("model_instance", Metadata("object_id", objectId), Metadata("instance_id", instance)));
        }

        resources.ReplaceNodes(newResources);
        model.SetAttributeValue(XNamespace.Xmlns + "p", Production.NamespaceName);
        build.SetAttributeValue(Production + "UUID", Guid.NewGuid());
        model.SetAttributeValue(XNamespace.Xmlns + "BambuStudio", "http://schemas.bambulab.com/package/2021");
        resources.AddBeforeSelf(new XElement(Core + "metadata", new XAttribute("name", "BambuStudio:3mfVersion"), "1"));
        translated.Add(plate);
        if (hasFuzzyPaint)
            warnings.Add(T("Fuzzy skin painting from the source project was skipped.", "La pittura della superficie fuzzy del progetto di origine non è stata trasferita."));
        if (archive.Names.Any(name => name.Contains("layer_heights_profile", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("layer_config_ranges", StringComparison.OrdinalIgnoreCase)))
            warnings.Add(T("PrusaSlicer or SuperSlicer variable layer height profiles and height range settings were skipped.", "I profili di altezza variabile e le impostazioni per intervalli di altezza di PrusaSlicer/SuperSlicer non sono stati trasferiti."));
        if (archive.Contains("Metadata/Prusa_Slicer_custom_gcode_per_print_z.xml"))
            warnings.Add(T("Custom layer events from PrusaSlicer or SuperSlicer were skipped.", "Gli eventi personalizzati per layer di PrusaSlicer/SuperSlicer non sono stati trasferiti."));
        return new XDocument(translated);
    }

    private static XElement CreateMesh(XElement[] vertices, ReadOnlySpan<XElement> triangles, ref bool hasFuzzyPaint)
    {
        var indexMap = new Dictionary<int, int>();
        var newVertices = new XElement(Core + "vertices");
        var newTriangles = new XElement(Core + "triangles");
        foreach (XElement triangle in triangles)
        {
            var face = new XElement(Core + "triangle");
            foreach (string index in TriangleIndexes)
            {
                int old = Id(triangle, index);
                if (old >= vertices.Length)
                    throw InvalidModel();
                if (indexMap.TryAdd(old, indexMap.Count))
                    newVertices.Add(new XElement(vertices[old]));
                face.SetAttributeValue(index, indexMap[old]);
            }
            foreach (XAttribute attribute in triangle.Attributes())
            {
                switch (attribute.Name.LocalName)
                {
                    case "custom_supports": face.SetAttributeValue("paint_supports", attribute.Value); break;
                    case "custom_seam": face.SetAttributeValue("paint_seam", attribute.Value); break;
                    case "fuzzy_skin": hasFuzzyPaint |= attribute.Value.Length > 0; break;
                    case "mmu_segmentation" when attribute.Value.Length > 0:
                    case "pid":
                        throw new InvalidDataException(T("PrusaSlicer or SuperSlicer multicolor painting is unsupported.", "La pittura multicolore PrusaSlicer/SuperSlicer non è supportata."));
                }
            }
            newTriangles.Add(face);
        }
        return new XElement(Core + "mesh", newVertices, newTriangles);
    }

    private static void AddSettings(XElement destination, XElement? source, List<string> warnings)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement item in source?.Elements("metadata") ?? [])
        {
            if ((string?)item.Attribute("key") is string key && (string?)item.Attribute("value") is string value &&
                key is not ("matrix" or "modifier" or "volume_type" or "source_is_builtin_volume"))
                values[key] = value;
        }
        foreach ((string key, string value) in MapSettings(values, warnings))
        {
            if (key is not ("support_material_style" or "first_layer_height") &&
                (key != "support_material_auto" || !values.ContainsKey("support_material_style")))
                destination.Add(Metadata(key, value));
        }
    }

    private static string VolumeType(XElement volume) =>
        ((string?)volume.Elements("metadata").FirstOrDefault(item => (string?)item.Attribute("key") == "volume_type")?.Attribute("value")) switch
        {
            "ModelPart" => "normal_part",
            "NegativeVolume" => "negative_part",
            "ParameterModifier" => "modifier_part",
            "SupportEnforcer" => "support_enforcer",
            "SupportBlocker" => "support_blocker",
            null => volume.Elements("metadata").Any(item => (string?)item.Attribute("key") == "modifier" &&
                (string?)item.Attribute("value") == "1") ? "modifier_part" : "normal_part",
            _ => throw new InvalidDataException(T("The project contains an unsupported PrusaSlicer or SuperSlicer volume type.", "Il progetto contiene un tipo di volume PrusaSlicer/SuperSlicer non supportato."))
        };

    private static XAttribute Uuid() => new(Production + "UUID", Guid.NewGuid());
    private static XElement Metadata(string key, object value) =>
        new("metadata", new XAttribute("key", key), new XAttribute("value", value));
    private static int Id(XElement element, string key) =>
        int.TryParse((string?)element.Attribute(key), NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : throw InvalidModel();
    private static InvalidDataException InvalidModel() => new(T("The PrusaSlicer or SuperSlicer model has an invalid structure or mesh.", "Il modello PrusaSlicer/SuperSlicer ha una struttura o una mesh non valida."));
}
