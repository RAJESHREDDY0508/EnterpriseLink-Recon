using System.Text;
using EnterpriseLink.Ingestion.Storage.Local;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace EnterpriseLink.Ingestion.Tests;

/// <summary>
/// Unit tests for <see cref="LocalFileStorageService"/>.
///
/// <para>
/// Each test uses an isolated temp directory that is deleted in the test's
/// <see cref="IDisposable.Dispose"/> method, keeping the test runner's working
/// directory clean regardless of pass/fail.
/// </para>
/// </summary>
public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public LocalFileStorageServiceTests()
    {
        // Unique temp directory per test class instance.
        _tempRoot = Path.Combine(Path.GetTempPath(), $"el-storage-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LocalFileStorageService BuildService(string? basePath = null)
    {
        var opts = Options.Create(new LocalStorageOptions
        {
            BasePath = basePath ?? _tempRoot,
        });

        return new LocalFileStorageService(opts, NullLogger<LocalFileStorageService>.Instance);
    }

    private static Mock<IFormFile> BuildFileMock(
        string fileName = "transactions.csv",
        string content = "col1,col2\nrow1,row2")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(fileName);
        mock.Setup(f => f.ContentType).Returns("text/csv");
        mock.Setup(f => f.Length).Returns(bytes.Length);
        mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
        return mock;
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>File is written to disk and exists at the expected full path.</summary>
    [Fact]
    public async Task StoreAsync_writes_file_to_disk()
    {
        var service = BuildService();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var file = BuildFileMock();

        var result = await service.StoreAsync(tenantId, uploadId, file.Object);

        File.Exists(result.FullPath).Should().BeTrue("the file must be durably written to disk");
    }

    /// <summary>Stored file content matches the original uploaded bytes.</summary>
    [Fact]
    public async Task StoreAsync_preserves_file_content()
    {
        var service = BuildService();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        const string csvContent = "col1,col2\nval1,val2\nval3,val4";
        var file = BuildFileMock(content: csvContent);

        var result = await service.StoreAsync(tenantId, uploadId, file.Object);

        var writtenContent = await File.ReadAllTextAsync(result.FullPath);
        writtenContent.Should().Be(csvContent, "content must not be modified during storage");
    }

    /// <summary>
    /// RelativePath follows the <c>{tenantId}/{uploadId}/{fileName}</c> pattern,
    /// ensuring natural tenant and upload isolation.
    /// </summary>
    [Fact]
    public async Task StoreAsync_returns_tenant_scoped_relative_path()
    {
        var service = BuildService();
        var tenantId = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000001");
        var uploadId = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000002");
        var file = BuildFileMock(fileName: "data.csv");

        var result = await service.StoreAsync(tenantId, uploadId, file.Object);

        result.RelativePath.Should()
            .StartWith(tenantId.ToString(), "top-level directory must be the tenant ID")
            .And.Contain(uploadId.ToString(), "second-level directory must be the upload ID")
            .And.EndWith("data.csv", "file name must be preserved");
    }

    /// <summary>FullPath is an absolute path that contains the relative path as a suffix.</summary>
    [Fact]
    public async Task StoreAsync_full_path_contains_relative_path()
    {
        var service = BuildService();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var file = BuildFileMock();

        var result = await service.StoreAsync(tenantId, uploadId, file.Object);

        result.FullPath.Should().Contain(result.RelativePath,
            "FullPath must be the absolute form of RelativePath");
        Path.IsPathRooted(result.FullPath).Should().BeTrue(
            "FullPath must be absolute so operations on it are unambiguous");
    }

    /// <summary>Provider identifier is always "local" for the local implementation.</summary>
    [Fact]
    public async Task StoreAsync_returns_local_provider_name()
    {
        var service = BuildService();
        var file = BuildFileMock();

        var result = await service.StoreAsync(Guid.NewGuid(), Guid.NewGuid(), file.Object);

        result.Provider.Should().Be("local");
    }

    /// <summary>StoredAt timestamp is close to UTC now.</summary>
    [Fact]
    public async Task StoreAsync_returns_recent_stored_at_timestamp()
    {
        var service = BuildService();
        var file = BuildFileMock();
        var before = DateTimeOffset.UtcNow;

        var result = await service.StoreAsync(Guid.NewGuid(), Guid.NewGuid(), file.Object);

        result.StoredAt.Should().BeOnOrAfter(before)
            .And.BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    // ── Directory creation ────────────────────────────────────────────────────

    /// <summary>Nested <c>{tenantId}/{uploadId}</c> directories are created automatically.</summary>
    [Fact]
    public async Task StoreAsync_creates_nested_directories()
    {
        var service = BuildService();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var file = BuildFileMock();

        var result = await service.StoreAsync(tenantId, uploadId, file.Object);

        var expectedDir = Path.Combine(_tempRoot, tenantId.ToString(), uploadId.ToString());
        Directory.Exists(expectedDir).Should().BeTrue(
            "the storage service must create intermediate directories before writing");
    }

    /// <summary>Two uploads from different tenants land in separate directories.</summary>
    [Fact]
    public async Task StoreAsync_isolates_files_per_tenant()
    {
        var service = BuildService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var upload1 = Guid.NewGuid();
        var upload2 = Guid.NewGuid();
        var file = BuildFileMock();

        var r1 = await service.StoreAsync(tenant1, upload1, file.Object);
        var r2 = await service.StoreAsync(tenant2, upload2, file.Object);

        r1.RelativePath.Should().StartWith(tenant1.ToString());
        r2.RelativePath.Should().StartWith(tenant2.ToString());
        r1.FullPath.Should().NotBe(r2.FullPath,
            "each tenant upload must have a unique full path");
    }

    // ── Security: path sanitisation ───────────────────────────────────────────

    /// <summary>
    /// A file name containing path traversal sequences (<c>../</c>) is sanitised —
    /// only the base file name is used, preventing writes outside the upload directory.
    /// </summary>
    [Fact]
    public async Task StoreAsync_sanitises_directory_traversal_in_file_name()
    {
        var service = BuildService();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();

        // Attacker-supplied file name with traversal sequence.
        var file = BuildFileMock(fileName: "../../evil.csv");

        var result = await service.StoreAsync(tenantId, uploadId, file.Object);

        result.FullPath.Should().StartWith(_tempRoot,
            "the written path must always be inside the configured BasePath");
        result.FullPath.Should().NotContain("..",
            "directory traversal sequences must be stripped from the file name");
    }
}
