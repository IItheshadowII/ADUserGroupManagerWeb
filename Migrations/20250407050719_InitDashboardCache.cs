using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADUserGroupManagerWeb.Migrations
{
    /// <inheritdoc />
    public partial class InitDashboardCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TotalUsers = table.Column<int>(type: "int", nullable: false),
                    ActiveUsers = table.Column<int>(type: "int", nullable: false),
                    LockedUsers = table.Column<int>(type: "int", nullable: false),
                    TotalServers = table.Column<int>(type: "int", nullable: false),
                    OnlineServers = table.Column<int>(type: "int", nullable: false),
                    OfflineServers = table.Column<int>(type: "int", nullable: false),
                    TotalClinics = table.Column<int>(type: "int", nullable: false),
                    ActiveToday = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Environment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GoogleSheetsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SpreadsheetId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClinicSheet = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SmtpServer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmtpPort = table.Column<int>(type: "int", nullable: false),
                    EmailUsername = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailPassword = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DomainName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DomainAdminUsername = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DomainAdminPassword = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardCache");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
