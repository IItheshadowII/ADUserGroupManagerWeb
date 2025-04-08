using System.DirectoryServices.AccountManagement;
using System.DirectoryServices;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace ADUserGroupManagerWeb.Middleware
{
    public class WindowsAuthClaimsMiddleware
    {
    }
}
using Microsoft.AspNetCore.Http;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ADUserGroupManagerWeb.Middleware
{
    public class WindowsAuthClaimsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<WindowsAuthClaimsMiddleware> _logger;
        private readonly IConfiguration _config;

        public WindowsAuthClaimsMiddleware(RequestDelegate next, ILogger<WindowsAuthClaimsMiddleware> logger, IConfiguration config)
        {
            _next = next;
            _logger = logger;
            _config = config;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Solo procesar si el usuario está autenticado y es una identidad Windows
            if (context.User?.Identity?.IsAuthenticated == true &&
                context.User.Identity is WindowsIdentity windowsIdentity)
            {
                // Si no tiene claims de rol, enriquecemos la identidad con información de AD
                if (!context.User.HasClaim(c => c.Type == ClaimTypes.Role))
                {
                    try
                    {
                        string username = windowsIdentity.Name.Split('\\').Last();

                        // Obtener información de grupos de AD
                        var adGroups = GetUserADGroups(username);
                        var appRoles = MapGroupsToRoles(adGroups);

                        // Crear una nueva identidad con los claims del usuario
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.Name, username)
                        };

                        // Agregar claims de grupos
                        foreach (var group in adGroups)
                        {
                            claims.Add(new Claim("ADGroup", group));
                        }

                        // Agregar claims de roles
                        foreach (var role in appRoles)
                        {
                            claims.Add(new Claim(ClaimTypes.Role, role));
                        }

                        // Copiar los claims existentes (excepto Name y Role que ya los tenemos)
                        foreach (var claim in context.User.Claims)
                        {
                            if (claim.Type != ClaimTypes.Name && claim.Type != ClaimTypes.Role && claim.Type != "ADGroup")
                            {
                                claims.Add(claim);
                            }
                        }

                        // Crear una nueva identidad y reemplazar la actual
                        var identity = new ClaimsIdentity(claims, "Windows", ClaimTypes.Name, ClaimTypes.Role);
                        var principal = new ClaimsPrincipal(identity);

                        // Establecer el nuevo principal en el contexto
                        context.User = principal;

                        _logger.LogInformation($"Enriquecida identidad Windows para usuario {username} con roles: {string.Join(", ", appRoles)}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al enriquecer identidad Windows: {ex.Message}");
                    }
                }

                // Agregar datos de permisos al contexto Http para que estén disponibles en las vistas
                context.Items["CanCreateUsers"] = context.User.IsInRole("Administrator") || context.User.IsInRole("UserManager");
                context.Items["CanModifyUsers"] = context.User.IsInRole("Administrator") || context.User.IsInRole("UserManager");
                context.Items["CanCreateClinicEnvironment"] = context.User.IsInRole("Administrator") || context.User.IsInRole("ClinicCreator");
                context.Items["IsAdministrator"] = context.User.IsInRole("Administrator");
            }

            await _next(context);
        }

        private List<string> GetUserADGroups(string username)
        {
            var groups = new List<string>();

            try
            {
                string domainDn = _config["AD:DomainDN"] ?? "DC=labs,DC=local";

                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username))
                    {
                        if (user != null)
                        {
                            using (var searcher = new DirectorySearcher(new DirectoryEntry($"LDAP://{domainDn}")))
                            {
                                searcher.Filter = $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={username}))";
                                var result = searcher.FindOne();

                                if (result != null && result.Properties["memberOf"] != null)
                                {
                                    foreach (var group in result.Properties["memberOf"])
                                    {
                                        var match = Regex.Match(group.ToString(), @"CN=([^,]+)");
                                        if (match.Success)
                                        {
                                            groups.Add(match.Groups[1].Value);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error obteniendo grupos de AD para {username}");
            }

            return groups;
        }

        private List<string> MapGroupsToRoles(List<string> adGroups)
        {
            var roles = new HashSet<string>();

            // Mapeo de grupos AD a roles
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { _config["AD:Groups:Administrators"] ?? "Domain Admins", "Administrator" },
                { _config["AD:Groups:UserManagers"] ?? "User Managers", "UserManager" },
                { _config["AD:Groups:ClinicCreators"] ?? "Clinic Creators", "ClinicCreator" },
                { _config["AD:Groups:Viewers"] ?? "AD Viewers", "Viewer" }
            };

            // Si el usuario es miembro de algún grupo especial, asignarle el rol correspondiente
            foreach (var adGroup in adGroups)
            {
                if (mappings.TryGetValue(adGroup, out string role))
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
    }

    // Extensión para agregar el middleware a la configuración
    public static class WindowsAuthClaimsMiddlewareExtensions
    {
        public static IApplicationBuilder UseWindowsAuthClaims(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<WindowsAuthClaimsMiddleware>();
        }
    }
}