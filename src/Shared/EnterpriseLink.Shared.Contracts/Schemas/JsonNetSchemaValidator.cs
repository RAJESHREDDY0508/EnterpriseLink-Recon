using Json.Schema;
using System.Text.Json.Nodes;

namespace EnterpriseLink.Shared.Contracts.Schemas;

/// <summary>
/// <see cref="IEventSchemaValidator"/> implementation backed by
/// <a href="https://json-everything.net/json-schema">JsonSchema.Net</a>
/// (JSON Schema Draft 2020-12).
///
/// <para><b>Schema loading</b></para>
/// Schemas are loaded from embedded resources in this assembly the first time they are
/// requested, then cached in a static <c>ConcurrentDictionary&lt;string, JsonSchema&gt;</c>
/// for the lifetime of the process. This avoids repeated I/O and parsing on every
/// validation call.
///
/// <para><b>Thread safety</b></para>
/// The schema cache is a <c>ConcurrentDictionary&lt;string, JsonSchema&gt;</c>;
/// multiple threads may call <see cref="Validate"/> concurrently without locking.
/// <c>JsonSchema</c> objects from <c>JsonSchema.Net</c> are immutable after construction
/// and safe to share across threads.
///
/// <para>
/// <b>Output format:</b> uses <see cref="OutputFormat.List"/> so that <em>all</em>
/// violated constraints are reported in a single pass — consumers see the complete set
/// of errors rather than stopping at the first failure.
/// </para>
/// </summary>
public sealed class JsonNetSchemaValidator : IEventSchemaValidator
{
    // Keyed by embedded resource name → parsed JsonSchema.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonSchema> SchemaCache
        = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public SchemaValidationResult Validate(string json, string schemaResourceName)
    {
        // ── Step 1: Parse the incoming JSON payload ───────────────────────────
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            return SchemaValidationResult.Fail(
            [
                $"Payload is not valid JSON: {ex.Message}",
            ]);
        }

        // ── Step 2: Load (or retrieve from cache) the target schema ───────────
        JsonSchema schema;
        try
        {
            schema = SchemaCache.GetOrAdd(schemaResourceName, LoadSchema);
        }
        catch (Exception ex)
        {
            return SchemaValidationResult.Fail(
            [
                $"Schema '{schemaResourceName}' could not be loaded: {ex.Message}",
            ]);
        }

        // ── Step 3: Evaluate the payload against the schema ───────────────────
        var result = schema.Evaluate(node, new EvaluationOptions
        {
            // Report all violations, not just the first one.
            OutputFormat = OutputFormat.List,
        });

        if (result.IsValid)
            return SchemaValidationResult.Valid;

        // ── Step 4: Collect all violation messages ────────────────────────────
        var errors = result.Details
            .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e =>
                $"[{d.InstanceLocation}] {e.Key}: {e.Value}"))
            .ToArray();

        return SchemaValidationResult.Fail(errors.Length > 0
            ? errors
            : ["Schema validation failed (no detailed error information available)."]);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads a JSON Schema from an embedded resource in this assembly.
    /// Called at most once per unique resource name (cache miss path).
    /// </summary>
    /// <param name="resourceName">
    /// The fully-qualified embedded resource name as defined in <see cref="KnownSchemas"/>.
    /// </param>
    /// <returns>A parsed, immutable <see cref="JsonSchema"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resource is not found in the assembly manifest.
    /// </exception>
    private static JsonSchema LoadSchema(string resourceName)
    {
        var assembly = typeof(JsonNetSchemaValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded schema resource '{resourceName}' was not found in assembly " +
                $"'{assembly.GetName().Name}'. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        var schemaJson = reader.ReadToEnd();

        // Wrap JsonSchema.FromText so that a corrupt embedded resource produces a clear
        // InvalidOperationException (caught by the caller's try/catch) rather than an
        // unhandled JsonException that bypasses the error collection path.
        try
        {
            return JsonSchema.FromText(schemaJson);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Embedded schema resource '{resourceName}' contains malformed JSON Schema. " +
                $"Verify the resource file is valid JSON Schema 2020-12. Inner: {ex.Message}", ex);
        }
    }
}
