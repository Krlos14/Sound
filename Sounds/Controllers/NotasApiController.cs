using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace Sounds.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/notas")]
    public class NotasApiController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly UserManager<IdentityUser> _userManager;

        public NotasApiController(IConfiguration config, UserManager<IdentityUser> userManager)
        {
            _config = config;
            _userManager = userManager;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        private NpgsqlConnection GetConn() => new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));

        [HttpGet("")]
        public async Task<IActionResult> GetNotas()
        {
            var userId = GetUserId();
            var notas = new List<object>();

            await using var conn = GetConn();
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT n.id, n.titulo, n.contenido, n.fecha_creacion, n.fecha_actualizacion,
                         COALESCE(array_agg(u.""UserName"") FILTER (WHERE u.""UserName"" IS NOT NULL), '{}') AS compartida_con
                  FROM notas n
                  LEFT JOIN notas_compartidas nc ON nc.nota_id = n.id
                  LEFT JOIN ""AspNetUsers"" u ON u.""Id"" = nc.user_id
                  WHERE n.user_id = @uid
                  GROUP BY n.id
                  ORDER BY COALESCE(n.fecha_actualizacion, n.fecha_creacion) DESC", conn);

            cmd.Parameters.AddWithValue("uid", userId);

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    notas.Add(new
                    {
                        id = reader.GetInt32(0),
                        titulo = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        contenido = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        fechaCreacion = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
                        fechaActualizacion = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                        compartidaCon = (string[])reader.GetValue(5),
                        esPropia = true
                    });
                }
            }

            await using var cmd2 = new NpgsqlCommand(
                @"SELECT n.id, n.titulo, n.contenido, n.fecha_creacion, n.fecha_actualizacion
                  FROM notas n
                  INNER JOIN notas_compartidas nc ON nc.nota_id = n.id
                  WHERE nc.user_id = @uid
                  ORDER BY COALESCE(n.fecha_actualizacion, n.fecha_creacion) DESC", conn);

            cmd2.Parameters.AddWithValue("uid", userId);

            await using (var reader2 = await cmd2.ExecuteReaderAsync())
            {
                while (await reader2.ReadAsync())
                {
                    notas.Add(new
                    {
                        id = reader2.GetInt32(0),
                        titulo = reader2.IsDBNull(1) ? "" : reader2.GetString(1),
                        contenido = reader2.IsDBNull(2) ? "" : reader2.GetString(2),
                        fechaCreacion = reader2.IsDBNull(3) ? (DateTime?)null : reader2.GetDateTime(3),
                        fechaActualizacion = reader2.IsDBNull(4) ? (DateTime?)null : reader2.GetDateTime(4),
                        compartidaCon = Array.Empty<string>(),
                        esPropia = false
                    });
                }
            }

            return Ok(notas);
        }

        [HttpPost("")]
        public async Task<IActionResult> CrearNota([FromBody] NotaRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.titulo))
                return BadRequest(new { error = "El título es obligatorio." });

            var userId = GetUserId();

            await using var conn = GetConn();
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO notas(titulo, contenido, user_id, fecha_creacion, fecha_actualizacion)
                  VALUES(@titulo, @contenido, @uid, NOW(), NOW()) RETURNING id", conn);

            cmd.Parameters.AddWithValue("titulo", req.titulo.Trim());
            cmd.Parameters.AddWithValue("contenido", req.contenido ?? "");
            cmd.Parameters.AddWithValue("uid", userId);

            var id = (int)(await cmd.ExecuteScalarAsync() ?? 0);

            return Ok(new { id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> EditarNota(int id, [FromBody] NotaRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.titulo))
                return BadRequest(new { error = "El título es obligatorio." });

            var userId = GetUserId();

            await using var conn = GetConn();
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"UPDATE notas
                  SET titulo=@titulo, contenido=@contenido, fecha_actualizacion=NOW()
                  WHERE id=@id AND user_id=@uid", conn);

            cmd.Parameters.AddWithValue("titulo", req.titulo.Trim());
            cmd.Parameters.AddWithValue("contenido", req.contenido ?? "");
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("uid", userId);

            var rows = await cmd.ExecuteNonQueryAsync();

            if (rows == 0)
                return NotFound(new { error = "Nota no encontrada o sin permisos." });

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> BorrarNota(int id)
        {
            var userId = GetUserId();

            await using var conn = GetConn();
            await conn.OpenAsync();

            await using var delShare = new NpgsqlCommand(
                "DELETE FROM notas_compartidas WHERE nota_id=@id", conn);
            delShare.Parameters.AddWithValue("id", id);
            await delShare.ExecuteNonQueryAsync();

            await using var cmd = new NpgsqlCommand(
                "DELETE FROM notas WHERE id=@id AND user_id=@uid", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("uid", userId);

            await cmd.ExecuteNonQueryAsync();

            return Ok();
        }

        [HttpPost("{id}/compartir")]
        public async Task<IActionResult> Compartir(int id, [FromBody] CompartirRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.username))
                return BadRequest(new { error = "El nombre de usuario es obligatorio." });

            var userId = GetUserId();

            await using var conn = GetConn();
            await conn.OpenAsync();

            await using var checkNota = new NpgsqlCommand(
                "SELECT COUNT(*) FROM notas WHERE id=@id AND user_id=@uid", conn);
            checkNota.Parameters.AddWithValue("id", id);
            checkNota.Parameters.AddWithValue("uid", userId);

            var own = (long)(await checkNota.ExecuteScalarAsync() ?? 0L);

            if (own == 0)
                return BadRequest(new { error = "Solo puedes compartir tus propias notas." });

            await using var findUser = new NpgsqlCommand(
                @"SELECT ""Id"" FROM ""AspNetUsers"" WHERE ""UserName"" = @username", conn);
            findUser.Parameters.AddWithValue("username", req.username.Trim());

            var targetId = await findUser.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(targetId))
                return BadRequest(new { error = $"No existe el usuario '{req.username}'." });

            if (targetId == userId)
                return BadRequest(new { error = "No puedes compartir una nota contigo mismo." });

            await using var checkShare = new NpgsqlCommand(
                "SELECT COUNT(*) FROM notas_compartidas WHERE nota_id=@nid AND user_id=@uid2", conn);
            checkShare.Parameters.AddWithValue("nid", id);
            checkShare.Parameters.AddWithValue("uid2", targetId);

            var ya = (long)(await checkShare.ExecuteScalarAsync() ?? 0L);

            if (ya > 0)
                return BadRequest(new { error = "Ya has compartido esta nota con ese usuario." });

            await using var ins = new NpgsqlCommand(
                "INSERT INTO notas_compartidas(nota_id, user_id) VALUES(@nid, @uid2)", conn);
            ins.Parameters.AddWithValue("nid", id);
            ins.Parameters.AddWithValue("uid2", targetId);

            await ins.ExecuteNonQueryAsync();

            return Ok(new { ok = true });
        }
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