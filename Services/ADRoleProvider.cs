using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ADUserGroupManagerWeb.Services
{
    public interface IADRoleProvider
    {
        bool HasPermission(ClaimsPrincipal user, string permission);
        bool CanCreateUsers(ClaimsPrincipal user);
        bool CanModifyUsers(ClaimsPrincipal user);
        bool CanCreateClinicEnvironment(ClaimsPrincipal user);
        bool IsAdministrator(ClaimsPrincipal user);
    }

    public class ADRoleProvider : IADRoleProvider
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ADRoleProvider> _logger;

        // Define los permisos para cada rol de la aplicación
        private readonly Dictionary<string, List<string>> _rolePermissions;

        // Mapa de grupos de AD a roles de aplicación
        private readonly Dictionary<string, string> _adGroupToRoleMapping;

        public ADRoleProvider(IConfiguration config, ILogger<ADRoleProvider> logger)
        {
            _config = config;
            _logger = logger;

            // Inicializar el mapeo de grupos AD a roles de aplicación
            _adGroupToRoleMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Estos valores deben venir de la configuración o usar valores predeterminados
                { _config["AD:Groups:Administrators"] ?? "Domain Admins", "Administrator" },
                { _config["AD:Groups:UserManagers"] ?? "User Managers", "UserManager" },
                { _config["AD:Groups:ClinicCreators"] ?? "Clinic Creators", "ClinicCreator" },
                { _config["AD:Groups:Viewers"] ?? "AD Viewers", "Viewer" }
            };

            // Inicializar los permisos para cada rol
            _rolePermissions = new Dictionary<string, List<string>>
            {
                {
                    "Administrator", new List<string> {
                        "CreateUsers", "ModifyUsers", "DeleteUsers", "UnlockUsers",
                        "DisableUsers", "EnableUsers", "ResetPasswords", "CreateClinicEnvironment",
                        "ViewDashboard", "ViewAuditLogs", "ModifySettings"
                    }
                },
                {
                    "UserManager", new List<string> {
                        "CreateUsers", "ModifyUsers", "UnlockUsers",
                        "DisableUsers", "EnableUsers", "ResetPasswords"
                    }
                },
                {
                    "ClinicCreator", new List<string> {
                        "CreateClinicEnvironment", "ViewDashboard"
                    }
                },
                {
                    "Viewer", new List<string> {
                        "ViewDashboard", "ViewUserDetails"
                    }
                },
                {
                    "BasicUser", new List<string> {
                        "ViewBasicInfo"
                    }
                }
            };
        }

        public bool HasPermission(ClaimsPrincipal user, string permission)
        {
            if (user == null)
                return false;

            // Si el usuario es administrador, tiene todos los permisos
            if (IsAdministrator(user))
                return true;

            // Asignar roles basados en los grupos de AD del usuario
            var userRoles = GetUserRoles(user);

            // Verificar si alguno de los roles del usuario tiene el permiso solicitado
            foreach (var role in userRoles)
            {
                if (_rolePermissions.ContainsKey(role) &&
                    _rolePermissions[role].Contains(permission))
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanCreateUsers(ClaimsPrincipal user)
        {
            return HasPermission(user, "CreateUsers");
        }

        public bool CanModifyUsers(ClaimsPrincipal user)
        {
            return HasPermission(user, "ModifyUsers");
        }

        public bool CanCreateClinicEnvironment(ClaimsPrincipal user)
        {
            return HasPermission(user, "CreateClinicEnvironment");
        }

        public bool IsAdministrator(ClaimsPrincipal user)
        {
            if (user == null)
                return false;

            var userRoles = GetUserRoles(user);
            return userRoles.Contains("Administrator");
        }

        private List<string> GetUserRoles(ClaimsPrincipal user)
        {
            var roles = new HashSet<string>();

            // Obtener los grupos de AD del usuario
            var adGroups = user.FindAll("ADGroup").Select(c => c.Value).ToList();

            // Si no hay grupos específicos, intentar obtener los grupos del token
            if (adGroups.Count == 0)
            {
                adGroups = user.FindAll(ClaimTypes.GroupSid).Select(c => c.Value).ToList();
            }

            // Si aún no hay grupos, verificar los roles ya asignados
            var assignedRoles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            if (assignedRoles.Any())
            {
                foreach (var role in assignedRoles)
                {
                    roles.Add(role);
                }
            }
            else
            {
                // Mapear los grupos de AD a roles de la aplicación
                foreach (var adGroup in adGroups)
                {
                    if (_adGroupToRoleMapping.TryGetValue(adGroup, out string role))
                    {
                        roles.Add(role);
                    }
                }
            }

            // Si no tiene roles específicos, asignar rol básico
            if (roles.Count == 0)
            {
                roles.Add("BasicUser");
            }

            return roles.ToList();
        }
    }
}