using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BiggerLobbies.Utils
{
    // A small JSON reader and writer for the mod's own files (the Give back records).
    // The mod targets netstandard2.1, which has no JSON library, and the game's Newtonsoft
    // is an Il2Cpp copy that is awkward to use from managed code.
    public static class MiniJson
    {
        /// <summary>Objects become Dictionary, arrays List, numbers double. Throws FormatException on bad input.</summary>
        public static object? Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException($"unexpected text at {i}");
            return v;
        }

        private static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object? Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        private static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        private static Dictionary<string, object?> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object?>();
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException($"expected a key at {i}");
                string k = Str(s, ref i);
                Ws(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"expected ':' at {i}");
                i++;
                d[k] = Value(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException($"expected ',' or '}}' at {i}");
            }
        }

        private static List<object?> Arr(string s, ref int i)
        {
            var l = new List<object?>();
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return l; }
                throw new FormatException($"expected ',' or ']' at {i}");
            }
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                            throw new FormatException($"bad \\u escape at {i}");
                        sb.Append((char)cp); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        private static double Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw new FormatException($"bad value at {start}");
            return v;
        }

        /// <summary>A JSON string literal, quotes included.</summary>
        public static string Quote(string v)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in v ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string Number(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>The same value written with sorted keys and no spaces, so two values can be compared whatever their layout.</summary>
        public static string Canonical(object? v)
        {
            switch (v)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case double d: return Number(d);
                case string s: return Quote(s);
                case Dictionary<string, object?> o:
                    return "{" + string.Join(",", o.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => Quote(k) + ":" + Canonical(o[k]))) + "}";
                case List<object?> l:
                    return "[" + string.Join(",", l.Select(Canonical)) + "]";
                default: return Quote(v.ToString() ?? "");
            }
        }

        public static string Str(Dictionary<string, object?> o, string key) => o.TryGetValue(key, out var v) && v is string s ? s : "";
        public static double Num(Dictionary<string, object?> o, string key) => o.TryGetValue(key, out var v) && v is double d ? d : 0;
    }
}
