namespace ADUserGroupManagerWeb.Models
{
    public class DashboardSummary
    {
        public int TotalUsers { get; set; }
        public int TotalClinics { get; set; }
        public int TotalServers { get; set; }
        public int ActiveUsers { get; set; }
        public int LockedUsers { get; set; }
        public int DisabledUsers { get; set; }
        public int ServersOnline { get; set; }
        public int ServersOffline { get; set; }
    }
}
