using Microsoft.Data.SqlClient;

namespace SistemaNutri.Servicios
{
    public class RecordatoriosComprasService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecordatoriosComprasService> _logger;

        public RecordatoriosComprasService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<RecordatoriosComprasService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    DateTime ahora = DateTime.Now;
                    DateTime proximaEjecucion = ahora.Date.AddHours(9);

                    if (proximaEjecucion <= ahora)
                        proximaEjecucion = proximaEjecucion.AddDays(1);

                    await Task.Delay(proximaEjecucion - ahora, stoppingToken);
                    await EnviarRecordatoriosPendientesAsync(stoppingToken);
                }
                catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error general en RecordatoriosComprasService.");

                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task EnviarRecordatoriosPendientesAsync(CancellationToken stoppingToken)
        {
            string connectionString =
                _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection.");

            var pendientes = new List<SolicitudPendienteRecordatorio>();

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(stoppingToken);

                const string sql = @"
SELECT
    S.SolicitudID,
    ISNULL(
        S.Folio,
        'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)
    ) AS Folio,
    S.EstatusID,
    P.PersonaID,
    LTRIM(RTRIM(
        ISNULL(P.Nombre, '') + ' ' +
        ISNULL(P.ApellidoPaterno, '')
    )) AS Solicitante,
    C.Proveedor,
    C.MontoTotal,
    H.FechaInicioEstado
FROM Compras_Solicitud S
INNER JOIN Usuarios U
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
LEFT JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
OUTER APPLY
(
    SELECT MAX(HP.FechaMovimiento) AS FechaInicioEstado
    FROM Compras_Historico_Pasos HP
    WHERE HP.SolicitudID = S.SolicitudID
      AND HP.EstatusID = S.EstatusID
) H
WHERE S.EstatusID IN (3, 14)
  AND H.FechaInicioEstado IS NOT NULL
  AND H.FechaInicioEstado <= DATEADD(HOUR, -24, GETDATE())
ORDER BY H.FechaInicioEstado ASC;";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(stoppingToken);

                while (await reader.ReadAsync(stoppingToken))
                {
                    pendientes.Add(new SolicitudPendienteRecordatorio
                    {
                        SolicitudID = Convert.ToInt32(reader["SolicitudID"]),
                        EstatusID = Convert.ToInt32(reader["EstatusID"]),
                        PersonaID = Convert.ToInt32(reader["PersonaID"]),
                        Folio = reader["Folio"]?.ToString() ?? "",
                        Solicitante = reader["Solicitante"]?.ToString() ?? "Solicitante",
                        Proveedor = reader["Proveedor"] == DBNull.Value
                            ? "No disponible"
                            : reader["Proveedor"]?.ToString() ?? "No disponible",
                        MontoTotal = reader["MontoTotal"] == DBNull.Value
                            ? 0
                            : Convert.ToDecimal(reader["MontoTotal"])
                    });
                }
            }

            if (!pendientes.Any())
            {
                _logger.LogInformation("Recordatorios Compras: no existen solicitudes pendientes para recordar.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();

            var notificaciones =
                scope.ServiceProvider.GetRequiredService<ServicioNotificaciones>();

            foreach (var solicitud in pendientes)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    if (solicitud.EstatusID == 14)
                    {
                        await EnviarRecordatorioAutorizacionAsync(
                            notificaciones,
                            solicitud
                        );
                    }
                    else if (solicitud.EstatusID == 3)
                    {
                        await EnviarRecordatorioDocumentacionAsync(
                            notificaciones,
                            solicitud
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error enviando recordatorio de Compras. SolicitudID={SolicitudID}, EstatusID={EstatusID}",
                        solicitud.SolicitudID,
                        solicitud.EstatusID
                    );
                }
            }
        }

        private async Task EnviarRecordatorioAutorizacionAsync(
            ServicioNotificaciones notificaciones,
            SolicitudPendienteRecordatorio solicitud)
        {
            string asunto =
                $"Tienes pendiente autorizar tu cotización - {solicitud.Folio}";

            string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
    <div style='max-width:650px; margin:0 auto; background:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
        <div style='padding:20px; background:#f97316; color:#ffffff; text-align:center;'>
            <h2 style='margin:0;'>Cotización pendiente de autorización</h2>
        </div>
        <div style='padding:20px; color:#333333;'>
            <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitud.Solicitante)}</strong>,</p>
            <p>Tu solicitud continúa pendiente de autorización de la cotización dictaminada por Compras.</p>
            <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
                <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(solicitud.Folio)}</p>
                <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(solicitud.Proveedor)}</p>
                <p style='margin:0;'><strong>Monto:</strong> {solicitud.MontoTotal:C}</p>
            </div>
            <p><strong>Acción requerida:</strong> ingresa al módulo de Compras, revisa la cotización y autorízala para continuar el proceso.</p>
            <p>Si la solicitud ya no es necesaria, utiliza la opción <strong>Cancelar solicitud</strong> para evitar mantener procesos abiertos.</p>
            <p>https://intranet.nsgroup.com.mx/</p>
            <p style='color:#666666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
        </div>
    </div>
</body>
</html>";

            await notificaciones.EnviarABccPersonasAsync(
                new List<int> { solicitud.PersonaID },
                asunto,
                html
            );
        }

        private async Task EnviarRecordatorioDocumentacionAsync(
            ServicioNotificaciones notificaciones,
            SolicitudPendienteRecordatorio solicitud)
        {
            string asunto =
                $"Tienes documentación pendiente en Compras - {solicitud.Folio}";

            string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
    <div style='max-width:650px; margin:0 auto; background:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
        <div style='padding:20px; background:#2563eb; color:#ffffff; text-align:center;'>
            <h2 style='margin:0;'>Documentación pendiente</h2>
        </div>
        <div style='padding:20px; color:#333333;'>
            <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitud.Solicitante)}</strong>,</p>
            <p>Tu solicitud de compra continúa esperando documentación de tu parte para poder continuar.</p>
            <div style='background:#eff6ff; border-left:4px solid #2563eb; padding:12px 14px; border-radius:6px; margin:14px 0;'>
                <p style='margin:0;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(solicitud.Folio)}</p>
            </div>
            <p><strong>Acción requerida:</strong> ingresa al detalle de la solicitud, revisa la documentación solicitada y completa el envío a Control Presupuestal.</p>
            <p>Si la solicitud ya no es necesaria, utiliza la opción <strong>Cancelar solicitud</strong> para evitar mantener procesos abiertos.</p>
            <p>https://intranet.nsgroup.com.mx/</p>
            <p style='color:#666666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
        </div>
    </div>
</body>
</html>";

            await notificaciones.EnviarABccPersonasAsync(
                new List<int> { solicitud.PersonaID },
                asunto,
                html
            );
        }

        private class SolicitudPendienteRecordatorio
        {
            public int SolicitudID { get; set; }
            public int EstatusID { get; set; }
            public int PersonaID { get; set; }
            public string Folio { get; set; } = "";
            public string Solicitante { get; set; } = "";
            public string Proveedor { get; set; } = "";
            public decimal MontoTotal { get; set; }
        }
    }
}