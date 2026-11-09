using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StreamOverlay;

/// <summary>Writes a snapshot as one JSON object. Null values are left out, so a reader sees a missing key, not a
/// made-up zero.</summary>
internal static class OverlayJson
{
    public static string Write(IReadOnlyDictionary<string, object?> values)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (KeyValuePair<string, object?> pair in values.Where(pair => pair.Value != null))
        {
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append(Quote(pair.Key)).Append(':');
            Append(sb, pair.Value!);
        }
        return sb.Append('}').ToString();
    }

    private static string Quote(string? value)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case < ' ': sb.Append("\\u").Append(((int)c).ToString("x4")); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static void Append(StringBuilder sb, object value)
    {
        switch (value)
        {
            case bool b: sb.Append(b ? "true" : "false"); break;
            case string s: sb.Append(Quote(s)); break;
            case int or long or short: sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case IEnumerable list:
                sb.Append('[');
                bool first = true;
                foreach (object? item in list)
                {
                    if (item == null)
                        continue;
                    if (!first)
                        sb.Append(',');
                    first = false;
                    Append(sb, item);
                }
                sb.Append(']');
                break;
            default: sb.Append(Quote(value.ToString())); break;
        }
    }
}
