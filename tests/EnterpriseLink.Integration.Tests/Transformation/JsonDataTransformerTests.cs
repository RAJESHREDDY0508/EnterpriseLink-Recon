using EnterpriseLink.Integration.Transformation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Integration.Tests.Transformation;

public sealed class JsonDataTransformerTests
{
    private readonly JsonDataTransformer _sut = new(NullLogger<JsonDataTransformer>.Instance);

    private static readonly Dictionary<string, string> Mappings = new()
    {
        ["Id"]          = "ExternalReferenceId",
        ["Amount__c"]   = "Amount",
        ["Description"] = "Description",
    };

    [Fact]
    public void Transform_ValidJsonArray_ProducesCorrectCsvRows()
    {
        const string json = """
            [
              {"Id":"SF-001","Amount__c":"1200.00","Description":"Invoice"},
              {"Id":"SF-002","Amount__c":"300.50","Description":"Credit"}
            ]
            """;

        var result = _sut.Transform(json, Mappings, "Salesforce", "TestAdapter");

        result.RowCount.Should().Be(2);
        result.CsvContent.Should().Contain("SF-001");
        result.CsvContent.Should().Contain("1200.00");
        result.CsvContent.Should().Contain("Salesforce");
        result.CsvContent.Should().StartWith("ExternalReferenceId");
    }

    [Fact]
    public void Transform_DescriptionWithComma_IsCsvEscaped()
    {
        const string json = """[{"Id":"1","Amount__c":"100","Description":"Fee, tax"}]""";

        var result = _sut.Transform(json, Mappings, "Src", "Adapter");

        result.CsvContent.Should().Contain("\"Fee, tax\"");
    }

    [Fact]
    public void Transform_EmptyArray_ReturnsZeroRows()
    {
        var result = _sut.Transform("[]", Mappings, "Src", "Adapter");
        result.RowCount.Should().Be(0);
    }

    [Fact]
    public void Transform_NotAnArray_ReturnsZeroRows()
    {
        var result = _sut.Transform("""{"data":"value"}""", Mappings, "Src", "Adapter");
        result.RowCount.Should().Be(0);
    }

    [Fact]
    public void Transform_InvalidJson_ReturnsZeroRows()
    {
        var result = _sut.Transform("not json", Mappings, "Src", "Adapter");
        result.RowCount.Should().Be(0);
    }

    [Fact]
    public void Transform_SuggestedFileName_ContainsAdapterName()
    {
        var result = _sut.Transform("[]", Mappings, "Src", "MyAdapter");
        result.SuggestedFileName.Should().StartWith("myadapter_");
    }
}
