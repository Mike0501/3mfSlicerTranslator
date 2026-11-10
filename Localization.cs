global using static Translator.Localization;

using System.Globalization;

namespace Translator;

internal static class Localization
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3MFTranslator", "language.txt");

    internal static bool IsItalian { get; private set; } = ReadLanguage();

    internal static string T(string english, string italian) => IsItalian ? italian : english;

    internal static void SetLanguage(bool italian)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        File.WriteAllText(PreferencePath, italian ? "it" : "en");
        IsItalian = italian;
    }

    private static bool ReadLanguage()
    {
        try
        {
            return File.ReadAllText(PreferencePath).Trim() switch
            {
                "it" => true,
                "en" => false,
                _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it"
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";
        }
    }
}
