namespace EnterpriseLink.Integration.Transformation;

/// <summary>
/// Transforms raw external data (XML, JSON, or CSV bytes) into the standard
/// internal CSV format consumed by the Worker service.
///
/// <para>
/// All adapters produce the same CSV schema:
/// <c>ExternalReferenceId,Amount,Description,SourceSystem</c>
/// </para>
/// </summary>
public interface IDataTransformer
{
    /// <summary>
    /// Transforms <paramref name="rawData"/> using the supplied <paramref name="fieldMappings"/>
    /// and returns a <see cref="TransformResult"/> containing CSV content.
    /// </summary>
    /// <param name="rawData">Raw response from the external system (XML/JSON/CSV string).</param>
    /// <param name="fieldMappings">
    /// Maps external field names (keys) to internal CSV column names (values).
    /// Recognised internal columns: <c>ExternalReferenceId</c>, <c>Amount</c>, <c>Description</c>.
    /// </param>
    /// <param name="sourceSystem">Value written to every row's <c>SourceSystem</c> column.</param>
    /// <param name="adapterName">Adapter name used when building the suggested file name.</param>
    TransformResult Transform(
        string rawData,
        Dictionary<string, string> fieldMappings,
        string sourceSystem,
        string adapterName);
}
