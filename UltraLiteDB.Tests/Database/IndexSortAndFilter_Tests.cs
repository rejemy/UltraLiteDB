using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB.Tests.Database
{
   [TestClass]
   public class IndexSortAndFilterTest
   {
       private UltraLiteCollection<Item> _collection = null!;
       private TempFile _tempFile = null!;
       private UltraLiteDatabase _database = null!;

       [TestInitialize]
       public void Init()
       {
           _tempFile = new TempFile();
           _database = new UltraLiteDatabase(_tempFile.Filename);
           _collection = _database.GetCollection<Item>("items");
       }

       [TestCleanup]
       public void Cleanup()
       {
           _database.Dispose();
           _tempFile.Dispose();
       }

       [TestMethod]
       public void FilterAndSortAscending()
       {
           _collection.EnsureIndex(nameof(Item.Value));

           PrepareData(_collection);
           var result = FilterAndSortById(_collection, Query.Ascending);

           Assert.AreEqual("B", result[0].Id);
           Assert.AreEqual("C", result[1].Id);
       }

       [TestMethod]
       public void FilterAndSortAscendingWithoutIndex()
       {
           PrepareData(_collection);
           var result = FilterAndSortById(_collection, Query.Ascending);

           Assert.AreEqual("B", result[0].Id);
           Assert.AreEqual("C", result[1].Id);
       }

       [TestMethod]
       public void FilterAndSortDescending()
       {
           _collection.EnsureIndex(nameof(Item.Value));

           PrepareData(_collection);
           var result = FilterAndSortById(_collection, Query.Descending);

           Assert.AreEqual("C", result[0].Id);
           Assert.AreEqual("B", result[1].Id);
       }

       private void PrepareData(UltraLiteCollection<Item> collection)
       {
           collection.Upsert(new Item() { Id = "C", Value = "Value 1" });
           collection.Upsert(new Item() { Id = "A", Value = "Value 2" });
           collection.Upsert(new Item() { Id = "B", Value = "Value 1" });
       }

       private List<Item> FilterAndSortById(UltraLiteCollection<Item> collection, int order)
       {
           var filterQuery = Query.EQ(nameof(Item.Value), "Value 1");
           var sortQuery = Query.All(order);
           var query = Query.And(sortQuery, filterQuery);

           var result = collection.Find(query).ToList();
           return result;
       }

       public class Item
       {
           public string? Id { get; set; }

           public string? Value { get; set; }
       }
   }
}