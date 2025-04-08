using System.Collections.Generic;
using System.Threading.Tasks;
using ADUserGroupManagerWeb.Models;

namespace ADUserGroupManagerWeb.Services
{
    public interface IDashboardService
    {
        Task<DashboardSummary> GetDashboardSummary();
        Task<List<Alert>> GetAlerts();
        Task<UsersCreatedStats> GetUsersCreatedStats();
        Task<CredentialAgeStats> GetCredentialAgeStats();
    }
}
