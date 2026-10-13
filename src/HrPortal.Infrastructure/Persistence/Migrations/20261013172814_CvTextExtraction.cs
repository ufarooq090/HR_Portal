using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CvTextExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TextExtractedAtUtc",
                table: "CandidateCvs",
                type: "timestamp with time zone",
                nullable: true);

            // CVs that already have text were extracted at upload; the rest are left for the CvTextBackfill job.
            migrationBuilder.Sql("""UPDATE "CandidateCvs" SET "TextExtractedAtUtc" = "UploadedAtUtc" WHERE "ExtractedText" IS NOT NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TextExtractedAtUtc",
                table: "CandidateCvs");
        }
    }
}
