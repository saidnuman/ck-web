using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ProductShowcase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotionEditorAndSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVisible",
                table: "PageSections",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Categories",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "PageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PageKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    PageTitle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    CategorySlug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false),
                    Summary = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageSnapshots", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    LogoUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    LogoAlt = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    HeaderBannerText = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    FooterText = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    HomeIsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PageSnapshots_CreatedAt",
                table: "PageSnapshots",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PageSnapshots_PageKey_CreatedAt",
                table: "PageSnapshots",
                columns: new[] { "PageKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_Key",
                table: "SiteSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageSnapshots");

            migrationBuilder.DropTable(
                name: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "IsVisible",
                table: "PageSections");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Categories");
        }
    }
}
