using System.Collections.Generic;

namespace ADUserGroupManagerWeb.Models
{
    public class UsersCreatedStats
    {
        public int CreatedToday { get; set; }
        public int CreatedThisMonth { get; set; }
        public Dictionary<string, int> MonthlyBreakdown { get; set; } = new Dictionary<string, int>();
        public object ChartData { get; set; }
    }
}
