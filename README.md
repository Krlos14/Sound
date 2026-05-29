# SOUNDS

Plataforma web colaborativa desarrollada como Proyecto Final de CFGS Desarrollo de Aplicaciones Multiplataforma (DAM).

Sounds integra comunicación en tiempo real, reuniones multimedia, gestión de notas y organización de eventos dentro de una única aplicación web moderna.

---

## Características principales

- Sistema de autenticación y registro de usuarios.
- Chats en tiempo real mediante SignalR.
- Videollamadas multimedia utilizando WebRTC.
- Compartición de pantalla entre participantes.
- Gestión de grupos y reuniones.
- Sistema de notas compartidas.
- Calendario integrado con Google Calendar API.
- Persistencia de datos mediante PostgreSQL y Supabase.

---

## Tecnologías utilizadas

### Backend
- ASP.NET Core Razor Pages
- ASP.NET Identity
- SignalR
- Entity Framework Core

### Frontend
- HTML
- CSS
- JavaScript

### Multimedia y tiempo real
- WebRTC
- Web APIs (`getUserMedia`, `getDisplayMedia`)

### Base de datos
- PostgreSQL
- Supabase

### APIs externas
- Google Calendar API

---

## Arquitectura del proyecto

El proyecto sigue una arquitectura cliente-servidor basada en ASP.NET Core.

La aplicación se organiza mediante diferentes carpetas encargadas de separar responsabilidades:

```bash
Sounds/
│
├── Areas/
├── Controllers/
├── Data/
├── Hubs/
├── Models/
├── Pages/
├── wwwroot/
├── Utilidades/
└── Program.cs
