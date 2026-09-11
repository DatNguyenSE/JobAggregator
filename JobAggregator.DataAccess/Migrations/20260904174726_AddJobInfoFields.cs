using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAggregator.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddJobInfoFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeRequirement",
                table: "JobPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Experience",
                table: "JobPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "JobPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobInfo",
                table: "JobPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Vacancies",
                table: "JobPosts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkingFormat",
                table: "JobPosts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeRequirement",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "Experience",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "JobInfo",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "Vacancies",
                table: "JobPosts");

            migrationBuilder.DropColumn(
                name: "WorkingFormat",
                table: "JobPosts");
        }
    }
}
