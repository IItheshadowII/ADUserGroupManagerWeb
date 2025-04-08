namespace ADUserGroupManagerWeb.Models
{
    public class UserActionRequest
    {
        public string Username { get; set; }
        public string? NewPassword { get; set; }  // ✅ nuevo campo opcional
    }
}
