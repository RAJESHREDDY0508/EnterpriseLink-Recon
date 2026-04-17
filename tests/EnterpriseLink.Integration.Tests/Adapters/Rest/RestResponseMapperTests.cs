using EnterpriseLink.Integration.Adapters.Rest;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Integration.Tests.Adapters.Rest;

public sealed class RestResponseMapperTests
{
    private readonly RestResponseMapper _sut = new(NullLogger<RestResponseMapper>.Instance);

    [Fact]
    public void ExtractArray_RootIsArray_ReturnsRootJson()
    {
        const string json = """[{"Id":"1","Amount":"100"}]""";

        var result = _sut.ExtractArray(json, string.Empty);

        result.Should().Contain("\"Id\"");
        result.Should().StartWith("[");
    }

    [Fact]
    public void ExtractArray_NestedPath_NavigatesCorrectly()
    {
        const string json = """{"data":{"records":[{"Id":"1"}]}}""";

        var result = _sut.ExtractArray(json, "data.records");

        result.Should().StartWith("[");
        result.Should().Contain("\"Id\"");
    }

    [Fact]
    public void ExtractArray_PathNotFound_ReturnsEmptyArray()
    {
        const string json = """{"other":[]}""";

        var result = _sut.ExtractArray(json, "missing.path");

        result.Should().Be("[]");
    }

    [Fact]
    public void ExtractArray_InvalidJson_ReturnsEmptyArray()
    {
        var result = _sut.ExtractArray("not json", string.Empty);

        result.Should().Be("[]");
    }

    [Fact]
    public void ExtractArray_EmptyString_ReturnsEmptyArray()
    {
        _sut.ExtractArray(string.Empty, string.Empty).Should().Be("[]");
    }
}
