using System.Collections.Generic;

namespace ADUserGroupManagerWeb.Models
{
    public enum AlertType
    {
        Info,
        Warning,
        Error
    }

    public class Alert
    {
        public AlertType Type { get; set; }
        public string Message { get; set; }
        public List<string> RelatedItems { get; set; } = new List<string>();
    }
}
