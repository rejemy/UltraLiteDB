using System;
using System.Collections.Generic;
using System.Globalization;

namespace UltraLiteDB
{
    /// <summary>
    /// Defines string comparison rules (culture + <see cref="CompareOptions"/>) used by the database for
    /// ordering and matching. Default is invariant culture with <see cref="CompareOptions.IgnoreCase"/>.
    /// Also implements <see cref="IComparer{T}"/> for both <see cref="BsonValue"/> and <see cref="string"/>.
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