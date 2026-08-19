using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Statefalse.Infrastructure.Data;

#nullable disable

namespace Statefalse.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260819150000_RepairNotifications")]
public partial class RepairNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "Notifications" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Notifications" PRIMARY KEY AUTOINCREMENT,
                "RecipientGitHubId" INTEGER NOT NULL,
                "Kind" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Body" TEXT NOT NULL,
                "Repo" TEXT NULL,
                "PrNumber" INTEGER NULL,
                "PrUrl" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "IsRead" INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_Notifications_RecipientGitHubId_CreatedAt"
                ON "Notifications" ("RecipientGitHubId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_Notifications_RecipientGitHubId_IsRead_CreatedAt"
                ON "Notifications" ("RecipientGitHubId", "IsRead", "CreatedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Notifications_RecipientGitHubId_IsRead_CreatedAt";
            DROP INDEX IF EXISTS "IX_Notifications_RecipientGitHubId_CreatedAt";
            DROP TABLE IF EXISTS "Notifications";
            """);
    }
}
