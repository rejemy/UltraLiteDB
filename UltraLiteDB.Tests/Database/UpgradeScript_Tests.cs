using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;

namespace UltraLiteDB.Tests.Database
{
    [TestClass]
    public class UpgradeScript_Tests
    {
        // [TestMethod]
        public void UpgradeScript_Test()
        {
            // export data TO JSON
            using (var db = new UltraLiteDatabase(new MemoryStream()))
            {
                var data = new BsonDocument();
                foreach(var name in db.GetCollectionNames())
                {
                    data[name] = new BsonArray(db.GetCollection(name).FindAll());
                }
                File.WriteAllText(@"C:\Temp\dump-data.json", JsonSerializer.Serialize(data));
            }

            // import data FROM JSON
            using (var db = new UltraLiteDatabase(new MemoryStream()))
            {
				var data = JsonSerializer.Deserialize(File.ReadAllText(@"C:\Temp\dump-data.json")).AsDocument ?? throw new System.Exception("Deserialized data was null");
				foreach (var name in data.Keys)
                {
                    db.GetCollection(name).Insert(data[name].AsArray!.Select(x => x.AsDocument!));
                }
            }
        }
    }
}