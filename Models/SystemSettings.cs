using System.ComponentModel.DataAnnotations;

namespace ADUserGroupManagerWeb.Models
{
    public class SystemSettings
    {
        [Key]
        public int Id { get; set; }

        // Configuración general
        public string Environment { get; set; }

        // Configuración de Google Sheets
        public bool GoogleSheetsEnabled { get; set; }
        public string SpreadsheetId { get; set; }
        public string ClinicSheet { get; set; }

        // Configuración de Email
        public bool EmailEnabled { get; set; }
        public string SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string EmailUsername { get; set; }
        public string EmailPassword { get; set; }
        public string FromAddress { get; set; }

        // Información de auditoría
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        public string ModifiedBy { get; set; }
    }
}