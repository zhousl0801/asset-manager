using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auto_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    day_of_month = table.Column<int>(type: "integer", nullable: true),
                    day_of_week = table.Column<int>(type: "integer", nullable: true),
                    time = table.Column<TimeOnly>(type: "time", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    generate_from = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    fund_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auto_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_auto_plans_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    plan_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    fund_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    related_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_occurrences", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_occurrences_auto_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "auto_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auto_plans_ledger_id",
                table: "auto_plans",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_auto_plans_ledger_id_type",
                table: "auto_plans",
                columns: new[] { "ledger_id", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_occurrences_ledger_id_due_date_status",
                table: "plan_occurrences",
                columns: new[] { "ledger_id", "due_date", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_occurrences_plan_id_period_key",
                table: "plan_occurrences",
                columns: new[] { "plan_id", "period_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_occurrences");

            migrationBuilder.DropTable(
                name: "auto_plans");
        }
    }
}
