using System.Globalization;
using System.Text;

namespace AutoFlow.Infrastructure.Documents;

/// <summary>
/// Text handling for the two standard PDF fonts the invoice uses (Helvetica
/// and Helvetica-Bold with WinAnsi encoding): which byte a character becomes,
/// how wide it prints, and how a string is written into a content stream.
/// Standard fonts need no embedding, which keeps the file small and the
/// renderer free of native dependencies (ADR 0020).
/// </summary>
internal static class PdfText
{
    private const int FirstWidth = 32;
    private const int FallbackWidth = 556;

    // Advance widths of the characters 32 (space) to 126 (~) in 1/1000 of the
    // font size, from the Adobe font metrics of the standard 14 fonts.
    private static readonly int[] RegularWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly int[] BoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    /// <summary>WinAnsi (Windows-1252) bytes of the characters it has beyond Latin-1.</summary>
    private static readonly Dictionary<char, byte> WinAnsiExtras = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['‘'] = 0x91, ['’'] = 0x92,
        ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['™'] = 0x99,
        ['Š'] = 0x8A, ['š'] = 0x9A, ['Ž'] = 0x8E, ['ž'] = 0x9E, ['Œ'] = 0x8C, ['œ'] = 0x9C, ['Ÿ'] = 0x9F,
    };

    /// <summary>Letters without a canonical decomposition that still have an obvious Latin stand-in.</summary>
    private static readonly Dictionary<char, char> Lookalikes = new()
    {
        ['ı'] = 'i', ['ł'] = 'l', ['Ł'] = 'L', ['đ'] = 'd', ['Đ'] = 'D',
    };

    /// <summary>Printed width of <paramref name="text"/> in points.</summary>
    public static double WidthOf(string text, double size, bool bold)
    {
        var widths = bold ? BoldWidths : RegularWidths;
        var total = 0;
        foreach (var b in Encode(text))
        {
            var index = b - FirstWidth;
            total += index >= 0 && index < widths.Length ? widths[index] : FallbackWidth;
        }

        return total * size / 1000d;
    }

    /// <summary>
    /// The text as a PDF literal string: WinAnsi bytes, with parentheses and
    /// backslashes escaped and everything outside printable ASCII written as
    /// an octal escape, so content streams stay plain ASCII.
    /// </summary>
    public static string Literal(string text)
    {
        var literal = new StringBuilder("(");
        foreach (var b in Encode(text))
        {
            switch (b)
            {
                case (byte)'(' or (byte)')' or (byte)'\\':
                    literal.Append('\\').Append((char)b);
                    break;
                case >= 32 and <= 126:
                    literal.Append((char)b);
                    break;
                default:
                    literal.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    break;
            }
        }

        return literal.Append(')').ToString();
    }

    /// <summary>
    /// Breaks <paramref name="text"/> into lines no wider than
    /// <paramref name="maxWidth"/> points, at spaces where possible. A word
    /// that is too wide on its own is split.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, double maxWidth, double size, bool bold)
    {
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            while (true)
            {
                var candidate = current.Length == 0 ? rest : $"{current} {rest}";
                if (WidthOf(candidate, size, bold) <= maxWidth)
                {
                    current = candidate;
                    break;
                }

                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = string.Empty;
                    continue;
                }

                // The word alone does not fit: put as many characters as fit on this line.
                var fits = 1;
                while (fits < rest.Length && WidthOf(rest[..(fits + 1)], size, bold) <= maxWidth)
                {
                    fits++;
                }

                lines.Add(rest[..fits]);
                rest = rest[fits..];
                if (rest.Length == 0)
                {
                    break;
                }
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    /// <summary>A coordinate or size as PDF writes numbers: invariant, at most two decimals.</summary>
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// WinAnsi bytes of <paramref name="text"/>. Characters the encoding lacks
    /// are replaced by their base letter where there is one (ğ → g, ı → i)
    /// and by a question mark otherwise, so a name is never dropped.
    /// </summary>
    private static IEnumerable<byte> Encode(string text)
    {
        foreach (var character in text)
        {
            if (character is (>= ' ' and <= '~') or (>= ' ' and <= 'ÿ'))
            {
                yield return (byte)character;
            }
            else if (WinAnsiExtras.TryGetValue(character, out var extra))
            {
                yield return extra;
            }
            else if (Lookalikes.TryGetValue(character, out var lookalike))
            {
                yield return (byte)lookalike;
            }
            else if (char.IsWhiteSpace(character))
            {
                yield return (byte)' ';
            }
            else
            {
                var baseLetter = character.ToString().Normalize(NormalizationForm.FormD)[0];
                yield return baseLetter is >= ' ' and <= '~' ? (byte)baseLetter : (byte)'?';
            }
        }
    }
}
