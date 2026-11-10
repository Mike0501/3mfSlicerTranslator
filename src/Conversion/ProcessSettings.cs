using System.Globalization;
using System.Text.Json.Nodes;

namespace Translator;

internal static class ProcessSettings
{
    private static readonly string[] Positive =
    [
        "layer_height", "initial_layer_print_height"
    ];

    private static readonly string[] NonNegative =
    [
        "brim_width", "brim_object_gap", "support_top_z_distance", "support_bottom_z_distance",
        "support_object_xy_distance", "top_shell_thickness", "bottom_shell_thickness",
        "support_angle", "support_base_pattern_spacing", "support_interface_spacing",
        "support_bottom_interface_spacing", "support_object_first_layer_gap", "support_flow_ratio",
        "support_interface_flow_ratio", "support_ironing_spacing", "support_ironing_direction",
        "support_ironing_inset", "raft_contact_distance", "raft_expansion",
        "tree_support_branch_angle", "tree_support_branch_angle_organic", "tree_support_branch_diameter",
        "tree_support_branch_diameter_angle", "tree_support_branch_diameter_organic",
        "tree_support_branch_distance", "tree_support_branch_distance_organic", "tree_support_tip_diameter",
        "tree_support_angle_slow", "tree_support_brim_width",
        "bottom_solid_infill_flow_ratio", "top_solid_infill_flow_ratio", "print_flow_ratio",
        "bridge_flow", "internal_bridge_flow", "elefant_foot_compensation", "max_bridge_length",
        "ironing_spacing", "fuzzy_skin_point_distance", "fuzzy_skin_thickness", "minimum_sparse_infill_area",
        "skirt_distance", "slice_closing_radius", "resolution", "brim_ears_detection_length",
        "brim_ears_max_angle", "wall_transition_angle", "make_overhang_printable_angle"
    ];

    private static readonly string[] Integers =
    [
        "wall_loops", "top_shell_layers", "bottom_shell_layers", "raft_layers",
        "support_interface_top_layers", "support_threshold_angle", "enforce_support_layers",
        "support_interface_bottom_layers", "tree_support_wall_count", "elefant_foot_compensation_layers",
        "skirt_loops", "skirt_height", "wall_distribution_count", "slow_down_layers", "seam_slope_steps"
    ];

    private static readonly string[] Switches =
    [
        "enable_support", "support_on_build_plate_only", "only_one_wall_top", "only_one_wall_first_layer",
        "spiral_mode", "alternate_extra_wall", "ensure_vertical_shell_thickness",
        "independent_support_layer_height", "support_critical_regions_only", "support_interface_loop_pattern",
        "support_interface_not_for_body", "support_remove_small_overhang", "support_ironing",
        "enable_support_ironing", "tree_support_adaptive_layer_height", "tree_support_auto_brim", "bridge_no_support",
        "accel_to_decel_enable", "enable_arc_fitting", "enable_overhang_speed", "detect_overhang_wall",
        "detect_thin_wall", "detect_narrow_internal_solid_infill", "infill_combination",
        "extra_perimeters_on_overhangs", "infill_first", "is_infill_first", "reduce_crossing_wall",
        "interface_shells", "precise_outer_wall", "precise_z_height", "rotate_solid_infill_direction",
        "seam_slope_conditional", "seam_slope_entire_loop", "seam_slope_inner_walls", "staggered_inner_seams",
        "thick_bridges", "thick_internal_bridges", "make_overhang_printable"
    ];

    private static readonly Dictionary<string, string[]> Choices = new(StringComparer.Ordinal)
    {
        ["sparse_infill_pattern"] =
        [
            "grid", "gyroid", "rectilinear", "line", "honeycomb", "3dhoneycomb", "cubic",
            "adaptivecubic", "supportcubic", "triangles", "trihexagon", "concentric"
        ],
        ["top_surface_pattern"] = ["rectilinear", "monotonic", "monotonicline", "concentric", "hilbertcurve"],
        ["bottom_surface_pattern"] = ["rectilinear", "monotonic", "monotonicline", "concentric", "hilbertcurve"],
        ["internal_solid_infill_pattern"] = ["rectilinear", "zig-zag", "monotonic", "monotonicline", "concentric"],
        ["support_type"] = ["normal(auto)", "normal(manual)", "tree(auto)", "tree(manual)"],
        ["support_style"] = ["default", "grid", "snug", "tree_slim", "tree_strong", "tree_hybrid", "tree_organic"],
        ["support_base_pattern"] = ["default", "rectilinear", "rectilinear-grid", "honeycomb", "lightning", "hollow"],
        ["support_interface_pattern"] = ["auto", "rectilinear", "concentric", "rectilinear_interlaced", "grid"],
        ["support_ironing_pattern"] = ["concentric", "rectilinear", "zig-zag"],
        ["wall_generator"] = ["classic", "arachne"],
        ["ensure_vertical_shell_thickness"] =
            ["0", "1", "none", "ensure_critical_only", "ensure_moderate", "ensure_all", "disabled", "partial", "enabled"],
        ["seam_position"] = ["nearest", "aligned", "back", "random"],
        ["brim_type"] = ["auto_brim", "brim_ears", "outer_only", "inner_only", "outer_and_inner", "no_brim"],
        ["ironing_type"] = ["no ironing", "top", "topmost", "solid"],
        ["ironing_pattern"] = ["rectilinear", "zig-zag", "concentric"],
        ["print_sequence"] = ["by layer", "by object"],
        ["wall_sequence"] = ["inner wall/outer wall", "outer wall/inner wall", "inner-outer-inner wall"],
        ["slicing_mode"] = ["regular", "even_odd", "close_holes"],
        ["draft_shield"] = ["disabled", "limited", "enabled"],
        ["fuzzy_skin"] = ["none", "external", "all"]
    };

    private static readonly string[] SlotSettings =
    [
        "support_filament", "support_interface_filament", "solid_infill_filament", "sparse_infill_filament", "wall_filament",
        "outer_wall_filament_id", "inner_wall_filament_id", "internal_solid_filament_id", "sparse_infill_filament_id",
        "top_surface_filament_id", "bottom_surface_filament_id"
    ];

    private static readonly Dictionary<string, string[]> SettingAliases = new(StringComparer.Ordinal)
    {
        ["enable_support_ironing"] = ["support_ironing"],
        ["support_ironing"] = ["enable_support_ironing"],
        ["infill_first"] = ["is_infill_first"],
        ["is_infill_first"] = ["infill_first"],
        ["wall_filament"] = ["outer_wall_filament_id", "inner_wall_filament_id"],
        ["solid_infill_filament"] = ["internal_solid_filament_id", "top_surface_filament_id", "bottom_surface_filament_id"],
        ["sparse_infill_filament"] = ["sparse_infill_filament_id"]
    };
    private static readonly Dictionary<string, string> SourceAliases = SettingAliases
        .SelectMany(pair => pair.Value.Select(key => (Target: key, Source: pair.Key)))
        .ToDictionary(pair => pair.Target, pair => pair.Source, StringComparer.Ordinal);

    internal static bool UsesFilamentSlot(string key) => SlotSettings.Contains(key);
    private static readonly string[] Percentages =
    [
        "sparse_infill_density", "skeleton_infill_density", "skin_infill_density",
        "support_threshold_overlap", "support_ironing_flow",
        "raft_first_layer_density", "tree_support_top_rate", "ironing_flow", "seam_gap", "bridge_density",
        "internal_bridge_density", "accel_to_decel_factor", "infill_wall_overlap", "top_bottom_infill_wall_overlap"
    ];

    private static readonly string[] SignedNumbers =
    [
        "support_expansion", "raft_first_layer_expansion", "xy_contour_compensation", "xy_hole_compensation",
        "bridge_angle", "internal_bridge_angle", "infill_direction", "solid_infill_direction", "ironing_angle"
    ];

    private static readonly string[] NumericOrPercent =
    [
        "support_line_width", "line_width", "initial_layer_line_width", "inner_wall_line_width", "outer_wall_line_width",
        "sparse_infill_line_width", "internal_solid_infill_line_width", "top_surface_line_width",
        "infill_anchor", "infill_anchor_max", "small_perimeter_threshold", "min_bead_width",
        "initial_layer_min_bead_width", "min_feature_size", "wall_transition_filter_deviation", "wall_transition_length",
        "bridge_speed", "internal_bridge_speed", "gap_infill_speed", "initial_layer_speed",
        "initial_layer_infill_speed", "initial_layer_travel_speed", "inner_wall_speed", "outer_wall_speed",
        "internal_solid_infill_speed", "sparse_infill_speed", "top_surface_speed", "support_speed",
        "support_interface_speed", "ironing_speed", "skirt_speed", "small_perimeter_speed", "travel_speed",
        "travel_speed_z", "overhang_1_4_speed", "overhang_2_4_speed", "overhang_3_4_speed", "overhang_4_4_speed",
        "default_acceleration", "bridge_acceleration", "initial_layer_acceleration", "inner_wall_acceleration",
        "outer_wall_acceleration", "internal_solid_infill_acceleration", "sparse_infill_acceleration",
        "top_surface_acceleration", "travel_acceleration", "default_jerk", "infill_jerk", "initial_layer_jerk",
        "inner_wall_jerk", "outer_wall_jerk", "top_surface_jerk", "travel_jerk"
    ];

    private static readonly HashSet<string> VariantSettings = new(StringComparer.Ordinal)
    {
        "initial_layer_speed", "initial_layer_infill_speed", "outer_wall_speed", "inner_wall_speed",
        "small_perimeter_speed", "small_perimeter_threshold", "sparse_infill_speed", "internal_solid_infill_speed",
        "top_surface_speed", "enable_overhang_speed", "overhang_1_4_speed", "overhang_2_4_speed",
        "overhang_3_4_speed", "overhang_4_4_speed", "bridge_speed", "gap_infill_speed", "support_speed",
        "support_interface_speed", "travel_speed", "travel_speed_z", "default_acceleration", "travel_acceleration",
        "initial_layer_acceleration", "outer_wall_acceleration", "inner_wall_acceleration",
        "sparse_infill_acceleration", "top_surface_acceleration", "top_solid_infill_flow_ratio"
    };

    internal static IEnumerable<string> Keys =>
        Positive.Concat(NonNegative).Concat(Integers).Concat(Switches)
            .Concat(Choices.Keys).Concat(SlotSettings).Concat(Percentages).Concat(SignedNumbers)
            .Concat(NumericOrPercent).Distinct(StringComparer.Ordinal);

    internal static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? result) ? result : null;

    internal static List<string> Transfer(JsonObject source, JsonObject? preset, JsonObject target, int slots,
        List<string> warnings, List<string> retained, (int Index, int Count)? variant = null, ReviewSettings? review = null)
    {
        var transferred = new List<string>();
        var skipped = new List<string>();
        var differences = new List<SettingDifference>();
        foreach (string key in Keys)
        {
            if (!target.ContainsKey(key) && TargetKeys(key, target).Any())
                continue;
            string? alias = SourceAliases.GetValueOrDefault(key);
            JsonNode? presetValue = FindValue(preset, key, alias, target);
            JsonNode? raw = presetValue ?? FindValue(source, key, alias, target);
            if (raw is null)
                continue;

            if (JsonNode.DeepEquals(raw, target[key]))
                continue;

            JsonNode preferred = raw;
            int? preferredIndex = null;
            if (presetValue is null && variant is { } active && VariantSettings.Contains(key) &&
                raw is JsonArray activeValues && activeValues.Count == active.Count && activeValues[active.Index] is JsonNode selected)
            {
                preferred = selected;
                preferredIndex = active.Index;
            }
            JsonNode? translated = TranslateValue(key, preferred, target, slots);
            if (review is null)
            {
                if (translated is not null)
                {
                    target[key] = translated;
                    transferred.Add(key);
                }
                else
                    skipped.Add(key);
                continue;
            }

            string destinationChoice = target[key] is null
                ? T("Leave unset", "Lascia non impostato") : T("Keep destination", "Mantieni destinazione");
            var options = new List<SettingOption> { new(destinationChoice, null) };
            JsonArray? names = ((presetValue is null ? source : preset)?["print_extruder_variant"] ??
                source["print_extruder_variant"]) as JsonArray;
            if (translated is not null)
                options.Add(SourceOption(translated, preferredIndex is int selectedIndex
                    ? VariantName(names, selectedIndex, ((JsonArray)raw).Count) : null));
            if (raw is JsonArray variants && VariantSettings.Contains(key))
            {
                for (int index = 0; index < variants.Count; index++)
                {
                    if (variants[index] is not JsonNode option || TranslateValue(key, option, target, slots) is not JsonNode candidate ||
                        options.Any(item => JsonNode.DeepEquals(item.Value, candidate)))
                        continue;
                    options.Add(SourceOption(candidate, VariantName(names, index, variants.Count)));
                }
            }
            differences.Add(new SettingDifference(key, DisplayValue(raw), DisplayValue(target[key]), options,
                translated is null ? SettingDifference.DestinationIndex : 1));
        }
        if (differences.Count > 0)
        {
            IReadOnlyDictionary<string, int> choices = review!(differences);
            foreach (SettingDifference difference in differences)
            {
                int index = choices.GetValueOrDefault(difference.Key, difference.DefaultIndex);
                if (index < 0 || index >= difference.Options.Count)
                    throw new InvalidDataException(T("A setting choice is invalid.", "Una scelta delle impostazioni non è valida."));
                if (difference.Options[index].Value is JsonNode value)
                {
                    target[difference.Key] = value;
                    transferred.Add(difference.Key);
                }
                else if (difference.Options.Count > 1)
                    retained.Add(difference.Key);
                else
                    skipped.Add(difference.Key);
            }
        }

        if (skipped.Count > 0)
            warnings.Add(T("Source values without a compatible mapping · ", "Valori di origine senza una mappatura compatibile · ") + string.Join(", ", skipped) + ".");
        if (preset is null && source["different_settings_to_system"] is JsonArray { Count: > 0 } groups &&
            StringValue(groups[0]) is string sourceOverrides)
        {
            string[] unmapped = sourceOverrides.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(key => !transferred.Contains(key) && !skipped.Contains(key) && !retained.Contains(key) &&
                    !TargetKeys(key, target).Any(mapped => transferred.Contains(mapped) || skipped.Contains(mapped) ||
                        retained.Contains(mapped) || JsonNode.DeepEquals(source[key], target[mapped])) &&
                    !JsonNode.DeepEquals(source[key], target[key]))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (unmapped.Length > 0)
                warnings.Add(T("Process overrides without an available mapping · ", "Personalizzazioni di processo senza una mappatura disponibile: ") + string.Join(", ", unmapped) + ".");
        }
        return transferred;
    }

    private static SettingOption SourceOption(JsonNode value, string? variant)
    {
        string label = variant is null ? "" : $" ({variant})";
        string display = DisplayValue(value);
        return new(T($"Use source{label} · {display}", $"Usa origine{label} · {display}"), value);
    }

    private static string VariantName(JsonArray? names, int index, int count) =>
        names?.Count == count && StringValue(names[index]) is string name
            ? name : T($"Variant {index + 1}", $"Variante {index + 1}");

    private static JsonNode? TranslateValue(string key, JsonNode value, JsonObject target, int slots)
    {
        if (NumericOrPercent.Contains(key) && TryTransferNumeric(key, value, target, out JsonNode? numeric))
            return numeric;
        string? scalar = StringValue(value);
        if (value is JsonArray { Count: > 0 } array)
        {
            scalar = StringValue(array[0]);
            if (array.Any(item => StringValue(item) != scalar))
                return null;
        }
        if (scalar is null || !TryTranslate(key, scalar, target, slots, out string translated))
            return null;
        return target[key] is JsonArray targetValues
            ? new JsonArray(Enumerable.Range(0, targetValues.Count).Select(_ => (JsonNode?)JsonValue.Create(translated)).ToArray())
            : JsonValue.Create(translated);
    }

    private static string DisplayValue(JsonNode? value) => value switch
    {
        null => T("Unavailable", "Non disponibile"),
        JsonArray array => string.Join(" / ", array.Select(item => StringValue(item) ?? item?.ToJsonString() ?? "–")),
        _ => StringValue(value) ?? value.ToJsonString()
    };

    private static bool TryTransferNumeric(string key, JsonNode source, JsonObject target, out JsonNode? value)
    {
        value = null;
        string? scalar = StringValue(source);
        if (source is JsonArray { Count: > 0 } sourceValues)
        {
            scalar = StringValue(sourceValues[0]);
            if (sourceValues.Any(item => StringValue(item) != scalar))
                return false;
        }
        if (scalar is null || !IsNonNegativeNumberOrPercent(scalar))
            return false;

        if (target[key] is JsonArray { Count: > 0 } targetValues &&
            targetValues.All(item => StringValue(item) is not null))
            value = new JsonArray(Enumerable.Range(0, targetValues.Count).Select(_ => (JsonNode?)JsonValue.Create(scalar)).ToArray());
        else if (StringValue(target[key]) is not null)
            value = JsonValue.Create(scalar);
        return value is not null;
    }

    private static JsonNode? FindValue(JsonObject? profile, string key, string? alias, JsonObject target) =>
        profile?[key] ?? (alias is not null && !target.ContainsKey(alias) ? profile?[alias] : null);

    internal static IEnumerable<string> TargetKeys(string key, JsonObject target) =>
        target.ContainsKey(key) ? [key] :
        SettingAliases.TryGetValue(key, out string[]? aliases) ? aliases.Where(target.ContainsKey) : [];

    internal static bool TryTranslate(string key, string value, JsonObject target, int slots, out string translatedValue)
    {
        translatedValue = (key, value) switch
        {
            ("support_type", "normal") => "normal(manual)",
            ("support_type", "tree") => "tree(manual)",
            ("support_type", "hybrid(auto)") => "tree(auto)",
            ("support_base_pattern", "none") => "hollow",
            ("support_ironing_pattern", "zig-zag") when target.ContainsKey("support_ironing") => "rectilinear",
            ("support_ironing_pattern", "rectilinear") when target.ContainsKey("enable_support_ironing") => "zig-zag",
            ("internal_solid_infill_pattern" or "ironing_pattern", "zig-zag") when target.ContainsKey("support_ironing") => "rectilinear",
            ("ensure_vertical_shell_thickness", "1" or "enabled") when IsOrcaShellSetting(target) => "ensure_all",
            ("ensure_vertical_shell_thickness", "0" or "disabled") when IsOrcaShellSetting(target) => "none",
            ("ensure_vertical_shell_thickness", "ensure_all" or "enabled") when StringValue(target[key]) is "0" or "1" => "1",
            ("ensure_vertical_shell_thickness", "none" or "disabled") when StringValue(target[key]) is "0" or "1" => "0",
            ("ensure_vertical_shell_thickness", "partial") when IsOrcaShellSetting(target) => "ensure_moderate",
            ("ensure_vertical_shell_thickness", "ensure_all" or "1") when IsBambuShellSetting(target) => "enabled",
            ("ensure_vertical_shell_thickness", "none" or "0") when IsBambuShellSetting(target) => "disabled",
            ("ensure_vertical_shell_thickness", "ensure_moderate") when IsBambuShellSetting(target) => "partial",
            _ => value
        };
        return CanTransfer(key, translatedValue, target, slots);
    }

    private static bool IsOrcaShellSetting(JsonObject target) =>
        StringValue(target["ensure_vertical_shell_thickness"]) is "none" or "ensure_critical_only" or "ensure_moderate" or "ensure_all";

    private static bool IsBambuShellSetting(JsonObject target) =>
        StringValue(target["ensure_vertical_shell_thickness"]) is "disabled" or "partial" or "enabled";

    private static bool CanTransfer(string key, string value, JsonObject target, int slots)
    {
        if (StringValue(target[key]) is null &&
            !(VariantSettings.Contains(key) && target[key] is JsonArray { Count: > 0 } values &&
                values.All(item => StringValue(item) is not null)))
            return false;
        if (key == "ensure_vertical_shell_thickness")
        {
            if (IsOrcaShellSetting(target))
                return value is "none" or "ensure_critical_only" or "ensure_moderate" or "ensure_all";
            if (IsBambuShellSetting(target))
                return value is "disabled" or "partial" or "enabled";
            return value is "0" or "1";
        }
        if (Choices.TryGetValue(key, out string[]? choices))
            return choices.Contains(value, StringComparer.Ordinal);
        if (Switches.Contains(key))
            return value is "0" or "1";
        if (Integers.Contains(key))
            return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer) &&
                (integer >= 0 || (integer == -1 && key is "support_interface_bottom_layers" or "tree_support_wall_count"));
        if (SlotSettings.Contains(key))
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot) && slot <= slots;
        if (Positive.Contains(key))
            return TryNumber(value, out double positive) && positive > 0;
        if (NonNegative.Contains(key))
            return TryNumber(value, out double number) && number >= 0;
        if (SignedNumbers.Contains(key))
            return TryNumber(value, out _);
        if (Percentages.Contains(key))
            return value.EndsWith('%') && TryNumber(value[..^1], out double percent) && percent is >= 0 and <= 100;
        return NumericOrPercent.Contains(key) && IsNonNegativeNumberOrPercent(value);
    }

    private static bool IsNonNegativeNumberOrPercent(string value) =>
        TryNumber(value.EndsWith('%') ? value[..^1] : value, out double number) && number >= 0;

    internal static bool TryNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}
