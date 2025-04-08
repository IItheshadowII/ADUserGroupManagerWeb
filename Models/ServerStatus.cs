namespace ADUserGroupManagerWeb.Models
{
    public class ServerStatus
    {
        public string Name { get; set; }
        public DateTime LastCheckin { get; set; }
        public bool IsOnline { get; set; }
    }
}
