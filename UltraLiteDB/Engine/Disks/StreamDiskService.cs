using System.Collections.Generic;
using System.IO;

namespace UltraLiteDB
{
    /// <summary>
    /// Stream-based <see cref="IDiskService"/> implementation that reads/writes pages to any seekable <see cref="Stream"/>.
    /// Does not support journaling or flush — suitable for in-memory streams or scenarios where crash recovery is not needed.
    /// </summary>
    public class StreamDiskService : IDiskService
    {
        /// <summary>
        /// Position, on page, about page type
        /// </summary>
        private const int PAGE_TYPE_POSITION = 4;

        private Stream _stream;
        private Logger _log; // will be initialize in "Initialize()"
        private bool _disposeStream;

        #region Initialize disk

        public StreamDiskService(Stream stream, bool disposeStream = false)
        {
            _stream = stream;
            _disposeStream = disposeStream;
        }

        public void Initialize(Logger log, string password)
        {
            // get log instance to disk
            _log = log;

            // if stream are empty, create header page and save to stream
            if (_stream.Length == 0)
            {
                _log.Write(Logger.DISK, "initialize new datafile");

                // create datafile
                UltraLiteEngine.CreateDatabase(_stream, password);
            }
        }

        public virtual void Dispose()
        {
            if (_disposeStream)
            {
                _stream.Dispose();
            }
            else
            {
                // do nothing - keeps stream opened
            }
        }

        #endregion

        #region Read/Write

        /// <summary>
        /// Read page bytes from disk
        /// </summary>
        public virtual byte[] ReadPage(uint pageID)
        {
            var buffer = new byte[BasePage.PAGE_SIZE];
            var position = BasePage.GetSizeOfPages(pageID);

            lock (_stream)
            {
                // position cursor
                if (_stream.Position != position)
                {
                    _stream.Seek(position, SeekOrigin.Begin);
                }

                // read bytes from data file
                _stream.Read(buffer, 0, BasePage.PAGE_SIZE);
            }

            _log.Write(Logger.DISK, "read page #{0:0000} :: {1}", pageID, (PageType)buffer[PAGE_TYPE_POSITION]);

            return buffer;
        }

        /// <summary>
        /// Persist single page bytes to disk
        /// </summary>
        public virtual void WritePage(uint pageID, byte[] buffer)
        {
            var position = BasePage.GetSizeOfPages(pageID);

            _log.Write(Logger.DISK, "write page #{0:0000} :: {1}", pageID, (PageType)buffer[PAGE_TYPE_POSITION]);

            lock (_stream)
            {
                // position cursor
                if (_stream.Position != position)
                {
                    _stream.Seek(position, SeekOrigin.Begin);
                }

                _stream.Write(buffer, 0, BasePage.PAGE_SIZE);
            }
        }

        /// <summary>
        /// Set datafile length
        /// </summary>
        public void SetLength(long fileSize)
        {
            lock (_stream)
            {
                // fileSize parameter tell me final size of data file - helpful to extend first datafile
                _stream.SetLength(fileSize);
            }
        }

        /// <summary>
        /// Returns file length
        /// </summary>
        public long FileLength { get { return _stream.Length; } }

        #endregion

        #region Not implemented in Stream

        /// <summary>
        /// No journal in Stream
        /// </summary>
        public bool IsJournalEnabled { get { return false; } }

        /// <summary>
        /// No journal implemented
        /// </summary>
        public void WriteJournal(ICollection<byte[]> pages, uint lastPageID)
        {
        }

        /// <summary>
        /// No journal implemented
        /// </summary>
        public IEnumerable<byte[]> ReadJournal(uint lastPageID)
        {
            yield break;
        }

        /// <summary>
        /// No journal implemented
        /// </summary>
        public void ClearJournal(uint lastPageID)
        {
        }


        /// <summary>
        /// No flush implemented
        /// </summary>
        public void Flush()
        {
        }

        #endregion
    }
}