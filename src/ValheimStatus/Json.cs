using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimStatus
{
    // Insertion-ordered object so the output key order is stable across writes.
    internal sealed class JsonObject : List<KeyValuePair<string, object>>
    {
        public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));
    }

    // Minimal JSON writer. Unity's JsonUtility cannot serialize dictionaries and
    // Newtonsoft is not guaranteed to ship with the game, so this covers exactly the
    // shapes we emit: JsonObject, IList, string, bool, integers, float/double, null.
    internal static class Json
    {
        public static string Serialize(object value, bool pretty)
        {
            var sb = new StringBuilder(1024);
            Write(sb, value, pretty, 0);
            if (pretty) sb.Append('\n');
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, bool pretty, int depth)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    WriteNumber(sb, f);
                    break;
                case double d:
                    WriteNumber(sb, d);
                    break;
                case JsonObject o:
                    WriteObject(sb, o, pretty, depth);
                    break;
                case IList list:
                    WriteArray(sb, list, pretty, depth);
                    break;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            // "R" round-trips; make sure whole numbers still read as floats ("1.0")
            // so consumers that inferred float from the old producer keep working.
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
            sb.Append(s);
        }

        private static void WriteObject(StringBuilder sb, JsonObject o, bool pretty, int depth)
        {
            if (o.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            for (int i = 0; i < o.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty) Indent(sb, depth + 1);
                WriteString(sb, o[i].Key);
                sb.Append(pretty ? ": " : ":");
                Write(sb, o[i].Value, pretty, depth + 1);
            }
            if (pretty) Indent(sb, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IList list, bool pretty, int depth)
        {
            if (list.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty) Indent(sb, depth + 1);
                Write(sb, list[i], pretty, depth + 1);
            }
            if (pretty) Indent(sb, depth);
            sb.Append(']');
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        // Player names are user input: escape everything JSON requires, pass other
        // non-ASCII through as UTF-8.
        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
