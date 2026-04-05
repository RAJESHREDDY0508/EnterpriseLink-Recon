using EnterpriseLink.Ingestion.Configuration;
using EnterpriseLink.Ingestion.Models;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Ingestion.Validation;

/// <summary>
/// FluentValidation validator for <see cref="FileUploadRequest"/>.
///
/// <para><b>File rules</b></para>
/// <list type="bullet">
///   <item><description>File must be provided (not null).</description></item>
///   <item><description>File must not be empty (length &gt; 0).</description></item>
///   <item><description>
///     File size must not exceed <see cref="IngestionOptions.MaxFileSizeBytes"/>
///     (validated here at the application layer; Kestrel enforces the same limit at
///     the connection layer as a defence-in-depth measure).
///   </description></item>
///   <item><description>File extension must be <c>.csv</c> (case-insensitive).</description></item>
///   <item><description>
///     Content-Type must be one of: <c>text/csv</c>, <c>text/plain</c>,
///     <c>application/octet-stream</c>, or <c>application/vnd.ms-excel</c>.
///   </description></item>
/// </list>
///
/// <para><b>Metadata rules</b></para>
/// <list type="bullet">
///   <item><description><c>SourceSystem</c> is required, 1–100 characters, alphanumeric + <c>-_</c> and spaces. Pure-whitespace values are rejected.</description></item>
///   <item><description><c>Description</c> is optional, maximum 500 characters.</description></item>
/// </list>
///
/// <para><b>Cascade behaviour</b></para>
/// File-specific rules (size, extension, content-type) run only when <c>File</c> is
/// not null, preventing NullReferenceExceptions from cascading rule chains.
/// <c>FileName</c> is first checked for null/empty before the extension rule runs,
/// so a null <c>FileName</c> produces a clear error rather than a
/// <c>NullReferenceException</c>.
/// </summary>
public sealed class FileUploadRequestValidator : AbstractValidator<FileUploadRequest>
{
    /// <summary>
    /// Content-Type values accepted for CSV uploads.
    /// Some HTTP clients send <c>application/octet-stream</c> or <c>application/vnd.ms-excel</c>
    /// when the OS does not recognise the <c>.csv</c> MIME type.
    /// </summary>
    private static readonly string[] AllowedContentTypes =
    [
        "text/csv",
        "text/plain",
        "application/octet-stream",
        "application/vnd.ms-excel",
    ];

    /// <summary>
    /// Initialises the validator with file-size limits from configuration.
    /// </summary>
    /// <param name="options">
    /// Ingestion service options — provides <see cref="IngestionOptions.MaxFileSizeBytes"/>.
    /// </param>
    public FileUploadRequestValidator(IOptions<IngestionOptions> options)
    {
        var maxBytes = options.Value.MaxFileSizeBytes;

        // ── File presence ────────────────────────────────────────────────────
        RuleFor(x => x.File)
            .NotNull()
            .WithMessage("A file must be provided.");

        // ── File-specific rules (only when file is present) ──────────────────
        When(x => x.File is not null, () =>
        {
            RuleFor(x => x.File.Length)
                .GreaterThan(0)
                .WithMessage("The uploaded file must not be empty.");

            RuleFor(x => x.File.Length)
                .LessThanOrEqualTo(maxBytes)
                .WithMessage(x =>
                    $"File size {x.File.Length:N0} bytes exceeds the maximum allowed size of {maxBytes:N0} bytes ({maxBytes / 1_048_576} MB).");

            // FileName: NotEmpty in its own RuleFor so the .When() on the extension
            // check below does not inadvertently skip the NotEmpty check when the
            // file name is null or empty (FluentValidation .When() applies to the
            // entire preceding chain within a single RuleFor call).
            RuleFor(x => x.File.FileName)
                .NotEmpty()
                .WithMessage("File name must not be null or empty.");

            // Extension check: separate RuleFor so .When() only guards this rule.
            RuleFor(x => x.File.FileName)
                .Must(name => Path.GetExtension(name)
                    .Equals(".csv", StringComparison.OrdinalIgnoreCase))
                .When(x => !string.IsNullOrEmpty(x.File?.FileName))
                .WithMessage("Only .csv files are accepted. Received: '{PropertyValue}'.");

            RuleFor(x => x.File.ContentType)
                .Must(ct => AllowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase))
                .WithMessage(x =>
                    $"Content-Type '{x.File.ContentType}' is not accepted. " +
                    $"Allowed values: {string.Join(", ", AllowedContentTypes)}.");
        });

        // ── Metadata rules ───────────────────────────────────────────────────
        // NotEmpty in its own RuleFor so the .When() on the chain below does not
        // skip the required check when SourceSystem is null or empty string.
        RuleFor(x => x.SourceSystem)
            .NotEmpty()
            .WithMessage("SourceSystem is required.");

        // Pure-whitespace values (e.g. "   ") pass NotEmpty() but are semantically
        // meaningless. Length and pattern checks run in the same chain.
        // The .When() here applies only to this RuleFor (not to the NotEmpty above),
        // so empty/null is already caught and we avoid double-failing on those inputs.
        RuleFor(x => x.SourceSystem)
            .Must(s => !string.IsNullOrWhiteSpace(s))
            .WithMessage("SourceSystem must not be blank or whitespace-only.")
            .MaximumLength(100)
            .WithMessage("SourceSystem must not exceed 100 characters.")
            .Matches(@"^[a-zA-Z0-9\-_\s]+$")
            .WithMessage(
                "SourceSystem may only contain letters, digits, hyphens, underscores, and spaces.")
            .When(x => !string.IsNullOrEmpty(x.SourceSystem));

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .WithMessage("Description must not exceed 500 characters.")
            .When(x => x.Description is not null);
    }
}
