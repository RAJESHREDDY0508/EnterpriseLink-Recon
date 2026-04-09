using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ProcessedUploads table ────────────────────────────────────────────
            //
            // One row per FileUploadedEvent.UploadId. The primary key (UploadId) acts
            // as the distributed idempotency lock: the first worker to INSERT wins;
            // all others receive a primary-key-violation and skip processing.
            //
            // TenantId FK references Tenants with RESTRICT so audit records are never
            // orphaned. RowVersion provides optimistic concurrency on status updates.
            migrationBuilder.CreateTable(
                name: "ProcessedUploads",
                columns: table => new
                {
                    UploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RowsInserted = table.Column<int>(type: "int", nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedUploads", x => x.UploadId);
                    table.ForeignKey(
                        name: "FK_ProcessedUploads_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedUploads_TenantId",
                table: "ProcessedUploads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedUploads_TenantId_CreatedAt",
                table: "ProcessedUploads",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProcessedUploads");
        }
    }
}
