using ADUserGroupManagerWeb.Models;
using Microsoft.EntityFrameworkCore;
using System;
using ADUserGroupManagerWeb.Data;

namespace ADUserGroupManagerWeb.Services
{
    public interface ISettingsService
    {
        Task<SystemSettings> GetSettingsAsync();
        Task SaveSettingsAsync(SystemSettings settings);
    }

    public class SettingsService : ISettingsService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<SettingsService> _logger;

        public SettingsService(AppDbContext db, ILogger<SettingsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<SystemSettings> GetSettingsAsync()
        {
            return await _db.Settings.FirstOrDefaultAsync() ?? new SystemSettings();
        }

        public async Task SaveSettingsAsync(SystemSettings settings)
        {
            var existing = await _db.Settings.FirstOrDefaultAsync();
            if (existing != null)
            {
                _db.Entry(existing).CurrentValues.SetValues(settings);
            }
            else
            {
                _db.Settings.Add(settings);
            }

            await _db.SaveChangesAsync();
        }
    }
}
