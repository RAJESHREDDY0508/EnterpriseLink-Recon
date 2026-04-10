using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sprint 10 — Audit &amp; Compliance migration.
    ///
    /// Story 1 — Temporal Tables: Enables SQL Server system-versioning on all five
    /// mutable tables (Tenants, Users, Transactions, ProcessedUploads,
    /// InvalidTransactions). Each table gets a paired *History table populated
    /// automatically by SQL Server.
    ///
    /// Story 2 — Audit Logs: Creates the AuditLogs table for before/after state
    /// storage written atomically by AppDbContext.BuildAuditEntries().
    ///
    /// Story 3 — Data Lineage: Adds UploadId and SourceSystem columns to
    /// Transactions so each row can be traced back to its source file and system.
    /// </summary>
    public partial class Sprint10AuditCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Story 3: Data lineage columns on Transactions ─────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "UploadId",
                table: "Transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                table: "Transactions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_UploadId",
                table: "Transactions",
                columns: new[] { "TenantId", "UploadId" },
                filter: "[UploadId] IS NOT NULL");

            // ── Story 2: AuditLogs table ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false,
                        defaultValueSql: "NEWSEQUENTIALID()"),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAt",
                table: "AuditLogs",
                column: "OccurredAt");

            // ── Story 1: Enable system-versioning on all mutable tables ────────
            // Transactions
            migrationBuilder.Sql(@"
ALTER TABLE [Transactions]
ADD
    SysStartTime datetime2 GENERATED ALWAYS AS ROW START HIDDEN
        CONSTRAINT DF_Transactions_SysStartTime DEFAULT '0001-01-01 00:00:00.0000000' NOT NULL,
    SysEndTime datetime2 GENERATED ALWAYS AS ROW END HIDDEN
        CONSTRAINT DF_Transactions_SysEndTime DEFAULT '9999-12-31 23:59:59.9999999' NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
ALTER TABLE [Transactions]
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[TransactionsHistory]));");

            // Tenants
            migrationBuilder.Sql(@"
ALTER TABLE [Tenants]
ADD
    SysStartTime datetime2 GENERATED ALWAYS AS ROW START HIDDEN
        CONSTRAINT DF_Tenants_SysStartTime DEFAULT '0001-01-01 00:00:00.0000000' NOT NULL,
    SysEndTime datetime2 GENERATED ALWAYS AS ROW END HIDDEN
        CONSTRAINT DF_Tenants_SysEndTime DEFAULT '9999-12-31 23:59:59.9999999' NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
ALTER TABLE [Tenants]
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[TenantsHistory]));");

            // Users
            migrationBuilder.Sql(@"
ALTER TABLE [Users]
ADD
    SysStartTime datetime2 GENERATED ALWAYS AS ROW START HIDDEN
        CONSTRAINT DF_Users_SysStartTime DEFAULT '0001-01-01 00:00:00.0000000' NOT NULL,
    SysEndTime datetime2 GENERATED ALWAYS AS ROW END HIDDEN
        CONSTRAINT DF_Users_SysEndTime DEFAULT '9999-12-31 23:59:59.9999999' NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
ALTER TABLE [Users]
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UsersHistory]));");

            // ProcessedUploads
            migrationBuilder.Sql(@"
ALTER TABLE [ProcessedUploads]
ADD
    SysStartTime datetime2 GENERATED ALWAYS AS ROW START HIDDEN
        CONSTRAINT DF_ProcessedUploads_SysStartTime DEFAULT '0001-01-01 00:00:00.0000000' NOT NULL,
    SysEndTime datetime2 GENERATED ALWAYS AS ROW END HIDDEN
        CONSTRAINT DF_ProcessedUploads_SysEndTime DEFAULT '9999-12-31 23:59:59.9999999' NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
ALTER TABLE [ProcessedUploads]
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[ProcessedUploadsHistory]));");

            // InvalidTransactions
            migrationBuilder.Sql(@"
ALTER TABLE [InvalidTransactions]
ADD
    SysStartTime datetime2 GENERATED ALWAYS AS ROW START HIDDEN
        CONSTRAINT DF_InvalidTransactions_SysStartTime DEFAULT '0001-01-01 00:00:00.0000000' NOT NULL,
    SysEndTime datetime2 GENERATED ALWAYS AS ROW END HIDDEN
        CONSTRAINT DF_InvalidTransactions_SysEndTime DEFAULT '9999-12-31 23:59:59.9999999' NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime);
ALTER TABLE [InvalidTransactions]
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[InvalidTransactionsHistory]));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Disable system-versioning before dropping period columns
            foreach (var table in new[] { "Transactions", "Tenants", "Users", "ProcessedUploads", "InvalidTransactions" })
            {
                migrationBuilder.Sql($@"
ALTER TABLE [{table}] SET (SYSTEM_VERSIONING = OFF);
ALTER TABLE [{table}] DROP PERIOD FOR SYSTEM_TIME;
ALTER TABLE [{table}] DROP COLUMN SysStartTime, SysEndTime;
DROP TABLE IF EXISTS [dbo].[{table}History];");
            }

            migrationBuilder.DropTable(name: "AuditLogs");

            migrationBuilder.DropIndex(name: "IX_Transactions_TenantId_UploadId", table: "Transactions");
            migrationBuilder.DropColumn(name: "UploadId", table: "Transactions");
            migrationBuilder.DropColumn(name: "SourceSystem", table: "Transactions");
        }
    }
}
