using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ReportSystem.Migrations
{
    /// <inheritdoc />
    public partial class SyncCreatedDateType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ATTACHMENT",
                columns: table => new
                {
                    ATTACHMENT_ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    REPORT_ID = table.Column<int>(type: "integer", nullable: false),
                    FOLLOW_UP_ID = table.Column<int>(type: "integer", nullable: false),
                    FILE_NAME = table.Column<string>(type: "text", nullable: false),
                    FILE_PATH = table.Column<string>(type: "text", nullable: false),
                    FILE_SIZE = table.Column<int>(type: "integer", nullable: false),
                    FILE_TYPE = table.Column<string>(type: "text", nullable: false),
                    UPLOAD_DATE = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UPLOADED_BY = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ATTACHMENT", x => x.ATTACHMENT_ID);
                });

            migrationBuilder.CreateTable(
                name: "MASTER_FOLLOW_UP",
                columns: table => new
                {
                    FOLLOW_UP_ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    REPORT_ID = table.Column<int>(type: "integer", nullable: false),
                    FOLLOW_UP_DATE = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RESPONSIBLE_PERSON = table.Column<string>(type: "text", nullable: false),
                    MATERIALS_USED = table.Column<string>(type: "text", nullable: false),
                    DESCRIPTION = table.Column<string>(type: "text", nullable: false),
                    UPDATED_BY = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MASTER_FOLLOW_UP", x => x.FOLLOW_UP_ID);
                });

            migrationBuilder.CreateTable(
                name: "MASTER_MACHINE",
                columns: table => new
                {
                    MACHINE_ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MACHINE_NAME = table.Column<string>(type: "text", nullable: false),
                    AREA = table.Column<string>(type: "text", nullable: false),
                    TYPE = table.Column<string>(type: "text", nullable: false),
                    CREATED_DATE = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CREATED_ID = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MASTER_MACHINE", x => x.MACHINE_ID);
                });

            migrationBuilder.CreateTable(
                name: "MASTER_USER",
                columns: table => new
                {
                    USER_ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FULLNAME = table.Column<string>(type: "text", nullable: false),
                    USERNAME = table.Column<string>(type: "text", nullable: false),
                    EMAIL = table.Column<string>(type: "text", nullable: false),
                    PASSWORD = table.Column<string>(type: "text", nullable: false),
                    ROLE = table.Column<string>(type: "text", nullable: false),
                    CREATED_DATE = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UPDATED_DATE = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IS_ACTIVE = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MASTER_USER", x => x.USER_ID);
                });

            migrationBuilder.CreateTable(
                name: "MASTER_REPORT",
                columns: table => new
                {
                    REPORT_ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    REPORT_TYPE = table.Column<string>(type: "text", nullable: false),
                    TITLE = table.Column<string>(type: "text", nullable: false),
                    REPORTED_NAME = table.Column<string>(type: "text", nullable: false),
                    DESCRIPTION = table.Column<string>(type: "text", nullable: false),
                    SUBMISSION_DATE = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AREA = table.Column<string>(type: "text", nullable: false),
                    STATUS = table.Column<string>(type: "text", nullable: false),
                    UPDATED_DATE = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UPDATED_BY = table.Column<int>(type: "integer", nullable: false),
                    MACHINE_ID = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MASTER_REPORT", x => x.REPORT_ID);
                    table.ForeignKey(
                        name: "FK_MASTER_REPORT_MASTER_MACHINE_MACHINE_ID",
                        column: x => x.MACHINE_ID,
                        principalTable: "MASTER_MACHINE",
                        principalColumn: "MACHINE_ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MASTER_REPORT_MACHINE_ID",
                table: "MASTER_REPORT",
                column: "MACHINE_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ATTACHMENT");

            migrationBuilder.DropTable(
                name: "MASTER_FOLLOW_UP");

            migrationBuilder.DropTable(
                name: "MASTER_REPORT");

            migrationBuilder.DropTable(
                name: "MASTER_USER");

            migrationBuilder.DropTable(
                name: "MASTER_MACHINE");
        }
    }
}
