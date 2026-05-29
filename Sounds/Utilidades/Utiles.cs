
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
using Microsoft.Identity.Client;
using Npgsql;
using Sounds.Data;
using Sounds.Model;
using System.Security.Claims;

namespace Sounds.Utilidades
{
    public class Utiles : PageModel
    {
        public UserManager<IdentityUser> userManager { get; }

        // Para guardar el id del usuario (aquí está declarada pero no se usa)
        public string? id_usuario { get; set; }

        public void CargarUsuario(ClaimsPrincipal user)
        {
            id_usuario = user.FindFirstValue(ClaimTypes.NameIdentifier);

        }

    }
}
