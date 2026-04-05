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

    // ── Partial-file cleanup on exception ─────────────────────────────────────

    /// <summary>
    /// Custom <see cref="Stream"/> that throws <see cref="IOException"/> on every
    /// read operation, simulating a mid-copy disk or network failure.
    /// Moq cannot reliably intercept <c>CopyToAsync</c> because the BCL calls
    /// internal buffer-read paths; a real subclass is required.
    /// </summary>
    private sealed class BrokenReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated I/O error during CopyToAsync");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            Task.FromException<int>(new IOException("Simulated I/O error during CopyToAsync"));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromException<int>(new IOException("Simulated I/O error during CopyToAsync"));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// If <c>CopyToAsync</c> fails after the target file is created, the incomplete
    /// file must be deleted so callers never see a partial write on disk.
    /// </summary>
    [Fact]
    public async Task StoreAsync_deletes_partial_file_when_copy_throws()
    {
        var service = BuildService();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("data.csv");
        fileMock.Setup(f => f.Length).Returns(1024);
        // BrokenReadStream throws IOException on every Read — the real stream subclass
        // ensures CopyToAsync sees the exception regardless of which read path it uses.
        fileMock.Setup(f => f.OpenReadStream()).Returns(new BrokenReadStream());

        // Act — StoreAsync must throw and clean up.
        var act = () => service.StoreAsync(tenantId, uploadId, fileMock.Object);
        await act.Should().ThrowAsync<IOException>("the original exception must propagate");

        // Assert — no partial file survives on disk.
        var expectedDir = Path.Combine(_tempRoot, tenantId.ToString(), uploadId.ToString());
        if (Directory.Exists(expectedDir))
        {
            Directory.GetFiles(expectedDir).Should().BeEmpty(
                "the partial file must be deleted after a copy failure");
        }
    }

    /// <summary>
    /// When the upload token is cancelled the incomplete file is removed from disk.
    /// Using a pre-cancelled token avoids fragile stream-mock timing issues —
    /// <see cref="Stream.CopyToAsync(Stream, CancellationToken)"/> checks
    /// <c>cancellationToken.IsCancellationRequested</c> before the first read,
    /// so a pre-cancelled token triggers the cancellation path reliably.
    /// </summary>
    [Fact]
    public async Task StoreAsync_deletes_partial_file_when_cancelled()
    {
        var service = BuildService();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before StoreAsync is even called.

        var file = BuildFileMock(); // Normal file — token cancels before any read.

        var act = () => service.StoreAsync(tenantId, uploadId, file.Object, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>(
            "a pre-cancelled token must cause CopyToAsync to throw OperationCanceledException");

        var expectedDir = Path.Combine(_tempRoot, tenantId.ToString(), uploadId.ToString());
        if (Directory.Exists(expectedDir))
        {
            Directory.GetFiles(expectedDir).Should().BeEmpty(
                "any partially-created file must be deleted when the upload is cancelled");
        }
    }

    // ── BasePath validation ───────────────────────────────────────────────────

    /// <summary>
    /// If <c>BasePath</c> is configured to point to an existing file (not a directory),
    /// the service constructor must throw <see cref="InvalidOperationException"/>
    /// immediately — failing fast at startup rather than producing opaque errors on
    /// every upload attempt.
    /// </summary>
    [Fact]
    public void Constructor_throws_when_BasePath_is_an_existing_file()
    {
        // Create a file at the path that would be used as BasePath.
        var filePath = Path.Combine(_tempRoot, "iam-a-file.txt");
        File.WriteAllText(filePath, "not a directory");

        var act = () => BuildService(basePath: filePath);

        act.Should().Throw<InvalidOperationException>(
            "BasePath pointing to a file must be detected at construction time")
            .WithMessage("*BasePath*exists as a file*");
    }
}
