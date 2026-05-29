using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using Sounds.Model;
using System.Security.Claims;

namespace Sounds.Pages
{
    [Authorize]
    public class GruposModel : PageModel
    {
        private readonly IConfiguration _config;
        private readonly UserManager<IdentityUser> _userManager;

        public GruposModel(IConfiguration config, UserManager<IdentityUser> userManager)
        {
            _config = config;
            _userManager = userManager;
        }

        public void OnGet() { }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        private NpgsqlConnection GetConn() => new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
    }

    public class CrearGrupoRequest { public string name { get; set; } = ""; public string code { get; set; } = ""; }
    public class UnirseGrupoRequest { public string code { get; set; } = ""; }
    public class SalirGrupoRequest { public int id_grupo { get; set; } }
}