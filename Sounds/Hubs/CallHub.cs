using Microsoft.AspNetCore.SignalR;
using Npgsql;
using Sounds.Utilidades;
using Microsoft.AspNetCore.Authorization;

namespace Sounds.Hubs
{
    [Authorize]
    public class CallHub : Hub
    {
        private readonly IConfiguration _config;

        public CallHub(IConfiguration config)
        {
            _config = config;
        }

        private NpgsqlConnection GetConn() =>
            new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));

        // Diccionarios en memoria para saber qué usuarios están conectados a cada llamada.
        // Ojo: al reiniciar el servidor se pierden, porque no están guardados en BD.
        static Dictionary<string, Dictionary<string, string>> groupUsers = new();
        static Dictionary<string, string> connToGroup = new();
        static Dictionary<string, string> connToUser = new();

        private static readonly object _lock = new();

        // Comprueba en BD si el usuario actual pertenece al grupo indicado.
        // Esto evita que alguien entre a un grupo cambiando el código desde consola.
        private async Task<bool> UserBelongsToGroup(string groupCode)
        {
            var userId = Context.UserIdentifier;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(groupCode))
                return false;

            await using var conn = GetConn();
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT COUNT(*)
                  FROM grupo gr
                  INNER JOIN usergrupos ug ON ug.id_grupo = gr.id
                  WHERE gr.code = @code AND ug.id_user = @userId", conn);

            cmd.Parameters.AddWithValue("code", groupCode);
            cmd.Parameters.AddWithValue("userId", userId);

            var result = (long)await cmd.ExecuteScalarAsync();
            return result > 0;
        }

        // Entra en una llamada de grupo y avisa a los demás usuarios.
        public async Task JoinCall(string groupCode)
        {
            if (!await UserBelongsToGroup(groupCode))
                throw new HubException("No tienes permiso para entrar en este grupo.");

            var username = Context.User?.Identity?.Name ?? "Anónimo";
            var connId = Context.ConnectionId;

            await Groups.AddToGroupAsync(connId, groupCode);

            List<(string connId, string username)> existingUsers;

            lock (_lock)
            {
                if (!groupUsers.TryGetValue(groupCode, out var members))
                {
                    members = new Dictionary<string, string>();
                    groupUsers[groupCode] = members;
                }

                // Guardamos los usuarios que ya estaban antes de añadir al nuevo.
                // El usuario nuevo usará esta lista para crear ofertas WebRTC.
                existingUsers = members
                    .Select(kv => (kv.Value, kv.Key))
                    .ToList();

                members[username] = connId;
                connToGroup[connId] = groupCode;
                connToUser[connId] = username;
            }

            var existingList = existingUsers
                .Select(u => new { connId = u.connId, username = u.username })
                .ToList();

            await Clients.Caller.SendAsync("ExistingPeers", existingList);

            await Clients.OthersInGroup(groupCode).SendAsync("PeerJoined", new
            {
                connId,
                username
            });

            await BroadcastUserList(groupCode);
        }

        // Sale de la llamada y limpia la conexión del usuario.
        public async Task LeaveCall(string groupCode)
        {
            if (!await UserBelongsToGroup(groupCode))
                return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupCode);
            await CleanupConnection(Context.ConnectionId);
        }

        // Devuelve los usuarios conectados actualmente a una llamada.
        public async Task<List<object>> GetCallUsers(string groupCode)
        {
            if (!await UserBelongsToGroup(groupCode))
                throw new HubException("No tienes permiso para ver este grupo.");

            lock (_lock)
            {
                if (groupUsers.TryGetValue(groupCode, out var members))
                {
                    return members
                        .Select(kv => (object)new { username = kv.Key, connId = kv.Value })
                        .ToList();
                }

                return new List<object>();
            }
        }

        // Si el usuario cierra la pestaña, pierde conexión o recarga, se limpia igual.
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await CleanupConnection(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        // Elimina una conexión de los diccionarios y avisa al grupo.
        private async Task CleanupConnection(string connId)
        {
            string? groupCode;
            string? username;

            lock (_lock)
            {
                connToGroup.TryGetValue(connId, out groupCode);
                connToUser.TryGetValue(connId, out username);

                connToGroup.Remove(connId);
                connToUser.Remove(connId);

                if (groupCode != null && groupUsers.TryGetValue(groupCode, out var members))
                {
                    if (username != null)
                        members.Remove(username);

                    if (members.Count == 0)
                        groupUsers.Remove(groupCode);
                }
            }

            if (groupCode != null)
            {
                await Clients.Group(groupCode).SendAsync("PeerLeft", connId);
                await BroadcastUserList(groupCode);
            }
        }

        // Envía a todos los usuarios del grupo la lista actualizada de participantes.
        private async Task BroadcastUserList(string groupCode)
        {
            List<string> usernames;

            lock (_lock)
            {
                usernames = groupUsers.TryGetValue(groupCode, out var members)
                    ? members.Keys.ToList()
                    : new List<string>();
            }

            await Clients.Group(groupCode).SendAsync("CallUsersUpdated", usernames);
        }

        // Avisa a los demás de que este usuario ha activado o apagado la cámara.
        public async Task NotificarCamaraActiva(string groupCode, bool activa)
        {
            if (!await UserBelongsToGroup(groupCode))
                throw new HubException("No tienes permiso para modificar este grupo.");

            if (connToUser.TryGetValue(Context.ConnectionId, out var username))
            {
                await Clients.OthersInGroup(groupCode).SendAsync(
                    "UsuarioCambioEstadoCamara",
                    Context.ConnectionId,
                    username,
                    activa
                );
            }
        }

        // Avisa a los demás de que este usuario ha empezado o dejado de compartir pantalla.
        public async Task NotificarPantallaCompartida(string groupCode, bool compartiendo)
        {
            if (!await UserBelongsToGroup(groupCode))
                throw new HubException("No tienes permiso para modificar este grupo.");

            if (connToUser.TryGetValue(Context.ConnectionId, out var username))
            {
                await Clients.OthersInGroup(groupCode).SendAsync(
                    "UsuarioCambioEstadoPantalla",
                    Context.ConnectionId,
                    username,
                    compartiendo
                );
            }
        }

        // Envía una oferta WebRTC a otro usuario del mismo grupo.
        public async Task SendOffer(string targetConnId, string offer)
        {
            var from = Context.ConnectionId;

            if (!connToGroup.TryGetValue(from, out var fromGroup))
                throw new HubException("No estás dentro de ninguna llamada.");

            if (!connToGroup.TryGetValue(targetConnId, out var targetGroup))
                throw new HubException("El usuario destino no está en llamada.");

            if (fromGroup != targetGroup)
                throw new HubException("No puedes enviar señalización a otro grupo.");

            var fromUser = connToUser.GetValueOrDefault(from, "Anónimo");

            await Clients.Client(targetConnId).SendAsync("ReceiveOffer", from, fromUser, offer);
        }

        // Envía la respuesta WebRTC al usuario que mandó la oferta.
        public async Task SendAnswer(string targetConnId, string answer)
        {
            var from = Context.ConnectionId;

            if (!connToGroup.TryGetValue(from, out var fromGroup))
                throw new HubException("No estás dentro de ninguna llamada.");

            if (!connToGroup.TryGetValue(targetConnId, out var targetGroup))
                throw new HubException("El usuario destino no está en llamada.");

            if (fromGroup != targetGroup)
                throw new HubException("No puedes enviar señalización a otro grupo.");

            await Clients.Client(targetConnId).SendAsync("ReceiveAnswer", from, answer);
        }

        // Envía candidatos ICE para que WebRTC pueda establecer la conexión.
        public async Task SendIceCandidate(string targetConnId, string candidate)
        {
            var from = Context.ConnectionId;

            if (!connToGroup.TryGetValue(from, out var fromGroup))
                throw new HubException("No estás dentro de ninguna llamada.");

            if (!connToGroup.TryGetValue(targetConnId, out var targetGroup))
                throw new HubException("El usuario destino no está en llamada.");

            if (fromGroup != targetGroup)
                throw new HubException("No puedes enviar señalización a otro grupo.");

            await Clients.Client(targetConnId).SendAsync("ReceiveIceCandidate", from, candidate);
        }

        // Guarda y envía un mensaje de chat al grupo.
        public async Task SendMessage(string groupcode, string text)
        {
            if (!await UserBelongsToGroup(groupcode))
                throw new HubException("No tienes permiso para escribir en este grupo.");

            if (string.IsNullOrWhiteSpace(groupcode) || string.IsNullOrWhiteSpace(text))
                return;

            var user = Context.User?.Identity?.Name ?? "Anónimo";
            var time = DateTime.Now.ToString("HH:mm");

            text = text.Trim();

            // Límite también en servidor, porque el maxlength del HTML se puede saltar.
            if (text.Length > 500)
                text = text.Substring(0, 500);

            try
            {
                await using var conn = GetConn();
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(
                    @"INSERT INTO mensajes_chat(grupo_code, username, texto, fecha)
                      VALUES(@gc, @u, @t, NOW())", conn);

                cmd.Parameters.AddWithValue("gc", groupcode);
                cmd.Parameters.AddWithValue("u", user);
                cmd.Parameters.AddWithValue("t", text);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error guardando mensaje: " + ex.Message);
            }

            await Clients.Group(groupcode).SendAsync("ReceiveMessage", new { user, text, time });
        }

        // Carga los últimos mensajes del grupo desde la base de datos.
        public async Task<List<object>> GetChatHistory(string groupCode, int limit = 50)
        {
            if (!await UserBelongsToGroup(groupCode))
                throw new HubException("No tienes permiso para ver este grupo.");

            // Limitamos la cantidad para evitar peticiones exageradas.
            if (limit < 1) limit = 1;
            if (limit > 100) limit = 100;

            var mensajes = new List<object>();

            try
            {
                await using var conn = GetConn();
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(
                    @"SELECT username, texto, TO_CHAR(fecha, 'HH24:MI') AS time
                      FROM mensajes_chat
                      WHERE grupo_code = @gc
                      ORDER BY fecha DESC
                      LIMIT @lim", conn);

                cmd.Parameters.AddWithValue("gc", groupCode);
                cmd.Parameters.AddWithValue("lim", limit);

                await using var reader = await cmd.ExecuteReaderAsync();

                var temp = new List<object>();

                while (await reader.ReadAsync())
                {
                    temp.Add(new
                    {
                        user = reader.GetString(0),
                        text = reader.GetString(1),
                        time = reader.GetString(2)
                    });
                }

                // La consulta trae los últimos primero, aquí los ponemos en orden normal.
                temp.Reverse();
                mensajes = temp;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error cargando historial: " + ex.Message);
            }

            return mensajes;
        }
    }
}