using System;
using System.Collections.Generic;
using System.Globalization;

namespace UltraLiteDB
{
    /// <summary>
    /// Implement how database will compare to order by/find strings according defined culture/compare options
    /// If not set, default is CurrentCulture with IgnoreCase
    /// </summary>
    public class Collation : IComparer<BsonValue>, IComparer<string>, IEqualityComparer<BsonValue>
    {
        private readonly CompareInfo _compareInfo;


        public Collation(CompareOptions sortOptions)
        {
            this.SortOptions = sortOptions;
            this.Culture = new CultureInfo("");

            _compareInfo = this.Culture.CompareInfo;
        }

        public static Collation Default = new Collation(CompareOptions.IgnoreCase);

        public static Collation Binary = new Collation(CompareOptions.Ordinal);


        /// <summary>
        /// Get database language culture
        /// </summary>
        public CultureInfo Culture { get; }

        /// <summary>
        /// Get options to how string should be compared in sort
        /// </summary>
        public CompareOptions SortOptions { get; }

        /// <summary>
        /// Compare 2 string values using current culture/compare options
        /// </summary>
        public int Compare(string left, string right)
        {
            var result = _compareInfo.Compare(left, right, this.SortOptions);

            return result < 0 ? -1 : result > 0 ? +1 : 0;
        }

        public int Compare(BsonValue left, BsonValue rigth)
        {
            return left.CompareTo(rigth, this);
        }

        public bool Equals(BsonValue x, BsonValue y)
        {
            return this.Compare(x, y) == 0;
        }

        public int GetHashCode(BsonValue obj)
        {
            return obj.GetHashCode();
        }

        public override string ToString()
        {
            return this.Culture.Name + "/" + this.SortOptions.ToString();
        }
    }
}