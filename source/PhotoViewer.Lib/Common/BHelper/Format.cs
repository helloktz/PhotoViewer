
using System;
using System.Globalization;
using System.Linq;

namespace PhotoViewer.Common;

public partial class BHelper
{

    public static string FormatSize(long size)
    {
        var mod = 1024d;
        var sized = size * 1d;

        var units = new string[] { "bytes", "KB", "MB", "GB", "TB", "PB" };
        int i;
        for (i = 0; sized > mod; i++)
        {
            sized /= mod;
        }

        return String.Format("{0} {1}", Math.Round(sized, 2), units[i]);
    }

    public static string FormatUtcRoundtrip(DateTime dt)
    {
        return dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    public static bool TryParseUtcRoundtrip(string? value, out DateTime utc)
    {
        utc = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
        {
            utc = dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime();
            return true;
        }

        return false;
    }

    public static string FormatDateTime(string? str, bool includeTime = true)
    {
        var dt = ConvertDateTime(str);

        return FormatDateTime(dt, includeTime);
    }

    public static string FormatDateTime(DateTime? dt, bool includeTime = true)
    {
        if (dt != null)
        {
            const string DATETIME_FORMAT = "yyyy/MM/dd HH:mm:ss";
            const string DATE_FORMAT = "yyyy/MM/dd";

            return dt.Value.ToString(includeTime ? DATETIME_FORMAT : DATE_FORMAT);
        }

        return string.Empty;
    }

    public static DateTime? ConvertDateTime(string? dt)
    {
        if (DateTime.TryParseExact(dt,
                    "yyyy:MM:dd HH:mm:ss",
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    out var dateTaken))
        {
            return dateTaken;
        }

        return null;
    }

    public static int FormatStarRating(int rating)
    {
        var star = 0;

        if (rating >= 1 && rating <= 12)
            star = 1;
        else if (rating >= 13 && rating <= 37)
            star = 2;
        else if (rating >= 38 && rating <= 62)
            star = 3;
        else if (rating >= 63 && rating <= 87)
            star = 4;
        else if (rating >= 88 && rating <= 99)
            star = 5;

        return star;
    }

    public static string FormatStarRatingText(int rating)
    {
        var star = FormatStarRating(rating);
        var ratingText = star > 0
            ? string.Empty.PadRight(star, '⭐').PadRight(5, '✰').Replace("✰", " ✰")
            : string.Empty;

        return ratingText;
    }

    public static int[] SimplifyFractions(params int[] numbers)
    {
        var gcd = numbers.Aggregate((currentGcd, arg) => CalculateGcd(currentGcd, arg));

        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] /= gcd;
        }

        return numbers;

        int CalculateGcd(int a, int b)
        {
            while (b > 0)
            {
                int rem = a % b;
                a = b;
                b = rem;
            }
            return a;
        }
    }

}
