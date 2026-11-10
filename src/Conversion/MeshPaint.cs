using System.Text;

namespace Translator;

internal static class MeshPaint
{
    // The slicer stores the root first, starting at the rightmost hexadecimal digit.
    internal static string Remap(string encoded, Func<int, int> map)
    {
        const string hex = "0123456789ABCDEF";
        var result = new StringBuilder(encoded.Length);
        int position = encoded.Length - 1;
        int pending = 1;
        while (pending > 0)
        {
            int code = ReadDigit();
            int splits = code & 3;
            pending--;
            if (splits > 0)
            {
                pending += splits + 1;
                result.Append(hex[code]);
                continue;
            }

            int slot = code >> 2;
            if (slot == 3)
                slot = 3 + ReadDigit();
            int destination = slot == 0 ? 0 : map(slot);
            if (destination > 16)
                throw InvalidPaint();
            result.Append(hex[Math.Min(destination, 3) << 2]);
            if (destination >= 3)
                result.Append(hex[destination - 3]);
        }
        if (position != -1)
            throw InvalidPaint();
        char[] digits = result.ToString().ToCharArray();
        Array.Reverse(digits);
        return new string(digits);

        int ReadDigit()
        {
            int digit = position < 0 ? -1 : hex.IndexOf(char.ToUpperInvariant(encoded[position--]));
            return digit >= 0 ? digit : throw InvalidPaint();
        }
    }

    private static InvalidDataException InvalidPaint() => new(T("The mesh contains an unsupported painted color encoding.",
        "La mesh contiene una codifica del colore dipinto non supportata."));
}
