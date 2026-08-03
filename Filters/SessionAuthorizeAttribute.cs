using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ReportSystem.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class SessionAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string[] _allowedRoles;

        public IReadOnlyList<string> AllowedRoles => _allowedRoles;

        public SessionAuthorizeAttribute(params string[] roles)
        {
            _allowedRoles = roles;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var session = context.HttpContext.Session;
            var userIdStr = session.GetString("UserId");
            var userRole = session.GetString("Role"); // make sure Login sets this

            // Not logged in
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out _))
            {
                context.Result = new JsonResult(new { message = "Unauthorized. Please log in.", redirect = "/Login" })
                {
                    StatusCode = 401
                };
                return;
            }

            // Role check (only if roles were specified)
            if (_allowedRoles.Length > 0 &&
                !_allowedRoles.Contains(userRole, StringComparer.OrdinalIgnoreCase))
            {
                context.Result = new JsonResult(new { message = "Forbidden. You do not have permission." })
                {
                    StatusCode = 403
                };
            }
        }
    }
}
