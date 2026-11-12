using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace Translator;

internal sealed record LargeModelPart(string Name, long Bytes);
internal sealed record LargeModelRequest(string ArchiveName, IReadOnlyList<LargeModelPart> Parts)
{
    internal long RequiredBytes => Parts.Max(part => part.Bytes);
    internal long RequiredMiB => (RequiredBytes + ProjectArchive.MiB - 1) / ProjectArchive.MiB;
}

internal sealed class ProjectArchive : IDisposable
{
    internal const long MiB = 1024 * 1024;
    internal const long DefaultModelSizeLimit = 128 * MiB;
    internal const long MaximumModelSizeLimit = 512 * MiB;
    internal const string Model = "3D/3dmodel.model";
    internal const string Settings = "Metadata/project_settings.config";
    internal const string ModelSettings = "Metadata/model_settings.config";
    internal const string LayerEvents = "Metadata/custom_gcode_per_layer.xml";
    internal const string ContentTypes = "[Content_Types].xml";
    internal const string RootRelationships = "_rels/.rels";
    internal static readonly XNamespace Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    private readonly ZipArchive zip;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<LargeModelRequest, long?>? reviewLargeModels;
    private readonly string archiveName;
    internal long ModelSizeLimit { get; private set; } = DefaultModelSizeLimit;

    internal ProjectArchive(string path, Func<LargeModelRequest, long?>? reviewLargeModels = null)
    {
        this.reviewLargeModels = reviewLargeModels;
        archiveName = Path.GetFileName(path);
        zip = ZipFile.OpenRead(path);
        try
        {
            if (zip.Entries.Count > 10_000 || zip.Entries.Sum(entry => (decimal)entry.Length) > 2_000_000_000)
                throw new InvalidDataException(T("The 3MF archive exceeds this version's size limits.", "L'archivio 3MF supera i limiti di dimensione di questa versione."));

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (!IsPartName(entry.FullName.TrimEnd('/')))
                    throw new InvalidDataException(T("The archive contains an invalid internal path.", "L'archivio contiene un percorso interno non valido."));
                if (!entry.FullName.EndsWith('/') && !entries.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException(T("The archive contains duplicate parts.", "L'archivio contiene parti duplicate."));
            }
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    internal static bool IsPartName(string name) =>
        name.Split('/').All(segment => segment.Length > 0 && segment is not ("." or "..") &&
            !segment.EndsWith('.') && !segment.EndsWith(' ') &&
            !segment.Any(character => character is '\\' or ':' || char.IsControl(character)));

    internal bool Contains(string name) => entries.ContainsKey(name);
    internal IEnumerable<string> Names => entries.Keys;

    private Stream Open(string name, long limit)
    {
        if (!entries.TryGetValue(name, out ZipArchiveEntry? entry))
            throw new InvalidDataException(T($"Missing 3MF part · {name}.", $"Parte mancante nel 3MF: {name}."));
        if (entry.Length > limit)
            throw new InvalidDataException(T($"3MF part exceeds the size limit · {name}.", $"Parte troppo grande nel 3MF: {name}."));
        return entry.Open();
    }

    internal XDocument ReadXml(string name)
    {
        long limit = DefaultModelSizeLimit;
        if (IsGeometryModel(name))
        {
            if (entries.TryGetValue(name, out var entry) && entry.Length > ModelSizeLimit)
            {
                var largeParts = entries.Values.Where(part => IsGeometryModel(part.FullName) && part.Length > DefaultModelSizeLimit)
                    .Select(part => new LargeModelPart(part.FullName, part.Length)).ToArray();
                var request = new LargeModelRequest(archiveName, largeParts);
                if (request.RequiredBytes > MaximumModelSizeLimit)
                    throw new InvalidDataException(T($"A model in {archiveName} requires {request.RequiredMiB} MiB. This version supports up to {MaximumModelSizeLimit / MiB} MiB per model.",
                        $"Un modello in {archiveName} richiede {request.RequiredMiB} MiB. Questa versione supporta fino a {MaximumModelSizeLimit / MiB} MiB per modello."));
                if (reviewLargeModels is null)
                    throw new InvalidDataException(T($"3MF model exceeds the default read limit · {name} ({request.RequiredMiB} MiB required; {DefaultModelSizeLimit / MiB} MiB default). Use the desktop app to approve a higher limit.",
                        $"Il modello 3MF supera il limite di lettura predefinito · {name} (richiesti {request.RequiredMiB} MiB; predefiniti {DefaultModelSizeLimit / MiB} MiB). Usa l’app desktop per autorizzare un limite maggiore."));
                long selected = reviewLargeModels(request) ?? throw new OperationCanceledException();
                if (selected < request.RequiredBytes || selected > MaximumModelSizeLimit)
                    throw new InvalidDataException(T("The selected model read limit is outside the supported range.",
                        "Il limite di lettura scelto per i modelli non rientra nell’intervallo supportato."));
                ModelSizeLimit = selected;
            }
            limit = ModelSizeLimit;
        }
        using Stream stream = Open(name, limit);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = limit
        });
        return XDocument.Load(reader);
    }

    private static bool IsGeometryModel(string name) => name.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".model", StringComparison.OrdinalIgnoreCase);

    internal JsonObject ReadSettings(string name = Settings)
    {
        return JsonNode.Parse(ReadText(name)) as JsonObject ??
            throw new InvalidDataException(T("The 3MF project profile does not contain a JSON object.", "Il profilo del progetto 3MF non contiene un oggetto JSON."));
    }

    internal string ReadText(string name)
    {
        using Stream stream = Open(name, 16 * 1024 * 1024);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static bool IsProcessPreset(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, @"\AMetadata/process_settings_\d+\.config\z",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal void CopyTo(string name, ZipArchive destination)
    {
        ZipArchiveEntry entry = entries[name];
        using Stream input = entry.Open();
        using Stream output = destination.CreateEntry(name, CompressionLevel.Optimal).Open();
        input.CopyTo(output);
    }

    internal static void WriteXml(ZipArchive destination, string name, XDocument document)
    {
        using Stream output = destination.CreateEntry(name).Open();
        using XmlWriter writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true
        });
        document.Save(writer);
    }

    internal void Validate(string label)
    {
        if (!Contains(Settings) || !Contains(ModelSettings))
            throw new InvalidDataException(T($"The {label} file must include the settings of a supported project.", $"Il file {label} deve contenere le impostazioni di un progetto supportato."));
        ValidateStructure(label);
    }

    internal void ValidateStructure(string label)
    {
        if (!Contains(ContentTypes) || !Contains(RootRelationships) || !Contains(Model))
            throw new InvalidDataException(T($"The {label} project has an incomplete or unsupported 3MF structure.", $"Il progetto {label} ha una struttura 3MF incompleta o non supportata."));
    }

    public void Dispose() => zip.Dispose();
}
