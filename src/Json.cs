// Lecture : JavaScriptSerializer du .NET Framework (objets -> Dictionary<string, object>,
// tableaux -> object[]). Ecriture : maison, pour garder les accents lisibles et
// produire un settings.json indente comme l'original (JavaScriptSerializer
// echappe <, >, & et ' et n'indente pas).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace ClaudeNotch
{
    static class Json
    {
        public static object Parse(string text)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            return js.DeserializeObject(text);
        }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        public static object[] Arr(object o)
        {
            var a = o as object[];
            if (a != null) return a;
            var l = o as ArrayList;
            return l != null ? l.ToArray() : null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string Write(object o, bool indent = false)
        {
            var sb = new StringBuilder();
            WriteValue(sb, o, indent, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object o, bool indent, int depth)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is int || o is long || o is decimal || o is double || o is float)
            {
                sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture));
                return;
            }

            var dict = o as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(indent ? ": " : ":");
                    WriteValue(sb, kv.Value, indent, depth + 1);
                }
                NewLine(sb, indent, depth);
                sb.Append('}');
                return;
            }

            var list = o as IEnumerable;
            if (list != null)
            {
                var items = new List<object>();
                foreach (var x in list) items.Add(x);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, indent, depth + 1);
                    WriteValue(sb, items[i], indent, depth + 1);
                }
                NewLine(sb, indent, depth);
                sb.Append(']');
                return;
            }

            WriteString(sb, Convert.ToString(o, CultureInfo.InvariantCulture));
        }

        static void NewLine(StringBuilder sb, bool indent, int depth)
        {
            if (!indent) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void WriteString(StringBuilder sb, string s)
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
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
