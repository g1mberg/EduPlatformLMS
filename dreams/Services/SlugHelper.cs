using System.Text;

namespace dreams.Services;

public static class SlugHelper
{
    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а']="a",['б']="b",['в']="v",['г']="g",['д']="d",['е']="e",['ё']="e",['ж']="zh",
        ['з']="z",['и']="i",['й']="y",['к']="k",['л']="l",['м']="m",['н']="n",['о']="o",
        ['п']="p",['р']="r",['с']="s",['т']="t",['у']="u",['ф']="f",['х']="h",['ц']="ts",
        ['ч']="ch",['ш']="sh",['щ']="sch",['ъ']="",['ы']="y",['ь']="",['э']="e",['ю']="yu",['я']="ya"
    };

    public static string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (Translit.TryGetValue(ch, out var t)) sb.Append(t);
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('-');
            // прочие символы пропускаем
        }

        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
