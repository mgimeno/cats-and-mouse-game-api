using Microsoft.AspNetCore.Mvc;


namespace CatsAndMouseGame.Controllers
{
    [Route("status")]
    [ApiController]
    public class StatusController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;

        public StatusController(IWebHostEnvironment env, IConfiguration configuration)
        {
            _env = env;
            _configuration = configuration;
        }

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
