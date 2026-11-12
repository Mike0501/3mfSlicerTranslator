using System.Text.Json;

namespace Translator;

internal sealed record DestinationProfile(string Name, string Printer, string Process, string Filament,
    string Bed, decimal LayerHeight, int Slots, string? ReferencePath = null, PrintPreset? Preset = null,
    ReferenceMachine? Machine = null)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Printer : Name;
}

internal static class ProfileStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3MFTranslator", "destinations.json");

    internal static List<DestinationProfile> ReadSaved() =>
        File.Exists(StorePath) ? JsonSerializer.Deserialize<List<DestinationProfile>>(File.ReadAllText(StorePath)) ?? [] : [];

    internal static DirectoryInfo CreateMigrationFolder() =>
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(StorePath)!, "migration", Guid.NewGuid().ToString("N")));

    internal static DestinationProfile ImportReference(string name, string source, DestinationProfile? previous = null,
        ReferenceMachine? machine = null, string? buildPlate = null)
    {
        // Validate the archive before creating a copy, and use the saved copy as the source of profile metadata.
        _ = ProjectConverter.GetReferencePrinter(source);
        string root = Path.GetDirectoryName(StorePath)!;
        DirectoryInfo folder = Directory.CreateDirectory(Path.Combine(root, "references", Guid.NewGuid().ToString("N")));
        bool saved = false;
        try
        {
            string path = Path.Combine(folder.FullName, Path.GetFileName(source));
            using (FileStream input = File.OpenRead(source))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                input.CopyTo(output);
            ReferenceConfiguration reference = ProjectConverter.PrepareReferenceCopy(path, machine, buildPlate);
            string printer = reference.Machine.Preset;
            string plate = reference.CurrentPlate ?? "";
            var profile = previous is null
                ? new DestinationProfile(name, printer, "", "", plate, 0, reference.FilamentSlots, path, Machine: reference.Machine)
                : previous with { Printer = printer, ReferencePath = path, Bed = plate, Machine = reference.Machine, Slots = reference.FilamentSlots };

            Save(profile, previous);
            saved = true;
            return profile;
        }
        finally
        {
            if (!saved)
                folder.Delete(recursive: true);
        }
    }

    internal static DestinationProfile SavePreset(DestinationProfile source, string name, PrintPreset preset)
    {
        _ = ProjectConverter.GetDestinationPreset(source with { Preset = preset });
        var variant = source with { Name = name, Preset = preset };
        bool editing = source.Preset is not null && source.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase);
        Save(variant, editing ? source : null);
        return variant;
    }

    internal static void Delete(DestinationProfile profile) => Delete(profile, StorePath);

    private static void Delete(DestinationProfile profile, string storePath)
    {
        using var gate = new FileStream(storePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        List<DestinationProfile> profiles = File.Exists(storePath)
            ? JsonSerializer.Deserialize<List<DestinationProfile>>(File.ReadAllText(storePath)) ?? [] : [];
        int index = profiles.FindIndex(item => item.Name.Equals(profile.Name, StringComparison.CurrentCultureIgnoreCase));
        if (index < 0 || profiles[index] != profile)
            throw new InvalidOperationException(T("The profile has changed. Reopen the library and try again.",
                "Il profilo è cambiato. Riapri la libreria e riprova."));
        profiles.RemoveAt(index);
        WriteProfiles(storePath, profiles);
    }

    private static void Save(DestinationProfile profile, DestinationProfile? previous)
    {
        using var gate = new FileStream(StorePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        List<DestinationProfile> profiles = ReadSaved();
        int index = profiles.FindIndex(item => item.Name.Equals(profile.Name, StringComparison.CurrentCultureIgnoreCase));
        if (previous is null)
        {
            if (index >= 0)
                throw new InvalidOperationException(T("A profile with this name already exists.", "Esiste già un profilo con questo nome."));
            profiles.Add(profile);
        }
        else
        {
            if (index < 0 || profiles[index] != previous)
                throw new InvalidOperationException(T("The profile has changed. Reopen the library and try again.",
                    "Il profilo è cambiato. Riapri la libreria e riprova."));
            profiles[index] = profile;
        }

        WriteProfiles(StorePath, profiles);
    }

    private static void WriteProfiles(string storePath, List<DestinationProfile> profiles)
    {
        string temporary = storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profiles.OrderBy(item => item.Name)));
            File.Move(temporary, storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
