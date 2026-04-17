using EnterpriseLink.Integration.Adapters.Soap;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Integration.Tests.Adapters.Soap;

public sealed class SoapResponseParserTests
{
    private readonly SoapResponseParser _sut = new(NullLogger<SoapResponseParser>.Instance);

    [Fact]
    public void ExtractBody_ValidEnvelope_ReturnsInnerBody()
    {
        const string soap = """
            <?xml version="1.0"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <GetTransactionsResponse>
                  <Transaction><Id>1</Id></Transaction>
                </GetTransactionsResponse>
              </soap:Body>
            </soap:Envelope>
            """;

        var result = _sut.ExtractBody(soap);

        result.Should().Contain("GetTransactionsResponse");
        result.Should().Contain("<Id>1</Id>");
    }

    [Fact]
    public void ExtractBody_SoapFault_ThrowsInvalidOperationException()
    {
        const string soap = """
            <?xml version="1.0"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <soap:Fault>
                  <faultcode>Server</faultcode>
                  <faultstring>Internal server error</faultstring>
                </soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """;

        var act = () => _sut.ExtractBody(soap);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Internal server error*");
    }

    [Fact]
    public void ExtractBody_EmptyString_ReturnsEmpty()
    {
        _sut.ExtractBody(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ExtractBody_InvalidXml_ReturnsEmpty()
    {
        _sut.ExtractBody("not xml at all {{ }}").Should().BeEmpty();
    }
}
