using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Principal;
using ADUserGroupManagerWeb.Services;

namespace ADUserGroupManagerWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class WindowsAuthController : ControllerBase
    {
        private readonly IADRoleProvider _roleProvider;
        private readonly ILogger<WindowsAuthController> _logger;

        public WindowsAuthController(
            IADRoleProvider roleProvider,
            ILogger<WindowsAuthController> logger)
        {
            _roleProvider = roleProvider;
            _logger = logger;
        }

        [HttpGet("user-info")]
        public ActionResult<UserInfo> GetUserInfo()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized(new { message = "No autenticado" });
            }

            var info = new UserInfo
            {
                Username = User.Identity.Name,
                Roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList(),
                Groups = User.FindAll("ADGroup").Select(c => c.Value).ToList(),
                CanCreateUsers = _roleProvider.CanCreateUsers(User),
                CanModifyUsers = _roleProvider.CanModifyUsers(User),
                CanCreateClinicEnvironment = _roleProvider.CanCreateClinicEnvironment(User),
                IsAdministrator = _roleProvider.IsAdministrator(User)
            };

            return info;
        }
    }

    public class UserInfo
    {
        public string Username { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public List<string> Groups { get; set; } = new List<string>();
        public bool CanCreateUsers { get; set; }
        public bool CanModifyUsers { get; set; }
        public bool CanCreateClinicEnvironment { get; set; }
        public bool IsAdministrator { get; set; }
    }
}