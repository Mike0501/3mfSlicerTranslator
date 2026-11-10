using System.Text.Json.Nodes;

namespace Translator;

internal sealed record ReferenceMachine(string Preset, string Brand, string Model, double NozzleDiameter);

internal sealed record ReferenceConfiguration(ReferenceMachine Machine, string? CurrentPlate, IReadOnlyList<string> Plates,
    int FilamentSlots)
{
    internal static readonly IReadOnlyDictionary<string, string> PlateTemperatureKeys = new Dictionary<string, string>
    {
        ["Textured PEI Plate"] = "textured_plate_temp",
        ["High Temp Plate"] = "hot_plate_temp",
        ["Cool Plate"] = "cool_plate_temp",
        ["Engineering Plate"] = "eng_plate_temp"
    };

    internal static ReferenceConfiguration Read(JsonObject settings, string preset, double nozzleDiameter)
    {
        string model = Text("printer_model") ?? preset;
        string brand = Text("printer_vendor") ?? Text("printer_manufacturer") ?? InferBrand(model);
        string? plate = Text("curr_bed_type");
        var plates = new List<string>();
        if (plate is not null)
            plates.Add(plate);
        int slots = (settings["filament_colour"] as JsonArray)?.Count ?? 0;
        if (Text("support_multi_bed_types") is "1" or "true" && slots > 0)
        {
            foreach ((string candidate, string key) in PlateTemperatureKeys)
                if (candidate != plate && HasTemperatures(settings[key], slots) &&
                    HasTemperatures(settings[key + "_initial_layer"], slots))
                    plates.Add(candidate);
        }
        return new(new(preset, brand, model, nozzleDiameter), plate, plates, slots);

        string? Text(string key) =>
            ProcessSettings.StringValue(settings[key])?.Trim() is { Length: > 0 } value ? value : null;
    }

    private static string InferBrand(string model)
    {
        foreach (string brand in new[] { "Bambu Lab", "Original Prusa" })
            if (model.Equals(brand, StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith(brand + " ", StringComparison.OrdinalIgnoreCase))
                return brand;
        int separator = model.IndexOf(' ');
        return separator > 0 ? model[..separator] : T("Other", "Altro");
    }

    private static bool HasTemperatures(JsonNode? node, int slots) =>
        node is JsonArray temperatures && temperatures.Count >= slots &&
        temperatures.Take(slots).All(value => ProcessSettings.StringValue(value) is string text &&
            ProcessSettings.TryNumber(text, out double temperature) && temperature > 0);
}
