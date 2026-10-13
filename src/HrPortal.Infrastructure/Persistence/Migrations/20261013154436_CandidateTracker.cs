using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HrPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CandidateTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HiringManagerEmployeeId",
                table: "Requisitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "InterviewRounds",
                table: "Requisitions",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<List<string>>(
                name: "ScorecardCriteria",
                table: "Requisitions",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY['Technical skills', 'Problem solving', 'Communication', 'Team fit']");

            migrationBuilder.AddColumn<string>(
                name: "PhoneKey",
                table: "People",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InterviewRound = table.Column<int>(type: "integer", nullable: true),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StageChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Applications_Requisitions_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "Requisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateCvs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateCvs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateCvs_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateProfiles",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentEmployer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NoticePeriodDays = table.Column<int>(type: "integer", nullable: true),
                    CurrentCtcAmount = table.Column<string>(type: "text", nullable: true),
                    CurrentCtcCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    ExpectedCtcAmount = table.Column<string>(type: "text", nullable: true),
                    ExpectedCtcCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateProfiles", x => x.PersonId);
                    table.ForeignKey(
                        name: "FK_CandidateProfiles_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RejectionReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RejectionReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationNotes_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Interviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Criteria = table.Column<List<string>>(type: "text[]", nullable: false),
                    CalendarEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OrganizerEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Interviews_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationStageChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FromRound = table.Column<int>(type: "integer", nullable: true),
                    ToStage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ToRound = table.Column<int>(type: "integer", nullable: true),
                    RejectionReasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReasonName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationStageChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationStageChanges_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationStageChanges_RejectionReasons_RejectionReasonId",
                        column: x => x.RejectionReasonId,
                        principalTable: "RejectionReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InterviewPanelists",
                columns: table => new
                {
                    InterviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewPanelists", x => new { x.InterviewId, x.EmployeeId });
                    table.ForeignKey(
                        name: "FK_InterviewPanelists_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterviewPanelists_Interviews_InterviewId",
                        column: x => x.InterviewId,
                        principalTable: "Interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scorecards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewerEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ratings = table.Column<string>(type: "jsonb", nullable: false),
                    Recommendation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scorecards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scorecards_Employees_InterviewerEmployeeId",
                        column: x => x.InterviewerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Scorecards_Interviews_InterviewId",
                        column: x => x.InterviewId,
                        principalTable: "Interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RejectionReasons",
                columns: new[] { "Id", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("0199c3a0-0000-7000-8000-000000000001"), true, "Skills do not match the role", 10 },
                    { new Guid("0199c3a0-0000-7000-8000-000000000002"), true, "Not enough experience", 20 },
                    { new Guid("0199c3a0-0000-7000-8000-000000000003"), true, "Salary expectations too high", 30 },
                    { new Guid("0199c3a0-0000-7000-8000-000000000004"), true, "Did not pass the interview", 40 },
                    { new Guid("0199c3a0-0000-7000-8000-000000000005"), true, "Notice period too long", 50 },
                    { new Guid("0199c3a0-0000-7000-8000-000000000006"), true, "Position filled", 60 },
                    { new Guid("0199c3a0-0000-7000-8000-000000000007"), true, "Other", 100 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requisitions_HiringManagerEmployeeId",
                table: "Requisitions",
                column: "HiringManagerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_People_PersonalEmail",
                table: "People",
                column: "PersonalEmail");

            migrationBuilder.CreateIndex(
                name: "IX_People_PhoneKey",
                table: "People",
                column: "PhoneKey");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationNotes_ApplicationId",
                table: "ApplicationNotes",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_PersonId",
                table: "Applications",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_RequisitionId_PersonId",
                table: "Applications",
                columns: new[] { "RequisitionId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationStageChanges_ApplicationId",
                table: "ApplicationStageChanges",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationStageChanges_RejectionReasonId",
                table: "ApplicationStageChanges",
                column: "RejectionReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateCvs_PersonId",
                table: "CandidateCvs",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewPanelists_EmployeeId",
                table: "InterviewPanelists",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_ApplicationId",
                table: "Interviews",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_RejectionReasons_Name",
                table: "RejectionReasons",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scorecards_InterviewerEmployeeId",
                table: "Scorecards",
                column: "InterviewerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Scorecards_InterviewId_InterviewerEmployeeId",
                table: "Scorecards",
                columns: new[] { "InterviewId", "InterviewerEmployeeId" },
                unique: true);

            // Existing requisitions: the requester is the hiring manager; existing people get a phone key (CT-09).
            migrationBuilder.Sql("""
                UPDATE "Requisitions" SET "HiringManagerEmployeeId" = "RequesterEmployeeId";
                UPDATE "People" SET "PhoneKey" = right(regexp_replace("Phone", '[^0-9]', '', 'g'), 10)
                WHERE length(regexp_replace(coalesce("Phone", ''), '[^0-9]', '', 'g')) >= 7;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Requisitions_Employees_HiringManagerEmployeeId",
                table: "Requisitions",
                column: "HiringManagerEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requisitions_Employees_HiringManagerEmployeeId",
                table: "Requisitions");

            migrationBuilder.DropTable(
                name: "ApplicationNotes");

            migrationBuilder.DropTable(
                name: "ApplicationStageChanges");

            migrationBuilder.DropTable(
                name: "CandidateCvs");

            migrationBuilder.DropTable(
                name: "CandidateProfiles");

            migrationBuilder.DropTable(
                name: "InterviewPanelists");

            migrationBuilder.DropTable(
                name: "Scorecards");

            migrationBuilder.DropTable(
                name: "RejectionReasons");

            migrationBuilder.DropTable(
                name: "Interviews");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_Requisitions_HiringManagerEmployeeId",
                table: "Requisitions");

            migrationBuilder.DropIndex(
                name: "IX_People_PersonalEmail",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_PhoneKey",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HiringManagerEmployeeId",
                table: "Requisitions");

            migrationBuilder.DropColumn(
                name: "InterviewRounds",
                table: "Requisitions");

            migrationBuilder.DropColumn(
                name: "ScorecardCriteria",
                table: "Requisitions");

            migrationBuilder.DropColumn(
                name: "PhoneKey",
                table: "People");
        }
    }
}
