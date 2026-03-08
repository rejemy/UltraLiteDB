using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UltraLiteDB
{
    /// <summary>
    /// Provides static methods for serializing and deserializing <see cref="BsonValue"/> instances
    /// to and from JSON extended format strings.
    /// </summary>
    public static class JsonSerializer
    {
        #region Serialize

        /// <summary>
        /// Serializes a <see cref="BsonValue"/> into a JSON string.
        /// </summary>
        /// <param name="value">The <see cref="BsonValue"/> to serialize.</param>
        /// <returns>A JSON string representation of the value.</returns>
        public static string Serialize(BsonValue value)
        {
            var sb = new StringBuilder();

            Serialize(value, sb);

            return sb.ToString();
        }

        /// <summary>
        /// Serializes a <see cref="BsonValue"/> as JSON, writing the output to a <see cref="TextWriter"/>.
        /// </summary>
        /// <param name="value">The <see cref="BsonValue"/> to serialize. If null, <see cref="BsonValue.Null"/> is used.</param>
        /// <param name="writer">The <see cref="TextWriter"/> to write the JSON output to.</param>
        public static void Serialize(BsonValue value, TextWriter writer)
        {
            var json = new JsonWriter(writer);

            json.Serialize(value ?? BsonValue.Null);
        }

        /// <summary>
        /// Serializes a <see cref="BsonValue"/> as JSON, writing the output to a <see cref="StringBuilder"/>.
        /// </summary>
        /// <param name="value">The <see cref="BsonValue"/> to serialize. If null, <see cref="BsonValue.Null"/> is used.</param>
        /// <param name="sb">The <see cref="StringBuilder"/> to append the JSON output to.</param>
        public static void Serialize(BsonValue value, StringBuilder sb)
        {
            using (var writer = new StringWriter(sb))
            {
                var w = new JsonWriter(writer);

                w.Serialize(value ?? BsonValue.Null);
            }
        }

        #endregion

        #region Deserialize

        /// <summary>
        /// Deserializes a JSON string into a <see cref="BsonValue"/>.
        /// </summary>
        /// <param name="json">The JSON string to deserialize.</param>
        /// <returns>The deserialized <see cref="BsonValue"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
        public static BsonValue Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            using (var sr = new StringReader(json))
            {
                var reader = new JsonReader(sr);

                return reader.Deserialize();
            }
        }

        /// <summary>
        /// Deserializes JSON from a <see cref="TextReader"/> into a <see cref="BsonValue"/>.
        /// </summary>
        /// <param name="reader">The <see cref="TextReader"/> containing JSON to deserialize.</param>
        /// <returns>The deserialized <see cref="BsonValue"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is null.</exception>
        public static BsonValue Deserialize(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var jr = new JsonReader(reader);

            return jr.Deserialize();
        }

        /// <summary>
        /// Deserializes a JSON array string into a lazily-enumerated sequence of <see cref="BsonValue"/> elements.
        /// </summary>
        /// <param name="json">The JSON array string to deserialize.</param>
        /// <returns>An <see cref="IEnumerable{BsonValue}"/> yielding each element of the JSON array.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
        public static IEnumerable<BsonValue> DeserializeArray(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            var sr = new StringReader(json);
            var reader = new JsonReader(sr);
            return reader.DeserializeArray();
        }

        /// <summary>
        /// Deserializes a JSON array from a <see cref="TextReader"/> into a lazily-enumerated sequence of
        /// <see cref="BsonValue"/> elements, reading from the stream on demand.
        /// </summary>
        /// <param name="reader">The <see cref="TextReader"/> containing a JSON array to deserialize.</param>
        /// <returns>An <see cref="IEnumerable{BsonValue}"/> yielding each element of the JSON array.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is null.</exception>
        public static IEnumerable<BsonValue> DeserializeArray(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var jr = new JsonReader(reader);

            return jr.DeserializeArray();
        }

        #endregion
    }
}