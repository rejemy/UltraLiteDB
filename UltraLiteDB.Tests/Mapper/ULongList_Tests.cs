using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace UltraLiteDB.Tests.Mapper
{
    #region Model

    public class Gang
    {
        public ObjectId? Id { get; set; }
        public string? Name { get; set; }
        public ulong LeaderId { get; set; }
        public ulong GuildId { get; set; }
        public List<ulong>? Members { get; set; }
        public double Wealth { get; set; } = 0.0;
        public DateTime Raid { get; set; } = DateTime.UtcNow.AddYears(-1);
    }

    #endregion

    [TestClass]
    public class ULongList_Tests
    {
        [TestMethod]
        public void ULongList_Mapper()
        {
            using (var file = new TempFile())
            using (var db = new UltraLiteDatabase(file.Filename))
            {
                var col = db.GetCollection<Gang>();

                col.Insert(new Gang
                {
                    GuildId = 1,
                    LeaderId = 2,
                    Members = new List<ulong> { 5, 6, 7}
                });

                ulong userId = 5;
                ulong guildId = 1;

                Query q = Query.And(Query.EQ("GuildId", guildId), Query.EQ("LeaderId", userId));
                var e = col.Exists(q);

                //Assert.IsTrue(e);

            }
        }
    }
}