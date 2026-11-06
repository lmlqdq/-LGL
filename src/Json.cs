using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Liuguang.Core
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// 轻量 JSON 实现（不依赖任何第三方库，兼容 .NET Framework 4.0+）
    /// </summary>
    public class Json
    {
        public JsonKind Kind = JsonKind.Null;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue = "";
        public List<Json> Items;
        public Dictionary<string, Json> Props;

        private static readonly Json NullInstance = new Json();

        public static Json Null
        {
            get { return NullInstance; }
        }

        public static Json NewObject()
        {
            Json j = new Json();
            j.Kind = JsonKind.Object;
            j.Props = new Dictionary<string, Json>();
            return j;
        }

        public static Json NewArray()
        {
            Json j = new Json();
            j.Kind = JsonKind.Array;
            j.Items = new List<Json>();
            return j;
        }

        public static Json Str(string value)
        {
            Json j = new Json();
            j.Kind = JsonKind.String;
            j.StringValue = value == null ? "" : value;
            return j;
        }

        public static Json Num(double value)
        {
            Json j = new Json();
            j.Kind = JsonKind.Number;
            j.NumberValue = value;
            return j;
        }

        public static Json Bool(bool value)
        {
            Json j = new Json();
            j.Kind = JsonKind.Bool;
            j.BoolValue = value;
            return j;
        }

        public bool IsNull
        {
            get { return Kind == JsonKind.Null; }
        }

        public bool IsObject
        {
            get { return Kind == JsonKind.Object && Props != null; }
        }

        public bool IsArray
        {
            get { return Kind == JsonKind.Array && Items != null; }
        }

        public int Count
        {
            get
            {
                if (IsArray) return Items.Count;
                if (IsObject) return Props.Count;
                return 0;
            }
        }

        public Json this[string key]
        {
            get
            {
                if (!IsObject) return Null;
                Json v;
                if (Props.TryGetValue(key, out v) && v != null) return v;
                return Null;
            }
        }

        public Json this[int index]
        {
            get
            {
                if (!IsArray) return Null;
                if (index < 0 || index >= Items.Count) return Null;
                return Items[index];
            }
        }

        public bool Has(string key)
        {
            return IsObject && Props.ContainsKey(key);
        }

        public List<Json> AsArray()
        {
            if (IsArray) return Items;
            return new List<Json>();
        }

        public string AsString(string def)
        {
            if (Kind == JsonKind.String) return StringValue;
            if (Kind == JsonKind.Number) return NumberValue.ToString(CultureInfo.InvariantCulture);
            if (Kind == JsonKind.Bool) return BoolValue ? "true" : "false";
            return def;
        }

        public string AsString()
        {
            return AsString("");
        }

        public int AsInt(int def)
        {
            if (Kind == JsonKind.Number) return (int)NumberValue;
            if (Kind == JsonKind.String)
            {
                int r;
                if (int.TryParse(StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            }
            return def;
        }

        public long AsLong(long def)
        {
            if (Kind == JsonKind.Number) return (long)NumberValue;
            if (Kind == JsonKind.String)
            {
                long r;
                if (long.TryParse(StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            }
            return def;
        }

        public double AsDouble(double def)
        {
            if (Kind == JsonKind.Number) return NumberValue;
            if (Kind == JsonKind.String)
            {
                double r;
                if (double.TryParse(StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            }
            return def;
        }

        public bool AsBool(bool def)
        {
            if (Kind == JsonKind.Bool) return BoolValue;
            if (Kind == JsonKind.String) return StringValue == "true";
            return def;
        }

        public Json Set(string key, Json value)
        {
            if (!IsObject)
            {
                Kind = JsonKind.Object;
                Props = new Dictionary<string, Json>();
            }
            Props[key] = value;
            return this;
        }

        public Json Add(Json value)
        {
            if (!IsArray)
            {
                Kind = JsonKind.Array;
                Items = new List<Json>();
            }
            Items.Add(value);
            return this;
        }

        public override string ToString()
        {
            return ToJson(false);
        }

        public string ToJson()
        {
            return ToJson(true);
        }

        public string ToJson(bool pretty)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, pretty, 0);
            return sb.ToString();
        }

        private void Write(StringBuilder sb, bool pretty, int indent)
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(BoolValue ? "true" : "false");
                    break;
                case JsonKind.Number:
                    if (NumberValue == Math.Floor(NumberValue) && Math.Abs(NumberValue) < 1e15)
                        sb.Append(((long)NumberValue).ToString(CultureInfo.InvariantCulture));
                    else
                        sb.Append(NumberValue.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonKind.String:
                    WriteString(sb, StringValue);
                    break;
                case JsonKind.Array:
                    if (Items.Count == 0)
                    {
                        sb.Append("[]");
                        break;
                    }
                    sb.Append('[');
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n'); Indent(sb, indent + 1); }
                        Items[i].Write(sb, pretty, indent + 1);
                    }
                    if (pretty) { sb.Append('\n'); Indent(sb, indent); }
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    if (Props.Count == 0)
                    {
                        sb.Append("{}");
                        break;
                    }
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, Json> kv in Props)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        if (pretty) { sb.Append('\n'); Indent(sb, indent + 1); }
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        if (pretty) sb.Append(' ');
                        kv.Value.Write(sb, pretty, indent + 1);
                    }
                    if (pretty) { sb.Append('\n'); Indent(sb, indent); }
                    sb.Append('}');
                    break;
            }
        }

        private static void Indent(StringBuilder sb, int level)
        {
            for (int i = 0; i < level; i++) sb.Append("  ");
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
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
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static Json Parse(string text)
        {
            if (text == null) throw new JsonException("JSON 内容为空");
            Parser p = new Parser(text);
            p.SkipWhitespace();
            Json value = p.ParseValue();
            p.SkipWhitespace();
            if (!p.End) throw new JsonException("JSON 解析失败：位置 " + p.Position + " 处存在多余内容");
            return value;
        }

        public static Json TryParse(string text, Json fallback)
        {
            try
            {
                return Parse(text);
            }
            catch
            {
                return fallback;
            }
        }

        private class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string text)
            {
                s = text;
                i = 0;
            }

            public int Position
            {
                get { return i; }
            }

            public bool End
            {
                get { return i >= s.Length; }
            }

            public void SkipWhitespace()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF') i++;
                    else break;
                }
            }

            public Json ParseValue()
            {
                SkipWhitespace();
                if (i >= s.Length) throw new JsonException("JSON 意外结束");
                char c = s[i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return Json.Str(ParseString());
                if (c == 't') { Expect("true"); return Json.Bool(true); }
                if (c == 'f') { Expect("false"); return Json.Bool(false); }
                if (c == 'n') { Expect("null"); return Json.Null; }
                return ParseNumber();
            }

            private void Expect(string word)
            {
                if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                    throw new JsonException("JSON 解析失败：位置 " + i + " 处期望 " + word);
                i += word.Length;
            }

            private Json ParseObject()
            {
                Json obj = Json.NewObject();
                i++; // {
                while (true)
                {
                    SkipWhitespace();
                    if (i >= s.Length) throw new JsonException("JSON 对象未闭合");
                    if (s[i] == '}')
                    {
                        i++;
                        return obj;
                    }
                    if (s[i] != '"') throw new JsonException("JSON 解析失败：对象键必须是字符串，位置 " + i);
                    string key = ParseString();
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != ':') throw new JsonException("JSON 解析失败：缺少冒号，位置 " + i);
                    i++;
                    obj.Props[key] = ParseValue();
                    SkipWhitespace();
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == '}')
                    {
                        i++;
                        return obj;
                    }
                    throw new JsonException("JSON 解析失败：对象分隔符错误，位置 " + i);
                }
            }

            private Json ParseArray()
            {
                Json arr = Json.NewArray();
                i++; // [
                while (true)
                {
                    SkipWhitespace();
                    if (i >= s.Length) throw new JsonException("JSON 数组未闭合");
                    if (s[i] == ']')
                    {
                        i++;
                        return arr;
                    }
                    arr.Items.Add(ParseValue());
                    SkipWhitespace();
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == ']')
                    {
                        i++;
                        return arr;
                    }
                    throw new JsonException("JSON 解析失败：数组分隔符错误，位置 " + i);
                }
            }

            private string ParseString()
            {
                i++; // "
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw new JsonException("JSON 字符串未闭合");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length) throw new JsonException("JSON 转义不完整");
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
                            if (i + 4 > s.Length) throw new JsonException("JSON \\u 转义不完整");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default:
                            throw new JsonException("JSON 非法转义：\\" + e);
                    }
                }
            }

            private Json ParseNumber()
            {
                int start = i;
                while (i < s.Length)
                {
                    char c = s[i];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                    else break;
                }
                if (start == i) throw new JsonException("JSON 解析失败：位置 " + start + " 处存在非法字符");
                double d;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new JsonException("JSON 数字格式错误：" + s.Substring(start, i - start));
                return Json.Num(d);
            }
        }
    }

    public class JsonException : Exception
    {
        public JsonException(string message) : base(message)
        {
        }
    }
}
