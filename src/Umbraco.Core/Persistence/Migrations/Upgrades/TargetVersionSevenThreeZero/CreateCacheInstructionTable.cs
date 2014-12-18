using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Configuration;

namespace Umbraco.Core.Persistence.Migrations.Upgrades.TargetVersionSevenThreeZero
{
    [Migration("7.3.0", 0, GlobalSettings.UmbracoMigrationName)]
    public class CreateCacheInstructionTable : MigrationBase
    {
        public override void Up()
        {
            Create.Table("umbracoCacheInstruction")
                .WithColumn("id").AsInt32().PrimaryKey("PK_umbracoCacheInstruction").Identity().NotNullable()
                .WithColumn("utcStamp").AsDateTime().NotNullable()
                .WithColumn("jsonInstruction").AsString().NotNullable();

        }

        public override void Down()
        {
            Delete.PrimaryKey("PK_umbracoCacheInstruction").FromTable("cmsContentType2ContentType");
            Delete.Table("cmsContentType2ContentType");
        }
    }
}
