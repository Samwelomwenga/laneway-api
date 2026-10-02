using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public static class PagedQuery
{
    public static async Task<(List<T> Items, int TotalCount)> ReadAsync<T>(
        IQueryable<T> query, int pageNumber, int pageSize)
    {
        var totalCount = await query.CountAsync();
        var offset = (long)(pageNumber - 1) * pageSize;
        if (offset >= totalCount)
        {
            return ([], totalCount);
        }

        var items = await query.Skip((int)offset).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }
}
