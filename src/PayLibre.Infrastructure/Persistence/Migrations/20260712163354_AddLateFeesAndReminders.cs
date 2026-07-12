using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayLibre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLateFeesAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReminderAtUtc",
                table: "student_fees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastReminderStage",
                table: "student_fees",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LateFeeAppliedAtUtc",
                table: "student_fees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LateFeeAppliedKobo",
                table: "student_fees",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "LateFeeBps",
                table: "schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LateFeeGraceDays",
                table: "schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AppliesLateFee",
                table: "fees",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReminderAtUtc",
                table: "student_fees");

            migrationBuilder.DropColumn(
                name: "LastReminderStage",
                table: "student_fees");

            migrationBuilder.DropColumn(
                name: "LateFeeAppliedAtUtc",
                table: "student_fees");

            migrationBuilder.DropColumn(
                name: "LateFeeAppliedKobo",
                table: "student_fees");

            migrationBuilder.DropColumn(
                name: "LateFeeBps",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "LateFeeGraceDays",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "AppliesLateFee",
                table: "fees");
        }
    }
}
