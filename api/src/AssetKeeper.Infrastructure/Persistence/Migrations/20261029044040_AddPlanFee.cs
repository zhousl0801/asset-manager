using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "fee",
                table: "plan_occurrences",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "fee",
                table: "auto_plans",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fee",
                table: "plan_occurrences");

            migrationBuilder.DropColumn(
                name: "fee",
                table: "auto_plans");
        }
    }
}
