using ADUserGroupManagerWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ADUserGroupManagerWeb.Authorization;

namespace ADUserGroupManagerWeb.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ClinicController : ControllerBase
    {
        private readonly IADService _adService;
        private readonly IReportingService _reportingService;
        private readonly IADRoleProvider _roleProvider;
        private readonly ILogger<ClinicController> _logger;

        public ClinicController(
            IADService adService,
            IReportingService reportingService,
            IADRoleProvider roleProvider,
            ILogger<ClinicController> logger)
        {
            _adService = adService;
            _reportingService = reportingService;
            _roleProvider = roleProvider;
            _logger = logger;
        }

        [HttpPost("create-environment")]
        [RequirePermission("CreateClinicEnvironment")]
        public async Task<IActionResult> CreateEnvironment([FromBody] CreateEnvironmentRequest request)
        {
            if (!_roleProvider.CanCreateClinicEnvironment(User))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.ClinicName) || string.IsNullOrWhiteSpace(request.ServerName))
                return BadRequest(new { message = "ClinicName and ServerName are required" });

            var username = User.Identity?.Name ?? "SYSTEM";

            try
            {
                // Ejecuta toda la lógica "Do All" en un solo llamado
                var result = await _adService.CreateClinicEnvironment(
                    request.ClinicName,
                    request.ServerName,
                    request.UserCount,
                    username);

                // Opcional: resetear contraseña del admin local
                if (request.ResetAdminPassword)
                {
                    var newPass = _adService.GenerateSecurePassword();
                    if (await _adService.ResetLocalAdminPassword(request.ServerName, newPass))
                        result.AdminPassword = newPass;
                }

                // Opcional: Google Sheets + email
                if (request.SendToGoogleSheets)
                    await _reportingService.AddToGoogleSheets(result);

                if (request.SendEmail)
                    await _reportingService.SendEmailReport(result, request.EmailRecipient);

                _logger.LogInformation($"Entorno de clínica {request.ClinicName} creado por {username}");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating environment for {request.ServerName}");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("create-users")]
        [RequirePermission("CreateUsers")]
        public async Task<IActionResult> CreateUsers([FromBody] CreateUsersRequest request)
        {
            if (!_roleProvider.CanCreateUsers(User))
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(request.ServerName) || request.UserCount <= 0)
            {
                return BadRequest(new { message = "Server name and user count are required" });
            }

            // Get the current user
            var username = User.Identity.Name;

            try
            {
                var result = await _adService.CreateAdditionalUsers(
                    request.ServerName,
                    request.UserCount,
                    username);

                // Send to reporting services if enabled
                if (request.SendToGoogleSheets)
                {
                    await _reportingService.AddUsersToGoogleSheets(request.ServerName, result);
                }

                if (request.SendEmail)
                {
                    await _reportingService.SendEmailUserReport(request.ServerName, result, request.EmailRecipient);
                }

                _logger.LogInformation($"{result.Count} usuarios adicionales creados para {request.ServerName} por {username}");
                return Ok(new { users = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating additional users");
                return StatusCode(500, new { message = "An error occurred while creating additional users" });
            }
        }

        [HttpGet("query/{serverCode}")]
        [RequirePermission("ViewDashboard")]
        public async Task<IActionResult> QueryClinic(string serverCode)
        {
            if (string.IsNullOrEmpty(serverCode))
            {
                return BadRequest(new { message = "Server code is required" });
            }

            try
            {
                var result = await _adService.GetClinicInfo(serverCode);

                if (result == null)
                {
                    return NotFound(new { message = $"Clinic with server code {serverCode} not found" });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error querying clinic {serverCode}");
                return StatusCode(500, new { message = "An error occurred while querying the clinic" });
            }
        }

        [HttpPost("reset-admin-password")]
        [RequirePermission("ResetPasswords")]
        public async Task<IActionResult> ResetAdminPassword([FromBody] ResetAdminPasswordRequest request)
        {
            if (!_roleProvider.HasPermission(User, "ResetPasswords"))
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(request.ServerName) || string.IsNullOrEmpty(request.NewPassword))
            {
                return BadRequest(new { message = "Server name and new password are required" });
            }

            try
            {
                var result = await _adService.ResetLocalAdminPassword(request.ServerName, request.NewPassword);

                if (!result)
                {
                    return StatusCode(500, new { message = "An error occurred while resetting the admin password" });
                }

                _logger.LogInformation($"Contraseña de administrador local restablecida para {request.ServerName} por {User.Identity.Name}");
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting admin password for {request.ServerName}");
                return StatusCode(500, new { message = "An error occurred while resetting the admin password" });
            }
        }
    }

    public class CreateEnvironmentRequest
    {
        public bool ResetAdminPassword { get; set; }
        public string ClinicName { get; set; }
        public string ServerName { get; set; }
        public int UserCount { get; set; } = 3;
        public bool SendToGoogleSheets { get; set; } = false;
        public bool SendEmail { get; set; } = false;
        public string EmailRecipient { get; set; }
    }

    public class CreateUsersRequest
    {
        public string ServerName { get; set; }
        public int UserCount { get; set; } = 1;
        public bool SendToGoogleSheets { get; set; } = false;
        public bool SendEmail { get; set; } = false;
        public string EmailRecipient { get; set; }
    }

    public class ResetAdminPasswordRequest
    {
        public string ServerName { get; set; }
        public string NewPassword { get; set; }
    }
}