namespace ADUserGroupManagerWeb.Models
{
    public class CredentialAgeStats
    {
        public int ExpiredCount { get; set; }
        public int LessThan30Days { get; set; }
        public int Between30And90Days { get; set; }
        public int MoreThan90Days { get; set; }
        public object ChartData { get; set; }
    }
}
