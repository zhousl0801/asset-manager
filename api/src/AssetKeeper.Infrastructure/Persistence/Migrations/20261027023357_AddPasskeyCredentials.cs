using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasskeyCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "passkey_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<byte[]>(type: "bytea", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    sign_count = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    transports = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    backup_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    aaguid = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkey_credentials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_passkey_credentials_credential_id",
                table: "passkey_credentials",
                column: "credential_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "passkey_credentials");
        }
    }
}
