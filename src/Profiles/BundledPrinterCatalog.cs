using System.Reflection;
using System.Text.Json;

namespace Translator;

internal static class BundledPrinterCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> Models = new(ReadModels);

    internal static IReadOnlyDictionary<string, string[]> ByBrand => Models.Value;

    private static IReadOnlyDictionary<string, string[]> ReadModels()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Translator.PrinterModelCatalog.json")
            ?? throw new InvalidOperationException("The bundled printer model catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidDataException("The bundled printer model catalog is invalid.");
    }
}
