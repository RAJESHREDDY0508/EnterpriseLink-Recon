namespace EnterpriseLink.Integration.Adapters.Soap;

/// <summary>Describes a single operation discovered in a WSDL document.</summary>
public sealed class WsdlOperation
{
    /// <summary>Operation local name (e.g. <c>GetTransactions</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Input message name, if present.</summary>
    public string? InputMessage { get; init; }

    /// <summary>Output message name, if present.</summary>
    public string? OutputMessage { get; init; }
}
