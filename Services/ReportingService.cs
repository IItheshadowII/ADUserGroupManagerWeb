using System.Net.Mail;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.Management;
using System.DirectoryServices;


namespace ADUserGroupManagerWeb.Services
{
    public class ReportingService : IReportingService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ReportingService> _logger;
        private readonly SheetsService _sheetsService;
        private readonly string _spreadsheetId;
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly string _emailFrom;
        private readonly bool _isGoogleSheetsEnabled;
        private readonly bool _isEmailEnabled;

        public ReportingService(IConfiguration config, ILogger<ReportingService> logger)
        {
            _config = config;
            _logger = logger;

            // Initialize Google Sheets settings
            _isGoogleSheetsEnabled = bool.Parse(_config["Reporting:GoogleSheets:Enabled"] ?? "false");
            _spreadsheetId = _config["Reporting:GoogleSheets:SpreadsheetId"];

            if (_isGoogleSheetsEnabled)
            {
                try
                {
                    string credentialsPath = _config["Reporting:GoogleSheets:CredentialsPath"];

                    if (File.Exists(credentialsPath))
                    {
                        // Initialize the Google Sheets service
                        var credential = GoogleCredential.FromFile(credentialsPath)
                            .CreateScoped(SheetsService.Scope.Spreadsheets);

                        _sheetsService = new SheetsService(new BaseClientService.Initializer()
                        {
                            HttpClientInitializer = credential,
                            ApplicationName = "ADUserGroupManager Web"
                        });

                        _logger.LogInformation("Google Sheets service initialized successfully");
                    }
                    else
                    {
                        _logger.LogWarning($"Google Sheets credentials file not found at: {credentialsPath}");
                        _isGoogleSheetsEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initializing Google Sheets service");
                    _isGoogleSheetsEnabled = false;
                }
            }

            // Initialize Email settings
            _isEmailEnabled = bool.Parse(_config["Reporting:Email:Enabled"] ?? "false");
            _smtpServer = _config["Reporting:Email:SmtpServer"];
            _smtpPort = int.Parse(_config["Reporting:Email:SmtpPort"] ?? "25");
            _smtpUsername = _config["Reporting:Email:Username"];
            _smtpPassword = _config["Reporting:Email:Password"];
            _emailFrom = _config["Reporting:Email:FromAddress"];
        }

        public async Task<bool> AddToGoogleSheets(ClinicEnvironmentResult result)
        {
            if (!_isGoogleSheetsEnabled || _sheetsService == null)
            {
                _logger.LogWarning("Google Sheets reporting is disabled or not configured");
                return false;
            }

            try
            {
                string sheetName = _config["Reporting:GoogleSheets:ClinicSheet"] ?? "Clinics";

                // Prepare rows for the new clinic environment
                var rows = new List<IList<object>>();

                // Add header row if sheet is empty
                var range = $"{sheetName}!A1:J1";
                var response = await _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, range).ExecuteAsync();

                if (response.Values == null || response.Values.Count == 0)
                {
                    // Add header row
                    rows.Add(new List<object>
                    {
                        "Fecha", "Código", "Cliente", "Servidor", "Usuario", "Contraseña",
                        "Grupo", "AnyDesk", "RDWeb", "Creado Por"
                    });
                }

                // Add a row for each user created
                foreach (var user in result.CreatedUsers)
                {
                    rows.Add(new List<object>
                    {
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        result.ServerName,
                        result.ClinicName,
                        result.ServerName,
                        user.Username,
                        user.Password,
                        result.RDSGroupName,
                        result.AnyDeskId ?? "N/A",
                        result.RDWebLink,
                        Thread.CurrentPrincipal?.Identity?.Name ?? "System"
                    });
                }

                // Append the rows to the sheet
                var valueRange = new ValueRange
                {
                    Values = rows
                };

                var appendRequest = _sheetsService.Spreadsheets.Values.Append(valueRange, _spreadsheetId, $"{sheetName}!A:J");
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                var appendResponse = await appendRequest.ExecuteAsync();

                _logger.LogInformation($"Added {rows.Count} rows to Google Sheets");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding data to Google Sheets");
                return false;
            }
        }

        public async Task<bool> AddUsersToGoogleSheets(string serverName, List<UserCredential> users)
        {
            if (!_isGoogleSheetsEnabled || _sheetsService == null)
            {
                _logger.LogWarning("Google Sheets reporting is disabled or not configured");
                return false;
            }

            try
            {
                string sheetName = _config["Reporting:GoogleSheets:ClinicSheet"] ?? "Clinics";

                // Get clinic information
                var clientInfo = await GetClinicInfo(serverName);

                // Prepare rows for the new users
                var rows = new List<IList<object>>();

                // Add a row for each user created
                foreach (var user in users)
                {
                    rows.Add(new List<object>
                    {
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        serverName,
                        clientInfo?.ClinicName ?? serverName,
                        serverName,
                        user.Username,
                        user.Password,
                        $"RDS-{serverName}",
                        clientInfo?.AnyDeskId ?? "N/A",
                        $"https://rdweb.domain.com/RDWeb/Feed/webfeed.aspx?username={user.Username}",
                        Thread.CurrentPrincipal?.Identity?.Name ?? "System"
                    });
                }

                // Append the rows to the sheet
                var valueRange = new ValueRange
                {
                    Values = rows
                };

                var appendRequest = _sheetsService.Spreadsheets.Values.Append(valueRange, _spreadsheetId, $"{sheetName}!A:J");
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                var appendResponse = await appendRequest.ExecuteAsync();

                _logger.LogInformation($"Added {rows.Count} users to Google Sheets");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding users to Google Sheets");
                return false;
            }
        }

        public async Task<bool> SendEmailReport(ClinicEnvironmentResult result, string recipient)
        {
            if (!_isEmailEnabled || string.IsNullOrEmpty(recipient))
            {
                _logger.LogWarning("Email reporting is disabled or recipient not specified");
                return false;
            }

            try
            {
                // Create mail message
                var mail = new MailMessage
                {
                    From = new MailAddress(_emailFrom),
                    Subject = $"Nuevo entorno creado: {result.ClinicName} ({result.ServerName})",
                    IsBodyHtml = true
                };

                mail.To.Add(recipient);

                // Build HTML body
                var body = new StringBuilder();
                body.AppendLine("<html><body>");
                body.AppendLine("<h2>Resumen de Creación de Entorno</h2>");
                body.AppendLine("<p><strong>Fecha:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</p>");
                body.AppendLine("<p><strong>Cliente:</strong> " + result.ClinicName + "</p>");
                body.AppendLine("<p><strong>Servidor:</strong> " + result.ServerName + "</p>");
                body.AppendLine("<p><strong>AnyDesk ID:</strong> " + (result.AnyDeskId ?? "N/A") + "</p>");
                body.AppendLine("<p><strong>RDWeb:</strong> <a href='" + result.RDWebLink + "'>" + result.RDWebLink + "</a></p>");

                body.AppendLine("<h3>Credenciales Creadas:</h3>");
                body.AppendLine("<table border='1' cellpadding='5' cellspacing='0'>");
                body.AppendLine("<tr><th>Usuario</th><th>Contraseña</th></tr>");

                foreach (var user in result.CreatedUsers)
                {
                    body.AppendLine("<tr>");
                    body.AppendLine("<td>" + user.Username + "</td>");
                    body.AppendLine("<td>" + user.Password + "</td>");
                    body.AppendLine("</tr>");
                }

                body.AppendLine("</table>");
                body.AppendLine("<p>Este correo fue generado automáticamente por ADUserGroupManager Web.</p>");
                body.AppendLine("</body></html>");

                mail.Body = body.ToString();

                // Send email
                using (var client = new SmtpClient(_smtpServer, _smtpPort))
                {
                    if (!string.IsNullOrEmpty(_smtpUsername) && !string.IsNullOrEmpty(_smtpPassword))
                    {
                        client.Credentials = new System.Net.NetworkCredential(_smtpUsername, _smtpPassword);
                        client.EnableSsl = true;
                    }

                    await client.SendMailAsync(mail);
                }

                _logger.LogInformation($"Sent email report to {recipient}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email report");
                return false;
            }
        }

        public async Task<bool> SendEmailUserReport(string serverName, List<UserCredential> users, string recipient)
        {
            if (!_isEmailEnabled || string.IsNullOrEmpty(recipient))
            {
                _logger.LogWarning("Email reporting is disabled or recipient not specified");
                return false;
            }

            try
            {
                // Get clinic information
                var clientInfo = await GetClinicInfo(serverName);

                // Create mail message
                var mail = new MailMessage
                {
                    From = new MailAddress(_emailFrom),
                    Subject = $"Usuarios adicionales creados: {clientInfo?.ClinicName ?? serverName} ({serverName})",
                    IsBodyHtml = true
                };

                mail.To.Add(recipient);

                // Build HTML body
                var body = new StringBuilder();
                body.AppendLine("<html><body>");
                body.AppendLine("<h2>Resumen de Creación de Usuarios</h2>");
                body.AppendLine("<p><strong>Fecha:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</p>");
                body.AppendLine("<p><strong>Cliente:</strong> " + (clientInfo?.ClinicName ?? serverName) + "</p>");
                body.AppendLine("<p><strong>Servidor:</strong> " + serverName + "</p>");

                body.AppendLine("<h3>Credenciales Creadas:</h3>");
                body.AppendLine("<table border='1' cellpadding='5' cellspacing='0'>");
                body.AppendLine("<tr><th>Usuario</th><th>Contraseña</th></tr>");

                foreach (var user in users)
                {
                    body.AppendLine("<tr>");
                    body.AppendLine("<td>" + user.Username + "</td>");
                    body.AppendLine("<td>" + user.Password + "</td>");
                    body.AppendLine("</tr>");
                }

                body.AppendLine("</table>");
                body.AppendLine("<p>Este correo fue generado automáticamente por ADUserGroupManager Web.</p>");
                body.AppendLine("</body></html>");

                mail.Body = body.ToString();

                // Send email
                using (var client = new SmtpClient(_smtpServer, _smtpPort))
                {
                    if (!string.IsNullOrEmpty(_smtpUsername) && !string.IsNullOrEmpty(_smtpPassword))
                    {
                        client.Credentials = new System.Net.NetworkCredential(_smtpUsername, _smtpPassword);
                        client.EnableSsl = true;
                    }

                    await client.SendMailAsync(mail);
                }

                _logger.LogInformation($"Sent user email report to {recipient}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending user email report");
                return false;
            }
        }

        #region Helper Methods

        private async Task<ClientInfo> GetClinicInfo(string serverName)
        {
            try
            {
                // Simple implementation to retrieve client name from OU
                var prodOUName = $"PROD_{serverName.ToUpper()}";
                var domainPath = _config["AD:DomainPath"] ?? "LDAP://localhost";
                var clinicOUPath = _config["AD:ClinicOUPath"] ?? "OU=Clinic," + domainPath;

                var prodOUPath = $"LDAP://OU={prodOUName},{clinicOUPath.Substring(clinicOUPath.IndexOf('O'))}"; // Remove "LDAP://"

                using (var ou = new DirectoryEntry(prodOUPath))
                {
                    return new ClientInfo
                    {
                        ClinicName = ou.Properties["description"].Value?.ToString(),
                        AnyDeskId = await GetAnyDeskId(serverName)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting clinic info for {serverName}");
                return null;
            }
        }

        private async Task<string> GetAnyDeskId(string computerName)
        {
            try
            {
                var options = new ConnectionOptions();
                var scope = new ManagementScope($"\\\\{computerName}\\root\\cimv2", options);

                try
                {
                    scope.Connect();
                }
                catch
                {
                    return null; // Computer not available
                }

                // Try to query registry for AnyDesk ID
                var classInstance = new ManagementClass(scope, new ManagementPath("StdRegProv"), null);

                // Parameters for registry access
                var inParams = classInstance.GetMethodParameters("GetStringValue");
                inParams["hDefKey"] = 0x80000002; // HKLM
                inParams["sSubKeyName"] = "SOFTWARE\\AnyDesk";
                inParams["sValueName"] = "ClientID";

                // Execute registry query
                var outParams = classInstance.InvokeMethod("GetStringValue", inParams, null);

                // Check if method was successful
                if ((uint)outParams["ReturnValue"] == 0 && outParams["sValue"] != null)
                {
                    return outParams["sValue"].ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting AnyDesk ID for {computerName}");
            }

            return null;
        }

        #endregion
    }

    public class ClientInfo
    {
        public string ClinicName { get; set; }
        public string AnyDeskId { get; set; }
    }

    public interface IReportingService
    {
        Task<bool> AddToGoogleSheets(ClinicEnvironmentResult result);
        Task<bool> AddUsersToGoogleSheets(string serverName, List<UserCredential> users);
        Task<bool> SendEmailReport(ClinicEnvironmentResult result, string recipient);
        Task<bool> SendEmailUserReport(string serverName, List<UserCredential> users, string recipient);
    }
}