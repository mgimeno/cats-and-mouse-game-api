using Microsoft.AspNetCore.Mvc;

namespace CatsAndMouseApi.Controllers
{
    [Route("status")]
    [ApiController]
    public class StatusController(IWebHostEnvironment env, IConfiguration configuration) : ControllerBase
    {
        private readonly IWebHostEnvironment _env = env;
        private readonly IConfiguration _configuration = configuration;

        [HttpGet]
        public IActionResult Status()
        {
            var isDebugMode = false;
#if DEBUG
            isDebugMode = true;
#endif
            var allowedOrigins = string.Join(", ", _configuration.GetSection("AllowedOrigins").Get<string[]>() ?? []);

            return new ContentResult
            {
                ContentType = "text/plain",
                Content = $@"
Cats & Mouse API is ready

Build Mode: {(isDebugMode ? "DEBUG (Development)" : "RELEASE (Production)")}
Environment: {_env.EnvironmentName.ToUpperInvariant()}
Allowed origins: {allowedOrigins}"
            };
        }
    }
}
