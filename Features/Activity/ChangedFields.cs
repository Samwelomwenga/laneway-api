using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Laneway.Api;

public static class ChangedFields
{
    public static bool Changed<TEntity>(this EntityEntry<TEntity> entry) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.State == EntityState.Modified;
    }

    public static Was<TValue>? Old<TEntity, TValue>(
        this EntityEntry<TEntity> entry, Expression<Func<TEntity, TValue>> field)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entry);
        var property = entry.Property(field);
        return property.IsModified ? new Was<TValue>(property.OriginalValue) : null;
    }
}
