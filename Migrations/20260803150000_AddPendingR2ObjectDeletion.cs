using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ReportSystem.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260803150000_AddPendingR2ObjectDeletion")]
public partial class AddPendingR2ObjectDeletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PENDING_R2_OBJECT_DELETION",
            columns: table => new
            {
                PENDING_R2_OBJECT_DELETION_ID = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OBJECT_KEY = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                RECORDED_AT = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ATTEMPT_COUNT = table.Column<int>(type: "integer", nullable: false),
                LAST_ATTEMPT_AT = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PENDING_R2_OBJECT_DELETION", x => x.PENDING_R2_OBJECT_DELETION_ID);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PENDING_R2_OBJECT_DELETION_OBJECT_KEY",
            table: "PENDING_R2_OBJECT_DELETION",
            column: "OBJECT_KEY",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PENDING_R2_OBJECT_DELETION");
    }
}
