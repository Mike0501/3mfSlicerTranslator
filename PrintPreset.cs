using System.Globalization;
using System.Text.Json.Nodes;

namespace Translator;

internal sealed record PrintPreset(decimal? LayerHeight, int? WallLoops, decimal? InfillDensity, bool? Supports)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal bool HasSettings => LayerHeight is not null || WallLoops is not null || InfillDensity is not null || Supports is not null;

    internal static PrintPreset Read(JsonObject settings)
    {
        string? infill = ProcessSettings.StringValue(settings["sparse_infill_density"]);
        decimal? density = infill?.EndsWith('%') == true ? ReadNumber(infill[..^1]) : null;
        return new PrintPreset(
            ReadNumber(ProcessSettings.StringValue(settings["layer_height"])) is > 0 and var height ? height : null,
            int.TryParse(ProcessSettings.StringValue(settings["wall_loops"]), NumberStyles.None, Invariant, out int walls) ? walls : null,
            density is >= 0 and <= 100 ? density : null,
            ProcessSettings.StringValue(settings["enable_support"]) switch { "1" => true, "0" => false, _ => null });
    }

    internal IReadOnlyList<string> Apply(JsonObject settings, LayerHeightRange range)
    {
        PrintPreset original = Read(settings);
        var changed = new List<string>();
        if (LayerHeight is decimal height && height != original.LayerHeight)
        {
            if (original.LayerHeight is null || height < range.Minimum || height > range.Maximum)
                throw new InvalidDataException(T("The preset layer height is outside the reference printer's limits.",
                    "L’altezza strato del preset è fuori dai limiti della stampante di riferimento."));
            Set("layer_height", height.ToString("G29", Invariant));
        }
        if (WallLoops is int walls && walls != original.WallLoops)
        {
            if (original.WallLoops is null || walls is < 0 or > 100)
                throw InvalidValue();
            Set("wall_loops", walls.ToString(Invariant));
        }
        if (InfillDensity is decimal density && density != original.InfillDensity)
        {
            if (original.InfillDensity is null || density is < 0 or > 100)
                throw InvalidValue();
            Set("sparse_infill_density", density.ToString("G29", Invariant) + "%");
        }
        if (Supports is bool supports && supports != original.Supports)
        {
            if (original.Supports is null)
                throw InvalidValue();
            Set("enable_support", supports ? "1" : "0");
        }
        return changed;

        void Set(string key, string value)
        {
            settings[key] = value;
            changed.Add(key);
        }
    }

    internal static decimal? ReadNumber(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, Invariant, out decimal number) ? number : null;

    private static InvalidDataException InvalidValue() => new(T("A preset value is invalid for this reference.",
        "Un valore del preset non è valido per questo riferimento."));
}

internal sealed record LayerHeightRange(decimal Minimum, decimal Maximum)
{
    internal static LayerHeightRange Read(JsonObject settings)
    {
        decimal nozzle = ReadPositive(settings["nozzle_diameter"]) ?? 0.01m;
        decimal max = Math.Min(ReadPositive(settings["max_layer_height"]) ?? nozzle, nozzle);
        decimal min = Math.Min(ReadPositive(settings["min_layer_height"]) ?? 0.01m, max);
        return new(min, max);
    }

    private static decimal? ReadPositive(JsonNode? node) =>
        PrintPreset.ReadNumber(ProcessSettings.StringValue(node is JsonArray { Count: 1 } array ? array[0] : node))
            is > 0 and var number ? number : null;
}
