namespace FinPair.Contracts;

public sealed record ApiResponse<T>(T? Data, ApiError? Error, object Meta)
{
    public static ApiResponse<T> Ok(T data, object? meta = null) => new(data, null, meta ?? new { });

    public static ApiResponse<T> Fail(
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? details = null) =>
        new(default, new ApiError(code, message, details), new { });
}

public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null);

public sealed record ItemsResponse<T>(IReadOnlyList<T> Items);

public sealed record PagedItemsResponse<T>(IReadOnlyList<T> Items, Pagination Pagination);

public sealed record Pagination(int Page, int PageSize, int TotalItems, int TotalPages)
{
    public static Pagination Create(int page, int pageSize, int totalItems)
    {
        var safePageSize = pageSize <= 0 ? 20 : pageSize;
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)safePageSize);

        return new Pagination(page <= 0 ? 1 : page, safePageSize, totalItems, totalPages);
    }
}
