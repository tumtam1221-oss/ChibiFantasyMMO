using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// Builds a small JSON object.
    /// </summary>
    /// <remarks>
    /// Written by hand because Unity's <c>JsonUtility</c> cannot express a nested object
    /// without a matching serializable class per shape, and adding a third-party serializer
    /// to the one assembly that talks to the outside world is a dependency this does not
    /// need. Eight endpoints send flat objects with one nested block between them.
    ///
    /// Every value is escaped through <see cref="Escape"/>, so a player's name or password
    /// containing a quote or a backslash produces valid JSON rather than a malformed body
    /// the server rejects -- or worse, a body whose structure the value changed.
    /// </remarks>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _builder = new StringBuilder("{");
        private bool _hasAny;

        public JsonWriter Add(string key, string value)
        {
            Separate();
            _builder.Append('"').Append(Escape(key)).Append("\":");

            if (value == null) _builder.Append("null");
            else _builder.Append('"').Append(Escape(value)).Append('"');

            return this;
        }

        public JsonWriter Add(string key, int value)
        {
            Separate();
            _builder.Append('"').Append(Escape(key)).Append("\":")
                .Append(value.ToString(CultureInfo.InvariantCulture));

            return this;
        }

        public JsonWriter Add(string key, bool value)
        {
            Separate();
            _builder.Append('"').Append(Escape(key)).Append("\":")
                .Append(value ? "true" : "false");

            return this;
        }

        public JsonWriter AddObject(string key, JsonWriter nested)
        {
            Separate();
            _builder.Append('"').Append(Escape(key)).Append("\":").Append(nested.ToJson());

            return this;
        }

        public string ToJson()
        {
            return _builder.ToString() + "}";
        }

        private void Separate()
        {
            if (_hasAny) _builder.Append(',');
            _hasAny = true;
        }

        /// <summary>
        /// Escapes a string for inclusion in JSON.
        /// </summary>
        /// <remarks>Control characters are emitted as \u escapes rather than dropped: a
        /// value containing one must survive the round trip intact, because silently
        /// changing what a player typed is how a password stops matching.</remarks>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new StringBuilder(value.Length + 8);

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Reads a JSON object or array without allocating a document model.
    /// </summary>
    /// <remarks>
    /// <b>Total by construction.</b> Every accessor returns a default when the key is
    /// missing, the type is wrong, or the input was never valid JSON at all. A malformed
    /// response therefore produces empty values that the caller's own validation rejects,
    /// rather than an exception escaping from inside a network callback where nothing is
    /// prepared to catch it.
    ///
    /// It is a scanner, not a parser: it walks the text looking for a key at the current
    /// nesting depth. That is enough for responses this API actually returns -- flat objects
    /// and one array of flat objects -- and it is deliberately not a general JSON library.
    /// If a future response nests deeper, this must be replaced rather than extended.
    /// </remarks>
    public readonly struct JsonReader
    {
        private readonly string _json;

        private JsonReader(string json)
        {
            _json = json ?? string.Empty;
        }

        public static JsonReader Parse(string json)
        {
            return new JsonReader(json);
        }

        public bool IsEmpty => string.IsNullOrEmpty(_json);

        /// <summary>The string at a key, or empty.</summary>
        public string String(string key)
        {
            int value = ValueStart(key);

            if (value < 0) return string.Empty;

            if (_json[value] != '"') return string.Empty;

            var builder = new StringBuilder();

            for (int i = value + 1; i < _json.Length; i++)
            {
                char c = _json[i];

                if (c == '\\' && i + 1 < _json.Length)
                {
                    char next = _json[++i];

                    switch (next)
                    {
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'u':
                            if (i + 4 < _json.Length
                                && int.TryParse(_json.Substring(i + 1, 4),
                                    NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                    out int code))
                            {
                                builder.Append((char)code);
                                i += 4;
                            }

                            break;
                        default: builder.Append(next); break;
                    }

                    continue;
                }

                if (c == '"') break;

                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>The integer at a key, or zero.</summary>
        public int Int(string key)
        {
            int value = ValueStart(key);

            if (value < 0) return 0;

            int end = value;

            while (end < _json.Length
                && (char.IsDigit(_json[end]) || _json[end] == '-' || _json[end] == '+'))
            {
                end++;
            }

            return int.TryParse(_json.Substring(value, end - value), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
        }

        /// <summary>The boolean at a key. Missing reads as false.</summary>
        public bool Bool(string key)
        {
            int value = ValueStart(key);

            return value >= 0 && value + 3 < _json.Length
                && _json.Substring(value, 4) == "true";
        }

        /// <summary>Whether a key is present and explicitly null.</summary>
        public bool IsNull(string key)
        {
            int value = ValueStart(key);

            return value >= 0 && value + 3 < _json.Length && _json.Substring(value, 4) == "null";
        }

        /// <summary>
        /// The array of objects at a key.
        /// </summary>
        /// <remarks>Each element is returned as its own reader over the substring, so the
        /// accessors above work on it unchanged. Splitting on brace depth rather than on
        /// commas is what makes a comma inside a player's name harmless.</remarks>
        public IReadOnlyList<JsonReader> Array(string key)
        {
            var elements = new List<JsonReader>();

            int value = ValueStart(key);

            if (value < 0 || _json[value] != '[') return elements;

            int depth = 0;
            int start = -1;
            bool inString = false;

            for (int i = value; i < _json.Length; i++)
            {
                char c = _json[i];

                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;

                    continue;
                }

                if (c == '"') { inString = true; continue; }

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;

                    if (depth == 0 && start >= 0)
                    {
                        elements.Add(new JsonReader(_json.Substring(start, i - start + 1)));
                        start = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }

            return elements;
        }

        /// <summary>
        /// Finds where the value for a key begins, at the top level of this object.
        /// </summary>
        /// <remarks>Skips over nested objects and arrays so a key inside one cannot be
        /// mistaken for the same key at this level, and ignores anything inside a string so
        /// a key-like sequence in a player's name cannot match.</remarks>
        private int ValueStart(string key)
        {
            if (string.IsNullOrEmpty(_json) || string.IsNullOrEmpty(key)) return -1;

            string needle = "\"" + key + "\"";

            int depth = 0;
            bool inString = false;

            for (int i = 0; i < _json.Length; i++)
            {
                char c = _json[i];

                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;

                    continue;
                }

                if (c == '"')
                {
                    // A key only counts at depth 1: the object this reader represents.
                    if (depth == 1 && i + needle.Length <= _json.Length
                        && string.CompareOrdinal(_json, i, needle, 0, needle.Length) == 0)
                    {
                        int colon = _json.IndexOf(':', i + needle.Length);

                        if (colon < 0) return -1;

                        int value = colon + 1;

                        while (value < _json.Length && char.IsWhiteSpace(_json[value])) value++;

                        return value < _json.Length ? value : -1;
                    }

                    inString = true;
                    continue;
                }

                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }

            return -1;
        }
    }
}
