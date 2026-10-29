using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanTradingScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trading_scope",
                table: "auto_plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "ThreeMarkets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trading_scope",
                table: "auto_plans");
        }
    }
}
