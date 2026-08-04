using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Overbless.Editor.Evidence
{
    public enum CanonicalJsonKind
    {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object
    }

    public sealed class CanonicalJsonProperty
    {
        public CanonicalJsonProperty(string name, CanonicalJsonValue value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Name { get; }
        public CanonicalJsonValue Value { get; }
    }

    /// <summary>
    /// A small JSON DOM that preserves input member order and duplicate members so evidence validation can reject them.
    /// </summary>
    public sealed class CanonicalJsonValue
    {
        private static readonly IReadOnlyList<CanonicalJsonProperty> EmptyProperties = new List<CanonicalJsonProperty>().AsReadOnly();
        private static readonly IReadOnlyList<CanonicalJsonValue> EmptyItems = new List<CanonicalJsonValue>().AsReadOnly();
        private readonly IReadOnlyList<CanonicalJsonProperty> properties;
        private readonly IReadOnlyList<CanonicalJsonValue> items;

        private CanonicalJsonValue(CanonicalJsonKind kind, bool booleanValue, double numberValue, string stringValue, IReadOnlyList<CanonicalJsonValue> arrayItems, IReadOnlyList<CanonicalJsonProperty> objectProperties)
        {
            Kind = kind;
            BooleanValue = booleanValue;
            NumberValue = numberValue;
            StringValue = stringValue;
            items = arrayItems ?? EmptyItems;
            properties = objectProperties ?? EmptyProperties;
        }

        public CanonicalJsonKind Kind { get; }
        public bool BooleanValue { get; }
        public double NumberValue { get; }
        public string StringValue { get; }
        public IReadOnlyList<CanonicalJsonValue> Items => items;
        public IReadOnlyList<CanonicalJsonProperty> Properties => properties;

        public static CanonicalJsonValue Null() => new CanonicalJsonValue(CanonicalJsonKind.Null, false, 0d, null, null, null);
        public static CanonicalJsonValue Boolean(bool value) => new CanonicalJsonValue(CanonicalJsonKind.Boolean, value, 0d, null, null, null);
        public static CanonicalJsonValue Number(double value) => new CanonicalJsonValue(CanonicalJsonKind.Number, false, value, null, null, null);
        public static CanonicalJsonValue String(string value) => new CanonicalJsonValue(CanonicalJsonKind.String, false, 0d, value ?? throw new ArgumentNullException(nameof(value)), null, null);

        public static CanonicalJsonValue Array(IEnumerable<CanonicalJsonValue> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new List<CanonicalJsonValue>();
            foreach (var value in values)
            {
                copy.Add(value ?? throw new ArgumentException("Array values cannot be null.", nameof(values)));
            }

            return new CanonicalJsonValue(CanonicalJsonKind.Array, false, 0d, null, copy.AsReadOnly(), null);
        }

        public static CanonicalJsonValue Object(IEnumerable<CanonicalJsonProperty> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new List<CanonicalJsonProperty>();
            foreach (var value in values)
            {
                copy.Add(value ?? throw new ArgumentException("Object properties cannot be null.", nameof(values)));
            }

            return new CanonicalJsonValue(CanonicalJsonKind.Object, false, 0d, null, null, copy.AsReadOnly());
        }

        public static CanonicalJsonValue Object(params CanonicalJsonProperty[] values) => Object((IEnumerable<CanonicalJsonProperty>)values);
        public static CanonicalJsonValue Array(params CanonicalJsonValue[] values) => Array((IEnumerable<CanonicalJsonValue>)values);

        public bool TryGetSingleProperty(string name, out CanonicalJsonValue value)
        {
            value = null;
            if (Kind != CanonicalJsonKind.Object || name == null) return false;

            var found = false;
            foreach (var property in properties)
            {
                if (!string.Equals(property.Name, name, StringComparison.Ordinal)) continue;
                if (found) return false;
                value = property.Value;
                found = true;
            }

            return found;
        }

        public CanonicalJsonValue WithoutTopLevelProperty(string name)
        {
            if (Kind != CanonicalJsonKind.Object) throw new InvalidOperationException("Only an object can remove a top-level property.");
            var result = new List<CanonicalJsonProperty>();
            foreach (var property in properties)
            {
                if (!string.Equals(property.Name, name, StringComparison.Ordinal)) result.Add(property);
            }

            return Object(result);
        }
    }

    /// <summary>RFC 8785-style canonical JSON primitives used for evidence bytes and hashes.</summary>
    public static class CanonicalJson
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public const int MaximumUtf8Bytes = 4 * 1024 * 1024;
        public const int MaximumNestingDepth = 64;
        public const int MaximumNodeCount = 100000;

        public static string Serialize(CanonicalJsonValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var builder = new StringBuilder();
            var nodeCount = 0;
            WriteValue(builder, value, 0, ref nodeCount);
            var serialized = builder.ToString();
            if (Utf8.GetByteCount(serialized) > MaximumUtf8Bytes)
            {
                throw new ArgumentException("Canonical JSON exceeds the maximum UTF-8 byte length.", nameof(value));
            }

            return serialized;
        }

        public static byte[] SerializeUtf8(CanonicalJsonValue value) => Utf8.GetBytes(Serialize(value));

        public static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (var hash = SHA256.Create())
            {
                return ToLowerHex(hash.ComputeHash(bytes));
            }
        }

        public static string Sha256Hex(CanonicalJsonValue value) => Sha256Hex(SerializeUtf8(value));
        public static string Sha256Hex(Stream stream, out long length)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Hash stream must be readable.", nameof(stream));

            using (var hash = SHA256.Create())
            {
                var buffer = new byte[81920];
                length = 0L;
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    length = checked(length + count);
                    hash.TransformBlock(buffer, 0, count, buffer, 0);
                }

                hash.TransformFinalBlock(new byte[0], 0, 0);
                return ToLowerHex(hash.Hash);
            }
        }

        public static bool TryParse(string json, out CanonicalJsonValue value, out string error)
        {
            value = null;
            error = null;
            if (json == null)
            {
                error = "JSON text is null.";
                return false;
            }

            try
            {
                if (Utf8.GetByteCount(json) > MaximumUtf8Bytes)
                {
                    error = "JSON exceeds the maximum UTF-8 byte length.";
                    return false;
                }

                var parser = new Parser(json);
                value = parser.ParseDocument();
                return true;
            }
            catch (FormatException exception)
            {
                error = exception.Message;
                return false;
            }
            catch (EncoderFallbackException)
            {
                error = "JSON text contains invalid UTF-16.";
                return false;
            }
        }

        public static bool TryParseUtf8(byte[] bytes, out CanonicalJsonValue value, out string error)
        {
            value = null;
            error = null;
            if (bytes == null)
            {
                error = "JSON bytes are null.";
                return false;
            }

            if (bytes.Length > MaximumUtf8Bytes)
            {
                error = "JSON exceeds the maximum UTF-8 byte length.";
                return false;
            }
            if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            {
                error = "UTF-8 BOM is forbidden.";
                return false;
            }

            try
            {
                return TryParse(Utf8.GetString(bytes), out value, out error);
            }
            catch (DecoderFallbackException)
            {
                error = "JSON is not valid UTF-8.";
                return false;
            }
        }

        public static bool TryParseCanonicalUtf8(byte[] bytes, out CanonicalJsonValue value, out string error)
        {
            if (!TryParseUtf8(bytes, out value, out error)) return false;
            if (bytes.Length == 0 || bytes[bytes.Length - 1] == (byte)'\n' || bytes[bytes.Length - 1] == (byte)'\r')
            {
                error = "Canonical JSON must not end with a newline.";
                value = null;
                return false;
            }

            byte[] canonical;
            try
            {
                canonical = SerializeUtf8(value);
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                value = null;
                return false;
            }

            if (!ByteArraysEqual(bytes, canonical))
            {
                error = "JSON bytes are not canonical.";
                value = null;
                return false;
            }

            return true;
        }

        public static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is required.", nameof(path));
            if (path.IndexOf('\0') >= 0 || path.IndexOf('\\') >= 0 || path[0] == '/' || path.IndexOf(':') >= 0)
            {
                throw new ArgumentException("Path must be a root-relative slash-delimited path.", nameof(path));
            }

            var normalized = path.Normalize(NormalizationForm.FormC);
            var segments = normalized.Split('/');
            if (segments.Length == 0) throw new ArgumentException("Path is required.", nameof(path));
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    throw new ArgumentException("Path contains an invalid segment.", nameof(path));
                }
            }

            return normalized;
        }

        public static bool IsNormalizedRelativePath(string path)
        {
            try
            {
                return string.Equals(path, NormalizeRelativePath(path), StringComparison.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static int CompareUtf8Ordinal(string left, string right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            var leftBytes = Utf8.GetBytes(left);
            var rightBytes = Utf8.GetBytes(right);
            var count = Math.Min(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < count; index++)
            {
                if (leftBytes[index] != rightBytes[index]) return leftBytes[index] < rightBytes[index] ? -1 : 1;
            }

            return leftBytes.Length.CompareTo(rightBytes.Length);
        }

        public static bool IsLowerSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f')) return false;
            }

            return true;
        }

        public static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            var different = 0;
            for (var index = 0; index < left.Length; index++) different |= left[index] ^ right[index];
            return different == 0;
        }

        private static void WriteValue(StringBuilder builder, CanonicalJsonValue value, int depth, ref int nodeCount)
        {
            if (depth > MaximumNestingDepth) throw new ArgumentException("Canonical JSON exceeds the maximum nesting depth.");
            if (++nodeCount > MaximumNodeCount) throw new ArgumentException("Canonical JSON exceeds the maximum node count.");

            switch (value.Kind)
            {
                case CanonicalJsonKind.Null:
                    builder.Append("null");
                    return;
                case CanonicalJsonKind.Boolean:
                    builder.Append(value.BooleanValue ? "true" : "false");
                    return;
                case CanonicalJsonKind.Number:
                    builder.Append(FormatNumber(value.NumberValue));
                    return;
                case CanonicalJsonKind.String:
                    WriteString(builder, value.StringValue);
                    return;
                case CanonicalJsonKind.Array:
                    builder.Append('[');
                    for (var index = 0; index < value.Items.Count; index++)
                    {
                        if (index > 0) builder.Append(',');
                        WriteValue(builder, value.Items[index], depth + 1, ref nodeCount);
                    }
                    builder.Append(']');
                    return;
                case CanonicalJsonKind.Object:
                    WriteObject(builder, value.Properties, depth, ref nodeCount);
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void WriteObject(StringBuilder builder, IReadOnlyList<CanonicalJsonProperty> properties, int depth, ref int nodeCount)
        {
            var sorted = new List<CanonicalJsonProperty>(properties);
            sorted.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            for (var index = 1; index < sorted.Count; index++)
            {
                if (string.Equals(sorted[index - 1].Name, sorted[index].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Canonical JSON objects cannot contain duplicate keys.");
                }
            }

            builder.Append('{');
            for (var index = 0; index < sorted.Count; index++)
            {
                if (index > 0) builder.Append(',');
                WriteString(builder, sorted[index].Name);
                builder.Append(':');
                WriteValue(builder, sorted[index].Value, depth + 1, ref nodeCount);
            }
            builder.Append('}');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            if (value == null) throw new ArgumentException("JSON string cannot be null.");
            builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) throw new ArgumentException("JSON string contains an unpaired surrogate.");
                    builder.Append(character);
                    builder.Append(value[++index]);
                    continue;
                }

                if (char.IsLowSurrogate(character)) throw new ArgumentException("JSON string contains an unpaired surrogate.");
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else builder.Append(character);
                        break;
                }
            }
            builder.Append('"');
        }

        private static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Canonical JSON accepts finite numbers only.");
            if (value == 0d) return "0";

            var roundTrip = value.ToString("R", CultureInfo.InvariantCulture);
            var exponentIndex = roundTrip.IndexOf('E');
            if (exponentIndex < 0) exponentIndex = roundTrip.IndexOf('e');
            if (exponentIndex < 0) return roundTrip;

            var mantissa = roundTrip.Substring(0, exponentIndex);
            var exponent = int.Parse(roundTrip.Substring(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (exponent >= -6 && exponent < 21) return ExpandExponent(mantissa, exponent);

            var sign = string.Empty;
            if (mantissa[0] == '-')
            {
                sign = "-";
                mantissa = mantissa.Substring(1);
            }
            else if (mantissa[0] == '+') mantissa = mantissa.Substring(1);

            mantissa = mantissa.Replace(".", string.Empty);
            var significant = mantissa.TrimEnd('0');
            if (significant.Length == 0) return "0";
            var first = significant[0].ToString();
            var fraction = significant.Length > 1 ? "." + significant.Substring(1) : string.Empty;
            return sign + first + fraction + "e" + (exponent >= 0 ? "+" : string.Empty) + exponent.ToString(CultureInfo.InvariantCulture);
        }

        private static string ExpandExponent(string mantissa, int exponent)
        {
            var sign = string.Empty;
            if (mantissa[0] == '-')
            {
                sign = "-";
                mantissa = mantissa.Substring(1);
            }
            else if (mantissa[0] == '+') mantissa = mantissa.Substring(1);

            var dotIndex = mantissa.IndexOf('.');
            var digits = mantissa.Replace(".", string.Empty);
            var decimalIndex = (dotIndex < 0 ? digits.Length : dotIndex) + exponent;
            if (decimalIndex <= 0) return sign + "0." + new string('0', -decimalIndex) + digits;
            if (decimalIndex >= digits.Length) return sign + digits + new string('0', decimalIndex - digits.Length);
            return sign + digits.Substring(0, decimalIndex) + "." + digits.Substring(decimalIndex);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;
            private int nodeCount;

            public Parser(string text)
            {
                this.text = text;
            }

            public CanonicalJsonValue ParseDocument()
            {
                SkipWhitespace();
                var result = ParseValue(0);
                SkipWhitespace();
                if (index != text.Length) Fail("Unexpected content after the JSON value.");
                return result;
            }

            private CanonicalJsonValue ParseValue(int depth)
            {
                if (depth > MaximumNestingDepth) Fail("JSON exceeds the maximum nesting depth.");
                if (++nodeCount > MaximumNodeCount) Fail("JSON exceeds the maximum node count.");
                if (index >= text.Length) Fail("Expected a JSON value.");
                switch (text[index])
                {
                    case 'n': ConsumeLiteral("null"); return CanonicalJsonValue.Null();
                    case 't': ConsumeLiteral("true"); return CanonicalJsonValue.Boolean(true);
                    case 'f': ConsumeLiteral("false"); return CanonicalJsonValue.Boolean(false);
                    case '"': return CanonicalJsonValue.String(ParseString());
                    case '[': return ParseArray(depth);
                    case '{': return ParseObject(depth);
                    default: return ParseNumber();
                }
            }

            private CanonicalJsonValue ParseArray(int depth)
            {
                index++;
                SkipWhitespace();
                var values = new List<CanonicalJsonValue>();
                if (TryConsume(']')) return CanonicalJsonValue.Array(values);
                while (true)
                {
                    values.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (TryConsume(']')) return CanonicalJsonValue.Array(values);
                    Expect(',');
                    SkipWhitespace();
                }
            }

            private CanonicalJsonValue ParseObject(int depth)
            {
                index++;
                SkipWhitespace();
                var values = new List<CanonicalJsonProperty>();
                if (TryConsume('}')) return CanonicalJsonValue.Object(values);
                while (true)
                {
                    if (index >= text.Length || text[index] != '"') Fail("Expected an object key.");
                    var name = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    values.Add(new CanonicalJsonProperty(name, ParseValue(depth + 1)));
                    SkipWhitespace();
                    if (TryConsume('}')) return CanonicalJsonValue.Object(values);
                    Expect(',');
                    SkipWhitespace();
                }
            }

            private CanonicalJsonValue ParseNumber()
            {
                var start = index;
                if (TryConsume('-')) { }
                if (index >= text.Length) Fail("Invalid JSON number.");
                if (text[index] == '0') index++;
                else
                {
                    if (text[index] < '1' || text[index] > '9') Fail("Invalid JSON number.");
                    while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++;
                }
                if (TryConsume('.'))
                {
                    if (index >= text.Length || text[index] < '0' || text[index] > '9') Fail("Invalid JSON number.");
                    while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++;
                }
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++;
                    if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++;
                    if (index >= text.Length || text[index] < '0' || text[index] > '9') Fail("Invalid JSON number.");
                    while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++;
                }

                var raw = text.Substring(start, index - start);
                double number;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || double.IsNaN(number) || double.IsInfinity(number))
                {
                    Fail("JSON number is not finite IEEE-754.");
                }
                return CanonicalJsonValue.Number(number);
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (index < text.Length)
                {
                    var character = text[index++];
                    if (character == '"') return builder.ToString();
                    if (character < 0x20) Fail("Control character in JSON string.");
                    if (character != '\\')
                    {
                        if (char.IsHighSurrogate(character))
                        {
                            if (index >= text.Length || !char.IsLowSurrogate(text[index])) Fail("Unpaired high surrogate in JSON string.");
                            builder.Append(character);
                            builder.Append(text[index++]);
                        }
                        else
                        {
                            if (char.IsLowSurrogate(character)) Fail("Unpaired low surrogate in JSON string.");
                            builder.Append(character);
                        }
                        continue;
                    }

                    if (index >= text.Length) Fail("Incomplete JSON escape.");
                    var escaped = text[index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            var unicode = ParseUnicodeEscape();
                            if (char.IsHighSurrogate(unicode))
                            {
                                if (index + 1 >= text.Length || text[index] != '\\' || text[index + 1] != 'u') Fail("Unpaired high surrogate in JSON escape.");
                                index += 2;
                                var low = ParseUnicodeEscape();
                                if (!char.IsLowSurrogate(low)) Fail("Invalid surrogate pair in JSON escape.");
                                builder.Append(unicode);
                                builder.Append(low);
                            }
                            else
                            {
                                if (char.IsLowSurrogate(unicode)) Fail("Unpaired low surrogate in JSON escape.");
                                builder.Append(unicode);
                            }
                            break;
                        default: Fail("Invalid JSON escape."); break;
                    }
                }

                Fail("Unterminated JSON string.");
                return null;
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > text.Length) Fail("Incomplete Unicode escape.");
                var value = 0;
                for (var offset = 0; offset < 4; offset++)
                {
                    var character = text[index++];
                    value <<= 4;
                    if (character >= '0' && character <= '9') value += character - '0';
                    else if (character >= 'a' && character <= 'f') value += character - 'a' + 10;
                    else if (character >= 'A' && character <= 'F') value += character - 'A' + 10;
                    else Fail("Invalid Unicode escape.");
                }
                return (char)value;
            }

            private void SkipWhitespace()
            {
                while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) index++;
            }

            private void ConsumeLiteral(string literal)
            {
                if (index + literal.Length > text.Length || !string.Equals(text.Substring(index, literal.Length), literal, StringComparison.Ordinal)) Fail("Invalid JSON literal.");
                index += literal.Length;
            }

            private bool TryConsume(char expected)
            {
                if (index < text.Length && text[index] == expected)
                {
                    index++;
                    return true;
                }
                return false;
            }

            private void Expect(char expected)
            {
                if (!TryConsume(expected)) Fail("Expected '" + expected + "'.");
            }

            private void Fail(string message)
            {
                throw new FormatException(message + " At character " + index.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }
    }
}
