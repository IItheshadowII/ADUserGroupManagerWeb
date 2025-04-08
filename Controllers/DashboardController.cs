using ADUserGroupManagerWeb.Services;
using ADUserGroupManagerWeb.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ADUserGroupManagerWeb.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    [RequirePermission("ViewDashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;
        private readonly IADRoleProvider _roleProvider;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IDashboardService dashboardService,
            IADRoleProvider roleProvider,
            ILogger<DashboardController> logger)
        {
            _dashboardService = dashboardService;
            _roleProvider = roleProvider;
            _logger = logger;
        }

        [HttpGet("alerts")]
        public async Task<IActionResult> GetAlerts()
        {
            if (!_roleProvider.HasPermission(User, "ViewDashboard"))
            {
                return Forbid();
            }

            try
            {
                var result = await _dashboardService.GetAlerts();
                return Ok(new { alerts = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alerts");
                return StatusCode(500, new { message = "An error occurred while getting alerts" });
            }
        }

        [HttpGet("users-created-stats")]
        public async Task<IActionResult> GetUsersCreatedStats()
        {
            if (!_roleProvider.HasPermission(User, "ViewDashboard"))
            {
                return Forbid();
            }

            try
            {
                var result = await _dashboardService.GetUsersCreatedStats();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users created stats");
                return StatusCode(500, new { message = "An error occurred while getting users created stats" });
            }
        }

        [HttpGet("credential-age-stats")]
        public async Task<IActionResult> GetCredentialAgeStats()
        {
            if (!_roleProvider.HasPermission(User, "ViewDashboard"))
            {
                return Forbid();
            }

            try
            {
                var result = await _dashboardService.GetCredentialAgeStats();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting credential age stats");
                return StatusCode(500, new { message = "An error occurred while getting credential age stats" });
            }
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            if (!_roleProvider.HasPermission(User, "ViewDashboard"))
            {
                return Forbid();
            }

            try
            {
                var summary = await _dashboardService.GetDashboardSummary();
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard summary");
                return StatusCode(500, new { message = "An error occurred while getting dashboard summary" });
            }
        }
    }
}