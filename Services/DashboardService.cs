using ADUserGroupManagerWeb.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Threading.Tasks;

namespace ADUserGroupManagerWeb.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<DashboardService> _logger;
        private readonly string _clinicOUPath;
        private readonly string _serversOUPath;
        private readonly IADService _adService;
        private readonly IMemoryCache _cache;

        public DashboardService(IConfiguration config, IADService adService, ILogger<DashboardService> logger, IMemoryCache cache)
        {
            _config = config;
            _adService = adService;
            _logger = logger;
            _cache = cache;

            var domainDn = config["AD:DomainDN"] ?? "DC=localhost";
            var clinicOu = config["AD:ClinicOU"] ?? "OU=Clinic";
            var serversOu = config["AD:ServidoresOU"] ?? "OU=Servidores";

            _clinicOUPath = $"LDAP://{clinicOu},{domainDn}";
            _serversOUPath = $"LDAP://{serversOu},{domainDn}";
        }

        public async Task<DashboardSummary> GetDashboardSummary()
        {
            if (_cache.TryGetValue("DashboardSummaryCache", out DashboardSummary cachedSummary))
                return cachedSummary;

            var summary = new DashboardSummary
            {
                TotalUsers = await GetTotalUserCount(),
                TotalClinics = await GetTotalClinicCount(),
                TotalServers = await GetTotalServerCount(),
                ActiveUsers = await GetActiveUserCount(),
                LockedUsers = await GetLockedUserCount(),
                DisabledUsers = await GetDisabledUserCount(),
                ServersOnline = await GetOnlineServerCount(),
                ServersOffline = await GetTotalServerCount() - await GetOnlineServerCount()
            };

            _cache.Set("DashboardSummaryCache", summary, TimeSpan.FromMinutes(5));
            return summary;
        }

        public async Task<List<Alert>> GetAlerts()
        {
            var alerts = new List<Alert>();

            var expiredPasswords = await GetUsersWithExpiredPasswords();
            if (expiredPasswords.Any())
            {
                alerts.Add(new Alert
                {
                    Type = AlertType.Warning,
                    Message = $"{expiredPasswords.Count} users have expired passwords",
                    RelatedItems = expiredPasswords.Select(u => $"{u} has an expired password").ToList()
                });
            }

            var lockedAccounts = await GetLockedAccounts();
            if (lockedAccounts.Any())
            {
                alerts.Add(new Alert
                {
                    Type = AlertType.Warning,
                    Message = $"{lockedAccounts.Count} user accounts are locked",
                    RelatedItems = lockedAccounts.Select(u => $"{u} is locked").ToList()
                });
            }

            var offlineServers = await GetServersNotCheckedInRecently();
            if (offlineServers.Any())
            {
                alerts.Add(new Alert
                {
                    Type = AlertType.Error,
                    Message = $"{offlineServers.Count} servers haven't checked in recently",
                    RelatedItems = offlineServers.Select(s => $"{s.Name} last checked in {s.LastCheckin:g}").ToList()
                });
            }

            var expiringCerts = await GetExpiringCertificates();
            if (expiringCerts.Any())
            {
                alerts.Add(new Alert
                {
                    Type = AlertType.Warning,
                    Message = $"{expiringCerts.Count} SSL certificates are expiring soon",
                    RelatedItems = expiringCerts.Select(c => $"{c.Name} expires on {c.ExpirationDate:g}").ToList()
                });
            }

            return alerts;
        }

        public async Task<UsersCreatedStats> GetUsersCreatedStats()
        {
            var stats = new UsersCreatedStats();
            var today = DateTime.Today;
            var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(objectCategory=person)";
                searcher.PropertiesToLoad.Add("whenCreated");
                var allUsers = searcher.FindAll();

                foreach (SearchResult user in allUsers)
                {
                    if (user.Properties["whenCreated"].Count > 0)
                    {
                        DateTime createdDate = (DateTime)user.Properties["whenCreated"][0];

                        if (createdDate.Date == today)
                            stats.CreatedToday++;

                        if (createdDate >= firstDayOfMonth)
                            stats.CreatedThisMonth++;

                        var monthKey = $"{createdDate.Year}-{createdDate.Month:D2}";
                        if (!stats.MonthlyBreakdown.ContainsKey(monthKey))
                            stats.MonthlyBreakdown[monthKey] = 0;
                        stats.MonthlyBreakdown[monthKey]++;
                    }
                }

                stats.ChartData = stats.MonthlyBreakdown.OrderBy(kvp => kvp.Key)
                    .Select(kvp => new { month = kvp.Key, count = kvp.Value }).ToList();

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users created stats");
                throw;
            }
        }

        public async Task<CredentialAgeStats> GetCredentialAgeStats()
        {
            _logger.LogInformation("Inicio GetCredentialAgeStats");

            var stats = new CredentialAgeStats();
            var today = DateTime.Today;
            var thirtyDaysAgo = today.AddDays(-30);
            var ninetyDaysAgo = today.AddDays(-90);
            var maxPasswordAge = GetMaxPasswordAge();

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(objectCategory=person)";
                searcher.PropertiesToLoad.Add("pwdLastSet");
                var allUsers = searcher.FindAll();

                foreach (SearchResult user in allUsers)
                {
                    if (user.Properties["pwdLastSet"].Count > 0)
                    {
                        long pwdLastSet = (long)user.Properties["pwdLastSet"][0];
                        if (pwdLastSet > 0)
                        {
                            DateTime lastSet = DateTime.FromFileTime(pwdLastSet);
                            TimeSpan age = today - lastSet;

                            if (maxPasswordAge > 0 && age.TotalDays > maxPasswordAge)
                                stats.ExpiredCount++;
                            else if (lastSet >= thirtyDaysAgo)
                                stats.LessThan30Days++;
                            else if (lastSet >= ninetyDaysAgo)
                                stats.Between30And90Days++;
                            else
                                stats.MoreThan90Days++;
                        }
                    }
                }

                stats.ChartData = new List<object>
                {
                    new { category = "Expired", count = stats.ExpiredCount },
                    new { category = "< 30 days", count = stats.LessThan30Days },
                    new { category = "30-90 days", count = stats.Between30And90Days },
                    new { category = "> 90 days", count = stats.MoreThan90Days }
                };

                _logger.LogInformation("Fin GetCredentialAgeStats");
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting credential age stats");
                throw;
            }
        }

        // Métodos auxiliares privados:

        private async Task<int> GetTotalUserCount()
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(objectCategory=person)";
                var users = searcher.FindAll();
                return users.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total user count");
                return 0;
            }
        }

        private async Task<int> GetTotalClinicCount()
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(objectCategory=organizationalUnit)";
                var clinics = searcher.FindAll();
                return clinics.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total clinic count");
                return 0;
            }
        }

        private async Task<int> GetTotalServerCount()
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_serversOUPath));
                searcher.Filter = "(objectCategory=computer)";
                var servers = searcher.FindAll();
                return servers.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total server count");
                return 0;
            }
        }

        private async Task<int> GetActiveUserCount()
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(&(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(!(userAccountControl:1.2.840.113556.1.4.803:=16)))";
                var users = searcher.FindAll();
                return users.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active user count");
                return 0;
            }
        }

        private async Task<int> GetLockedUserCount()
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(&(objectCategory=person)(userAccountControl:1.2.840.113556.1.4.803:=16))";
                var users = searcher.FindAll();
                return users.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting locked user count");
                return 0;
            }
        }

        private async Task<int> GetDisabledUserCount()
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(&(objectCategory=person)(userAccountControl:1.2.840.113556.1.4.803:=2))";
                var users = searcher.FindAll();
                return users.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting disabled user count");
                return 0;
            }
        }

        private async Task<int> GetOnlineServerCount()
        {
            try
            {
                var servers = await GetServersWithLastCheckIn();
                var onlineCount = servers.Count(s => (DateTime.Now - s.LastCheckin).TotalHours < 24);
                return onlineCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting online server count");
                return 0;
            }
        }

        private async Task<List<string>> GetUsersWithExpiredPasswords()
        {
            var expiredUsers = new List<string>();
            var maxPasswordAge = GetMaxPasswordAge();
            var today = DateTime.Today;

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(objectCategory=person)";
                searcher.PropertiesToLoad.Add("pwdLastSet");
                searcher.PropertiesToLoad.Add("sAMAccountName");
                var allUsers = searcher.FindAll();

                foreach (SearchResult user in allUsers)
                {
                    if (user.Properties["pwdLastSet"].Count > 0 && user.Properties["sAMAccountName"].Count > 0)
                    {
                        long pwdLastSet = (long)user.Properties["pwdLastSet"][0];
                        string username = user.Properties["sAMAccountName"][0].ToString();

                        if (pwdLastSet > 0)
                        {
                            DateTime lastSet = DateTime.FromFileTime(pwdLastSet);
                            TimeSpan age = today - lastSet;

                            if (maxPasswordAge > 0 && age.TotalDays > maxPasswordAge)
                                expiredUsers.Add(username);
                        }
                    }
                }

                return expiredUsers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users with expired passwords");
                return new List<string>();
            }
        }

        private async Task<List<string>> GetLockedAccounts()
        {
            var lockedUsers = new List<string>();

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(&(objectCategory=person)(userAccountControl:1.2.840.113556.1.4.803:=16))";
                searcher.PropertiesToLoad.Add("sAMAccountName");
                var allUsers = searcher.FindAll();

                foreach (SearchResult user in allUsers)
                {
                    if (user.Properties["sAMAccountName"].Count > 0)
                    {
                        string username = user.Properties["sAMAccountName"][0].ToString();
                        lockedUsers.Add(username);
                    }
                }

                return lockedUsers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting locked accounts");
                return new List<string>();
            }
        }

        private async Task<List<ServerStatus>> GetServersWithLastCheckIn()
        {
            var servers = new List<ServerStatus>();

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_serversOUPath));
                searcher.Filter = "(objectCategory=computer)";
                searcher.PropertiesToLoad.Add("name");
                searcher.PropertiesToLoad.Add("lastLogonTimestamp");
                var allServers = searcher.FindAll();

                foreach (SearchResult server in allServers)
                {
                    if (server.Properties["name"].Count > 0)
                    {
                        string name = server.Properties["name"][0].ToString();
                        DateTime lastCheckin = DateTime.Now.AddDays(-1);

                        if (server.Properties["lastLogonTimestamp"].Count > 0)
                        {
                            long timestamp = (long)server.Properties["lastLogonTimestamp"][0];
                            lastCheckin = DateTime.FromFileTime(timestamp);
                        }

                        servers.Add(new ServerStatus
                        {
                            Name = name,
                            LastCheckin = lastCheckin,
                            IsOnline = (DateTime.Now - lastCheckin).TotalHours < 24
                        });
                    }
                }

                return servers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting servers with last check-in");
                return new List<ServerStatus>();
            }
        }

        private async Task<List<ServerStatus>> GetServersNotCheckedInRecently()
        {
            try
            {
                var allServers = await GetServersWithLastCheckIn();
                return allServers.Where(s => (DateTime.Now - s.LastCheckin).TotalHours >= 24).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting servers not checked in recently");
                return new List<ServerStatus>();
            }
        }

        private async Task<List<CertificateStatus>> GetExpiringCertificates()
        {
            var certificates = new List<CertificateStatus>();

            try
            {
                certificates.Add(new CertificateStatus
                {
                    Name = "example.com",
                    ExpirationDate = DateTime.Now.AddDays(30)
                });

                certificates.Add(new CertificateStatus
                {
                    Name = "api.example.com",
                    ExpirationDate = DateTime.Now.AddDays(45)
                });

                return certificates.Where(c => (c.ExpirationDate - DateTime.Now).TotalDays <= 60).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting expiring certificates");
                return new List<CertificateStatus>();
            }
        }

        private int GetMaxPasswordAge()
        {
            try
            {
                return 90;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting max password age");
                return 90;
            }
        }
    }
}
