using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;


namespace CatsAndMouseGame.Controllers
{
    [EnableCors("CorsPolicy")]
    [Route("api/[action]")]
    [ApiController]

    public class StatusController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private IConfiguration _configuration { get; }

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

            return new ContentResult
            {
                ContentType = "text/html",
                Content = $@"
                CATS & MOUSE API IS READY
                <br /><br />
                Build Mode: {(isDebugMode ? "DEBUG (Development)" : "RELEASE (Production)")}
                <br />
                Environment: {_env.EnvironmentName.ToUpper()}
                <br />
                AllowedOrigins: {_configuration.GetSection("AllowedOrigins").Value}"
            };
        }

    }
}
