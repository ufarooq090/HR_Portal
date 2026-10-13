using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledJobChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledJobChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfficeLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    GradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledJobChanges_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobChanges_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobChanges_Employees_NewManagerId",
                        column: x => x.NewManagerId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobChanges_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobChanges_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobChanges_OfficeLocations_OfficeLocationId",
                        column: x => x.OfficeLocationId,
                        principalTable: "OfficeLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobChanges_DepartmentId",
                table: "ScheduledJobChanges",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobChanges_EmployeeId",
                table: "ScheduledJobChanges",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobChanges_GradeId",
                table: "ScheduledJobChanges",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobChanges_LegalEntityId",
                table: "ScheduledJobChanges",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobChanges_NewManagerId",
                table: "ScheduledJobChanges",
                column: "NewManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobChanges_OfficeLocationId",
                table: "ScheduledJobChanges",
                column: "OfficeLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobChanges_Status_EffectiveDate",
                table: "ScheduledJobChanges",
                columns: new[] { "Status", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledJobChanges");
        }
    }
}
