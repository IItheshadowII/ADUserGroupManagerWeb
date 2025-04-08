using System.Text.Json;
using ADUserGroupManagerWeb.Data;
using ADUserGroupManagerWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace ADUserGroupManagerWeb.Services
{
    public class DashboardCacheService
    {
        private readonly AppDbContext _db;

        public DashboardCacheService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<DashboardSummary?> GetCachedSummaryAsync()
        {
            var entity = await _db.DashboardSummaries.FirstOrDefaultAsync();
            if (entity == null) return null;

            return JsonSerializer.Deserialize<DashboardSummary>(entity.JsonData);
        }

        public async Task SaveSummaryAsync(DashboardSummary summary)
        {
            var json = JsonSerializer.Serialize(summary);
            var entity = await _db.DashboardSummaries.FirstOrDefaultAsync();

            if (entity == null)
            {
                _db.DashboardSummaries.Add(new DashboardSummaryCache
                {
                    JsonData = json,
                    LastUpdated = DateTime.Now
                });
            }
            else
            {
                entity.JsonData = json;
                entity.LastUpdated = DateTime.Now;
            }

            await _db.SaveChangesAsync();
        }
    }
}
