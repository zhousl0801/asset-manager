using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using AssetKeeper.Infrastructure.Persistence;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261026100000_AddFundConfirmationDelay")]
    public partial class AddFundConfirmationDelay : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "confirmation_delay_days",
                table: "fund_positions",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fund_trade_date",
                table: "asset_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "confirmation_delay_days",
                table: "asset_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "confirmation_delay_days", table: "fund_positions");
            migrationBuilder.DropColumn(name: "confirmation_delay_days", table: "asset_transactions");
            migrationBuilder.DropColumn(name: "fund_trade_date", table: "asset_transactions");
        }
    }
}
