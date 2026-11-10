using System.Text.Json.Nodes;

namespace Translator;

internal sealed record SettingOption(string Label, JsonNode? Value);

internal sealed record SettingDifference(string Key, string SourceValue, string DestinationValue,
    IReadOnlyList<SettingOption> Options, int DefaultIndex)
{
    internal const int DestinationIndex = 0;
}

internal delegate IReadOnlyDictionary<string, int> ReviewSettings(IReadOnlyList<SettingDifference> differences);
