using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductShowcase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteSettingLogoZoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LogoZoomPercent",
                table: "SiteSettings",
                type: "int",
                nullable: false,
                defaultValue: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoZoomPercent",
                table: "SiteSettings");
        }
    }
}
