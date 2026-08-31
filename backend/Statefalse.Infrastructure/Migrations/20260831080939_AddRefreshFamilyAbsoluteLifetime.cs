using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Statefalse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshFamilyAbsoluteLifetime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FamilyExpiresAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("UPDATE \"RefreshTokens\" SET \"FamilyExpiresAt\" = \"ExpiresAt\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FamilyExpiresAt",
                table: "RefreshTokens");
        }
    }
}
