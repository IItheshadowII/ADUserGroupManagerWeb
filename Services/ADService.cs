using ADUserGroupManagerWeb.Models;
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Security.Cryptography;
using System.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using ADUserGroupManagerWeb.Models;

namespace ADUserGroupManagerWeb.Services
{
    public class ADService : IADService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ADService> _logger;
        private readonly string _domainPath;
        private readonly string _clinicOUPath;
        private readonly string _serversOUPath;
        private readonly string _groupsOUPath;
        private readonly string _computerOUPath;

        public ADService(IConfiguration config, ILogger<ADService> logger)
        {
            _config = config;
            _logger = logger;

            // Read paths from configuration
            _domainPath = _config["AD:DomainPath"] ?? "LDAP://DC=labs,DC=local";
            _clinicOUPath = _config["AD:ClinicOUPath"] ?? $"{_domainPath.TrimEnd('/')}";
            _serversOUPath = _config["AD:ServersOUPath"] ?? "OU=Servidores," + _domainPath;
            _groupsOUPath = _config["AD:GroupsOUPath"] ?? "OU=Grupos," + _domainPath;
            _computerOUPath = _config["AD:ComputersOUPath"] ?? "OU=Computers," + _domainPath;
        }

        public async Task<ClinicEnvironmentResult> CreateClinicEnvironment(string clinicName, string serverName, int userCount, string createdBy)
        {
            var result = new ClinicEnvironmentResult
            {
                ClinicName = clinicName,
                ServerName = serverName,
                CreatedUsers = new List<UserCredential>()
            };

            try
            {
                // 1. Create the OU PROD_{ServerName} inside OU=Clinic
                string prodOUName = $"PROD_{serverName.ToUpper()}";
                string prodOUPath = await CreateOrganizationalUnit(prodOUName, _clinicOUPath);
                result.ProductionOUPath = prodOUPath;

                // 2. Create the OU Cloud_{ServerName} inside OU=Servidores
                string cloudOUName = $"Cloud_{serverName.ToUpper()}";
                string cloudOUPath = await CreateOrganizationalUnit(cloudOUName, _serversOUPath);
                result.CloudOUPath = cloudOUPath;

                // 3. Move computer from Computers OU to Cloud OU
                await MoveComputer(serverName, cloudOUPath);

                // 4. Create RDS group
                string rdsGroupName = $"RDS-{serverName.ToUpper()}";
                await CreateGroup(rdsGroupName, _groupsOUPath, clinicName, createdBy);
                result.RDSGroupName = rdsGroupName;

                // 5. Create users and add them to the RDS group
                string prefix = serverName.Substring(0, Math.Min(3, serverName.Length)).ToLower();
                for (int i = 1; i <= userCount; i++)
                {
                    string username = $"{prefix}{i}";
                    string password = GenerateSecurePassword();

                    await CreateUser(username, password, prodOUPath, clinicName, createdBy, rdsGroupName);

                    result.CreatedUsers.Add(new UserCredential
                    {
                        Username = username,
                        Password = password
                    });
                }

                // 6. Get AnyDesk ID if available
                result.AnyDeskId = await GetAnyDeskId(serverName);

                _logger.LogInformation($"Successfully created clinic environment for {clinicName} ({serverName})");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating clinic environment for {clinicName} ({serverName})");
                throw;
            }
        }

        public async Task<List<UserCredential>> CreateAdditionalUsers(string serverName, int userCount, string createdBy)
        {
            try
            {
                // Find the existing OU and RDS group
                string prodOUName = $"PROD_{serverName.ToUpper()}";
                string prodOUPath = $"OU={prodOUName},{_clinicOUPath.Substring(_clinicOUPath.IndexOf('O'))}";
                string rdsGroupName = $"RDS-{serverName.ToUpper()}";

                // Find the clinic name (from existing description)
                string clinicName = await GetClinicNameFromOU(prodOUPath);

                // Find last user index
                int lastIndex = GetLastUserIndex(prodOUPath, serverName);

                var createdUsers = new List<UserCredential>();
                string prefix = serverName.Substring(0, Math.Min(3, serverName.Length)).ToLower();

                for (int i = lastIndex + 1; i <= lastIndex + userCount; i++)
                {
                    string username = $"{prefix}{i}";
                    string password = GenerateSecurePassword();

                    await CreateUser(username, password, prodOUPath, clinicName, createdBy, rdsGroupName);

                    createdUsers.Add(new UserCredential
                    {
                        Username = username,
                        Password = password
                    });
                }

                _logger.LogInformation($"Created {userCount} additional users for {serverName}");
                return createdUsers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating additional users for {serverName}");
                throw;
            }
        }

        public async Task<ClientQueryResult> GetClinicInfo(string serverCode)
        {
            var result = new ClientQueryResult { ServerCode = serverCode };
            string prodOUName = $"PROD_{serverCode.ToUpper()}";

            // Busco la OU PROD_<serverCode> en todo el dominio
            using (var root = new DirectoryEntry(_domainPath))
            using (var searcher = new DirectorySearcher(root))
            {
                searcher.Filter = $"(&(objectCategory=organizationalUnit)(ou={prodOUName}))";
                searcher.SearchScope = SearchScope.Subtree;

                var ouResult = searcher.FindOne();
                if (ouResult != null)
                {
                    var prodOU = ouResult.GetDirectoryEntry();
                    prodOU.RefreshCache(new[] { "info", "description", "whenCreated" });

                    result.CreatedBy = prodOU.Properties["info"]?.Count > 0
                        ? prodOU.Properties["info"][0]?.ToString()
                        : "Unknown";
                    result.ClinicName = prodOU.Properties["description"]?.Count > 0
                        ? prodOU.Properties["description"][0]?.ToString()
                        : null;
                    result.CreationDate = GetOUCreationDate(prodOU);

                    using var userSearcher = new DirectorySearcher(prodOU) { Filter = "(objectCategory=person)" };
                    result.UserCount = userSearcher.FindAll().Count;
                }
                else
                {
                    _logger.LogWarning($"OU {prodOUName} no encontrada en el dominio");
                }
            }

            // Busco el equipo por nombre para obtener OU actual + último reboot
            using (var searcher = new DirectorySearcher(new DirectoryEntry(_domainPath)))
            {
                searcher.Filter = $"(&(objectCategory=computer)(name={serverCode}))";
                var serverResult = searcher.FindOne();
                if (serverResult != null)
                {
                    var dn = serverResult.Properties["distinguishedName"][0].ToString();
                    result.CurrentComputerOU = ExtractOUFromDN(dn);
                    result.LastReboot = await GetLastRebootTime(serverCode);
                }
            }

            return result;
        }

        public async Task<UserQueryResult> GetUserInfo(string username)
        {
            var result = new UserQueryResult { Username = username };

            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
                    if (user == null)
                    {
                        _logger.LogWarning($"User {username} not found.");
                        return null;
                    }

                    result.FullName = user.DisplayName;
                    result.Email = user.EmailAddress;
                    result.IsLocked = user.IsAccountLockedOut();
                    result.PasswordLastSet = user.LastPasswordSet ?? DateTime.MinValue;

                    using (DirectoryEntry entry = new DirectoryEntry($"LDAP://{user.DistinguishedName}"))
                    {
                        entry.RefreshCache(new[] { "userAccountControl", "memberOf", "info", "description" });

                        // Log flags
                        var flagsObj = entry.Properties["userAccountControl"]?.Value;
                        if (flagsObj != null)
                        {
                            int flags = (int)flagsObj;
                            _logger.LogInformation($"userAccountControl for {username}: {flags}");
                            result.IsDisabled = (flags & 0x2) != 0;
                        }
                        else
                        {
                            _logger.LogWarning($"userAccountControl property is null for {username}");
                        }

                        result.Groups = new List<string>();
                        if (entry.Properties["memberOf"] != null)
                        {
                            foreach (var group in entry.Properties["memberOf"])
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(group.ToString(), @"CN=([^,]+)");
                                if (match.Success)
                                {
                                    result.Groups.Add(match.Groups[1].Value);
                                }
                            }
                            _logger.LogInformation($"Groups for {username}: {string.Join(", ", result.Groups)}");
                        }
                        else
                        {
                            _logger.LogWarning($"memberOf property is null for {username}");
                        }

                        if (entry.Properties["info"].Count > 0)
                            result.CreatedBy = entry.Properties["info"][0]?.ToString();

                        if (entry.Properties["description"].Count > 0)
                            result.Description = entry.Properties["description"][0]?.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving info for user {username}");
            }

            return result;
        }






        public async Task<bool> ResetLocalAdminPassword(string serverName, string newPassword)
        {
            try
            {
                var options = new ConnectionOptions
                {
                    Username = _config["AD:AdminUsername"],
                    Password = _config["AD:AdminPassword"]
                };

                var scope = new ManagementScope($"\\\\{serverName}\\root\\cimv2", options);
                scope.Connect();

                var query = new SelectQuery("SELECT * FROM Win32_UserAccount WHERE LocalAccount=True AND Name='Administrator'");
                var searcher = new ManagementObjectSearcher(scope, query);
                var accounts = searcher.Get();

                foreach (ManagementObject account in accounts)
                {
                    var parameters = account.GetMethodParameters("SetPassword");
                    parameters["Password"] = newPassword;
                    account.InvokeMethod("SetPassword", parameters, null);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting local admin password for {serverName}");
                return false;
            }
        }

        public string GenerateSecurePassword()
        {
            string[] words = _config.GetSection("PasswordGenerator:Words").Get<string[]>() ??
                new string[]
                {
                    "Table", "Chair", "Lamp", "Book", "Phone", "Desk", "Window", "House",
                    "Tree", "Paper", "Door", "Floor", "Water", "Coffee", "Music", "Mountain",
                    "River", "Ocean", "Cloud", "Flower", "Garden", "School", "Office", "Market",
                    "Store", "Glass", "Bottle", "Plate", "Spoon", "Fork", "Knife", "Bread",
                    "Cheese", "Apple", "Orange", "Banana", "Carrot", "Potato", "Tomato", "Onion",
                    "Pepper", "Sugar", "Sunshine", "Rainbow", "Thunder", "Planet", "Galaxy", "Universe"
                };

            char[] symbols = new char[] { '*', '-', '.' };

            using (var rng = RandomNumberGenerator.Create())
            {
                string[] selectedWords = new string[3];
                byte[] randomBytes = new byte[4];

                for (int i = 0; i < 3; i++)
                {
                    rng.GetBytes(randomBytes);
                    int index = Math.Abs(BitConverter.ToInt32(randomBytes, 0)) % words.Length;
                    selectedWords[i] = words[index];
                }

                rng.GetBytes(randomBytes);
                int symbolIndex1 = Math.Abs(BitConverter.ToInt32(randomBytes, 0)) % symbols.Length;

                rng.GetBytes(randomBytes);
                int symbolIndex2 = Math.Abs(BitConverter.ToInt32(randomBytes, 0)) % symbols.Length;

                return $"{selectedWords[0]}{symbols[symbolIndex1]}{selectedWords[1]}{symbols[symbolIndex2]}{selectedWords[2]}";
            }
        }

        // ---- MÉTODOS HELPER ---- //

        private async Task<string> CreateOrganizationalUnit(string ouName, string parentPath)
        {
            using var parentOU = new DirectoryEntry(parentPath);
            using var searcher = new DirectorySearcher(parentOU) { Filter = $"(ou={ouName})" };
            var existing = searcher.FindOne();
            if (existing != null) return existing.Path;

            using var newOU = parentOU.Children.Add($"OU={ouName}", "organizationalUnit");
            newOU.CommitChanges();
            return newOU.Path;
        }

        private async Task MoveComputer(string computerName, string destinationOUPath)
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_domainPath));
                searcher.Filter = $"(&(objectCategory=computer)(name={computerName}))";
                var result = searcher.FindOne();

                if (result != null)
                {
                    var computer = result.GetDirectoryEntry();
                    var destinationOU = new DirectoryEntry(destinationOUPath);
                    computer.MoveTo(destinationOU);
                    _logger.LogInformation($"Moved computer {computerName} to {destinationOUPath}");
                }
                else
                {
                    _logger.LogWarning($"Computer {computerName} not found in the domain");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error moving computer {computerName}");
                throw;
            }
        }

        private async Task CreateGroup(string groupName, string ouPath, string description, string createdBy)
        {
            using var ou = new DirectoryEntry(ouPath);
            try
            {
                using var searcher = new DirectorySearcher(ou) { Filter = $"(cn={groupName})" };
                var result = searcher.FindOne();
                if (result != null)
                {
                    _logger.LogInformation($"Group {groupName} already exists");
                    return;
                }

                using var newGroup = ou.Children.Add($"CN={groupName}", "group");
                newGroup.Properties["sAMAccountName"].Value = groupName;
                newGroup.Properties["groupType"].Value = -2147483646; // Global security group

                if (!string.IsNullOrEmpty(description))
                {
                    newGroup.Properties["description"].Value = description;
                }

                if (!string.IsNullOrEmpty(createdBy))
                {
                    newGroup.Properties["info"].Value = createdBy;
                }

                newGroup.CommitChanges();
                _logger.LogInformation($"Created group {groupName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating group {groupName}");
                throw;
            }
        }

        private async Task CreateUser(string username, string password, string ouPath, string description, string createdBy, string groupName)
        {
            using var ou = new DirectoryEntry(ouPath);
            try
            {
                using var searcher = new DirectorySearcher(ou) { Filter = $"(sAMAccountName={username})" };
                var result = searcher.FindOne();
                if (result != null)
                {
                    _logger.LogInformation($"User {username} already exists");
                    return;
                }

                using var newUser = ou.Children.Add($"CN={username}", "user");
                newUser.Properties["sAMAccountName"].Value = username;
                newUser.Properties["userPrincipalName"].Value = $"{username}@{_config["AD:DomainName"]}";
                newUser.Properties["displayName"].Value = username.ToUpper();

                if (!string.IsNullOrEmpty(description))
                {
                    newUser.Properties["description"].Value = description;
                }

                if (!string.IsNullOrEmpty(createdBy))
                {
                    newUser.Properties["info"].Value = createdBy;
                }

                newUser.CommitChanges();

                newUser.Invoke("SetPassword", new object[] { password });

                int userFlags = (int)newUser.Properties["userAccountControl"].Value;
                newUser.Properties["userAccountControl"].Value = userFlags & ~0x2;
                newUser.Properties["userAccountControl"].Value = (int)newUser.Properties["userAccountControl"].Value | 0x10000;

                newUser.CommitChanges();
                _logger.LogInformation($"Created user {username}");

                if (!string.IsNullOrEmpty(groupName))
                {
                    await AddUserToGroup(username, groupName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating user {username}");
                throw;
            }
        }

        private async Task AddUserToGroup(string username, string groupName)
        {
            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_domainPath))
                {
                    Filter = $"(&(objectCategory=group)(sAMAccountName={groupName}))"
                };
                var result = searcher.FindOne();

                if (result != null)
                {
                    var group = result.GetDirectoryEntry();

                    using var userSearcher = new DirectorySearcher(new DirectoryEntry(_domainPath))
                    {
                        Filter = $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={username}))"
                    };
                    var userResult = userSearcher.FindOne();

                    if (userResult != null)
                    {
                        group.Properties["member"].Add(userResult.Properties["distinguishedName"][0]);
                        group.CommitChanges();
                        _logger.LogInformation($"Added user {username} to group {groupName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding user {username} to group {groupName}");
                throw;
            }
        }

        private async Task<string> GetClinicNameFromOU(string ouPath)
        {
            try
            {
                var ou = new DirectoryEntry(ouPath);
                return ou.Properties["description"].Value?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private DateTime GetOUCreationDate(DirectoryEntry ou)
        {
            try
            {
                if (ou.Properties["whenCreated"].Value != null)
                {
                    return (DateTime)ou.Properties["whenCreated"].Value;
                }
            }
            catch { }

            return DateTime.MinValue;
        }

        private async Task<DateTime> GetLastRebootTime(string computerName)
        {
            try
            {
                var options = new ConnectionOptions();
                var scope = new ManagementScope($"\\\\{computerName}\\root\\cimv2", options);

                try
                {
                    scope.Connect();
                }
                catch
                {
                    return DateTime.MinValue; // Computer not available
                }

                var query = new ObjectQuery("SELECT LastBootUpTime FROM Win32_OperatingSystem");
                var searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["LastBootUpTime"] != null)
                    {
                        var lastBootString = obj["LastBootUpTime"].ToString();
                        if (ManagementDateTimeConverter.ToDateTime(lastBootString) is DateTime lastBoot)
                        {
                            return lastBoot;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting last reboot time for {computerName}");
            }

            return DateTime.MinValue;
        }

        private string ExtractOUFromDN(string distinguishedName)
        {
            int cnIndex = distinguishedName.IndexOf(",");
            if (cnIndex > 0)
            {
                return distinguishedName.Substring(cnIndex + 1);
            }
            return distinguishedName;
        }

        // ----- STUBS PARA MÉTODOS NO IMPLEMENTADOS ANTERIORMENTE -----

        private async Task<string> GetAnyDeskId(string computerName)
        {
            // Implementa aquí la lógica para obtener el AnyDesk ID
            // Por ahora, devolvemos null
            return await Task.FromResult<string>(null);
        }

        private int GetLastUserIndex(string ouPath, string serverPrefix)
        {
            // Implementa aquí la lógica para obtener el último índice usado
            // Por ahora, devolvemos 0 (como si no hubiese usuarios previos)
            return 0;
        }

        // ----- IMPLEMENTACIÓN DE MÉTODOS FALTANTES DE IADService -----

        public async Task<bool> UnlockUser(string username)
        {
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
                    if (user != null && user.IsAccountLockedOut())
                    {
                        user.UnlockAccount();
                        _logger.LogInformation($"User {username} has been unlocked.");
                        return await Task.FromResult(true);
                    }
                    else
                    {
                        _logger.LogWarning($"User {username} not found or is not locked.");
                        return await Task.FromResult(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlocking user {username}");
                throw;
            }
        }

        public async Task<bool> EnableUser(string username)
        {
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
                    if (user != null)
                    {
                        DirectoryEntry de = user.GetUnderlyingObject() as DirectoryEntry;
                        if (de != null)
                        {
                            // Verifica si la propiedad "userAccountControl" es nula.
                            if (de.Properties["userAccountControl"].Value == null)
                            {
                                // Asigna un valor predeterminado (512: normal y habilitado)
                                _logger.LogWarning($"La propiedad 'userAccountControl' era nula para el usuario {username}. Se asigna el valor predeterminado 512.");
                                de.Properties["userAccountControl"].Value = 512;
                            }
                            else
                            {
                                // Si ya existe, quita la bandera de deshabilitado (0x2).
                                int flags = (int)de.Properties["userAccountControl"].Value;
                                de.Properties["userAccountControl"].Value = flags & ~0x2;
                            }
                            de.CommitChanges();
                            _logger.LogInformation($"User {username} has been enabled.");
                            return await Task.FromResult(true);
                        }
                    }
                    _logger.LogWarning($"User {username} not found.");
                    return await Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error enabling user {username}");
                throw;
            }
        }



        public async Task<bool> DisableUser(string username)
        {
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
                    if (user != null)
                    {
                        // Si el usuario ya está deshabilitado, no hace nada
                        if (user.Enabled != false)
                        {
                            user.Enabled = false;
                            user.Save();
                            _logger.LogInformation($"User {username} has been disabled.");
                            return await Task.FromResult(true);
                        }
                        else
                        {
                            _logger.LogInformation($"User {username} is already disabled.");
                            return await Task.FromResult(true);
                        }
                    }
                    _logger.LogWarning($"User {username} not found.");
                    return await Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error disabling user {username}");
                throw;
            }
        }




        public async Task<bool> ChangeUserPassword(string username, string newPassword)
        {
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
                    if (user != null)
                    {
                        // Resetea la contraseña del usuario
                        user.SetPassword(newPassword);
                        user.Save();
                        _logger.LogInformation($"Password for user {username} has been changed.");
                        return await Task.FromResult(true);
                    }
                    else
                    {
                        _logger.LogWarning($"User {username} not found.");
                        return await Task.FromResult(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error changing password for user {username}");
                throw;
            }
        }

        public async Task<List<UserQueryResult>> GetAllUsers()
        {
            var users = new List<UserQueryResult>();

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(objectCategory=person)";
                searcher.PropertiesToLoad.Add("sAMAccountName");
                searcher.PropertiesToLoad.Add("displayName");
                searcher.PropertiesToLoad.Add("userAccountControl");
                searcher.PropertiesToLoad.Add("pwdLastSet");
                searcher.PropertiesToLoad.Add("lockoutTime");

                var results = searcher.FindAll();

                foreach (SearchResult result in results)
                {
                    var user = new UserQueryResult();

                    if (result.Properties["sAMAccountName"].Count > 0)
                        user.Username = result.Properties["sAMAccountName"][0].ToString();

                    if (result.Properties["displayName"].Count > 0)
                        user.FullName = result.Properties["displayName"][0].ToString();

                    if (result.Properties["userAccountControl"].Count > 0)
                    {
                        int flags = (int)result.Properties["userAccountControl"][0];
                        user.IsDisabled = (flags & 0x2) != 0;
                    }

                    if (result.Properties["pwdLastSet"].Count > 0)
                    {
                        long pwdLastSet = (long)result.Properties["pwdLastSet"][0];
                        user.PasswordLastSet = DateTime.FromFileTime(pwdLastSet);
                    }

                    // Verificar si está bloqueado
                    if (result.Properties["lockoutTime"].Count > 0)
                    {
                        long lockout = (long)result.Properties["lockoutTime"][0];
                        user.IsLocked = lockout != 0;
                    }

                    users.Add(user);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todos los usuarios");
            }

            return users;
        }

        public async Task<List<ServerStatus>> GetAllComputers()
        {
            var servers = new List<ServerStatus>();

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_serversOUPath));
                searcher.Filter = "(objectCategory=computer)";
                searcher.PropertiesToLoad.Add("name");

                var results = searcher.FindAll();

                foreach (SearchResult result in results)
                {
                    if (result.Properties["name"].Count > 0)
                    {
                        var serverName = result.Properties["name"][0].ToString();
                        var serverStatus = new ServerStatus
                        {
                            Name = serverName,
                            LastCheckin = DateTime.MinValue,
                            // Comprobamos si está online por ping
                            IsOnline = await IsComputerOnline(serverName)
                        };

                        servers.Add(serverStatus);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener la lista de servidores");
            }

            return servers;
        }

        public async Task<List<string>> GetAllClinics()
        {
            var clinics = new List<string>();

            try
            {
                using var searcher = new DirectorySearcher(new DirectoryEntry(_clinicOUPath));
                searcher.Filter = "(&(objectCategory=organizationalUnit)(ou=PROD_*))";
                searcher.PropertiesToLoad.Add("description");

                var results = searcher.FindAll();

                foreach (SearchResult result in results)
                {
                    if (result.Properties["description"].Count > 0)
                    {
                        clinics.Add(result.Properties["description"][0].ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener las clínicas");
            }

            return clinics;
        }

        private async Task<bool> IsComputerOnline(string computerName)
        {
            try
            {
                var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(computerName, 500);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
    }

    public class ServerStatus
    {
        public string Name { get; set; }
        public DateTime LastCheckin { get; set; }
        public bool IsOnline { get; set; } // Esta propiedad la usás en GetAllComputers
    }

    public class ClinicEnvironmentResult
    {
        public string AdminPassword { get; set; }
        public string ClinicName { get; set; }
        public string ServerName { get; set; }
        public string ProductionOUPath { get; set; }
        public string CloudOUPath { get; set; }
        public string RDSGroupName { get; set; }
        public List<UserCredential> CreatedUsers { get; set; } = new List<UserCredential>();
        public string AnyDeskId { get; set; }
        public string RDWebLink => $"https://rdweb.domain.com/RDWeb/Feed/webfeed.aspx?username={CreatedUsers.FirstOrDefault()?.Username}";
        public string ScanAwayLink => $"https://scanaway.domain.com/data/{ServerName}";
    }

    public class UserCredential
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class ClientQueryResult
    {
        public string ServerCode { get; set; }
        public string ClinicName { get; set; }
        public DateTime CreationDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime LastReboot { get; set; }
        public int UserCount { get; set; }
        public string CurrentComputerOU { get; set; }
    }

    public class UserQueryResult
    {
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsLocked { get; set; }
        public DateTime PasswordLastSet { get; set; }
        public string CreatedBy { get; set; }
        public string Description { get; set; }
        public List<string> Groups { get; set; } = new List<string>();
    }
}
