using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ProductShowcase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPageSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PageSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PageKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    SectionKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    TemplateType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    InsertAfterSectionKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IsCustom = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Brand = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DiscountPercent = table.Column<int>(type: "int", nullable: true),
                    ImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageSections", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PageSections_PageKey_SectionKey",
                table: "PageSections",
                columns: new[] { "PageKey", "SectionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageSections");
        }
    }
}
