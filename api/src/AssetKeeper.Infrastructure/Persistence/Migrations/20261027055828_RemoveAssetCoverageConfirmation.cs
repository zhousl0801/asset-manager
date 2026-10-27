using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAssetCoverageConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "coverage_confirmed",
                table: "ledgers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "coverage_confirmed",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
