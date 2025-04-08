using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ADUserGroupManagerWeb.Models;
using ADUserGroupManagerWeb.Services;
using ADUserGroupManagerWeb.Authorization;

namespace ADUserGroupManagerWeb.Controllers
{
    [ApiController]
    [Route("api/user")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IADService _adService;
        private readonly IADRoleProvider _roleProvider;
        private readonly ILogger<UserController> _logger;

        public UserController(
            IADService adService,
            IADRoleProvider roleProvider,
            ILogger<UserController> logger)
        {
            _adService = adService;
            _roleProvider = roleProvider;
            _logger = logger;
        }

        [HttpPost("query")]
        [RequirePermission("ViewUserDetails")]
        public async Task<IActionResult> QueryUser([FromBody] UserQueryRequest request)
        {
            if (!_roleProvider.HasPermission(User, "ViewUserDetails"))
            {
                return Forbid();
            }

            try
            {
                var result = await _adService.GetUserInfo(request.Username);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error querying user {request.Username}");
                return StatusCode(500, new { message = $"Error querying user {request.Username}" });
            }
        }

        [HttpPost("disable")]
        [RequirePermission("DisableUsers")]
        public async Task<IActionResult> DisableUser([FromBody] UserActionRequest request)
        {
            if (!_roleProvider.HasPermission(User, "DisableUsers"))
            {
                return Forbid();
            }

            try
            {
                var result = await _adService.DisableUser(request.Username);
                if (result)
                {
                    _logger.LogInformation($"Usuario {request.Username} deshabilitado por {User.Identity.Name}");
                    return Ok(new { message = "User disabled successfully." });
                }
                else
                {
                    return BadRequest(new { message = $"No se pudo deshabilitar el usuario {request.Username}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error disabling user {request.Username}");
                return StatusCode(500, new { message = $"Error disabling user {request.Username}" });
            }
        }

        [HttpPost("enable")]
        [RequirePermission("EnableUsers")]
        public async Task<IActionResult> EnableUser([FromBody] UserActionRequest request)
        {
            if (!_roleProvider.HasPermission(User, "EnableUsers"))
            {
                return Forbid();
            }

            try
            {
                var result = await _adService.EnableUser(request.Username);
                if (result)
                {
                    _logger.LogInformation($"Usuario {request.Username} habilitado por {User.Identity.Name}");
                    return Ok(new { message = "User enabled successfully." });
                }
                else
                {
                    return BadRequest(new { message = $"No se pudo habilitar el usuario {request.Username}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error enabling user {request.Username}");
                return StatusCode(500, new { message = $"Error enabling user {request.Username}" });
            }
        }

        [HttpPost("unlock")]
        [RequirePermission("UnlockUsers")]
        public async Task<IActionResult> UnlockUser([FromBody] UserActionRequest request)
        {
            if (!_roleProvider.HasPermission(User, "UnlockUsers"))
            {
                return Forbid();
            }

            try
            {
                var result = await _adService.UnlockUser(request.Username);
                if (result)
                {
                    _logger.LogInformation($"Usuario {request.Username} desbloqueado por {User.Identity.Name}");
                    return Ok(new { message = "User unlocked successfully." });
                }
                else
                {
                    return BadRequest(new { message = $"No se pudo desbloquear el usuario {request.Username}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlocking user {request.Username}");
                return StatusCode(500, new { message = $"Error unlocking user {request.Username}" });
            }
        }

        [HttpPost("reset-password")]
        [RequirePermission("ResetPasswords")]
        public async Task<IActionResult> ResetPassword([FromBody] UserActionRequest request)
        {
            if (!_roleProvider.HasPermission(User, "ResetPasswords"))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { message = "Debe proporcionar una nueva contraseña." });
            }

            try
            {
                var result = await _adService.ChangeUserPassword(request.Username, request.NewPassword);
                if (result)
                {
                    _logger.LogInformation($"Contraseña de {request.Username} cambiada por {User.Identity.Name}");
                    return Ok(new { message = "Password reset successfully." });
                }
                else
                {
                    return BadRequest(new { message = $"No se pudo cambiar la contraseña de {request.Username}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting password for {request.Username}");
                return StatusCode(500, new { message = $"Error resetting password for {request.Username}" });
            }
        }

        [HttpGet("current")]
        public IActionResult GetCurrentUser()
        {
            var username = User.Identity?.Name;

            if (string.IsNullOrEmpty(username))
                return Unauthorized(new { message = "No autenticado" });

            var permissions = new
            {
                CanCreateUsers = _roleProvider.CanCreateUsers(User),
                CanModifyUsers = _roleProvider.CanModifyUsers(User),
                CanCreateClinicEnvironment = _roleProvider.CanCreateClinicEnvironment(User),
                IsAdministrator = _roleProvider.IsAdministrator(User)
            };

            return Ok(new
            {
                username,
                permissions,
                groups = User.FindAll("ADGroup").Select(c => c.Value).ToList(),
                roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList()
            });
        }
    }

    public class UserQueryRequest
    {
        public string Username { get; set; }
    }

    public class UserActionRequest
    {
        public string Username { get; set; }
        public string NewPassword { get; set; }
    }
}