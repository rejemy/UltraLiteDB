using System.IO;
using System.Linq;

namespace UltraLiteDB
{
    public partial class UltraLiteEngine
    {
        /// <summary>
        /// Compacts the database by copying all data to a temporary file, eliminating fragmentation.
        /// Optionally changes the encryption password. Returns the number of bytes saved.
        /// </summary>
        /// <param name="password">New password, or null to remove encryption.</param>
        /// <param name="tempDisk">Temporary disk for the compacted copy; defaults to an in-memory stream.</param>
        public long Shrink(string? password = null, IDiskService? tempDisk = null)
        {
            var originalSize = _disk.FileLength;

            // if temp disk are not passed, use memory stream disk
            using (var temp = tempDisk ?? new StreamDiskService(new MemoryStream()))
            using (var engine = new UltraLiteEngine(temp, password))
            {
                // read all collection
                foreach (var collectionName in this.GetCollectionNames())
                {
                    // first create all user indexes (exclude _id index)
                    foreach (var index in this.GetIndexes(collectionName).Where(x => x.Field != "_id"))
                    {
                        engine.EnsureIndex(collectionName, index.Field, index.Unique);
                    }
                    
                    // now copy documents 
                    var docs = this.Find(collectionName, Query.All());

                    engine.InsertBulk(collectionName, docs);

                    // fix collection sequence number
                    // collectionName comes from GetCollectionNames(), so the collection exists
                    var seq = _collections.Get(collectionName)!.Sequence;

                    engine.WriteTransaction(collectionName, (col) =>
                    {
                        col.Sequence = seq;
                        engine._pager.SetDirty(col);
                        return true;
                    });

                }

                // copy user version
                engine.UserVersion = this.UserVersion;

                // set current disk size to exact new disk usage
                _disk.SetLength(temp.FileLength);

                // read new header page to start copy
                var header = (HeaderPage)BasePage.ReadPage(temp.ReadPage(0));

                // copy (as is) all pages from temp disk to original disk
                for (uint i = 0; i <= header.LastPageID; i++)
                {
                    // skip lock page
                    if (i == 1) continue;

                    var page = temp.ReadPage(i);

                    _disk.WritePage(i, page);
                }

                // create/destroy crypto class
                if (_crypto != null) _crypto.Dispose();

                _crypto = password == null ? null : new AesEncryption(password, header.Salt);

                // initialize all services again (crypto can be changed)
                this.InitializeServices();
                
                // return how many bytes are reduced
                return originalSize - temp.FileLength;
            }
        }
    }
}
