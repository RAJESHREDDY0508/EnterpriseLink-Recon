using BenchmarkDotNet.Running;
using EnterpriseLink.Worker.Benchmarks;

// ── EnterpriseLink Worker — Batch Insert Benchmarks ──────────────────────────
//
// Measures TransactionBatchInserter throughput at BatchSize ∈ {100, 500, 1000, 5000}
// over 10,000 rows using an InMemory EF Core database.
//
// Usage (must build in Release mode for accurate timings):
//   dotnet run --project tests/EnterpriseLink.Worker.Benchmarks -c Release
//
// For SQL Server benchmarks, replace UseInMemoryDatabase in BatchInsertBenchmarks.Setup
// with UseSqlServer and supply a ConnectionStrings__DefaultConnection environment variable.
// ─────────────────────────────────────────────────────────────────────────────

BenchmarkRunner.Run<BatchInsertBenchmarks>();
