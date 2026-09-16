using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAggregator.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookCatalogAndScrapeRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApifyRunId",
                table: "JobSearchRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FacebookGroupId",
                table: "JobSearchRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedCount",
                table: "JobSearchRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FetchedCount",
                table: "JobSearchRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "JobSearchRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InsertedCount",
                table: "JobSearchRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "JobSearchRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "JobSearchRequests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProcessingResultsJson",
                table: "JobSearchRequests",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "RawDataKey",
                table: "JobSearchRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectedCount",
                table: "JobSearchRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequestedLimit",
                table: "JobSearchRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScopeKey",
                table: "JobSearchRequests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "JobSearchRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnchangedCount",
                table: "JobSearchRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UpdatedCount",
                table: "JobSearchRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "JobPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FacebookGroupId",
                table: "JobPosts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Roles",
                table: "JobPosts",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "FacebookGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalGroupId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CanonicalUrl = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulScrapedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobSearchRequests_FacebookGroupId",
                table: "JobSearchRequests",
                column: "FacebookGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_JobSearchRequests_OwnerId_CreatedAt",
                table: "JobSearchRequests",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobSearchRequests_OwnerId_IdempotencyKey",
                table: "JobSearchRequests",
                columns: new[] { "OwnerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobSearchRequests_ScopeKey_CreatedAt",
                table: "JobSearchRequests",
                columns: new[] { "ScopeKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPosts_FacebookGroupId_PostedDate_Id",
                table: "JobPosts",
                columns: new[] { "FacebookGroupId", "PostedDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPosts_Platform_PostedDate_Id",
                table: "JobPosts",
                columns: new[] { "Platform", "PostedDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookGroups_CanonicalUrl",
                table: "FacebookGroups",
                column: "CanonicalUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacebookGroups_ExternalGroupId",
                table: "FacebookGroups",
                column: "ExternalGroupId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPosts_FacebookGroups_FacebookGroupId",
                table: "JobPosts",
                column: "FacebookGroupId",
                principalTable: "FacebookGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobSearchRequests_FacebookGroups_FacebookGroupId",
                table: "JobSearchRequests",
                column: "FacebookGroupId",
                principalTable: "FacebookGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Only backfill group identity proven by a Facebook group-post URL.
            migrationBuilder.Sql("""
                INSERT INTO "FacebookGroups" ("Id", "Name", "CanonicalUrl", "CreatedAt", "ExternalGroupId")
                SELECT md5('facebook-group:' || slug)::uuid, slug,
                    'https://www.facebook.com/groups/' || slug || '/', MIN("CreatedAt"),
                    CASE WHEN slug ~ '^[0-9]+$' THEN slug ELSE NULL END
                FROM (
                    SELECT "CreatedAt", substring("SourceUrl" from '^https://(?:www\.facebook\.com|facebook\.com)/groups/([A-Za-z0-9._-]+)/(?:posts|permalink)/[0-9]+') AS slug
                    FROM "JobPosts" WHERE lower("Platform") = 'facebook'
                ) source WHERE slug IS NOT NULL GROUP BY slug
                ON CONFLICT ("CanonicalUrl") DO NOTHING;
                UPDATE "JobPosts" j SET "FacebookGroupId" = g."Id"
                FROM "FacebookGroups" g
                WHERE lower(j."Platform") = 'facebook' AND j."FacebookGroupId" IS NULL
                  AND substring(j."SourceUrl" from '^https://(?:www\.facebook\.com|facebook\.com)/groups/([A-Za-z0-9._-]+)/(?:posts|permalink)/[0-9]+') = g."Name";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobPosts_FacebookGroups_FacebookGroupId",
                table: "JobPosts");

            migrationBuilder.DropForeignKey(
                name: "FK_JobSearchRequests_FacebookGroups_FacebookGroupId",
                table: "JobSearchRequests");

            migrationBuilder.DropTable(
                name: "FacebookGroups");

            migrationBuilder.DropIndex(
                name: "IX_JobSearchRequests_FacebookGroupId",
                table: "JobSearchRequests");

            migrationBuilder.DropIndex(
                name: "IX_JobSearchRequests_OwnerId_CreatedAt",
                table: "JobSearchRequests");

            migrationBuilder.DropIndex(
                name: "IX_JobSearchRequests_OwnerId_IdempotencyKey",
                table: "JobSearchRequests");

            migrationBuilder.DropIndex(
                name: "IX_JobSearchRequests_ScopeKey_CreatedAt",
                table: "JobSearchRequests");

            migrationBuilder.DropIndex(
                name: "IX_JobPosts_FacebookGroupId_PostedDate_Id",
                table: "JobPosts");

            migrationBuilder.DropIndex(
                name: "IX_JobPosts_Platform_PostedDate_Id",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "ApifyRunId",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "FacebookGroupId",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "FailedCount",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "FetchedCount",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "InsertedCount",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "ProcessingResultsJson",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "RawDataKey",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "RejectedCount",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "RequestedLimit",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "ScopeKey",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "UnchangedCount",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "UpdatedCount",
                table: "JobSearchRequests");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "FacebookGroupId",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "Roles",
                table: "JobPosts");
        }
    }
}
