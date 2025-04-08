using ADUserGroupManagerWeb.Models;
using ADUserGroupManagerWeb.Services;
using ADUserGroupManagerWeb.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ADUserGroupManagerWeb.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IADRoleProvider _roleProvider;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            ISettingsService settingsService,
            IADRoleProvider roleProvider,
            ILogger<SettingsController> logger)
        {
            _settingsService = settingsService;
            _roleProvider = roleProvider;
            _logger = logger;
        }

        [HttpGet]
        [RequirePermission("ModifySettings")]
        public async Task<IActionResult> GetSettings()
        {
            if (!_roleProvider.IsAdministrator(User))
            {
                return Forbid();
            }

            try
            {
                var dbSettings = await _settingsService.GetSettingsAsync();

                var dto = new SettingsDto
                {
                    Environment = dbSettings.Environment,
                    GoogleSheets = new GoogleSheetsSettings
                    {
                        Enabled = dbSettings.GoogleSheetsEnabled,
                        SpreadsheetId = dbSettings.SpreadsheetId,
                        ClinicSheet = dbSettings.ClinicSheet
                    },
                    Email = new EmailSettings
                    {
                        Enabled = dbSettings.EmailEnabled,
                        SmtpServer = dbSettings.SmtpServer,
                        SmtpPort = dbSettings.SmtpPort,
                        Username = dbSettings.EmailUsername,
                        Password = dbSettings.EmailPassword,
                        FromAddress = dbSettings.FromAddress
                    }
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting settings from database");
                return StatusCode(500, new { message = "An error occurred while getting settings" });
            }
        }

        [HttpPost]
        [RequirePermission("ModifySettings")]
        public async Task<IActionResult> SaveSettings([FromBody] SettingsDto settings)
        {
            if (!_roleProvider.IsAdministrator(User))
            {
                return Forbid();
            }

            try
            {
                var dbSettings = new SystemSettings
                {
                    Environment = settings.Environment,
                    GoogleSheetsEnabled = settings.GoogleSheets.Enabled,
                    SpreadsheetId = settings.GoogleSheets.SpreadsheetId,
                    ClinicSheet = settings.GoogleSheets.ClinicSheet,
                    EmailEnabled = settings.Email.Enabled,
                    SmtpServer = settings.Email.SmtpServer,
                    SmtpPort = settings.Email.SmtpPort,
                    EmailUsername = settings.Email.Username,
                    EmailPassword = settings.Email.Password,
                    FromAddress = settings.Email.FromAddress
                };

                await _settingsService.SaveSettingsAsync(dbSettings);

                _logger.LogInformation("Settings updated by user: {User}", User?.Identity?.Name ?? "Unknown");

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings");
                return StatusCode(500, new { message = "An error occurred while saving settings" });
            }
        }
    }

    public class SettingsDto
    {
        public string Environment { get; set; }
        public GoogleSheetsSettings GoogleSheets { get; set; }
        public EmailSettings Email { get; set; }
    }

    public class GoogleSheetsSettings
    {
        public bool Enabled { get; set; }
        public string SpreadsheetId { get; set; }
        public string ClinicSheet { get; set; }
    }

    public class EmailSettings
    {
        public bool Enabled { get; set; }
        public string SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string FromAddress { get; set; }
    }
}