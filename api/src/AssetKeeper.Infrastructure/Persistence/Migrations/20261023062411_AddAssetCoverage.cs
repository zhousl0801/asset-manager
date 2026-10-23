using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "coverage_confirmed",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "fund_not_held",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "gold_not_held",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "provident_fund_not_held",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "coverage_confirmed",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "fund_not_held",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "gold_not_held",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "provident_fund_not_held",
                table: "ledgers");
        }
    }
}
