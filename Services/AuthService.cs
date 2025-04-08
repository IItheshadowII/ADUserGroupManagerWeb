using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ADUserGroupManagerWeb.Services
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;

        // Grupos de AD mapeados a roles en la aplicación
        private readonly Dictionary<string, string> _adGroupToRoleMapping;

        public AuthService(IConfiguration config, ILogger<AuthService> logger)
        {
            _config = config;
            _logger = logger;

            // Inicializar el mapeo de grupos AD a roles de aplicación
            _adGroupToRoleMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Estos valores deben venir de la configuración
                { _config["AD:Groups:Administrators"] ?? "Domain Admins", "Administrator" },
                { _config["AD:Groups:UserManagers"] ?? "User Managers", "UserManager" },
                { _config["AD:Groups:ClinicCreators"] ?? "Clinic Creators", "ClinicCreator" },
                { _config["AD:Groups:Viewers"] ?? "AD Viewers", "Viewer" }
            };
        }

        public async Task<AuthResult> AuthenticateUser(string username, string password)
        {
            try
            {
                // Validate against Active Directory
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    bool isValid = context.ValidateCredentials(username, password);

                    if (!isValid)
                    {
                        _logger.LogWarning($"Failed login attempt for user: {username}");
                        return new AuthResult { Success = false, Message = "Invalid credentials" };
                    }

                    // Obtener el dominio desde la configuración
                    string domainDn = _config["AD:DomainDN"] ?? "DC=localhost";

                    // Get user details from AD
                    using (var searcher = new DirectorySearcher(new DirectoryEntry($"LDAP://{domainDn}")))
                    {
                        searcher.Filter = $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={username}))";
                        var result = searcher.FindOne();

                        if (result == null)
                        {
                            return new AuthResult { Success = false, Message = "User not found in directory" };
                        }

                        var userGroups = GetUserGroups(result);
                        var applicationRoles = MapGroupsToRoles(userGroups);

                        // Generate JWT token
                        var token = GenerateJwtToken(username, userGroups, applicationRoles);

                        _logger.LogInformation($"User {username} authenticated successfully with roles: {string.Join(", ", applicationRoles)}");
                        return new AuthResult
                        {
                            Success = true,
                            Token = token,
                            Username = username,
                            FullName = result.Properties["displayName"].Count > 0 ? result.Properties["displayName"][0]?.ToString() : username,
                            Groups = userGroups,
                            Roles = applicationRoles
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Authentication error for user {username}");
                return new AuthResult { Success = false, Message = "Authentication error" };
            }
        }

        private List<string> GetUserGroups(SearchResult user)
        {
            var groups = new List<string>();

            if (user.Properties["memberOf"] != null)
            {
                foreach (var group in user.Properties["memberOf"])
                {
                    // Extract CN from DN
                    var groupName = group.ToString();
                    var cnMatch = System.Text.RegularExpressions.Regex.Match(groupName, @"CN=([^,]+)");
                    if (cnMatch.Success)
                    {
                        groups.Add(cnMatch.Groups[1].Value);
                    }
                }
            }

            return groups;
        }

        private List<string> MapGroupsToRoles(List<string> adGroups)
        {
            var roles = new HashSet<string>();

            // Si el usuario es miembro de algún grupo especial, asignarle el rol correspondiente
            foreach (var adGroup in adGroups)
            {
                if (_adGroupToRoleMapping.TryGetValue(adGroup, out string role))
                {
                    roles.Add(role);
                }
            }

            // Si no tiene roles específicos, asignar rol básico
            if (roles.Count == 0)
            {
                roles.Add("BasicUser");
            }

            return roles.ToList();
        }

        private string GenerateJwtToken(string username, List<string> groups, List<string> roles)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username)
            };

            // Add groups as claims
            foreach (var group in groups)
            {
                claims.Add(new Claim("ADGroup", group));
            }

            // Add application roles as claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(8),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public class AuthResult
    {
        public bool Success { get; set; }
        public string Token { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Message { get; set; }
        public List<string> Groups { get; set; } = new List<string>();
        public List<string> Roles { get; set; } = new List<string>();
    }

    public interface IAuthService
    {
        Task<AuthResult> AuthenticateUser(string username, string password);
    }
}