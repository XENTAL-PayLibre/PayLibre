using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayLibre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassTeacherAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClassIdsCsv",
                table: "invites",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "school_user_classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_user_classes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_school_user_classes_classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_school_user_classes_school_users_SchoolUserId",
                        column: x => x.SchoolUserId,
                        principalTable: "school_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_school_user_classes_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_school_user_classes_ClassId",
                table: "school_user_classes",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_school_user_classes_SchoolId",
                table: "school_user_classes",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_school_user_classes_SchoolUserId",
                table: "school_user_classes",
                column: "SchoolUserId");

            migrationBuilder.CreateIndex(
                name: "IX_school_user_classes_SchoolUserId_ClassId",
                table: "school_user_classes",
                columns: new[] { "SchoolUserId", "ClassId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "school_user_classes");

            migrationBuilder.DropColumn(
                name: "ClassIdsCsv",
                table: "invites");
        }
    }
}
