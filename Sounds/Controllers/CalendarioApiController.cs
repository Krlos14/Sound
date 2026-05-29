using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Sounds.Controllers
{
    [Authorize]
    [Route("api/calendario")]
    [ApiController]
    public class CalendarioApiController : ControllerBase
    {
        private readonly IConfiguration _config;

        public CalendarioApiController(IConfiguration config)
        {
            _config = config;
        }

        private string GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        }

        private CalendarService GetCalendarService()
        {
            var jsonPath = _config["GoogleCalendar:ServiceAccountJson"];

            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new Exception("Falta GoogleCalendar:ServiceAccountJson en appsettings.json.");

            var credential = GoogleCredential
                .FromFile(jsonPath)
                .CreateScoped(CalendarService.Scope.Calendar);

            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Sounds"
            });
        }

        private string GetCalendarId()
        {
            return _config["GoogleCalendar:CalendarId"] ?? "primary";
        }

        [HttpGet("")]
        public async Task<IActionResult> GetEventos()
        {
            var userId = GetUserId();
            var service = GetCalendarService();
            var calendarId = GetCalendarId();

            var request = service.Events.List(calendarId);
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.TimeMin = DateTime.UtcNow.AddYears(-1);
            request.TimeMax = DateTime.UtcNow.AddYears(2);
            request.PrivateExtendedProperty = $"soundsUserId={userId}";

            var result = await request.ExecuteAsync();

            var eventos = result.Items.Select(e =>
            {
                var inicio = e.Start.DateTimeDateTimeOffset?.DateTime
                             ?? DateTime.Parse(e.Start.Date ?? DateTime.Now.ToString("yyyy-MM-dd"));

                DateTime? fin = null;

                if (e.End != null)
                {
                    fin = e.End.DateTimeDateTimeOffset?.DateTime
                          ?? DateTime.Parse(e.End.Date ?? inicio.ToString("yyyy-MM-dd"));
                }

                var color = "blue";

                if (e.ExtendedProperties?.Private__ != null &&
                    e.ExtendedProperties.Private__.ContainsKey("color"))
                {
                    color = e.ExtendedProperties.Private__["color"];
                }

                return new
                {
                    id = e.Id,
                    titulo = e.Summary ?? "",
                    descripcion = e.Description ?? "",
                    fechaInicio = inicio,
                    fechaFin = fin,
                    color,
                    hora = inicio.ToString("HH:mm")
                };
            }).ToList();

            return Ok(eventos);
        }

        [HttpPost("")]
        public async Task<IActionResult> CrearEvento([FromBody] EventoRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.titulo))
                return BadRequest(new { error = "El título es obligatorio." });

            if (string.IsNullOrWhiteSpace(req.fechaInicio))
                return BadRequest(new { error = "La fecha de inicio es obligatoria." });

            var userId = GetUserId();
            var service = GetCalendarService();
            var calendarId = GetCalendarId();

            var inicio = DateTime.Parse(req.fechaInicio);
            var fin = string.IsNullOrWhiteSpace(req.fechaFin)
                ? inicio.AddHours(1)
                : DateTime.Parse(req.fechaFin);

            if (fin <= inicio)
                fin = inicio.AddHours(1);

            var evento = new Event
            {
                Summary = req.titulo.Trim(),
                Description = req.descripcion ?? "",
                Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = inicio
                },
                End = new EventDateTime
                {
                    DateTimeDateTimeOffset = fin
                },
                ExtendedProperties = new Event.ExtendedPropertiesData
                {
                    Private__ = new Dictionary<string, string>
                    {
                        { "soundsUserId", userId },
                        { "color", req.color ?? "blue" }
                    }
                }
            };

            var creado = await service.Events.Insert(evento, calendarId).ExecuteAsync();

            return Ok(new { id = creado.Id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> EditarEvento(string id, [FromBody] EventoRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.titulo))
                return BadRequest(new { error = "El título es obligatorio." });

            if (string.IsNullOrWhiteSpace(req.fechaInicio))
                return BadRequest(new { error = "La fecha de inicio es obligatoria." });

            var userId = GetUserId();
            var service = GetCalendarService();
            var calendarId = GetCalendarId();

            Event evento;

            try
            {
                evento = await service.Events.Get(calendarId, id).ExecuteAsync();
            }
            catch
            {
                return NotFound(new { error = "Evento no encontrado." });
            }

            var perteneceAlUsuario =
                evento.ExtendedProperties?.Private__ != null &&
                evento.ExtendedProperties.Private__.TryGetValue("soundsUserId", out var ownerId) &&
                ownerId == userId;

            if (!perteneceAlUsuario)
                return NotFound(new { error = "Evento no encontrado o sin permisos." });

            var inicio = DateTime.Parse(req.fechaInicio);
            var fin = string.IsNullOrWhiteSpace(req.fechaFin)
                ? inicio.AddHours(1)
                : DateTime.Parse(req.fechaFin);

            if (fin <= inicio)
                fin = inicio.AddHours(1);

            evento.Summary = req.titulo.Trim();
            evento.Description = req.descripcion ?? "";
            evento.Start = new EventDateTime
            {
                DateTimeDateTimeOffset = inicio
            };
            evento.End = new EventDateTime
            {
                DateTimeDateTimeOffset = fin
            };

            evento.ExtendedProperties ??= new Event.ExtendedPropertiesData();
            evento.ExtendedProperties.Private__ ??= new Dictionary<string, string>();

            evento.ExtendedProperties.Private__["soundsUserId"] = userId;
            evento.ExtendedProperties.Private__["color"] = req.color ?? "blue";

            await service.Events.Update(evento, calendarId, id).ExecuteAsync();

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> BorrarEvento(string id)
        {
            var userId = GetUserId();
            var service = GetCalendarService();
            var calendarId = GetCalendarId();

            Event evento;

            try
            {
                evento = await service.Events.Get(calendarId, id).ExecuteAsync();
            }
            catch
            {
                return Ok();
            }

            var perteneceAlUsuario =
                evento.ExtendedProperties?.Private__ != null &&
                evento.ExtendedProperties.Private__.TryGetValue("soundsUserId", out var ownerId) &&
                ownerId == userId;

            if (!perteneceAlUsuario)
                return Ok();

            await service.Events.Delete(calendarId, id).ExecuteAsync();

            return Ok();
        }
    }

    public class EventoRequest
    {
        public string titulo { get; set; } = "";
        public string? descripcion { get; set; }
        public string fechaInicio { get; set; } = "";
        public string? fechaFin { get; set; }
        public string? color { get; set; }
    }
}