using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundBuyPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "fee",
                table: "asset_transactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "linked_transaction_id",
                table: "asset_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_asset_transactions_ledger_id_linked_transaction_id",
                table: "asset_transactions",
                columns: new[] { "ledger_id", "linked_transaction_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_asset_transactions_ledger_id_linked_transaction_id",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "fee",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "linked_transaction_id",
                table: "asset_transactions");
        }
    }
}
