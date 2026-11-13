namespace Drover.Data.Repositories;

/// <summary>
/// A bot's schedules. They are stored here and run by the scheduler, which is not part of this repository.
/// </summary>
public sealed class BotScheduleRepository(DroverDbContext db)
{
    #region Public Methods

    public Task<List<BotSchedule>> ListSchedules(Guid botId, CancellationToken cancellationToken) =>
        db.BotSchedules.AsNoTracking()
            .Where(schedule => schedule.BotId == botId)
            .OrderBy(schedule => schedule.Id)
            .ToListAsync(cancellationToken);

    /// <summary>The schedule, tracked, so that a change to it is saved when the scope commits.</summary>
    public Task<BotSchedule?> GetScheduleForUpdate(Guid botId, Guid id, CancellationToken cancellationToken) =>
        db.BotSchedules.SingleOrDefaultAsync(schedule => schedule.Id == id && schedule.BotId == botId, cancellationToken);

    public void Add(BotSchedule schedule) => db.BotSchedules.Add(schedule);

    public void Remove(BotSchedule schedule) => db.BotSchedules.Remove(schedule);

    #endregion
}
