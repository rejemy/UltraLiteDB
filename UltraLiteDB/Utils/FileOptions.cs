namespace UltraLiteDB
{
	/// <summary>
	/// Configuration options for <see cref="FileDiskService"/>, controlling journaling, file size limits, and I/O behavior.
	/// </summary>
	public class FileOptions
    {
        /// <summary>Whether write-ahead journaling is enabled for crash recovery.</summary>
        public bool Journal { get; set; }
        /// <summary>Pre-allocate the data file to this size (in bytes) when creating a new database. 0 = no pre-allocation.</summary>
        public long InitialSize { get; set; }
        /// <summary>Maximum allowed data file size in bytes. Writes that exceed this throw an exception.</summary>
        public long LimitSize { get; set; }
        /// <summary>Use "sync over async" pattern for file stream creation (needed for some UWP scenarios).</summary>
        public bool Async { get; set; }
        /// <summary>If true, flush bypasses OS write cache by calling FileStream.Flush(true).</summary>
        public bool Flush { get; set; } = false;

        public FileOptions()
        {
            this.Journal = true;
            this.InitialSize = 0;
            this.LimitSize = long.MaxValue;
            this.Flush = false;
        }
    }


}
