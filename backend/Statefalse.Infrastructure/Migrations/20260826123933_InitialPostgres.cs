using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Statefalse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckSuiteEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CheckSuiteId = table.Column<long>(type: "bigint", nullable: false),
                    Conclusion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    HeadBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HeadSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PrAuthorLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PrAuthorGitHubId = table.Column<long>(type: "bigint", nullable: true),
                    PrNumber = table.Column<int>(type: "integer", nullable: true),
                    RepoFullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasNotified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckSuiteEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GitHubUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    GitHubUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: true),
                    UserPatToken = table.Column<string>(type: "text", nullable: true),
                    SignalRConnectionId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientGitHubId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Repo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PrNumber = table.Column<long>(type: "bigint", nullable: true),
                    PrUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrNumber = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AuthorLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorGitHubId = table.Column<long>(type: "bigint", nullable: true),
                    RepoFullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HeadBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BaseBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PrUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Conclusion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Draft = table.Column<bool>(type: "boolean", nullable: false),
                    MergeableState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReviewApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastCommentBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastCommentBody = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastCommentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCommentUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastReviewFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastReviewLine = table.Column<int>(type: "integer", nullable: true),
                    ExtraInfo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HeadSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SubscriberIds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasNotified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PunishmentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    CulpritLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CulpritGitHubId = table.Column<long>(type: "bigint", nullable: true),
                    RepoFullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkflowName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WorkflowUrl = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasNotified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PunishmentEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    WorkflowName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Repo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HeadBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Trigger = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HtmlUrl = table.Column<string>(type: "text", nullable: true),
                    HeadSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TargetGitHubIds = table.Column<string>(type: "text", nullable: true),
                    IsIgnored = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckSuiteEvents_Conclusion",
                table: "CheckSuiteEvents",
                column: "Conclusion");

            migrationBuilder.CreateIndex(
                name: "IX_CheckSuiteEvents_OccurredAt",
                table: "CheckSuiteEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CheckSuiteEvents_PrAuthorLogin",
                table: "CheckSuiteEvents",
                column: "PrAuthorLogin");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubUsers_GitHubId",
                table: "GitHubUsers",
                column: "GitHubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubUsers_GitHubUsername",
                table: "GitHubUsers",
                column: "GitHubUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientGitHubId_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientGitHubId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientGitHubId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientGitHubId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestEvents_AuthorLogin",
                table: "PullRequestEvents",
                column: "AuthorLogin");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestEvents_PrNumber",
                table: "PullRequestEvents",
                column: "PrNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestEvents_Status",
                table: "PullRequestEvents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PunishmentEvents_CulpritLogin",
                table: "PunishmentEvents",
                column: "CulpritLogin");

            migrationBuilder.CreateIndex(
                name: "IX_PunishmentEvents_OccurredAt",
                table: "PunishmentEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_GitHubId_RevokedAt_ExpiresAt",
                table: "RefreshTokens",
                columns: new[] { "GitHubId", "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_GitHubId",
                table: "WorkflowRuns",
                column: "GitHubId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_RunId",
                table: "WorkflowRuns",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_Status",
                table: "WorkflowRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckSuiteEvents");

            migrationBuilder.DropTable(
                name: "GitHubUsers");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PullRequestEvents");

            migrationBuilder.DropTable(
                name: "PunishmentEvents");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "WorkflowRuns");
        }
    }
}
