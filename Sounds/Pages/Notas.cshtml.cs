using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using System.Security.Claims;

namespace Sounds.Pages
{
    [Authorize]
    public class NotasModel : PageModel
    {
        public void OnGet() { }
    }

    public class NotaRequest
    {
        public string titulo { get; set; } = "";
        public string contenido { get; set; } = "";
    }

    public class CompartirRequest
    {
        public string username { get; set; } = "";
    }
}