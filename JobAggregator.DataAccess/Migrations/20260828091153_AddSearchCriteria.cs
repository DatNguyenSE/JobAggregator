using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAggregator.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JobType",
                table: "SearchKeywords",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "SearchKeywords",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WebSocketConnections",
                columns: table => new
                {
                    ConnectionId = table.Column<string>(type: "text", nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSocketConnections", x => x.ConnectionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebSocketConnections");

            migrationBuilder.DropColumn(
                name: "JobType",
                table: "SearchKeywords");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "SearchKeywords");
        }
    }
}
