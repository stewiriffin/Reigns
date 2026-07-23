using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Allocation-conscious text helpers for TMP labels on the Android hot path.
/// Prefer these over string.Format / interpolation when updating every year or drag frame.
/// </summary>
public static class NoAllocText
{
    private static readonly StringBuilder SharedBuilder = new StringBuilder(64);

    /// <summary>Reusable builder for composing HUD strings without per-frame new strings.</summary>
    public static StringBuilder Builder
    {
        get
        {
            SharedBuilder.Length = 0;
            return SharedBuilder;
        }
    }

    /// <summary>
    /// TMP format overload writes into TMP's internal buffer (no managed string.Format garbage).
    /// Format must use TMP numeric placeholders, e.g. "Year {0}".
    /// </summary>
    public static void SetFormatted(TextMeshProUGUI label, string format, int arg0)
    {
        if (label == null)
            return;

        if (string.IsNullOrEmpty(format))
        {
            label.SetText(arg0);
            return;
        }

        label.SetText(format, arg0);
    }

    public static void SetFormatted(TextMeshProUGUI label, string format, int arg0, int arg1)
    {
        if (label == null)
            return;

        if (string.IsNullOrEmpty(format))
        {
            label.SetText(arg0);
            return;
        }

        label.SetText(format, arg0, arg1);
    }

    public static void SetFromBuilder(TextMeshProUGUI label, StringBuilder builder)
    {
        if (label == null || builder == null)
            return;

        label.SetText(builder);
    }

    /// <summary>
    /// Builds "You ruled for {years} year(s).\nLongest Reign: {longest}" with no intermediate strings.
    /// </summary>
    public static void SetGameOverYears(TextMeshProUGUI label, int yearsRuled, int longestReign)
    {
        if (label == null)
            return;

        StringBuilder sb = Builder;
        sb.Append("You ruled for ");
        sb.Append(yearsRuled);
        sb.Append(yearsRuled == 1 ? " year.\nLongest Reign: " : " years.\nLongest Reign: ");
        sb.Append(longestReign);
        label.SetText(sb);
    }
}
