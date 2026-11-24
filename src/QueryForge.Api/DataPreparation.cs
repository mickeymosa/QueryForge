using System.Globalization;
using System.Text.RegularExpressions;

namespace QueryForge.Api;

public sealed record WorkspaceColumnProfile(string Name, string SuggestedType, int Blank,
    int Distinct, int Placeholder, int InvariantNumbers, int UsNumbers, int EuNumbers,
    int UnambiguousDates, int AmbiguousDates, int CaseVariants, int Outliers);

public static class DataPreparation
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const NumberStyles DecimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    private static readonly Regex Us = new(@"^[+-]?(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)(?:\.[0-9]+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex Eu = new(@"^[+-]?(?:[0-9]+|[0-9]{1,3}(?:[. ][0-9]{3})+)(?:,[0-9]+)?$", RegexOptions.CultureInvariant);
    private static readonly string[] Iso = ["yyyy-MM-dd", "yyyy/MM/dd", "yyyy-M-d", "yyyy/M/d"];
    private static readonly string[] Dmy = ["d/M/yyyy", "d-M-yyyy", "d.M.yyyy"];
    private static readonly string[] Mdy = ["M/d/yyyy", "M-d-yyyy"];
    private static readonly string[] Named = ["MMM d, yyyy", "MMMM d, yyyy", "d MMM yyyy", "d MMMM yyyy"];
    private static readonly HashSet<string> Placeholders = new(["N/A", "NULL", "—", "NaN"], StringComparer.OrdinalIgnoreCase);

    public static bool IsPlaceholder(string value) => Placeholders.Contains(value.Trim());

    public static bool TryNumber(string raw, string policy, out decimal value)
    {
        value = 0;
        string text = raw.Trim().Replace('\u00a0', ' ').Replace('\u202f', ' ');
        if (policy == "invariant")
            return decimal.TryParse(text, DecimalStyle, Invariant, out value);
        if (policy == "us" && Us.IsMatch(text))
            return decimal.TryParse(text.Replace(",", ""), DecimalStyle, Invariant, out value);
        if (policy == "eu" && Eu.IsMatch(text) && !(text.Contains('.') && text.Contains(' ')))
            return decimal.TryParse(text.Replace(".", "").Replace(" ", "").Replace(',', '.'), DecimalStyle, Invariant, out value);
        return false;
    }

    public static bool TryDate(string raw, string policy, out DateOnly date)
    {
        date = default;
        string text = raw.Trim();
        if (DateOnly.TryParseExact(text, Iso.Concat(Named).ToArray(), Invariant, DateTimeStyles.None, out date))
            return true;
        bool d = DateOnly.TryParseExact(text, Dmy, Invariant, DateTimeStyles.None, out var day);
        bool m = DateOnly.TryParseExact(text, Mdy, Invariant, DateTimeStyles.None, out var month);
        if (policy == "dmy" && d) { date = day; return true; }
        if (policy == "mdy" && m) { date = month; return true; }
        if (policy == "unambiguous" && (d || m) && !(d && m && day != month))
        { date = d ? day : month; return true; }
        return false;
    }

    public static string Normalize(string original, TransformOperation operation, string? policy)
    {
        if (operation == TransformOperation.NormalizeMissing)
            return IsPlaceholder(original) ? "" : original;
        if (operation == TransformOperation.NormalizeNumber && TryNumber(original, policy!, out var number))
            return number.ToString("G29", Invariant);
        if (operation == TransformOperation.NormalizeDate && TryDate(original, policy!, out var date))
            return date.ToString("yyyy-MM-dd", Invariant);
        return original; // Invalid or unresolved input is never overwritten.
    }

    public static IReadOnlyList<WorkspaceColumnProfile> Profile(DatasetEntity dataset)
    {
        var rows = dataset.Rows;
        return dataset.Headers.Select(h =>
        {
            var values = rows.Select(r => r.GetValueOrDefault(h, "")).ToArray();
            var present = values.Where(v => !string.IsNullOrWhiteSpace(v) && !IsPlaceholder(v)).ToArray();
            int inv = present.Count(v => TryNumber(v, "invariant", out _));
            int us = present.Count(v => TryNumber(v, "us", out _));
            int eu = present.Count(v => TryNumber(v, "eu", out _));
            int dates = present.Count(v => TryDate(v, "unambiguous", out _));
            int ambiguous = present.Count(v => TryDate(v, "dmy", out var d) && TryDate(v, "mdy", out var m) && d != m);
            int distinct = present.Distinct(StringComparer.Ordinal).Count();
            int variants = distinct - present.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            bool identifier = h.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                h.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ||
                h.Contains("code", StringComparison.OrdinalIgnoreCase) ||
                present.Any(v => v.Trim().Length > 1 && v.Trim()[0] == '0' && v.Trim().All(char.IsDigit));
            string type = present.Length == 0 ? "empty" : identifier ? "identifier candidate" :
                dates + ambiguous >= Math.Ceiling(present.Length * .8) ? "date candidate" :
                Math.Max(inv, Math.Max(us, eu)) >= Math.Ceiling(present.Length * .8) ? "numeric candidate" :
                distinct <= Math.Min(20, Math.Max(2, present.Length / 2)) ? "category candidate" : "text";
            int outliers = 0;
            if (type == "numeric candidate" && inv == present.Length && inv >= 8)
            {
                var sorted = present.Select(v => { TryNumber(v, "invariant", out var n); return (double)n; }).Order().ToArray();
                double q1 = sorted[(sorted.Length-1)/4], q3 = sorted[3*(sorted.Length-1)/4], iqr = q3-q1;
                if (iqr > 0) outliers = sorted.Count(n => n < q1-1.5*iqr || n > q3+1.5*iqr);
            }
            return new WorkspaceColumnProfile(h,type,values.Count(string.IsNullOrWhiteSpace),distinct,
                values.Count(IsPlaceholder),inv,us,eu,dates,ambiguous,variants,outliers);
        }).ToArray();
    }
}
