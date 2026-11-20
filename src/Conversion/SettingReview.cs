using System.Text.Json.Nodes;

namespace Translator;

internal sealed record SettingOption(string Label, JsonNode? Value);

internal sealed record SettingDifference(string Key, string SourceValue, string DestinationValue,
    IReadOnlyList<SettingOption> Options, int DefaultIndex, string? ScopedKey = null, string? Scope = null)
{
    internal const int DestinationIndex = 0;
    internal string DisplayName => Scope is null ? Key : $"{Scope} · {ScopedKey}";
    internal bool IsObjectSetting => ProcessSettings.IsSourceObjectSetting(ScopedKey ?? Key);
    internal int ObjectSettingsIndex => IsObjectSetting ? DefaultIndex : DestinationIndex;

    internal int SelectedIndex(IReadOnlyDictionary<string, int>? selections)
    {
        int index = selections?.GetValueOrDefault(Key, DefaultIndex) ?? DefaultIndex;
        return index >= 0 && index < Options.Count ? index : throw new InvalidDataException(
            T("A setting choice is invalid.", "Una scelta delle impostazioni non è valida."));
    }
}

internal delegate IReadOnlyDictionary<string, int> ReviewSettings(IReadOnlyList<SettingDifference> differences);
