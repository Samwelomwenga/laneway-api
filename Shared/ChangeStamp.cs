using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IStamped
{
    DateTime? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

public static class ChangeStamp
{
    public static void StampChange(this DbContext context, IStamped entity, Actor actor, bool relatedChanged = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(actor);

        if (!relatedChanged && context.Entry(entity).State != EntityState.Modified)
        {
            return;
        }

        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = actor.Id;
    }
}
