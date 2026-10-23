using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Laneway.Api;

public sealed class DeliverySignal
{
    private readonly SemaphoreSlim _waiting = new(0, 1);

    public void Poke()
    {
        try
        {
            _waiting.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token) => _waiting.WaitAsync(timeout, token);
}

public sealed class DeliveryCommitPoke(DeliverySignal signal) : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, object> _added = [];

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Note(eventData?.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Note(eventData?.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        PokeOutsideTransaction(eventData?.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        PokeOutsideTransaction(eventData?.Context);
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        PokeIfAdded(eventData?.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        PokeIfAdded(eventData?.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Forget(eventData?.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData?.Context);
        return Task.CompletedTask;
    }

    private void Note(DbContext? context)
    {
        if (context is not null && context.ChangeTracker.Entries<Delivery>().Any(entry => entry.State == EntityState.Added))
        {
            _added.AddOrUpdate(context, this);
        }
    }

    private void PokeOutsideTransaction(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null)
        {
            PokeIfAdded(context);
        }
    }

    private void PokeIfAdded(DbContext? context)
    {
        if (context is not null && _added.Remove(context))
        {
            signal.Poke();
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _added.Remove(context);
        }
    }
}
