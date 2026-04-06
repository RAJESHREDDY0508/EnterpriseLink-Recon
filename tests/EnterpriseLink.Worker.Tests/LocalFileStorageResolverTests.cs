using EnterpriseLink.Worker.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="LocalFileStorageResolver"/>.
///
/// <para>
/// Tests verify path resolution, path traversal prevention, and guard conditions.
/// Each test uses an isolated temp directory cleaned up in <see cref="Dispose"/>.
/// </para>
/// </summary>
public sealed class LocalFileStorageResolverTests : IDisposable
{
    private readonly string _tempRoot;

    public LocalFileStorageResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"el-resolver-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private LocalFileStorageResolver BuildResolver(string? basePath = null)
    {
        var opts = Options.Create(new FileStorageResolverOptions
        {
            BasePath = basePath ?? _tempRoot,
        });
        return new LocalFileStorageResolver(opts, NullLogger<LocalFileStorageResolver>.Instance);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>A valid relative path is combined with BasePath to produce an absolute path.</summary>
    [Fact]
    public void ResolveFullPath_combines_basepath_with_relative_path()
    {
        var resolver = BuildResolver();
        var tenantId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var relative = $"{tenantId}/{uploadId}/data.csv";

        var result = resolver.ResolveFullPath(relative);

        result.Should().StartWith(_tempRoot,
            "the full path must be rooted at the configured BasePath");
        result.Should().Contain(tenantId.ToString());
        result.Should().Contain(uploadId.ToString());
        result.Should().EndWith("data.csv");
    }

    /// <summary>The returned path is always absolute (rooted).</summary>
    [Fact]
    public void ResolveFullPath_returns_absolute_path()
    {
        var resolver = BuildResolver();
        var result = resolver.ResolveFullPath($"{Guid.NewGuid()}/{Guid.NewGuid()}/file.csv");
        Path.IsPathRooted(result).Should().BeTrue("the resolved path must always be absolute");
    }

    /// <summary>The resolved path contains no double-slash separators.</summary>
    [Fact]
    public void ResolveFullPath_produces_clean_path_without_double_separators()
    {
        var resolver = BuildResolver();
        var result = resolver.ResolveFullPath($"{Guid.NewGuid()}/{Guid.NewGuid()}/file.csv");
        result.Should().NotContain("//", "Path.GetFullPath must normalise separators");
        result.Should().NotContain(@"\\", "Path.GetFullPath must normalise separators");
    }

    // ── Security: path traversal ──────────────────────────────────────────────

    /// <summary>
    /// A relative path containing <c>../</c> that escapes the storage root must
    /// be rejected with <see cref="ArgumentException"/> to prevent directory traversal.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("tenant-id/../../secret.csv")]
    [InlineData("../outside/file.csv")]
    public void ResolveFullPath_rejects_path_traversal_sequences(string maliciousPath)
    {
        var resolver = BuildResolver();

        var act = () => resolver.ResolveFullPath(maliciousPath);

        act.Should().Throw<ArgumentException>(
            "a relative path that escapes the storage root must be blocked")
            .WithMessage("*traversal*");
    }

    // ── Guard conditions ──────────────────────────────────────────────────────

    /// <summary>A null relative path throws <see cref="ArgumentException"/>.</summary>
    [Fact]
    public void ResolveFullPath_throws_for_null_path()
    {
        var resolver = BuildResolver();
        var act = () => resolver.ResolveFullPath(null!);
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>An empty relative path throws <see cref="ArgumentException"/>.</summary>
    [Fact]
    public void ResolveFullPath_throws_for_empty_path()
    {
        var resolver = BuildResolver();
        var act = () => resolver.ResolveFullPath(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A whitespace-only relative path throws <see cref="ArgumentException"/>.</summary>
    [Fact]
    public void ResolveFullPath_throws_for_whitespace_path()
    {
        var resolver = BuildResolver();
        var act = () => resolver.ResolveFullPath("   ");
        act.Should().Throw<ArgumentException>();
    }

    // ── Relative BasePath resolution ──────────────────────────────────────────

    /// <summary>
    /// A relative <c>BasePath</c> (e.g. <c>"uploads"</c>) is resolved against
    /// <see cref="AppContext.BaseDirectory"/> at construction time, producing an
    /// absolute internal path that does not change between calls.
    /// </summary>
    [Fact]
    public void Constructor_resolves_relative_basepath_to_absolute()
    {
        // Use a relative-looking path that won't escape the test runner directory.
        var resolver = BuildResolver(basePath: "uploads");
        var result = resolver.ResolveFullPath($"{Guid.NewGuid()}/{Guid.NewGuid()}/f.csv");
        Path.IsPathRooted(result).Should().BeTrue(
            "even when BasePath is relative, the resolved full path must be absolute");
    }
}
