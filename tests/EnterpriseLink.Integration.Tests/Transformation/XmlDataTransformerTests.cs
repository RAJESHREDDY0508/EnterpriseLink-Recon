using EnterpriseLink.Integration.Transformation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Integration.Tests.Transformation;

public sealed class XmlDataTransformerTests
{
    private readonly XmlDataTransformer _sut = new(NullLogger<XmlDataTransformer>.Instance);

    private static readonly Dictionary<string, string> Mappings = new()
    {
        ["TransactionId"] = "ExternalReferenceId",
        ["Amount"]        = "Amount",
        ["Description"]   = "Description",
    };

    [Fact]
    public void Transform_ValidXml_ProducesCorrectCsvRows()
    {
        const string xml = """
            <GetTransactionsResponse>
              <Transaction>
                <TransactionId>TX-001</TransactionId>
                <Amount>1500.00</Amount>
                <Description>Payment</Description>
              </Transaction>
              <Transaction>
                <TransactionId>TX-002</TransactionId>
                <Amount>250.00</Amount>
                <Description>Refund</Description>
              </Transaction>
            </GetTransactionsResponse>
            """;

        var result = _sut.Transform(xml, Mappings, "LegacyERP", "TestAdapter");

        result.RowCount.Should().Be(2);
        result.CsvContent.Should().Contain("TX-001");
        result.CsvContent.Should().Contain("1500.00");
        result.CsvContent.Should().Contain("LegacyERP");
        result.CsvContent.Should().StartWith("ExternalReferenceId");
    }

    [Fact]
    public void Transform_SoapEnvelope_UnwrapsBodyCorrectly()
    {
        const string soap = """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <GetTransactionsResponse>
                  <Transaction>
                    <TransactionId>TX-100</TransactionId>
                    <Amount>500.00</Amount>
                  </Transaction>
                </GetTransactionsResponse>
              </soap:Body>
            </soap:Envelope>
            """;

        var result = _sut.Transform(soap, Mappings, "SOAP", "TestAdapter");

        result.RowCount.Should().Be(1);
        result.CsvContent.Should().Contain("TX-100");
    }

    [Fact]
    public void Transform_EmptyXml_ReturnsZeroRows()
    {
        var result = _sut.Transform(string.Empty, Mappings, "SOAP", "TestAdapter");
        result.RowCount.Should().Be(0);
    }

    [Fact]
    public void Transform_InvalidXml_ReturnsZeroRows()
    {
        var result = _sut.Transform("not xml", Mappings, "SOAP", "TestAdapter");
        result.RowCount.Should().Be(0);
    }
}
