// Models/DashboardSummaryCache.cs
using System;

namespace ADUserGroupManagerWeb.Models
{
    public class DashboardSummaryCache
    {
        public int Id { get; set; }
        public string JsonData { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
