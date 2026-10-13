using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace HrPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResumeBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlacklistReason",
                table: "CandidateProfiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "CandidateProfiles",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExperienceYears",
                table: "CandidateProfiles",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastContactedAtUtc",
                table: "CandidateProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "CandidateProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Skills",
                table: "CandidateProfiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "CandidateProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "CandidateProfiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "CandidateCvs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "CandidateCvs",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('english', coalesce(\"ExtractedText\", ''))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "CandidateNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateNotes_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResumeBankStatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResumeBankStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResumeBankStatusChanges_CandidateProfiles_PersonId",
                        column: x => x.PersonId,
                        principalTable: "CandidateProfiles",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfiles_CountryCode_Status",
                table: "CandidateProfiles",
                columns: new[] { "CountryCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfiles_LastContactedAtUtc",
                table: "CandidateProfiles",
                column: "LastContactedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfiles_Skills",
                table: "CandidateProfiles",
                column: "Skills")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfiles_Tags",
                table: "CandidateProfiles",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateCvs_SearchVector",
                table: "CandidateCvs",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateNotes_PersonId",
                table: "CandidateNotes",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumeBankStatusChanges_PersonId",
                table: "ResumeBankStatusChanges",
                column: "PersonId");

            // Existing candidates take the country of their earliest application's requisition, else their employee record,
            // else the first configured country; their contact date and status come from their applications.
            migrationBuilder.Sql("""
                UPDATE "CandidateProfiles" p SET "CountryCode" = coalesce(
                    (SELECT r."CountryCode" FROM "Applications" a JOIN "Requisitions" r ON r."Id" = a."RequisitionId"
                     WHERE a."PersonId" = p."PersonId" ORDER BY a."AppliedAtUtc" LIMIT 1),
                    (SELECT e."CountryCode" FROM "Employees" e WHERE e."PersonId" = p."PersonId" LIMIT 1),
                    (SELECT min(c."CountryCode") FROM "CountrySettings" c));
                UPDATE "CandidateProfiles" p SET "LastContactedAtUtc" =
                    (SELECT max(a."StageChangedAtUtc") FROM "Applications" a WHERE a."PersonId" = p."PersonId");
                UPDATE "CandidateProfiles" p SET "Status" = (
                    SELECT CASE a."Stage"
                        WHEN 'Rejected' THEN 'NotShortlisted' WHEN 'NoShow' THEN 'NoShow' WHEN 'OfferDeclined' THEN 'OfferDeclined'
                        WHEN 'Joined' THEN 'Hired' ELSE 'Shortlisted' END
                    FROM "Applications" a WHERE a."PersonId" = p."PersonId"
                      AND a."Stage" IN ('Screening', 'Interview', 'Offer', 'OfferAccepted', 'Joined', 'Rejected', 'NoShow', 'OfferDeclined')
                    ORDER BY a."StageChangedAtUtc" DESC LIMIT 1);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_CandidateProfiles_CountrySettings_CountryCode",
                table: "CandidateProfiles",
                column: "CountryCode",
                principalTable: "CountrySettings",
                principalColumn: "CountryCode",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CandidateProfiles_CountrySettings_CountryCode",
                table: "CandidateProfiles");

            migrationBuilder.DropTable(
                name: "CandidateNotes");

            migrationBuilder.DropTable(
                name: "ResumeBankStatusChanges");

            migrationBuilder.DropIndex(
                name: "IX_CandidateProfiles_CountryCode_Status",
                table: "CandidateProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CandidateProfiles_LastContactedAtUtc",
                table: "CandidateProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CandidateProfiles_Skills",
                table: "CandidateProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CandidateProfiles_Tags",
                table: "CandidateProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CandidateCvs_SearchVector",
                table: "CandidateCvs");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "CandidateCvs");

            migrationBuilder.DropColumn(
                name: "BlacklistReason",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "ExperienceYears",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "LastContactedAtUtc",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "Skills",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "CandidateCvs");
        }
    }
}
