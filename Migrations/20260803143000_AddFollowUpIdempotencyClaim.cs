using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportSystem.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260803143000_AddFollowUpIdempotencyClaim")]
public partial class AddFollowUpIdempotencyClaim : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IDEMPOTENCY_CLAIM",
            table: "MASTER_FOLLOW_UP",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_MASTER_FOLLOW_UP_IDEMPOTENCY_CLAIM",
            table: "MASTER_FOLLOW_UP",
            column: "IDEMPOTENCY_CLAIM",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MASTER_FOLLOW_UP_IDEMPOTENCY_CLAIM",
            table: "MASTER_FOLLOW_UP");

        migrationBuilder.DropColumn(
            name: "IDEMPOTENCY_CLAIM",
            table: "MASTER_FOLLOW_UP");
    }
}
