using System.Globalization;
using System.Text.RegularExpressions;

namespace TheIsleOverlay.Core;

public static partial class ClipboardCoordinateParser
{
    private const int MaximumTextLength = 4096;
    private const double MaximumAbsoluteCoordinate = 2_000_000d;
    private const double MinimumPlausibleCoordinate = 1_000d;

    public static bool TryParse(string? text, out WorldLocation location)
    {
        location = new WorldLocation();
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextLength)
        {
            return false;
        }

        var normalized = text
            .Replace('\u2212', '-')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u00A0', ' ')
            .Replace('\u2009', ' ');

        var legacy = LegacyPattern().Match(normalized);
        if (legacy.Success)
        {
            var values = legacy.Groups.Cast<Group>()
                .Skip(1)
                .Where(group => group.Success)
                .Select(group => ParseUs(group.Value))
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .ToArray();
            if (TryCreate(values, out location))
            {
                return true;
            }
        }

        var us = Extract(normalized, UsNumberPattern(), ParseUs);
        var eu = Extract(normalized, EuNumberPattern(), ParseEu);
        var valuesToUse = Plausible(us) && Plausible(eu)
            ? Magnitude(us) >= Magnitude(eu) ? us : eu
            : Plausible(us) ? us : eu;
        return TryCreate(valuesToUse, out location);
    }

    private static double[] Extract(string text, Regex pattern, Func<string, double?> parser) =>
        pattern.Matches(text)
            .Cast<Match>()
            .Select(match => parser(match.Value))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Take(3)
            .ToArray();

    private static bool TryCreate(IReadOnlyList<double> values, out WorldLocation location)
    {
        location = new WorldLocation();
        if (!Plausible(values))
        {
            return false;
        }

        location = new WorldLocation
        {
            X = values[0],
            Y = values[1],
            Z = values.Count > 2 ? values[2] : null
        };
        return true;
    }

    private static bool Plausible(IReadOnlyList<double> values) =>
        values.Count >= 2 &&
        values.All(value => double.IsFinite(value) && Math.Abs(value) <= MaximumAbsoluteCoordinate) &&
        (Math.Abs(values[0]) >= MinimumPlausibleCoordinate || Math.Abs(values[1]) >= MinimumPlausibleCoordinate) &&
        (values.Take(2).Any(value => value != Math.Truncate(value)) ||
         values.Take(2).Any(value => Math.Abs(value) > 10_000d));

    private static double Magnitude(IReadOnlyList<double> values) =>
        values.Count < 2 ? double.NegativeInfinity : Math.Abs(values[0]) + Math.Abs(values[1]);

    private static double? ParseUs(string value) =>
        double.TryParse(
            value.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    private static double? ParseEu(string value) =>
        double.TryParse(
            value.Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    [GeneratedRegex(@"[-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex UsNumberPattern();

    [GeneratedRegex(@"[-+]?(?:\d{1,3}(?:\.\d{3})+|\d+)(?:,\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex EuNumberPattern();

    [GeneratedRegex(@"lat\s*[:=]\s*([-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?).*?long\s*[:=]\s*([-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)(?:.*?alt\s*[:=]\s*([-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?))?", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyPattern();
}
