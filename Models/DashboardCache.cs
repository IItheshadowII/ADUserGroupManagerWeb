using System;

namespace ADUserGroupManagerWeb.Models
{
    public class DashboardCache
    {
        public int Id { get; set; }

        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int LockedUsers { get; set; }

        public int TotalServers { get; set; }
        public int OnlineServers { get; set; }
        public int OfflineServers { get; set; }

        public int TotalClinics { get; set; }

        public int ActiveToday { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
