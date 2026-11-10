using System.Runtime.InteropServices;

namespace Translator;

internal static class AppTheme
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3MFTranslator", "theme.txt");

    internal static bool Dark { get; private set; } = ReadPreference();
    internal static Color Canvas => Shade(0xEDF2FA, 0x111827);
    internal static Color Surface => Shade(0xFFFFFF, 0x1C2638);
    internal static Color Input => Shade(0xF7F9FD, 0x141D2D);
    internal static Color Ink => Shade(0x16233F, 0xEEF3FF);
    internal static Color Muted => Shade(0x4C5D7C, 0xB4C1D9);
    internal static Color Navy => Shade(0x152241, 0x0C1424);
    internal static Color Accent => Shade(0x3D49C2, 0xC5CEFF);
    internal static Color AccentLight => Shade(0xE8ECFF, 0x29385F);
    internal static Color Primary => Shade(0x3D49C2, 0x4658C9);
    internal static Color Border => Shade(0xC9D2E8, 0x40506C);

    internal static Color Shade(int light, int dark) => Color.FromArgb(unchecked((int)0xFF000000) | (Dark ? dark : light));

    internal static void ApplyNativeTheme() =>
        Application.SetColorMode(Dark ? SystemColorMode.Dark : SystemColorMode.Classic);

    internal static void ApplyTitleBar(nint window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        const int immersiveDarkMode = 20;
        int enabled = Dark && !SystemInformation.HighContrast ? 1 : 0;
        _ = DwmSetWindowAttribute(window, immersiveDarkMode, in enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, in int value, int size);

    internal static void SetDark(bool dark)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        File.WriteAllText(PreferencePath, dark ? "dark" : "light");
        Dark = dark;
    }

    private static bool ReadPreference()
    {
        try { return File.ReadAllText(PreferencePath).Trim() == "dark"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
