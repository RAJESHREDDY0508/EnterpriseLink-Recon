namespace EnterpriseLink.Dashboard.Dtos;

/// <summary>
/// Generic paginated result wrapper returned by all Dashboard list endpoints.
///
/// <para>
/// Pagination is offset-based (<c>Page</c> + <c>PageSize</c>) rather than cursor-based.
/// Offset pagination suits the Dashboard use-case — operators browse historical
/// data in relatively small page sizes with random-access navigation.
/// </para>
///
/// <para>
/// <b>Derived computed properties:</b> <c>TotalPages</c>, <c>HasNextPage</c>, and
/// <c>HasPreviousPage</c> are computed from the stored values so client applications
/// do not need to recalculate navigation state.
/// </para>
/// </summary>
/// <typeparam name="T">The item type for this page of results.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>The items on the current page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Total number of records across all pages (used to compute <see cref="TotalPages"/>).</summary>
    public int TotalCount { get; }

    /// <summary>1-based current page number.</summary>
    public int Page { get; }

    /// <summary>Maximum records returned per page.</summary>
    public int PageSize { get; }

    /// <summary>Total number of pages given <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    public int TotalPages =>
        PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary><c>true</c> if a subsequent page exists.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary><c>true</c> if a preceding page exists.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Constructs a paginated result.
    /// </summary>
    /// <param name="items">Items on this page.</param>
    /// <param name="totalCount">Total records in the unpaged query.</param>
    /// <param name="page">Current 1-based page number.</param>
    /// <param name="pageSize">Page size used for the query.</param>
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}
