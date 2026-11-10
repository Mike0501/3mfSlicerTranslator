using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace Translator;

internal sealed class ProjectArchive : IDisposable
{
    internal const string Model = "3D/3dmodel.model";
    internal const string Settings = "Metadata/project_settings.config";
    internal const string ModelSettings = "Metadata/model_settings.config";
    internal const string LayerEvents = "Metadata/custom_gcode_per_layer.xml";
    internal const string ContentTypes = "[Content_Types].xml";
    internal const string RootRelationships = "_rels/.rels";
    internal static readonly XNamespace Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    private readonly ZipArchive zip;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.OrdinalIgnoreCase);

    internal ProjectArchive(string path)
    {
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
        const long limit = 128 * 1024 * 1024;
        using Stream stream = Open(name, limit);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = limit
        });
        return XDocument.Load(reader);
    }

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
