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
    public class ChatsModel : PageModel
    {
        private readonly IConfiguration _config;

        public ChatsModel(IConfiguration config)
        {
            _config = config;
        }

        public List<grupo> grupo { get; set; } = new();
        public string? ActiveGroupCode { get; set; }

        public async Task OnGetAsync(string? g = null)
        {
            ActiveGroupCode = g;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            await using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT gr.id, gr.name, gr.code
                  FROM grupo gr
                  INNER JOIN usergrupos ug ON ug.id_grupo = gr.id
                  WHERE ug.id_user = @uid
                  ORDER BY gr.name", conn);
            cmd.Parameters.AddWithValue("uid", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                grupo.Add(new grupo
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    code = reader.GetString(2)
                });
            }
        }
    }
}