using System;
using System.Collections.Generic;

namespace UltraLiteDB
{
    /// <summary>
    /// Abstraction over physical storage for the database engine. Implementations handle
    /// reading/writing pages and optional write-ahead journal support for crash recovery.
    /// </summary>
    public interface IDiskService : IDisposable
    {
        /// <summary>
        /// Open or create the data file and initialize internal streams. Must be called before any read/write operations.
        /// </summary>
        void Initialize(Logger log, string password);

        /// <summary>
        /// Read a single page (BasePage.PAGE_SIZE bytes) from the data file.
        /// </summary>
        byte[] ReadPage(uint pageID);

        /// <summary>
        /// Write a single page (BasePage.PAGE_SIZE bytes) to the data file.
        /// </summary>
        void WritePage(uint pageID, byte[] buffer);

        /// <summary>
        /// Pre-allocate the data file to the specified length before writing new pages.
        /// </summary>
        void SetLength(long fileSize);

        /// <summary>
        /// Gets the current data file length in bytes.
        /// </summary>
        long FileLength { get; }


        /// <summary>
        /// Whether this disk service supports write-ahead journaling. When false, the engine skips journal I/O.
        /// </summary>
        bool IsJournalEnabled { get; }

        /// <summary>
        /// Read all pages from the journal file for crash recovery. Returns raw page buffers in write order.
        /// </summary>
        IEnumerable<byte[]> ReadJournal(uint lastPageID);

        /// <summary>
        /// Write original (pre-modification) page bytes to the journal file for rollback support. Creates the journal if it does not exist.
        /// </summary>
        void WriteJournal(ICollection<byte[]> pages, uint lastPageID);

        /// <summary>
        /// Delete or truncate the journal file after a successful commit.
        /// </summary>
        void ClearJournal(uint lastPageID);

        /// <summary>
        /// Ensures all pages from the OS cache are persisted on medium
        /// </summary>
        void Flush();
        
    }
}