using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Translator;

internal delegate int[] ReviewFilamentSlots(int[] usedSlots, string[] sourceColors, int destinationCount);

internal static class SourceFilaments
{
    private static readonly HashSet<string> SlotGeometryAttributes = new(StringComparer.Ordinal)
    {
        "pid", "pindex", "p1", "p2", "p3"
    };

    internal static int[] Fit(ProjectArchive archive, XDocument model, XDocument modelSettings,
        JsonObject source, JsonObject? preset, string[] sourceColors, int destinationCount, List<string> warnings,
        ReviewFilamentSlots? review, Dictionary<string, XDocument> remappedMeshes)
    {
        int sourceCount = sourceColors.Length;
        if (sourceCount <= destinationCount)
            return Enumerable.Range(1, sourceCount).ToArray();

        if (HasSequences(archive, modelSettings, source, preset))
            throw MoreSlots(sourceCount, destinationCount, T("The project contains filament sequences or layer events.", "Il progetto contiene sequenze di filamento o eventi per layer."));

        var used = new SortedSet<int>();
        var paint = new List<XAttribute>();
        CollectPaint(model);
        foreach (string name in archive.Names.Where(name => !name.Equals(ProjectArchive.Model, StringComparison.OrdinalIgnoreCase) &&
                     name.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".model", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument mesh = archive.ReadXml(name);
            int count = paint.Count;
            CollectPaint(mesh);
            if (paint.Count != count)
                remappedMeshes.Add(name, mesh);
        }
        foreach (string encoded in paint.Select(attribute => attribute.Value).Distinct())
            MeshPaint.Remap(encoded, slot =>
            {
                if (slot > sourceCount)
                    throw new InvalidDataException(T("A painted color is outside the source palette.", "Un colore dipinto è fuori dalla tavolozza di origine."));
                used.Add(slot);
                return slot;
            });

        var metadata = modelSettings.Descendants("metadata").Where(item => (string?)item.Attribute("key") is string key &&
            (key == "extruder" || ProcessSettings.UsesFilamentSlot(key))).ToArray();
        var profileSlots = new List<(JsonObject Profile, string Key, int Slot)>();
        foreach (JsonObject profile in preset is null ? [source] : new[] { source, preset })
        {
            foreach ((string key, JsonNode? value) in profile)
            {
                if (!ProcessSettings.UsesFilamentSlot(key))
                    continue;
                int slot = ReadSlot(ProcessSettings.StringValue(value), sourceCount);
                if (slot > 0)
                {
                    used.Add(slot);
                    profileSlots.Add((profile, key, slot));
                }
            }
        }

        foreach (XElement item in metadata)
        {
            int slot = ReadSlot((string?)item.Attribute("value"), sourceCount);
            if (slot > 0)
                used.Add(slot);
        }
        var defaultObjects = modelSettings.Root!.Elements("object").Where(item =>
                !item.Elements("metadata").Any(entry => (string?)entry.Attribute("key") == "extruder" &&
                    (string?)entry.Attribute("value") is not (null or "0"))).ToArray();
        if (used.Count == 0 || defaultObjects.Length > 0)
            used.Add(1);

        if (used.Count > destinationCount && review is null)
            throw MoreSlots(used.Count, destinationCount, T("The slots are assigned to objects or print settings.", "Gli slot sono assegnati a oggetti o impostazioni di stampa."));

        int[] slots = used.ToArray();
        int[] destinations = review?.Invoke(slots, sourceColors, destinationCount) ?? Enumerable.Range(1, slots.Length).ToArray();
        if (destinations.Length != slots.Length || destinations.Any(slot => slot < 1 || slot > destinationCount))
            throw new InvalidDataException(T("The filament slot mapping is invalid.", "La mappatura degli slot filamento non è valida."));
        Dictionary<int, int> map = slots.Zip(destinations).ToDictionary(pair => pair.First, pair => pair.Second);
        if (map.TryGetValue(1, out int defaultSlot) && defaultSlot != 1)
            foreach (XElement item in defaultObjects)
            {
                XElement? assignment = item.Elements("metadata").FirstOrDefault(entry => (string?)entry.Attribute("key") == "extruder");
                if (assignment is null)
                    item.AddFirst(new XElement("metadata", new XAttribute("key", "extruder"), new XAttribute("value", defaultSlot)));
                else
                    assignment.SetAttributeValue("value", defaultSlot);
            }
        var encodedMappings = paint.Select(attribute => attribute.Value).Distinct()
            .ToDictionary(value => value, value => MeshPaint.Remap(value, slot => map[slot]));
        foreach (XAttribute attribute in paint)
            attribute.Value = encodedMappings[attribute.Value];
        foreach (XElement item in metadata)
        {
            int slot = ReadSlot((string?)item.Attribute("value"), sourceCount);
            if (slot > 0)
                item.SetAttributeValue("value", map[slot]);
        }
        foreach ((JsonObject profile, string key, int slot) in profileSlots)
            profile[key] = map[slot].ToString(CultureInfo.InvariantCulture);

        if (sourceCount > slots.Length)
            warnings.Add(T($"Removed {sourceCount - slots.Length} unused slots.", $"Rimossi {sourceCount - slots.Length} slot non utilizzati."));
        warnings.Add(
            T("Source → destination slot assignments · ", "Assegnazioni di origine → destinazione: ") + string.Join(", ", slots.Select(slot => $"{slot} → {map[slot]}")) + ".");
        if (destinations.Distinct().Count() < destinations.Length)
            warnings.Add(T("Some source filaments share one destination slot. Check their colors in the sliced preview.",
                "Alcuni filamenti di origine condividono uno slot di destinazione. Controlla i colori nell’anteprima dello slicing."));
        var paletteSlots = new int[destinations.Max()];
        foreach (int slot in slots)
            if (paletteSlots[map[slot] - 1] == 0)
                paletteSlots[map[slot] - 1] = slot;
        return paletteSlots;

        void CollectPaint(XDocument mesh)
        {
            foreach (XAttribute attribute in mesh.Descendants().Where(element => element.Name.LocalName is "triangle" or "object")
                         .SelectMany(element => element.Attributes()).Where(attribute => attribute.Value.Length > 0))
            {
                string name = attribute.Name.LocalName;
                if (SlotGeometryAttributes.Contains(name) || (review is null && name is "paint_color" or "mmu_segmentation"))
                    throw MoreSlots(sourceCount, destinationCount, T("The project contains mesh color assignments.", "Il progetto contiene assegnazioni di colore sulla mesh."));
                if (name is "paint_color" or "mmu_segmentation")
                    paint.Add(attribute);
            }
        }
    }

    internal static (int Index, int Count)? ActiveVariant(JsonObject source, XDocument modelSettings,
        int[] slots, int sourceCount)
    {
        if (source["print_extruder_id"] is not JsonArray ids || source["print_extruder_variant"] is not JsonArray variants ||
            ids.Count != variants.Count || source["extruder_type"] is not JsonArray types ||
            source["nozzle_volume_type"] is not JsonArray volumes)
            return null;

        string?[] fallback = (source["filament_map"] as JsonArray)?.Select(ProcessSettings.StringValue).ToArray() ?? [];
        string?[][] maps = modelSettings.Root!.Elements("plate").Select(plate =>
        {
            string? value = (string?)plate.Elements("metadata").FirstOrDefault(item =>
                (string?)item.Attribute("key") == "filament_maps")?.Attribute("value");
            return value is null ? fallback : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }).DefaultIfEmpty(fallback).ToArray();
        if (maps.Any(map => map.Length != sourceCount))
            return null;

        string?[] heads = maps.SelectMany(map => slots.Select(slot => map[slot - 1])).Distinct().ToArray();
        if (heads is not [string head] || !int.TryParse(head, out int number) || number < 1 ||
            number > types.Count || number > volumes.Count)
            return null;
        string variant = $"{ProcessSettings.StringValue(types[number - 1])} {ProcessSettings.StringValue(volumes[number - 1])}";
        int[] matching = Enumerable.Range(0, ids.Count).Where(index =>
            ProcessSettings.StringValue(ids[index]) == head && ProcessSettings.StringValue(variants[index]) == variant).ToArray();
        return matching is [int index] ? (index, ids.Count) : null;
    }

    private static bool HasSequences(ProjectArchive archive, XDocument modelSettings, JsonObject source, JsonObject? preset)
    {
        string[] keys = ["first_layer_print_sequence", "other_layers_print_sequence"];
        if (modelSettings.Root!.Elements("plate").SelectMany(plate => plate.Elements("metadata")).Any(item =>
                (string?)item.Attribute("key") is string key && keys.Contains(key) &&
                IsSequenceValue((string?)item.Attribute("value"))))
            return true;
        if (keys.Any(key => HasSequenceSetting(source[key]) || HasSequenceSetting(preset?[key])))
            return true;
        if (archive.Contains(ProjectArchive.LayerEvents) &&
            archive.ReadXml(ProjectArchive.LayerEvents).Descendants("layer").Any())
            return true;

        const string sequencePath = "Metadata/filament_sequence.json";
        if (!archive.Contains(sequencePath))
            return false;
        return archive.ReadSettings(sequencePath).Any(plate => plate.Value is not JsonObject settings ||
            settings.Any(item => item.Value is not JsonArray { Count: 0 }));
    }

    private static bool HasSequenceSetting(JsonNode? setting) =>
        setting is JsonArray array ? array.Any(item => IsSequenceValue(ProcessSettings.StringValue(item))) :
            IsSequenceValue(ProcessSettings.StringValue(setting));

    private static bool IsSequenceValue(string? value) => !string.IsNullOrWhiteSpace(value) && value != "0";

    private static int ReadSlot(string? value, int count) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot) && slot <= count
            ? slot : throw new InvalidDataException(T("The project contains a filament assignment that cannot be remapped.", "Il progetto contiene un'assegnazione filamento che non posso rimappare."));

    private static InvalidDataException MoreSlots(int sourceCount, int destinationCount, string reason) =>
        new(T($"{reason} The project needs {sourceCount} slots and the destination has {destinationCount}. ", $"{reason} Servono {sourceCount} slot e la destinazione ne ha {destinationCount}. ") +
            T($"Choose a profile with at least {sourceCount} slots or reduce the assigned filaments in the source slicer.", $"Scegli un profilo con almeno {sourceCount} slot oppure riduci i filamenti assegnati nello slicer di origine."));
}
