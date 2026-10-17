using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LanternLounge.Client.Online
{
    /// <summary>
    /// A tiny JSON reader and writer for the online messages, so the app needs no extra packages.
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) return null;
            var parser = new Parser(json);
            var value = parser.ReadValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new FormatException("Extra text after JSON value.");
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var pair in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        Write(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                }
                default: WriteString(sb, value.ToString()); break;
            }
        }

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
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string text;
            private int pos;

            public Parser(string text)
            {
                this.text = text;
            }

            public bool AtEnd => pos >= text.Length;

            public void SkipWhitespace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");
                char c = text[pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) throw new FormatException("Bad JSON literal.");
                pos += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var dict = new Dictionary<string, object>();
                pos++; // {
                SkipWhitespace();
                if (pos < text.Length && text[pos] == '}')
                {
                    pos++;
                    return dict;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[pos] != '"') throw new FormatException("Expected a property name.");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || text[pos] != ':') throw new FormatException("Expected ':'.");
                    pos++;
                    dict[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Unexpected end of object.");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == '}') { pos++; return dict; }
                    throw new FormatException("Expected ',' or '}'.");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                pos++; // [
                SkipWhitespace();
                if (pos < text.Length && text[pos] == ']')
                {
                    pos++;
                    return list;
                }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Unexpected end of array.");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == ']') { pos++; return list; }
                    throw new FormatException("Expected ',' or ']'.");
                }
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                pos++; // opening quote
                while (pos < text.Length)
                {
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (pos >= text.Length) break;
                    char e = text[pos++];
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
                            if (pos + 4 > text.Length) throw new FormatException("Bad \\u escape.");
                            sb.Append((char)int.Parse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default: throw new FormatException("Bad escape.");
                    }
                }
                throw new FormatException("Unterminated string.");
            }

            private double ReadNumber()
            {
                int start = pos;
                while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0) pos++;
                if (pos == start) throw new FormatException("Unexpected character in JSON.");
                return double.Parse(text.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Easy, forgiving reads from parsed JSON objects.</summary>
    internal static class Json
    {
        public static Dictionary<string, object> Obj(object o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> Arr(object o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as List<object> : null;

        public static string Str(object o, string key, string fallback = null) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is string s ? s : fallback;

        public static int Int(object o, string key, int fallback = 0) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is double n ? (int)n : fallback;

        public static bool Bool(object o, string key, bool fallback = false) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        public static int[] Ints(List<object> list)
        {
            if (list == null) return Array.Empty<int>();
            var result = new int[list.Count];
            for (int i = 0; i < result.Length; i++) result[i] = list[i] is double n ? (int)n : 0;
            return result;
        }
    }
}
