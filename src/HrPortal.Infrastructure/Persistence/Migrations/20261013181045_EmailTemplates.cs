using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailAttachmentUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedById = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAttachmentUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyHtml = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    DefaultSender = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CcManager = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    StarterKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailTemplates_CountrySettings_CountryCode",
                        column: x => x.CountryCode,
                        principalTable: "CountrySettings",
                        principalColumn: "CountryCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    To = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Cc = table.Column<List<string>>(type: "text[]", nullable: false),
                    Sender = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyHtml = table.Column<string>(type: "text", nullable: false),
                    Attachments = table.Column<string>(type: "jsonb", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentById = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SentByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailLog_EmailTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailLog_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailLog_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyHtml = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    DefaultSender = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CcManager = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedById = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChangedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailTemplateVersions_EmailTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailLog_BatchId",
                table: "EmailLog",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLog_EmployeeId",
                table: "EmailLog",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLog_PersonId_SentAtUtc",
                table: "EmailLog",
                columns: new[] { "PersonId", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailLog_TemplateId",
                table: "EmailLog",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_CountryCode",
                table: "EmailTemplates",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Name",
                table: "EmailTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_StarterKey",
                table: "EmailTemplates",
                column: "StarterKey",
                unique: true,
                filter: "\"StarterKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplateVersions_TemplateId_Version",
                table: "EmailTemplateVersions",
                columns: new[] { "TemplateId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailAttachmentUploads");

            migrationBuilder.DropTable(
                name: "EmailLog");

            migrationBuilder.DropTable(
                name: "EmailTemplateVersions");

            migrationBuilder.DropTable(
                name: "EmailTemplates");
        }
    }
}
