using Microsoft.AspNetCore.Authorization;
using ADUserGroupManagerWeb.Services;
using System.Threading.Tasks;

namespace ADUserGroupManagerWeb.Authorization
{
    // Atributo para requerir un permiso específico
    public class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permission)
        {
            Permission = permission;
            Policy = $"Permission:{permission}";
        }

        public string Permission { get; }
    }

    // Requisito de autorización para permisos
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permission)
        {
            Permission = permission;
        }

        public string Permission { get; }
    }

    // Handler que verifica los permisos usando el ADRoleProvider
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IADRoleProvider _roleProvider;
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(
            IADRoleProvider roleProvider,
            ILogger<PermissionAuthorizationHandler> logger)
        {
            _roleProvider = roleProvider;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var user = context.User;
            var permission = requirement.Permission;

            if (_roleProvider.HasPermission(user, permission))
            {
                _logger.LogInformation($"Usuario {user.Identity?.Name} tiene permiso {permission}");
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning($"Usuario {user.Identity?.Name} NO tiene permiso {permission}");
            }

            return Task.CompletedTask;
        }
    }
}