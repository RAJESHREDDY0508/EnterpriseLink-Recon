using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Storage;

namespace EnterpriseLink.Worker.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering the Worker
/// service's file storage resolver and CSV streaming parser.
///
/// <para>
/// Keeping these registrations in a dedicated extension keeps <c>Program.cs</c>
/// declarative and groups related infrastructure concerns together.
/// </para>
/// </summary>
public static class WorkerStorageExtensions
{
    /// <summary>
    /// Registers <see cref="IFileStorageResolver"/> and <see cref="ICsvStreamingParser"/>
    /// into the DI container.
    ///
    /// <para><b>Lifetimes</b></para>
    /// Both services are registered as <b>Singleton</b>:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="LocalFileStorageResolver"/> is stateless after construction
    ///       (only holds the resolved <c>BasePath</c> string) and is safe to share
    ///       across concurrent consumers.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="CsvStreamingParser"/> is stateless — all state lives in the
    ///       local variables of each <c>ParseAsync</c> async iterator call, making it
    ///       thread-safe and safe to share across consumers.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Usage in <c>Program.cs</c></b></para>
    /// <code>
    /// builder.Services.AddWorkerStorage(builder.Configuration);
    /// </code>
    /// </summary>
    /// <param name="services">The DI container to register services into.</param>
    /// <param name="configuration">Application configuration (reads <c>FileStorage:Local</c> section).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for fluent chaining.</returns>
    public static IServiceCollection AddWorkerStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<FileStorageResolverOptions>()
            .Bind(configuration.GetSection(FileStorageResolverOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IFileStorageResolver, LocalFileStorageResolver>();
        services.AddSingleton<ICsvStreamingParser, CsvStreamingParser>();

        return services;
    }
}
