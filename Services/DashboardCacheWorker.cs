using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADUserGroupManagerWeb.Services
{
    public class DashboardCacheWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DashboardCacheWorker> _logger;

        public DashboardCacheWorker(IServiceProvider serviceProvider, IMemoryCache cache, ILogger<DashboardCacheWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _cache = cache;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

                    // ✅ CORRECTO: El método se llama GetDashboardSummary
                    var summary = await dashboardService.GetDashboardSummary();

                    // ✅ CORRECTO: Uso de IMemoryCache, no de DashboardCache
                    _cache.Set("dashboard_summary", summary, TimeSpan.FromMinutes(5));

                    _logger.LogInformation("Dashboard summary cache actualizado correctamente.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error actualizando el caché del dashboard summary");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
