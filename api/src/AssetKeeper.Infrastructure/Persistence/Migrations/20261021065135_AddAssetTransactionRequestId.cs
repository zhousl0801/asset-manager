using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTransactionRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "asset_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE asset_transactions SET request_id = gen_random_uuid() WHERE request_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "request_id",
                table: "asset_transactions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_asset_transactions_ledger_id_request_id",
                table: "asset_transactions",
                columns: new[] { "ledger_id", "request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_asset_transactions_ledger_id_request_id",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "asset_transactions");
        }
    }
}
