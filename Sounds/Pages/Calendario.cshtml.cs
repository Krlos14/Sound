using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using System.Security.Claims;

namespace Sounds.Pages
{
    [Authorize]
    public class CalendarioModel : PageModel
    {
        public void OnGet() { }
    }

    
} 