using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayLibre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    XentalTransactionRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    NetCreditKobo = table.Column<long>(type: "bigint", nullable: false),
                    PayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fee_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentFeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fee_allocations_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fee_allocations_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fee_allocations_student_fees_StudentFeeId",
                        column: x => x.StudentFeeId,
                        principalTable: "student_fees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fee_allocations_PaymentId",
                table: "fee_allocations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_fee_allocations_SchoolId",
                table: "fee_allocations",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_fee_allocations_StudentFeeId",
                table: "fee_allocations",
                column: "StudentFeeId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_StudentId",
                table: "payments",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_XentalTransactionRef",
                table: "payments",
                column: "XentalTransactionRef",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fee_allocations");

            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}
