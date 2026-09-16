using JobAggregator.DataAccess.Data;
using Microsoft.EntityFrameworkCore;
namespace JobAggregator.BusinessLogic.Services;
public sealed class RunProcessingLock : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly string _key;
    private RunProcessingLock(AppDbContext db, string key) { _db = db; _key = key; }
    public static async Task<RunProcessingLock> AcquireAsync(AppDbContext db, Guid id)
    {
        var key = "process:" + id;
        await db.Database.OpenConnectionAsync();
        try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({key}, 0))"); }
        catch { await db.Database.CloseConnectionAsync(); throw; }
        return new RunProcessingLock(db, key);
    }
    public async ValueTask DisposeAsync()
    {
        try { await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({_key}, 0))"); }
        finally { await _db.Database.CloseConnectionAsync(); }
    }
}
