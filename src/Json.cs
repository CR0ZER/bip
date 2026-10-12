// JSON maison, lecture et ecriture. Lecture : objets -> Dictionary<string, object>,
// tableaux -> object[], nombres -> int, long, decimal ou double. Le JavaScriptSerializer
// du .NET Framework coutait ~60 ms de chargement a chaque lancement du hook.
// Ecriture : garde les accents lisibles et indente settings.json comme l'original.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeNotch
{
    static class Json
    {
        // Leve FormatException si le texte n'est pas du JSON valide.
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            int i = 0;
            var v = Value(text, ref i, 0);
            Ws(text, ref i);
            if (i != text.Length) throw Bad(i);
            return v;
        }

        static Exception Bad(int at) { return new FormatException("JSON invalide (position " + at + ")"); }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '﻿')) i++;
        }

        static object Value(string s, ref int i, int depth)
        {
            if (depth > 256) throw Bad(i);
            Ws(s, ref i);
            if (i >= s.Length) throw Bad(i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw Bad(i);
                    var key = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Bad(i);
                    i++;
                    d[key] = Value(s, ref i, depth + 1);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw Bad(i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                while (true)
                {
                    l.Add(Value(s, ref i, depth + 1));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                    throw Bad(i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            return Number(s, ref i);
        }

        static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static string Str(string s, ref int i)
        {
            i++;   // guillemet ouvrant
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Bad(i);
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw Bad(i - 1);
                }
            }
            throw Bad(i);
        }

        // Memes types que JavaScriptSerializer : int, puis long, puis decimal ; double en dernier recours.
        static object Number(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw Bad(i);
            var t = s.Substring(start, i - start);
            var inv = CultureInfo.InvariantCulture;
            int n; long l; decimal m; double d;
            if (int.TryParse(t, NumberStyles.AllowLeadingSign, inv, out n)) return n;
            if (long.TryParse(t, NumberStyles.AllowLeadingSign, inv, out l)) return l;
            if (decimal.TryParse(t, NumberStyles.Float, inv, out m)) return m;
            if (double.TryParse(t, NumberStyles.Float, inv, out d)) return d;
            throw Bad(start);
        }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        public static object[] Arr(object o) { return o as object[]; }

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
