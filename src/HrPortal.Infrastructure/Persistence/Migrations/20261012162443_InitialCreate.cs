using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HrPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CountrySettings",
                columns: table => new
                {
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ProbationMonths = table.Column<int>(type: "integer", nullable: false),
                    NoticePeriodDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountrySettings", x => x.CountryCode);
                });

            migrationBuilder.InsertData(
                table: "CountrySettings",
                columns: new[] { "CountryCode", "CurrencyCode", "Name", "NoticePeriodDays", "ProbationMonths" },
                values: new object[,]
                {
                    { "PK", "PKR", "Pakistan", 30, 3 },
                    { "US", "USD", "United States", 30, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ActorId",
                table: "AuditEntries",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            // Audit rows are append-only, enforced in the database as well as in the application.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_entries_prevent_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'AuditEntries rows are append-only (% is not allowed)', TG_OP;
                END;
                $$;

                CREATE TRIGGER audit_entries_no_update_delete
                    BEFORE UPDATE OR DELETE ON "AuditEntries"
                    FOR EACH ROW EXECUTE FUNCTION audit_entries_prevent_change();

                CREATE TRIGGER audit_entries_no_truncate
                    BEFORE TRUNCATE ON "AuditEntries"
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_entries_prevent_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_entries_no_truncate ON "AuditEntries";
                DROP TRIGGER IF EXISTS audit_entries_no_update_delete ON "AuditEntries";
                DROP FUNCTION IF EXISTS audit_entries_prevent_change();
                """);

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "CountrySettings");
        }
    }
}
