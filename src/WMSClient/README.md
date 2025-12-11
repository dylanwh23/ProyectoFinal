# WMSClient (Preview)

Este cliente WMS es un cliente Blazor que muestra sucursales, cámaras por sucursal, vistas previas en formato tarjeta y un visor en tiempo real con eventos y clips guardados.

Características implementadas:
- Listado de sucursales (derivado de `RutaCarpeta` de la cámara o fallback a "General").
- Grid de tarjetas de cámaras con preview en miniatura, estado online/offline y nombre.
- Al hacer click en la tarjeta, abre la vista en tiempo real donde se listan los eventos registrados para esa cámara.
- Al hacer click en un evento, carga los frames del evento y muestra el primer frame en la vista (reproducción de evento simplificada).
- No incluye funcionalidad de alta/edición/eliminación (según el requerimiento).

Archivos agregados:
- `Components/Pages/Dashboard.razor` -> Página principal con sucursales, tarjetas y vista en tiempo real.
- `Components/CameraCard.razor` -> Componente tarjeta para cada cámara.
- `Components/CameraView.razor` -> Vista en tiempo real y lista de eventos.
- `Services/WmsCameraService.cs` -> Servicio para consumir endpoints del backend.
- `Services/IWmsCameraService.cs` -> Interfaz del servicio.
- `wwwroot/css/wmsclient.css` -> Estilos globales del cliente.

Cómo usar:
1. Ejecuta el backend (ImageStreamer.Api / TelnetInterceptor / EventProcessor): `dotnet run` en cada proyecto.
2. Ejecuta WMSClient: `dotnet run --project src/WMSClient/WMSClient.csproj`.
3. Abre `https://localhost:5001` (puerto según su configuración) y deberías ver el dashboard.

Recomendaciones y cambios sugeridos para el backend (no realizados, solo listados):

### 1) Modelo & API de Sucursales (Branches)
- Agregar un modelo `Sucursal` (Branch) y asociar cámaras con `SucursalId` para evitar deducciones por `RutaCarpeta`.
- Endpoint: GET `/api/sucursales` -> retorna lista de sucursales con metadatos (Id, Nombre, Direccion, etc.).
- Endpoint: GET `/api/sucursales/{id}/camaras` -> retorna cámaras asociadas a una sucursal.
- Beneficio: poder listar y filtrar cámaras por sucursal directamente sin inferencia.

### 2) Datos de Cámara (Camera) enriquecidos
- Añadir propiedad `SucursalId` y `ThumbnailPath`/`LastFramePath` a modelo de cámara.
- Endpoint: GET `/api/camaras/estado` debería retornar `CameraDto` con `SucursalId`, `ThumbnailUrl` (para evitar múltiples peticiones de stream), `LastSeen`, `IsOnline`.
- Beneficio: UI más rápida y confiable (miniaturas pre-cachadas).

### 3) Endpoint de Clipes/Frames del Evento
- Endpoint que retorne metadata del clip (startFrame, endFrame, timestamps, totalFrames y URL base) además de la lista de paths: GET `/api/eventos/{eventId}/frames`.
- Incluir `clipId`, `eventId`, `type`, `confidence`, `cameraIp`, `timeStart`, `timeEnd`.

### 4) WebSocket/SignalR para Actualizaciones en Tiempo Real
- Implementar SignalR (o WebSocket) para notificar clientes en tiempo real sobre: nuevos eventos detectados, cambios de estado de cámaras, clips generados.
- Endpoints SignalR: `/hubs/events` con canales por `sucursal` o por `cameraIp`.
- Beneficio: evitar uso excesivo de polling y mejorar latencia en visibilidad de eventos.

### 5) Health & Monitoring
- Ampliar `/api/camaras/health/{ip}` para devolver más información (last frame path, last check timestamp, FPS, latency).
- Añadir endpoint `/api/thumbnail/{ip}` (alias `/api/camaras/thumbnail/{ip}`) que devuelva una `thumbnail` de tamaño configurable (ej. 320x180) para la vista previa.

### 6) Seguridad & Permisos
- Agregar autenticación/authorization al API para controlar acceso a streams y eventos (JWT + claims para sucursal/roles).
- Implementar CORS y verificación de origen para evitar exponer streams a terceros.

### 7) Optimización de consumo y streaming
- Agregar un proxy/forwarder para streams (técnica de multiplexing) para que múltiples clientes no soliciten una carga duplicada del stream origin.
- Implementar caching para thumbs y última imagen para mejorar UX en el dashboard.

### 8) Consistencia y metadatos
- Estandarizar nombres de endpoints, DTOs y rutas. Documentar el contrato Swagger/OpenAPI para facilitar clientes.
- Asegurar que eventos y clips estén normalizados (id único, timestamps UTC, cámara asignada, sucursal, tipo y confidence).

### 9) Telemetría e índices
- Añadir logs y trazabilidad para eventos detectados (kibana/elastic) y métricas (Prometheus).
- Indexar en la DB campos de búsqueda (camera IP, eventId, SucursalId, fecha) para consultas rápidas desde el cliente.

Si quieres, puedo generar los cambios de backend sugeridos en forma de un RFC o tareas concretas (migrations, controllers y DTOs) listos para implementar.
