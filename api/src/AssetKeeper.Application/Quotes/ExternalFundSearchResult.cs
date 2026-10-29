namespace AssetKeeper.Application.Quotes;

public sealed record ExternalFundSearchResult(
    string Code,
    string Name,
    string BaseName,
    string? ShareClass,
    string FundType,
    bool IsQdii,
    int ConfirmationDelayDays);

public static class FundProfileRules
{
    private static readonly HashSet<string> ShareClassSuffixes = ["A", "B", "C", "D", "E", "H", "I", "R", "Y", "Z"];

    public static bool IsQdii(string name, string? fundType)
    {
        var text = $"{name} {fundType}";
        return text.Contains("QDII", StringComparison.OrdinalIgnoreCase)
            || text.Contains("海外", StringComparison.OrdinalIgnoreCase)
            || text.Contains("全球", StringComparison.OrdinalIgnoreCase)
            || text.Contains("美元", StringComparison.OrdinalIgnoreCase)
            || text.Contains("港股", StringComparison.OrdinalIgnoreCase)
            || text.Contains("恒生", StringComparison.OrdinalIgnoreCase)
            || text.Contains("纳斯达克", StringComparison.OrdinalIgnoreCase)
            || text.Contains("标普", StringComparison.OrdinalIgnoreCase);
    }

    public static string? DetectShareClass(string name)
    {
        var value = (name ?? string.Empty).Trim();

        if (value.Length < 2)
        {
            return null;
        }

        var suffix = value[^1].ToString().ToUpperInvariant();

        if (!ShareClassSuffixes.Contains(suffix)
            || value.EndsWith("QDII", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var qdiiMarker = value.LastIndexOf("(QDII", StringComparison.OrdinalIgnoreCase);

        if (qdiiMarker >= 0)
        {
            var closing = value.IndexOf(')', qdiiMarker);

            if (closing != value.Length - 2)
            {
                return null;
            }
        }

        return suffix;
    }

    public static string BaseName(string name, string? shareClass)
    {
        var value = (name ?? string.Empty).Trim();

        if (!string.IsNullOrEmpty(shareClass)
            && value.Length > shareClass.Length
            && value.EndsWith(shareClass, StringComparison.OrdinalIgnoreCase))
        {
            return value[..^shareClass.Length].TrimEnd();
        }

        return value;
    }

    public static int ConfirmationDelayDays(bool isQdii) => isQdii ? 2 : 1;
}
