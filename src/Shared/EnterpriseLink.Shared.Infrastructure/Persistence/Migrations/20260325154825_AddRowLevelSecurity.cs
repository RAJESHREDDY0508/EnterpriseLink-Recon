using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Security predicate function ───────────────────────────────
            //
            // Reads SESSION_CONTEXT(N'TenantId') which is set per-connection by
            // TenantSessionContextInterceptor before any query executes.
            // WITH SCHEMABINDING prevents the referenced tables from being altered
            // without first dropping the function.
            migrationBuilder.Sql(
                """
                CREATE OR ALTER FUNCTION [dbo].[fn_TenantAccessPredicate]
                    (@TenantId UNIQUEIDENTIFIER)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                    RETURN
                        SELECT 1 AS [result]
                        WHERE CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER) = @TenantId;
                """);

            // ── Step 2: Row-Level Security policy ────────────────────────────────
            //
            // FILTER predicate: silently excludes rows that don't belong to the
            //   current tenant on SELECT / UPDATE / DELETE.
            //
            // BLOCK predicate AFTER INSERT: prevents inserting rows whose TenantId
            //   does not match the session context (defence-in-depth on top of the
            //   EF Core ApplyTenantId interceptor).
            migrationBuilder.Sql(
                """
                CREATE SECURITY POLICY [dbo].[TenantIsolationPolicy]
                    ADD FILTER PREDICATE [dbo].[fn_TenantAccessPredicate]([TenantId])
                        ON [dbo].[Users],
                    ADD BLOCK  PREDICATE [dbo].[fn_TenantAccessPredicate]([TenantId])
                        ON [dbo].[Users]  AFTER INSERT,
                    ADD FILTER PREDICATE [dbo].[fn_TenantAccessPredicate]([TenantId])
                        ON [dbo].[Transactions],
                    ADD BLOCK  PREDICATE [dbo].[fn_TenantAccessPredicate]([TenantId])
                        ON [dbo].[Transactions] AFTER INSERT
                WITH (STATE = ON, SCHEMABINDING = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP SECURITY POLICY IF EXISTS [dbo].[TenantIsolationPolicy];");

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS [dbo].[fn_TenantAccessPredicate];");
        }
    }
}
