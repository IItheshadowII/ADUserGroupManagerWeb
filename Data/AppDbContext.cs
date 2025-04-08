using ADUserGroupManagerWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace ADUserGroupManagerWeb.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<SystemSettings> Settings { get; set; }
        public DbSet<DashboardSummaryCache> DashboardSummaries { get; set; }

    }


}
