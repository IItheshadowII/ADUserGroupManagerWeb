namespace ADUserGroupManagerWeb.Services
{
    public interface IADService
    {
        Task<ClinicEnvironmentResult> CreateClinicEnvironment(string clinicName, string serverName, int userCount, string createdBy);
        Task<List<UserCredential>> CreateAdditionalUsers(string serverName, int userCount, string createdBy);
        Task<ClientQueryResult> GetClinicInfo(string serverCode);
        Task<UserQueryResult> GetUserInfo(string username);
        Task<bool> ResetLocalAdminPassword(string serverName, string newPassword);
        string GenerateSecurePassword();
        Task<bool> DisableUser(string username);
        Task<bool> EnableUser(string username);
        Task<bool> UnlockUser(string username);
        Task<bool> ChangeUserPassword(string username, string newPassword);
    }
}
