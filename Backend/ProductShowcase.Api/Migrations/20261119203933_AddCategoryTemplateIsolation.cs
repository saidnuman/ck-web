using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductShowcase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryTemplateIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "PageSections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategorySlug",
                table: "PageSections",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TemplateInitialized",
                table: "Categories",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PageSections_CategoryId",
                table: "PageSections",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PageSections_CategorySlug",
                table: "PageSections",
                column: "CategorySlug");

            migrationBuilder.AddForeignKey(
                name: "FK_PageSections_Categories_CategoryId",
                table: "PageSections",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PageSections_Categories_CategoryId",
                table: "PageSections");

            migrationBuilder.DropIndex(
                name: "IX_PageSections_CategoryId",
                table: "PageSections");

            migrationBuilder.DropIndex(
                name: "IX_PageSections_CategorySlug",
                table: "PageSections");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "PageSections");

            migrationBuilder.DropColumn(
                name: "CategorySlug",
                table: "PageSections");

            migrationBuilder.DropColumn(
                name: "TemplateInitialized",
                table: "Categories");
        }
    }
}
