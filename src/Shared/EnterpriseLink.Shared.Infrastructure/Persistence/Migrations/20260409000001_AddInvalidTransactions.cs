using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvalidTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvalidTransactions",
                columns: table => new
                {
                    InvalidTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    RawData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationErrors = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvalidTransactions", x => x.InvalidTransactionId);
                    table.ForeignKey(
                        name: "FK_InvalidTransactions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvalidTransactions_TenantId",
                table: "InvalidTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvalidTransactions_UploadId",
                table: "InvalidTransactions",
                column: "UploadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvalidTransactions_TenantId_UploadId",
                table: "InvalidTransactions",
                columns: new[] { "TenantId", "UploadId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InvalidTransactions");
        }
    }
}
