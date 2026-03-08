namespace UltraLiteDB
{
    /// <summary>
    /// Specifies the data type for auto-generated _id values when a document is inserted without an _id field.
    /// </summary>
    public enum BsonAutoId
    {
        Int32 = 2,
        Int64 = 3,
        ObjectId = 10,
        Guid = 11
    }
}