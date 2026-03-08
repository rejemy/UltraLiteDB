using System.Collections.Generic;

namespace UltraLiteDB
{
	/// <summary>
	/// Extracts a single named field value from a <see cref="BsonDocument"/>.
	/// </summary>
	public class BsonFields
	{
		private string Field;

		/// <summary>
		/// Creates a field extractor for the specified field name.
		/// </summary>
		public BsonFields(string field)
		{
			Field = field;
		}

		/// <summary>
		/// Extracts the field value from the document. Yields <see cref="BsonValue.Null"/> if the field is missing
		/// and <paramref name="includeNullIfEmpty"/> is true.
		/// </summary>
		public IEnumerable<BsonValue> Execute(BsonDocument doc, bool includeNullIfEmpty = true)
		{
			var index = 0;
			BsonValue value=null;
			if(doc.TryGetValue(Field, out value))
			{
				index++;
				yield return value;
			}

			if(index == 0 && includeNullIfEmpty) yield return BsonValue.Null;
		}
	}
}