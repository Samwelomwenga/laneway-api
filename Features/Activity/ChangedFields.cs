using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DefaultNamespace;

public static class ChangedFields
{
    public static bool Changed<TEntity>(this EntityEntry<TEntity> entry) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.State == EntityState.Modified;
    }

    public static string? Old<TEntity>(this EntityEntry<TEntity> entry, Expression<Func<TEntity, string>> field)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entry);
        var property = entry.Property(field);
        return property.IsModified ? property.OriginalValue : null;
    }

    public static TValue? Old<TEntity, TValue>(
        this EntityEntry<TEntity> entry, Expression<Func<TEntity, TValue>> field)
        where TEntity : class where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(entry);
        var property = entry.Property(field);
        return property.IsModified ? property.OriginalValue : null;
    }
}
