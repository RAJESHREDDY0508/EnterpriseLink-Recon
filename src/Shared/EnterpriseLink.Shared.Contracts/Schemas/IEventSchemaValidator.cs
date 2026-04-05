namespace EnterpriseLink.Shared.Contracts.Schemas;

/// <summary>
/// Validates a serialised event payload against a named JSON Schema contract.
///
/// <para>
/// This interface is the boundary between event producers/consumers and the
/// underlying JSON Schema library. Swapping <c>JsonSchema.Net</c> for another
/// library only requires a new implementation — callers remain unchanged.
/// </para>
///
/// <para><b>When to validate</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Producers</b> (Ingestion service) — validate before publishing to catch
///       regression when the C# event type and the schema file drift apart.
///       Typically run in CI and integration tests, not on every request.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Consumers</b> (Worker service) — optionally validate on receive to detect
///       messages from a mismatched producer version and route to a dead-letter queue.
///     </description>
///   </item>
/// </list>
/// </summary>
public interface IEventSchemaValidator
{
    /// <summary>
    /// Validates a JSON string against the schema identified by <paramref name="schemaResourceName"/>.
    /// </summary>
    /// <param name="json">
    /// The serialised event payload as a JSON string. Must be valid JSON; malformed JSON
    /// returns a failed result with a parse error message rather than throwing.
    /// </param>
    /// <param name="schemaResourceName">
    /// The embedded resource name of the target schema. Use constants from
    /// <see cref="KnownSchemas"/> (e.g. <see cref="KnownSchemas.FileUploadedEventV1"/>).
    /// </param>
    /// <returns>
    /// A <see cref="SchemaValidationResult"/> describing whether the payload is valid
    /// and, if not, which constraints were violated.
    /// </returns>
    SchemaValidationResult Validate(string json, string schemaResourceName);
}
