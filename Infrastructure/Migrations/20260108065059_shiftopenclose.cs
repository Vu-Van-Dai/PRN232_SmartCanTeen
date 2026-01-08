using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class shiftopenclose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosingCash",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "OpeningCash",
                table: "Shifts");

            migrationBuilder.RenameColumn(
                name: "OpenedAt",
                table: "Shifts",
                newName: "StartTime");

            migrationBuilder.RenameColumn(
                name: "ClosedAt",
                table: "Shifts",
                newName: "EndTime");

            migrationBuilder.AddColumn<decimal>(
                name: "StaffCashInput",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StaffQrInput",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SystemCashTotal",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SystemOnlineTotal",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SystemQrTotal",
                table: "Shifts",
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
                name: "StaffCashInput",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "StaffQrInput",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "SystemCashTotal",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "SystemOnlineTotal",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "SystemQrTotal",
                table: "Shifts");

            migrationBuilder.RenameColumn(
                name: "StartTime",
                table: "Shifts",
                newName: "OpenedAt");

            migrationBuilder.RenameColumn(
                name: "EndTime",
                table: "Shifts",
                newName: "ClosedAt");

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingCash",
                table: "Shifts",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningCash",
                table: "Shifts",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
