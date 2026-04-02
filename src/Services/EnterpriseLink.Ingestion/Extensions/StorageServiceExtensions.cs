using EnterpriseLink.Ingestion.Configuration;
using EnterpriseLink.Ingestion.Storage;
using EnterpriseLink.Ingestion.Storage.Local;

namespace EnterpriseLink.Ingestion.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering file storage services.
///
/// <para>
/// Reads <c>FileStorage:Provider</c> from configuration and registers the matching
/// <see cref="IFileStorageService"/> implementation. New providers are added here —
/// controllers and validators are never touched when swapping storage backends.
/// </para>
/// </summary>
public static class StorageServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="IFileStorageService"/> implementation selected by
    /// <c>FileStorage:Provider</c> in the application configuration.
    ///
    /// <para><b>Supported providers</b></para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>local</c> — <see cref="LocalFileStorageService"/>.
    ///       Also registers <see cref="LocalStorageOptions"/> with validation.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Usage in <c>Program.cs</c></b></para>
    /// <code>
    /// builder.Services.AddFileStorage(builder.Configuration);
    /// </code>
    /// </summary>
    /// <param name="services">The DI container to register services into.</param>
    /// <param name="configuration">The application configuration used to resolve provider settings.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when <c>FileStorage:Provider</c> specifies an unsupported value.
    /// </exception>
    public static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register and validate top-level options.
        services
            .AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var provider = configuration
            .GetSection(FileStorageOptions.SectionName)
            .GetValue<string>("Provider") ?? "local";

        if (provider.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            // Register local-specific options with their own validation.
            services
                .AddOptions<LocalStorageOptions>()
                .Bind(configuration.GetSection($"{FileStorageOptions.SectionName}:Local"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Scoped: a new instance per HTTP request ensures any per-request
            // context (future: request-scoped storage credentials) can be injected.
            services.AddScoped<IFileStorageService, LocalFileStorageService>();
            return services;
        }

        throw new InvalidOperationException(
            $"Unsupported FileStorage:Provider value '{provider}'. " +
            $"Supported values: local. Azure Blob support is planned for a future story.");
    }
}
