using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Sounds.Model;
using Sounds.Pages;
using System.Security.Claims;

[Authorize]
[Route("api/grupos")]
[ApiController]
public class GruposApiController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly UserManager<IdentityUser> _userManager;

    public GruposApiController(IConfiguration config, UserManager<IdentityUser> userManager)
    {
        _config = config;
        _userManager = userManager;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private NpgsqlConnection GetConn() => new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));

    // GET /api/grupos - Listar mis grupos
    [HttpGet("")]
    public async Task<IActionResult> GetMisGrupos()
    {
        var userId = GetUserId();
        var grupos = new List<grupo>();

        await using var conn = GetConn();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            @"SELECT g.id, g.name, g.code
                  FROM grupo g
                  INNER JOIN usergrupos ug ON ug.id_grupo = g.id
                  WHERE ug.id_user = @uid
                  ORDER BY g.name", conn);
        cmd.Parameters.AddWithValue("uid", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            grupos.Add(new grupo
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                code = reader.GetString(2)
            });
        }

        return Ok(grupos);
    }

    // POST /api/grupos/crear
    [HttpPost("crear")]
    public async Task<IActionResult> CrearGrupo([FromBody] CrearGrupoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.name))
            return BadRequest(new { error = "El nombre es obligatorio." });
        if (string.IsNullOrWhiteSpace(req.code) || req.code.Contains(' '))
            return BadRequest(new { error = "El código no puede estar vacío ni tener espacios." });

        var userId = GetUserId();

        await using var conn = GetConn();
        await conn.OpenAsync();

        // Verificar que el código no existe
        await using var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM grupo WHERE code = @code", conn);
        checkCmd.Parameters.AddWithValue("code", req.code.Trim());
        var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);
        if (count > 0)
            return BadRequest(new { error = "Ese código ya está en uso. Elige otro." });

        // Insertar grupo
        await using var insGrupo = new NpgsqlCommand(
            "INSERT INTO grupo(name, code) VALUES(@name, @code) RETURNING id", conn);
        insGrupo.Parameters.AddWithValue("name", req.name.Trim());
        insGrupo.Parameters.AddWithValue("code", req.code.Trim());
        var idGrupo = (int)(await insGrupo.ExecuteScalarAsync() ?? 0);

        // Insertar usergrupo
        await using var insUG = new NpgsqlCommand(
            "INSERT INTO usergrupos(id_grupo, id_user) VALUES(@ig, @iu)", conn);
        insUG.Parameters.AddWithValue("ig", idGrupo);
        insUG.Parameters.AddWithValue("iu", userId);
        await insUG.ExecuteNonQueryAsync();

        return Ok(new { id = idGrupo, name = req.name.Trim(), code = req.code.Trim() });
    }

    // POST /api/grupos/unirse
    [HttpPost("unirse")]
    public async Task<IActionResult> UnirseGrupo([FromBody] UnirseGrupoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.code))
            return BadRequest(new { error = "El código es obligatorio." });

        var userId = GetUserId();

        await using var conn = GetConn();
        await conn.OpenAsync();

        // Buscar grupo
        await using var findCmd = new NpgsqlCommand(
            "SELECT id, name, code FROM grupo WHERE code = @code", conn);
        findCmd.Parameters.AddWithValue("code", req.code.Trim());

        await using var reader = await findCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return BadRequest(new { error = "No existe ningún grupo con ese código." });

        var idGrupo = reader.GetInt32(0);
        var nombre = reader.GetString(1);
        var code = reader.GetString(2);
        await reader.CloseAsync();

        // Ver si ya pertenece
        await using var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM usergrupos WHERE id_grupo=@ig AND id_user=@iu", conn);
        checkCmd.Parameters.AddWithValue("ig", idGrupo);
        checkCmd.Parameters.AddWithValue("iu", userId);
        var ya = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);
        if (ya > 0)
            return BadRequest(new { error = "Ya perteneces a este grupo." });

        // Insertar
        await using var insCmd = new NpgsqlCommand(
            "INSERT INTO usergrupos(id_grupo, id_user) VALUES(@ig, @iu)", conn);
        insCmd.Parameters.AddWithValue("ig", idGrupo);
        insCmd.Parameters.AddWithValue("iu", userId);
        await insCmd.ExecuteNonQueryAsync();

        return Ok(new { id = idGrupo, name = nombre, code });
    }

    // POST /api/grupos/salir
    [HttpPost("salir")]
    public async Task<IActionResult> SalirGrupo([FromBody] SalirGrupoRequest req)
    {
        var userId = GetUserId();

        await using var conn = GetConn();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM usergrupos WHERE id_grupo=@ig AND id_user=@iu", conn);
        cmd.Parameters.AddWithValue("ig", req.id_grupo);
        cmd.Parameters.AddWithValue("iu", userId);
        await cmd.ExecuteNonQueryAsync();

        return Ok();
    }
}