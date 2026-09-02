using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAggregator.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployerInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "JobPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployerName",
                table: "JobPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecificAddress",
                table: "JobPosts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "EmployerName",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "SpecificAddress",
                table: "JobPosts");
        }
    }
}
