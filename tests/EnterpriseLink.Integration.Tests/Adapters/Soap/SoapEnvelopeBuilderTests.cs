using EnterpriseLink.Integration.Adapters.Soap;
using FluentAssertions;

namespace EnterpriseLink.Integration.Tests.Adapters.Soap;

public sealed class SoapEnvelopeBuilderTests
{
    private readonly SoapEnvelopeBuilder _sut = new();

    [Fact]
    public void Build_WithNamespace_ContainsTnsPrefix()
    {
        var envelope = _sut.Build(
            "GetTransactions",
            "http://example.com/transactions/",
            null);

        envelope.Should().Contain("tns:GetTransactions");
        envelope.Should().Contain("xmlns:tns=\"http://example.com/transactions/\"");
    }

    [Fact]
    public void Build_WithoutNamespace_UsesLocalName()
    {
        var envelope = _sut.Build("GetTransactions", string.Empty, null);

        envelope.Should().Contain("<GetTransactions>");
        envelope.Should().NotContain("tns:");
    }

    [Fact]
    public void Build_WithParameters_IncludesChildElements()
    {
        var envelope = _sut.Build(
            "GetTransactions",
            string.Empty,
            new Dictionary<string, string> { ["FromDate"] = "2026-01-01" });

        envelope.Should().Contain("<FromDate>2026-01-01</FromDate>");
    }

    [Fact]
    public void Build_ParameterWithSpecialChars_IsXmlEscaped()
    {
        var envelope = _sut.Build(
            "GetTransactions",
            string.Empty,
            new Dictionary<string, string> { ["Filter"] = "a&b<c>" });

        envelope.Should().Contain("<Filter>a&amp;b&lt;c&gt;</Filter>");
    }

    [Fact]
    public void Build_AlwaysContainsSoapEnvelopeNamespace()
    {
        var envelope = _sut.Build("Op", string.Empty, null);

        envelope.Should().Contain("http://schemas.xmlsoap.org/soap/envelope/");
        envelope.Should().Contain("<soap:Body>");
    }
}
