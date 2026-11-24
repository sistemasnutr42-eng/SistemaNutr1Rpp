using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SistemaNutri.Helpers;
using SistemaNutri.Models;
using SistemaNutri.Servicios;
using System.Data;
using System.Security.Claims;
using static System.Net.WebRequestMethods;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SistemaNutri.Controllers
{
    public class ComprasController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<ComprasController> _logger;
        private readonly ServicioNotificaciones _notif;
        private readonly RutaNas _rutaNas;
        private readonly ISftpStorage _sftp;

        private const int EmpresaNsEquipoId = 2;
        private const int EmpresaNsFortId = 4;

        // Solo se habilita mediante una opcion explicita durante las pruebas locales.
        private static bool CompradoresDemoHabilitados =>
            string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Environment.GetEnvironmentVariable("Compras__CompradoresDemo"), "1", StringComparison.Ordinal);


        public ComprasController(IConfiguration configuration, ILogger<ComprasController> logger, ServicioNotificaciones notif, RutaNas rutaNas, ISftpStorage sftp)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _notif = notif;
            _sftp = sftp;
            _rutaNas = rutaNas;


        }


      
        private async Task<List<SelectListItem>> ObtenerEmpresasPermitidasParaSolicitudAsync(
            SqlConnection conn,
            int usuarioId)
        {
            var empresas = new List<SelectListItem>();

            const string sql = @"
SELECT
    E.EmpresaID,
    E.Nombre
FROM Empresas AS E
WHERE E.Activa = 1
  AND
  (
      -- Empresa directa del usuario.
      EXISTS
      (
          SELECT 1
          FROM UsuariosEmpresas AS UE
          WHERE UE.UsuarioID = @UsuarioID
            AND UE.EmpresaID = E.EmpresaID
            AND UE.Activo = 1
      )

      -- Regla especial:
      -- Si el usuario pertenece a NS EQUIPO, tambien puede solicitar para NS FORT.
      OR
      (
          E.EmpresaID = @EmpresaNsFortId
          AND EXISTS
          (
              SELECT 1
              FROM UsuariosEmpresas AS UEEquipo
              INNER JOIN Empresas AS EEquipo
                  ON EEquipo.EmpresaID = UEEquipo.EmpresaID
              WHERE UEEquipo.UsuarioID = @UsuarioID
                AND UEEquipo.EmpresaID = @EmpresaNsEquipoId
                AND UEEquipo.Activo = 1
                AND EEquipo.Activa = 1
          )
      )
  )
ORDER BY
    CASE
        WHEN E.EmpresaID = @EmpresaNsEquipoId THEN 0
        WHEN E.EmpresaID = @EmpresaNsFortId THEN 1
        ELSE 2
    END,
    E.Nombre;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                cmd.Parameters.AddWithValue("@EmpresaNsEquipoId", EmpresaNsEquipoId);
                cmd.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        empresas.Add(new SelectListItem
                        {
                            Value = reader["EmpresaID"].ToString(),
                            Text = reader["Nombre"]?.ToString() ?? string.Empty
                        });
                    }
                }
            }

            return empresas;
        }

 
        private static int ObtenerEmpresaCompradoresId(int empresaSolicitudId)
        {
            return empresaSolicitudId == EmpresaNsFortId
                ? EmpresaNsEquipoId
                : empresaSolicitudId;
        }

       
        private static bool EmpresaSolicitudPermitidaEnBandeja(
            int empresaActivaId,
            int empresaSolicitudId)
        {
            return empresaSolicitudId == empresaActivaId
                || (empresaActivaId == EmpresaNsEquipoId
                    && empresaSolicitudId == EmpresaNsFortId);
        }



        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = new IndexComprasVm();
            var solicitudes = new List<MisComprasVm>();
            var stats = new ComprasDashboardVm();

            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string puesto = "";

                string sqlPuesto = @"
            SELECT P.Puesto
            FROM Usuarios U
            INNER JOIN Persona P ON U.PersonaID = P.PersonaID
            WHERE U.UsuarioID = @UsuarioID";

                using (var cmdPuesto = new SqlCommand(sqlPuesto, conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                ViewBag.Puesto = puesto;


                string departamentosUsuario = "";

                string sqlDeptos = @"
SELECT STRING_AGG(UPPER(D.NombreDepartamento), ',')
FROM EmpleadoDepartamentos ED
INNER JOIN Departamentos D 
    ON ED.DepartamentoID = D.DepartamentoID
WHERE ED.UsuarioID = @UsuarioID
  AND ED.Activo = 1
  AND D.Activo = 1";

                using (var cmdDeptos = new SqlCommand(sqlDeptos, conn))
                {
                    cmdDeptos.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    departamentosUsuario =
                        (await cmdDeptos.ExecuteScalarAsync())?.ToString() ?? "";
                }

                ViewBag.Puesto = puesto;
                ViewBag.DepartamentosUsuario = departamentosUsuario;


                string sqlTabla = @"
            SELECT S.SolicitudID, S.TipoCompra, S.FechaCreacion, 
                   S.EstatusID, E.Nombre AS EstatusNombre, 
                   Em.Nombre AS NombreEmpresa
            FROM Compras_Solicitud S
            INNER JOIN Cat_EstatusCompra E ON S.EstatusID = E.EstatusID
            INNER JOIN Empresas Em ON S.EmpresaID = Em.EmpresaID
            WHERE S.UsuarioID = @uid
            ORDER BY S.FechaCreacion DESC";

                using (var cmd = new SqlCommand(sqlTabla, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", usuarioId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            solicitudes.Add(new MisComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = "COM-" + reader["SolicitudID"].ToString().PadLeft(5, '0'),
                                Tipo = reader["TipoCompra"].ToString(),
                                Empresa = reader["NombreEmpresa"].ToString(),
                                Fecha = (DateTime)reader["FechaCreacion"],
                                Estatus = reader["EstatusNombre"].ToString(),
                                EstatusID = (int)reader["EstatusID"]
                            });
                        }
                    }
                }

                if (puesto == "DIRECCION COMPRAS")
                {
                    string sqlStats = @"
SELECT 
    (SELECT COUNT(*)
     FROM Compras_Solicitud SU
     INNER JOIN Cat_Urgencia CUrg ON SU.UrgenciaID = CUrg.UrgenciaID
     WHERE SU.EstatusID = 1
       AND ISNULL(CUrg.RequiereAutorizacion, 0) = 1
       AND DATEDIFF(HOUR, SU.FechaCreacion, GETDATE()) > ISNULL(CUrg.HorasObjetivoMax, 24)) AS Criticos,

    (SELECT COUNT(*)
     FROM Compras_Solicitud
     WHERE EstatusID NOT IN (10, 11, 12)) AS SolicitudesActivas,

    (SELECT COUNT(*)
     FROM Compras_Solicitud
     WHERE EstatusID = 14) AS PendientesAutorizacionUsuario,

    (SELECT COUNT(*)
     FROM Compras_Solicitud
     WHERE EstatusID = 13) AS EntregasParciales,

    (SELECT ISNULL(AVG(DATEDIFF(HOUR, Inicio.FechaInicio, Ultimo.FechaUltimoMovimiento)), 0)
     FROM
     (
         SELECT SolicitudID, MIN(FechaMovimiento) AS FechaInicio
         FROM Compras_Historico_Pasos
         WHERE EstatusID = 1
         GROUP BY SolicitudID
     ) Inicio
     INNER JOIN
     (
         SELECT SolicitudID, MAX(FechaMovimiento) AS FechaUltimoMovimiento
         FROM Compras_Historico_Pasos
         GROUP BY SolicitudID
     ) Ultimo
        ON Inicio.SolicitudID = Ultimo.SolicitudID
    ) AS PromedioGlobal,

    ISNULL(AVG(
        CASE 
            WHEN COALESCE(H14.FechaInicio, H3.FechaInicio) IS NOT NULL
                THEN DATEDIFF(HOUR, S.FechaCreacion, COALESCE(H14.FechaInicio, H3.FechaInicio))
            WHEN S.EstatusID IN (1, 2)
                THEN DATEDIFF(HOUR, S.FechaCreacion, GETDATE())
            ELSE NULL
        END
    ), 0) AS PromCompras,

    ISNULL(AVG(
        CASE
            WHEN H14.FechaInicio IS NOT NULL AND H3.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H14.FechaInicio, H3.FechaInicio)
            WHEN S.EstatusID = 14 AND H14.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H14.FechaInicio, GETDATE())
            ELSE NULL
        END
    ), 0) AS PromAutorizacionUsuario,

    ISNULL(AVG(
        CASE 
            WHEN S.FechaDictamen IS NOT NULL AND H4.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H4.FechaInicio, S.FechaDictamen)
            WHEN S.EstatusID = 4 AND H4.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H4.FechaInicio, GETDATE())
            ELSE NULL
        END
    ), 0) AS PromPresupuesto,

    ISNULL(AVG(
        CASE 
            WHEN OC.FechaOC IS NOT NULL AND H5.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H5.FechaInicio, OC.FechaOC)
            WHEN S.EstatusID = 5 AND H5.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H5.FechaInicio, GETDATE())
            ELSE NULL
        END
    ), 0) AS PromOC,

    ISNULL(AVG(
        CASE 
            WHEN UPPER(ISNULL(S.TipoGasto, '')) NOT IN ('REQUISICION', 'REQUISICIÓN')
                 AND OC.FechaEnvioProveedor IS NOT NULL
                 AND EU.FechaEntrega IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, EU.FechaEntrega)

            WHEN UPPER(ISNULL(S.TipoGasto, '')) NOT IN ('REQUISICION', 'REQUISICIÓN')
                 AND S.EstatusID IN (7, 9, 13)
                 AND OC.FechaEnvioProveedor IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, GETDATE())

            ELSE NULL
        END
    ), 0) AS PromProveedor,

    ISNULL(AVG(
        CASE 
            WHEN UPPER(ISNULL(S.TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN')
                 AND OC.FechaEnvioProveedor IS NOT NULL
                 AND R.FechaRecepcion IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, R.FechaRecepcion)

            WHEN UPPER(ISNULL(S.TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN')
                 AND S.EstatusID IN (8, 13)
                 AND OC.FechaEnvioProveedor IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, GETDATE())

            ELSE NULL
        END
    ), 0) AS PromAlmacen

FROM Compras_Solicitud S

OUTER APPLY (
    SELECT MAX(FechaMovimiento) AS FechaInicio
    FROM Compras_Historico_Pasos
    WHERE SolicitudID = S.SolicitudID
      AND EstatusID = 14
) H14

OUTER APPLY (
    SELECT MIN(FechaMovimiento) AS FechaInicio
    FROM Compras_Historico_Pasos
    WHERE SolicitudID = S.SolicitudID
      AND EstatusID = 3
      AND (H14.FechaInicio IS NULL OR FechaMovimiento >= H14.FechaInicio)
) H3

OUTER APPLY (
    SELECT MIN(FechaMovimiento) AS FechaInicio
    FROM Compras_Historico_Pasos
    WHERE SolicitudID = S.SolicitudID
      AND EstatusID = 4
) H4

OUTER APPLY (
    SELECT MIN(FechaMovimiento) AS FechaInicio
    FROM Compras_Historico_Pasos
    WHERE SolicitudID = S.SolicitudID
      AND EstatusID = 5
) H5

OUTER APPLY (
    SELECT TOP 1 FechaOC, FechaEnvioProveedor
    FROM Compras_OrdenCompra
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY OrdenCompraID DESC
) OC

OUTER APPLY (
    SELECT TOP 1 FechaRecepcion
    FROM Compras_Recepciones
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY RecepcionID DESC
) R

OUTER APPLY (
    SELECT TOP 1 FechaEntrega
    FROM Compras_EntregasUsuario
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY EntregaID DESC
) EU

WHERE S.EstatusID NOT IN (11, 12)";

                    using (var cmd = new SqlCommand(sqlStats, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            stats.CriticosVencidos = Convert.ToInt32(reader["Criticos"]);
                            stats.SolicitudesActivas = Convert.ToInt32(reader["SolicitudesActivas"]);
                            stats.PendientesAutorizacionUsuario = Convert.ToInt32(reader["PendientesAutorizacionUsuario"]);
                            stats.EntregasParciales = Convert.ToInt32(reader["EntregasParciales"]);
                            stats.PromedioTotal = Convert.ToDouble(reader["PromedioGlobal"]);

                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromCompras"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromAutorizacionUsuario"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromPresupuesto"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromOC"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromProveedor"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromAlmacen"]));
                        }
                    }

                    string sqlDona = @"
SELECT
    E.EstatusID,
    E.Nombre,
    COUNT(S.SolicitudID) AS Total
FROM Cat_EstatusCompra E
INNER JOIN Compras_Solicitud S
    ON E.EstatusID = S.EstatusID
GROUP BY E.EstatusID, E.Nombre
HAVING COUNT(S.SolicitudID) > 0
ORDER BY E.EstatusID;";

                    using (var cmd = new SqlCommand(sqlDona, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int estatusId = Convert.ToInt32(reader["EstatusID"]);

                            stats.DonaEstatusIds.Add(estatusId);
                            stats.DonaEtiquetas.Add(reader["Nombre"].ToString() ?? string.Empty);
                            stats.DonaValores.Add(Convert.ToDecimal(reader["Total"]));
                            stats.DonaColores.Add(estatusId switch
                            {
                                1 => "#64748B",
                                2 => "#0891B2",
                                3 => "#0EA5E9",
                                4 => "#7C3AED",
                                5 => "#2563EB",
                                6 => "#DB2777",
                                7 => "#0284C7",
                                8 => "#475569",
                                9 => "#0F766E",
                                10 => "#16A34A",
                                11 => "#DC2626",
                                12 => "#334155",
                                13 => "#F97316",
                                14 => "#9333EA",
                                _ => "#94A3B8"
                            });
                        }
                    }
                }
            }

            model.MisCompras = solicitudes;
            model.Estadisticas = stats;

            return View(model);
        }



        //METODO GET PARA CREAR NUEVAS SOLICITUDES

        [HttpGet("NuevaSolicitud")]
        [HttpGet("Compras/NuevaSolicitud")]
        public async Task<IActionResult> NuevaSolicitud()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                ViewBag.Empresas = await ObtenerEmpresasPermitidasParaSolicitudAsync(conn, usuarioId);

                var urgencias = new List<SelectListItem>();
                var categoriasCompraInfo = new List<CategoriaCompraInfoVm>();

                string sqlUrg = @"
SELECT UrgenciaID, Descripcion, TiempoObjetivo, Ejemplos, RequiereAutorizacion
FROM Cat_Urgencia
WHERE Activo = 1
ORDER BY ISNULL(OrdenVisual, 999), UrgenciaID";

                using (var cmdU = new SqlCommand(sqlUrg, conn))
                using (var readerU = await cmdU.ExecuteReaderAsync())
                {
                    while (await readerU.ReadAsync())
                    {
                        int categoriaId = Convert.ToInt32(readerU["UrgenciaID"]);
                        string descripcion = readerU["Descripcion"]?.ToString() ?? string.Empty;
                        string tiempoObjetivo = readerU["TiempoObjetivo"]?.ToString() ?? string.Empty;

                        urgencias.Add(new SelectListItem
                        {
                            Value = categoriaId.ToString(),
                            Text = descripcion + " — " + tiempoObjetivo
                        });

                        categoriasCompraInfo.Add(new CategoriaCompraInfoVm
                        {
                            UrgenciaID = categoriaId,
                            Descripcion = descripcion,
                            TiempoObjetivo = tiempoObjetivo,
                            Ejemplos = readerU["Ejemplos"]?.ToString() ?? string.Empty,
                            RequiereAutorizacion = readerU["RequiereAutorizacion"] != DBNull.Value && Convert.ToBoolean(readerU["RequiereAutorizacion"])
                        });
                    }
                }

                ViewBag.Urgencias = urgencias;
                ViewBag.CategoriasCompraInfo = categoriasCompraInfo;
            }

            var model = new CompraViewModel
            {
                Materiales = new List<MaterialItem>()
            };

            return View(model);
        }

        [HttpPost("NuevaSolicitud")]
        [HttpPost("Compras/NuevaSolicitud")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NuevaSolicitud(CompraViewModel model)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            if (model.Materiales == null || !model.Materiales.Any())
            {
                ModelState.AddModelError("", "Debes agregar al menos un material a la lista.");
            }
            else
            {
                for (int i = 0; i < model.Materiales.Count; i++)
                {
                    var material = model.Materiales[i];
                    if (string.IsNullOrWhiteSpace(material.Nombre))
                        ModelState.AddModelError($"Materiales[{i}].Nombre", "El producto es obligatorio.");
                    if (string.IsNullOrWhiteSpace(material.Descripcion))
                        ModelState.AddModelError($"Materiales[{i}].Descripcion", "La descripción del producto es obligatoria.");
                    if (material.Cantidad <= 0)
                        ModelState.AddModelError($"Materiales[{i}].Cantidad", "La cantidad debe ser mayor a cero.");
                }
            }

            if (model.TipoCompra != "Nacional" && model.TipoCompra != "Internacional")
                ModelState.AddModelError("TipoCompra", "Selecciona un tipo de compra válido.");

            if (model.ClasificacionSolicitud != "COMPRA" &&
                model.ClasificacionSolicitud != "GASTO_MANTENIMIENTO")
                ModelState.AddModelError("ClasificacionSolicitud", "Selecciona una clasificación válida.");
            if (model.DestinoEntrega != "ALMACEN" && model.DestinoEntrega != "GRANJA" &&
                model.DestinoEntrega != "USUARIO")
                ModelState.AddModelError("DestinoEntrega", "Selecciona un destino válido.");
            if (model.DestinoEntrega == "GRANJA" && string.IsNullOrWhiteSpace(model.GranjaDestino))
                ModelState.AddModelError("GranjaDestino", "Indica la granja destino.");
            if (!string.IsNullOrWhiteSpace(model.GranjaDestino) && model.GranjaDestino.Length > 180)
                ModelState.AddModelError("GranjaDestino", "La granja debe tener menos de 180 caracteres.");
            if (model.ProductoNuevo && string.IsNullOrWhiteSpace(model.DatosTecnicosProducto))
                ModelState.AddModelError("DatosTecnicosProducto", "Un producto nuevo requiere especificaciones técnicas.");
            if (!string.IsNullOrWhiteSpace(model.DatosTecnicosProducto) && model.DatosTecnicosProducto.Length > 4000)
                ModelState.AddModelError("DatosTecnicosProducto", "Las especificaciones no deben exceder 4,000 caracteres.");

            if (string.IsNullOrWhiteSpace(model.NombreProyecto))
            {
                ModelState.AddModelError("NombreProyecto", "Debes capturar el nombre del proyecto.");
            }

            // El presupuesto es opcional. Si el usuario captura un monto, debe ser mayor a cero.
            if (model.MontoPresupuestoSolicitado.HasValue &&
                model.MontoPresupuestoSolicitado.Value <= 0)
            {
                ModelState.AddModelError(
                    "MontoPresupuestoSolicitado",
                    "Si capturas un presupuesto, el monto debe ser mayor a cero."
                );
            }

           
            // Este documento se exige en la pantalla cuando la compra está fuera de presupuesto.
            // Anteriormente se mostraba en el formulario, pero el POST ignoraba el archivo.
            var extensionesDesviacionPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".pdf", ".jpg", ".jpeg", ".png", ".webp"
            };
            if (model.FueraPresupuestoUsuario &&
                (model.ArchivoDesviacion == null || model.ArchivoDesviacion.Length == 0))
            {
                ModelState.AddModelError("ArchivoDesviacion",
                    "Una compra fuera de presupuesto requiere adjuntar el formato de desviación firmado.");
            }
            if (model.ArchivoDesviacion != null && model.ArchivoDesviacion.Length > 0 &&
                !extensionesDesviacionPermitidas.Contains(
                    Path.GetExtension(model.ArchivoDesviacion.FileName)))
            {
                ModelState.AddModelError("ArchivoDesviacion",
                    "La desviación solo puede ser PDF o imagen JPG, PNG o WEBP.");
            }

            bool requiereAutorizacionUrgente =
                await EsUrgenciaConAutorizacionAsync(model.UrgenciaID);

            var extensionesAutorizacionUrgentePermitidas = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ".pdf",
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

            if (requiereAutorizacionUrgente)
            {
                if (model.ArchivoAutorizacionUrgente == null ||
                    model.ArchivoAutorizacionUrgente.Length == 0)
                {
                    ModelState.AddModelError(
                        "ArchivoAutorizacionUrgente",
                        "Las compras urgentes requieren adjuntar el archivo de autorización."
                    );
                }
                else
                {
                    string extensionAutorizacion =
                        Path.GetExtension(model.ArchivoAutorizacionUrgente.FileName)
                            ?.ToLowerInvariant() ?? string.Empty;

                    if (!extensionesAutorizacionUrgentePermitidas.Contains(extensionAutorizacion))
                    {
                        ModelState.AddModelError(
                            "ArchivoAutorizacionUrgente",
                            "La autorización de compra urgente solo puede ser PDF o imagen JPG, PNG o WEBP."
                        );
                    }
                }
            }

           
            var extensionesReferenciaPermitidas = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ".pdf",
                ".jpg",
                ".jpeg",
                ".png",
                ".webp",
                ".xlsx",
                ".xls"
            };

            if (model.Materiales != null)
            {
                for (int i = 0; i < model.Materiales.Count; i++)
                {
                    var archivoReferencia = model.Materiales[i].ArchivoReferencia;

                    if (archivoReferencia == null || archivoReferencia.Length == 0)
                        continue;

                    string extension = Path.GetExtension(archivoReferencia.FileName)
                        ?.ToLowerInvariant() ?? string.Empty;

                    if (!extensionesReferenciaPermitidas.Contains(extension))
                    {
                        ModelState.AddModelError(
                            $"Materiales[{i}].ArchivoReferencia",
                            $"El archivo de referencia del producto {i + 1} solo puede ser PDF, imagen JPG, PNG o WEBP, o Excel XLS/XLSX."
                        );
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(usuarioId);
                return View(model);
            }

            string puestoAsignado = model.TipoCompra == "Nacional"
                ? "Comprador Nacional"
                : "Comprador Internacional";

            string nombreSolicitante = User.Identity.Name ?? "Sistema";

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            bool empresaPermitida = await UsuarioTieneEmpresaAsync(
                                conn,
                                transaction,
                                usuarioId,
                                model.EmpresaID
                            );

                            if (!empresaPermitida)
                            {
                                transaction.Rollback();
                                return Forbid();
                            }
                            string sqlSolicitud = @"
INSERT INTO Compras_Solicitud 
(
    UsuarioID,    EmpresaID,    TipoCompra,    EsProyecto,    NombreProyecto,   UrgenciaID,    TransporteID,    ComentariosExtra,
    FechaCreacion,   EstatusID,    PuestoAsignado,    MontoPresupuestoSolicitado,    FueraPresupuestoUsuario,
    ClasificacionSolicitud, DestinoEntrega, GranjaDestino, ProductoNuevo, DatosTecnicosProducto
) 
VALUES 
(   @uid,    @eid,
    @tipo,    @esp,    @nom,    @urg,    @trans,    @com,    GETDATE(),    1,
    @puesto,   @MontoPresupuestoSolicitado,
    @FueraPresupuestoUsuario,
    @ClasificacionSolicitud, @DestinoEntrega, @GranjaDestino, @ProductoNuevo, @DatosTecnicosProducto
); 

SELECT SCOPE_IDENTITY();";

                            int nuevaSolicitudId;

                            using (var cmd = new SqlCommand(sqlSolicitud, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@uid", usuarioId);
                                cmd.Parameters.AddWithValue("@eid", model.EmpresaID);
                                cmd.Parameters.AddWithValue("@tipo", model.TipoCompra);
                                cmd.Parameters.AddWithValue("@esp", model.EsProyecto);
                                cmd.Parameters.AddWithValue("@nom", (object)model.NombreProyecto ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@urg", model.UrgenciaID);
                                cmd.Parameters.AddWithValue("@trans", (object)model.TransporteID ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@com", (object)model.Comentarios ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@puesto", puestoAsignado);
                                var parametroPresupuesto = new SqlParameter(
                                    "@MontoPresupuestoSolicitado",
                                    SqlDbType.Decimal
                                )
                                {
                                    Precision = 18,
                                    Scale = 2,
                                    Value = model.MontoPresupuestoSolicitado.HasValue
                                        ? model.MontoPresupuestoSolicitado.Value
                                        : DBNull.Value
                                };

                                cmd.Parameters.Add(parametroPresupuesto);
                                cmd.Parameters.AddWithValue("@FueraPresupuestoUsuario", model.FueraPresupuestoUsuario);
                                cmd.Parameters.Add("@ClasificacionSolicitud", SqlDbType.NVarChar, 40).Value = model.ClasificacionSolicitud;
                                cmd.Parameters.Add("@DestinoEntrega", SqlDbType.NVarChar, 24).Value = model.DestinoEntrega;
                                cmd.Parameters.Add("@GranjaDestino", SqlDbType.NVarChar, 180).Value =
                                    string.IsNullOrWhiteSpace(model.GranjaDestino) ? DBNull.Value : (object)model.GranjaDestino.Trim();
                                cmd.Parameters.Add("@ProductoNuevo", SqlDbType.Bit).Value = model.ProductoNuevo;
                                cmd.Parameters.Add("@DatosTecnicosProducto", SqlDbType.NVarChar, 4000).Value =
                                    string.IsNullOrWhiteSpace(model.DatosTecnicosProducto) ? DBNull.Value : (object)model.DatosTecnicosProducto.Trim();

                                nuevaSolicitudId = Convert.ToInt32(await cmd.ExecuteScalarAsync());


                            }

                            // Asociamos la desviación inicial a la solicitud, antes de tener
                            // una cotización seleccionada (CotizacionID = NULL).
                            if (model.FueraPresupuestoUsuario &&
                                model.ArchivoDesviacion != null &&
                                model.ArchivoDesviacion.Length > 0)
                            {
                                await GuardarArchivoCompraAsync(
                                    conn,
                                    transaction,
                                    nuevaSolicitudId,
                                    null,
                                    "DESVIACION",
                                    model.ArchivoDesviacion,
                                    usuarioId,
                                    _rutaNas.ObtenerRutaComprasDesviaciones(),
                                    "DesviacionInicial"
                                );
                            }

                            // Guardamos la autorización urgente en Compras_Archivos.
                            // No se requiere una columna nueva en Compras_Solicitud.
                            if (requiereAutorizacionUrgente &&
                                model.ArchivoAutorizacionUrgente != null &&
                                model.ArchivoAutorizacionUrgente.Length > 0)
                            {
                                await GuardarArchivoCompraAsync(
                                    conn,
                                    transaction,
                                    nuevaSolicitudId,
                                    null,
                                    "AUTORIZACION_URGENTE",
                                    model.ArchivoAutorizacionUrgente,
                                    usuarioId,
                                    _rutaNas.ObtenerRutaSolicitudesCompras(),
                                    "AutorizacionUrgente"
                                );
                            }



                            if (model.Materiales != null && model.Materiales.Count > 0)
                            {
                                int numeroMaterial = 0;

                                foreach (var mat in model.Materiales)
                                {
                                    numeroMaterial++;

                                    string sqlMat = @"
INSERT INTO Compras_Detalle_Materiales 
(
    SolicitudID,
    NombreMaterial,
    Descripcion,
    Cantidad,
    UnidadMedida
) 
VALUES 
(
    @sid,
    @nombre,
    @descripcion,
    @cantidad,
    @unidad
);

SELECT SCOPE_IDENTITY();";

                                    int detalleId;

                                    using (var cmdMat = new SqlCommand(sqlMat, conn, transaction))
                                    {
                                        cmdMat.Parameters.AddWithValue("@sid", nuevaSolicitudId);
                                        cmdMat.Parameters.AddWithValue("@nombre", (object?)mat.Nombre ?? DBNull.Value);
                                        cmdMat.Parameters.AddWithValue("@descripcion", (object?)mat.Descripcion ?? DBNull.Value);
                                        cmdMat.Parameters.AddWithValue("@cantidad", mat.Cantidad);
                                        cmdMat.Parameters.AddWithValue("@unidad", (object?)mat.UnidadMedida ?? DBNull.Value);

                                        detalleId = Convert.ToInt32(await cmdMat.ExecuteScalarAsync());
                                    }

                                    if (mat.ArchivoReferencia != null && mat.ArchivoReferencia.Length > 0)
                                    {
                                        string extension = Path.GetExtension(mat.ArchivoReferencia.FileName)
                                            ?.ToLowerInvariant() ?? string.Empty;

                                        // Validación defensiva. Normalmente este caso ya fue detectado
                                        // antes de abrir la conexión y comenzar la transacción.
                                        if (!extensionesReferenciaPermitidas.Contains(extension))
                                        {
                                            transaction.Rollback();

                                            ModelState.AddModelError(
                                                $"Materiales[{numeroMaterial - 1}].ArchivoReferencia",
                                                $"El archivo de referencia del producto {numeroMaterial} solo puede ser PDF, imagen JPG, PNG o WEBP, o Excel XLS/XLSX."
                                            );

                                            await CargarCatalogosAsync(usuarioId);
                                            return View(model);
                                        }

                                        string rutaContenedor = _rutaNas.ObtenerRutaSolicitudesCompras();

                                        _sftp.AsegurarDirectorio(rutaContenedor);

                                        string folioStr = nuevaSolicitudId.ToString().PadLeft(5, '0');
                                        string nombreOriginal = Path.GetFileName(mat.ArchivoReferencia.FileName);

                                        string nombreArchivo =
                                            $"COM-{folioStr}_Producto_{numeroMaterial}_Referencia_{Guid.NewGuid()}{extension}";

                                        string rutaArchivoSftp = $"{rutaContenedor}/{nombreArchivo}";

                                        using (var stream = mat.ArchivoReferencia.OpenReadStream())
                                        {
                                            _sftp.SubirStream(stream, rutaArchivoSftp);
                                        }

                                        string sqlUpdateArchivo = @"
UPDATE Compras_Detalle_Materiales
SET ArchivoReferenciaPath = @ArchivoReferenciaPath,
    NombreArchivoReferencia = @NombreArchivoReferencia,
    ExtensionArchivoReferencia = @ExtensionArchivoReferencia,
    ContentTypeArchivoReferencia = @ContentTypeArchivoReferencia,
    TamanoArchivoReferencia = @TamanoArchivoReferencia
WHERE DetalleID = @DetalleID";

                                        using (var cmdArchivo = new SqlCommand(sqlUpdateArchivo, conn, transaction))
                                        {
                                            cmdArchivo.Parameters.AddWithValue("@ArchivoReferenciaPath", rutaArchivoSftp);
                                            cmdArchivo.Parameters.AddWithValue("@NombreArchivoReferencia", nombreOriginal);
                                            cmdArchivo.Parameters.AddWithValue("@ExtensionArchivoReferencia", extension);
                                            cmdArchivo.Parameters.AddWithValue("@ContentTypeArchivoReferencia", mat.ArchivoReferencia.ContentType ?? "application/octet-stream");
                                            cmdArchivo.Parameters.AddWithValue("@TamanoArchivoReferencia", mat.ArchivoReferencia.Length);
                                            cmdArchivo.Parameters.AddWithValue("@DetalleID", detalleId);

                                            await cmdArchivo.ExecuteNonQueryAsync();
                                        }
                                    }
                                }
                            }


                            int? compradorAsignadoId = await ObtenerCompradorConMenosCargaAsync(
                                conn,
                                transaction,
                                puestoAsignado,
                                model.EmpresaID
                            );

                            if (compradorAsignadoId.HasValue)
                            {
                                string sqlAsignar = @"
                            UPDATE Compras_Solicitud
                            SET CompradorAsignadoUsuarioID = @CompradorID,
                                FechaAsignacionComprador = GETDATE(),
                                UsuarioAsignoCompradorID = @UsuarioAsignoID
                            WHERE SolicitudID = @SolicitudID";

                                using (var cmdAsignar = new SqlCommand(sqlAsignar, conn, transaction))
                                {
                                    cmdAsignar.Parameters.AddWithValue("@CompradorID", compradorAsignadoId.Value);
                                    cmdAsignar.Parameters.AddWithValue("@UsuarioAsignoID", usuarioId);
                                    cmdAsignar.Parameters.AddWithValue("@SolicitudID", nuevaSolicitudId);

                                    await cmdAsignar.ExecuteNonQueryAsync();
                                }

                                string sqlHistAsignacion = @"
                            INSERT INTO Compras_Asignaciones_Historico
                            (SolicitudID, UsuarioAsignadoAnteriorID, UsuarioAsignadoNuevoID, UsuarioDireccionID, FechaAsignacion, Comentario)
                            VALUES
                            (@SolicitudID, NULL, @NuevoID, @DireccionID, GETDATE(), @Comentario)";

                                using (var cmdHistAsig = new SqlCommand(sqlHistAsignacion, conn, transaction))
                                {
                                    cmdHistAsig.Parameters.AddWithValue("@SolicitudID", nuevaSolicitudId);
                                    cmdHistAsig.Parameters.AddWithValue("@NuevoID", compradorAsignadoId.Value);
                                    cmdHistAsig.Parameters.AddWithValue("@DireccionID", usuarioId);
                                    cmdHistAsig.Parameters.AddWithValue("@Comentario", "Asignación automática por menor carga");

                                    await cmdHistAsig.ExecuteNonQueryAsync();
                                }
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "No se encontró comprador activo para autoasignar la solicitud {SolicitudID}. EmpresaID: {EmpresaID}. Puesto: {Puesto}",
                                    nuevaSolicitudId,
                                    model.EmpresaID,
                                    puestoAsignado
                                );
                            }

                            string sqlHistorico = @"
                        INSERT INTO Compras_Historico_Pasos 
                        (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
                        VALUES 
                        (@sid, 1, GETDATE(), @responsable)";

                            using (var cmdHist = new SqlCommand(sqlHistorico, conn, transaction))
                            {
                                cmdHist.Parameters.AddWithValue("@sid", nuevaSolicitudId);
                                cmdHist.Parameters.AddWithValue("@responsable", nombreSolicitante);

                                await cmdHist.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();

                            await NotificarComprador_NuevaSolicitudAsync(nuevaSolicitudId);

                            TempData["Mensaje"] =
                                "Solicitud creada con éxito. Folio: COM-" +
                                nuevaSolicitudId.ToString().PadLeft(5, '0');

                            return RedirectToAction("Index");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError(ex, "Error en la transacción de NuevaSolicitud");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico al crear solicitud");
                ModelState.AddModelError("", "Ocurrió un error técnico: " + ex.Message);

                await CargarCatalogosAsync(usuarioId);
                return View(model);
            }
        }

        [HttpPost("CancelarSolicitud")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarSolicitud(int solicitudId, string motivoCancelacion)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            motivoCancelacion = motivoCancelacion?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(motivoCancelacion))
            {
                TempData["Error"] = "Debes indicar el motivo de la cancelación.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            if (motivoCancelacion.Length > 500)
            {
                TempData["Error"] = "El motivo de cancelación no puede superar los 500 caracteres.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            bool perteneceCompras = await UsuarioPerteneceADepartamentoAsync(conn, usuarioId, "COMPRAS");
            bool esDireccionCompras = false;
            int? empresaActivaId = null;

            if (perteneceCompras)
            {
                empresaActivaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioId);

                const string sqlPuesto = @"
SELECT P.Puesto
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE U.UsuarioID = @UsuarioID";

                using var cmdPuesto = new SqlCommand(sqlPuesto, conn);
                cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioId);

                string puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString()?.Trim().ToUpper() ?? "";

                esDireccionCompras =
                    puesto == "DIRECCION COMPRAS" ||
                    puesto == "DIRECCIÓN COMPRAS";
            }

            using var trans = conn.BeginTransaction();

            try
            {
                int usuarioSolicitanteId;
                int empresaSolicitudId;
                int estatusActual;
                int? compradorAsignadoUsuarioId;
                string folio;

                const string sqlSolicitud = @"
SELECT
    S.UsuarioID,
    S.EmpresaID,
    S.EstatusID,
    S.CompradorAsignadoUsuarioID,
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio
FROM Compras_Solicitud S WITH (UPDLOCK, ROWLOCK)
WHERE S.SolicitudID = @SolicitudID";

                using (var cmdSolicitud = new SqlCommand(sqlSolicitud, conn, trans))
                {
                    cmdSolicitud.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var reader = await cmdSolicitud.ExecuteReaderAsync();

                    if (!await reader.ReadAsync())
                    {
                        trans.Rollback();
                        TempData["Error"] = "No se encontró la solicitud.";
                        return RedirectToAction("Index");
                    }

                    usuarioSolicitanteId = Convert.ToInt32(reader["UsuarioID"]);
                    empresaSolicitudId = Convert.ToInt32(reader["EmpresaID"]);
                    estatusActual = Convert.ToInt32(reader["EstatusID"]);
                    compradorAsignadoUsuarioId = reader["CompradorAsignadoUsuarioID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(reader["CompradorAsignadoUsuarioID"]);
                    folio = reader["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                }

                if (estatusActual == 10)
                {
                    trans.Rollback();
                    TempData["Error"] = "La solicitud ya está cerrada y no puede cancelarse.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                if (estatusActual == 11)
                {
                    trans.Rollback();
                    TempData["Error"] = "La solicitud ya fue rechazada por Control Presupuestal.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                if (estatusActual == 12)
                {
                    trans.Rollback();
                    TempData["Error"] = "La solicitud ya está cancelada.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                bool esSolicitante = usuarioSolicitanteId == usuarioId;
                bool esCompradorAsignado = perteneceCompras &&
                                           compradorAsignadoUsuarioId.HasValue &&
                                           compradorAsignadoUsuarioId.Value == usuarioId;

                bool empresaPermitidaDireccion = perteneceCompras &&
                                                 esDireccionCompras &&
                                                 empresaActivaId.HasValue &&
                                                 EmpresaSolicitudPermitidaEnBandeja(
                                                     empresaActivaId.Value,
                                                     empresaSolicitudId
                                                 );

                bool puedeCancelar = esSolicitante || esCompradorAsignado || empresaPermitidaDireccion;

                if (!puedeCancelar)
                {
                    trans.Rollback();
                    return Forbid();
                }

                const string sqlCancelar = @"
UPDATE Compras_Solicitud
SET EstatusID = 12,
    MotivoCancelacion = @MotivoCancelacion,
    FechaCancelacion = GETDATE(),
    UsuarioCancelacionID = @UsuarioCancelacionID
WHERE SolicitudID = @SolicitudID
  AND EstatusID NOT IN (10, 11, 12)";

                using (var cmdCancelar = new SqlCommand(sqlCancelar, conn, trans))
                {
                    cmdCancelar.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdCancelar.Parameters.AddWithValue("@MotivoCancelacion", motivoCancelacion);
                    cmdCancelar.Parameters.AddWithValue("@UsuarioCancelacionID", usuarioId);

                    int filas = await cmdCancelar.ExecuteNonQueryAsync();

                    if (filas == 0)
                    {
                        trans.Rollback();
                        TempData["Error"] = "La solicitud cambió de estatus y ya no puede cancelarse.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }

                const string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(
    SolicitudID,
    EstatusID,
    FechaMovimiento,
    UsuarioResponsable
)
VALUES
(
    @SolicitudID,
    12,
    GETDATE(),
    @Responsable
)";

                using (var cmdHistorico = new SqlCommand(sqlHistorico, conn, trans))
                {
                    cmdHistorico.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdHistorico.Parameters.AddWithValue(
                        "@Responsable",
                        User.Identity?.Name ?? $"Usuario {usuarioId}"
                    );

                    await cmdHistorico.ExecuteNonQueryAsync();
                }

                trans.Commit();

                _logger.LogInformation(
                    "Solicitud {SolicitudID} cancelada por UsuarioID {UsuarioID}. Motivo: {Motivo}",
                    solicitudId,
                    usuarioId,
                    motivoCancelacion
                );

                TempData["Mensaje"] = $"La solicitud {folio} fue cancelada correctamente.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }
            catch (Exception ex)
            {
                trans.Rollback();

                _logger.LogError(
                    ex,
                    "Error al cancelar la solicitud {SolicitudID}. UsuarioID={UsuarioID}",
                    solicitudId,
                    usuarioId
                );

                TempData["Error"] = "Ocurrió un error al cancelar la solicitud.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }
        }

        //metodo pricado para obtener a un comprador 
        private async Task<int?> ObtenerCompradorConMenosCargaAsync(
       SqlConnection conn,
       SqlTransaction trans,
       string puestoComprador,
       int empresaId)
        {
            int empresaCompradoresId = ObtenerEmpresaCompradoresId(empresaId);

            string sql = @"
SELECT TOP 1 U.UsuarioID
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN UsuariosEmpresas UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaID
LEFT JOIN Compras_Solicitud S
    ON S.CompradorAsignadoUsuarioID = U.UsuarioID
   AND S.EstatusID = 1
WHERE P.EsColaboradorActivo = 1
  AND UPPER(P.Puesto) = UPPER(@Puesto)
GROUP BY U.UsuarioID
ORDER BY COUNT(S.SolicitudID) ASC, U.UsuarioID ASC";

            using (var cmd = new SqlCommand(sql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@Puesto", puestoComprador);
                cmd.Parameters.AddWithValue("@EmpresaID", empresaCompradoresId);

                var result = await cmd.ExecuteScalarAsync();

                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToInt32(result);
            }
        }
        private async Task<bool> UsuarioTieneEmpresaAsync(
       SqlConnection conn,
       SqlTransaction trans,
       int usuarioId,
       int empresaId)
        {
            const string sql = @"
SELECT COUNT(*)
FROM Empresas AS EDestino
WHERE EDestino.EmpresaID = @EmpresaID
  AND EDestino.Activa = 1
  AND
  (
      EXISTS
      (
          SELECT 1
          FROM UsuariosEmpresas AS UE
          WHERE UE.UsuarioID = @UsuarioID
            AND UE.EmpresaID = @EmpresaID
            AND UE.Activo = 1
      )
      OR
      (
          @EmpresaID = @EmpresaNsFortId
          AND EXISTS
          (
              SELECT 1
              FROM UsuariosEmpresas AS UEEquipo
              INNER JOIN Empresas AS EEquipo
                  ON EEquipo.EmpresaID = UEEquipo.EmpresaID
              WHERE UEEquipo.UsuarioID = @UsuarioID
                AND UEEquipo.EmpresaID = @EmpresaNsEquipoId
                AND UEEquipo.Activo = 1
                AND EEquipo.Activa = 1
          )
      )
  );";

            using (var cmd = new SqlCommand(sql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
                cmd.Parameters.AddWithValue("@EmpresaNsEquipoId", EmpresaNsEquipoId);
                cmd.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);

                int total = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return total > 0;
            }
        }


        // Autorizacion por recurso: pertenencia a la solicitud y empresa; no equivale a
        // mostrar el enlace en la interfaz. La excepcion de NS FORT conserva la regla
        // de compradores NS EQUIPO usada por el resto del controlador.
        private async Task<bool> UsuarioPuedeConsultarSolicitudAsync(
            SqlConnection conn, int usuarioId, int solicitudId)
        {
            const string sql = @"
SELECT CASE WHEN EXISTS (
  SELECT 1
  FROM dbo.Compras_Solicitud S
  WHERE S.SolicitudID = @SolicitudID
    AND (
      S.UsuarioID = @UsuarioID
      OR (
        EXISTS (
          SELECT 1 FROM dbo.UsuariosEmpresas UE
          INNER JOIN dbo.Empresas E ON E.EmpresaID = UE.EmpresaID
          WHERE UE.UsuarioID = @UsuarioID AND UE.Activo = 1 AND E.Activa = 1
            AND (UE.EmpresaID = S.EmpresaID
                 OR (S.EmpresaID = @EmpresaNsFortId
                     AND UE.EmpresaID = @EmpresaNsEquipoId))
        )
        AND (
          S.CompradorAsignadoUsuarioID = @UsuarioID
          OR EXISTS (
            SELECT 1
            FROM dbo.EmpleadoDepartamentos ED
            INNER JOIN dbo.Departamentos D ON D.DepartamentoID = ED.DepartamentoID
            WHERE ED.UsuarioID = @UsuarioID AND ED.Activo = 1 AND D.Activo = 1
              AND (UPPER(D.NombreDepartamento) LIKE N'%COMPRAS%'
                   OR UPPER(D.NombreDepartamento) LIKE N'%PRESUPUEST%'
                   OR UPPER(D.NombreDepartamento) LIKE N'%ALMAC%'
                   OR UPPER(D.NombreDepartamento) LIKE N'%CIS%')
          )
          OR EXISTS (
            SELECT 1 FROM dbo.Usuarios U
            INNER JOIN dbo.Persona P ON P.PersonaID = U.PersonaID
            WHERE U.UsuarioID = @UsuarioID AND (
              UPPER(P.Puesto) LIKE N'%DIREC%'
              OR UPPER(P.Puesto) LIKE N'%DIRECTOR%')
          )
        )
      )
    )
) THEN 1 ELSE 0 END;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@SolicitudID", SqlDbType.Int).Value = solicitudId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@EmpresaNsFortId", SqlDbType.Int).Value = EmpresaNsFortId;
            cmd.Parameters.Add("@EmpresaNsEquipoId", SqlDbType.Int).Value = EmpresaNsEquipoId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        // Un archivo solo es descargable si su ruta existe en una tabla de Compras,
        // corresponde a una solicitud que el usuario puede consultar y no se desactivo.
        // No se acepta una ruta SFTP arbitraria enviada por el navegador.
        private async Task<bool> UsuarioPuedeConsultarArchivoAsync(
            SqlConnection conn, int usuarioId, string ruta)
        {
            const string sql = @"
SELECT DISTINCT S.SolicitudID
FROM dbo.Compras_Solicitud S
WHERE S.ArchivoReferenciaPath = @Ruta
   OR EXISTS (SELECT 1 FROM dbo.Compras_Archivos A
              WHERE A.SolicitudID = S.SolicitudID AND A.RutaArchivo = @Ruta
                AND A.Activo = 1 AND A.Vigente = 1)
   OR EXISTS (SELECT 1 FROM dbo.Compras_Cotizaciones C
              WHERE C.SolicitudID = S.SolicitudID AND C.ArchivoPath = @Ruta)
   OR EXISTS (SELECT 1 FROM dbo.Compras_Detalle_Materiales M
              WHERE M.SolicitudID = S.SolicitudID AND M.ArchivoReferenciaPath = @Ruta)
   OR EXISTS (SELECT 1 FROM dbo.Compras_Recepciones R
              WHERE R.SolicitudID = S.SolicitudID AND R.EvidenciaRecepcionPath = @Ruta
                AND R.Activo = 1)
   OR EXISTS (SELECT 1 FROM dbo.Compras_EntregasUsuario E
              WHERE E.SolicitudID = S.SolicitudID AND E.EvidenciaEntregaPath = @Ruta
                AND E.Activo = 1);";
            var solicitudes = new List<int>();
            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@Ruta", SqlDbType.NVarChar, 1800).Value = ruta;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    solicitudes.Add(reader.GetInt32(0));
            }
            foreach (var id in solicitudes)
                if (await UsuarioPuedeConsultarSolicitudAsync(conn, usuarioId, id))
                    return true;
            return false;
        }

        public async Task<IActionResult> Detalle(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var vm = new DetalleCompraVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                if (!await UsuarioPuedeConsultarSolicitudAsync(conn, usuarioId, id))
                    return Forbid();

                string sqlCabecera = @"
SELECT 
    S.SolicitudID,
    S.Folio, 
    (P.Nombre + ' ' + P.ApellidoPaterno) AS NombreCompleto, 
    E.Nombre AS EmpresaNombre, 
    Est.Nombre AS EstatusTexto, 
    S.EstatusID,
    S.UrgenciaID,
    CU.Descripcion AS UrgenciaDescripcion,
    CU.TiempoObjetivo AS TiempoObjetivoCotizacion,
    CU.Ejemplos AS EjemplosCategoriaCompra,
    CU.DiasHabiles AS DiasHabilesObjetivo,
    CU.RequiereAutorizacion AS RequiereAutorizacionUrgente,
    S.TipoGasto,
    S.ClasificacionSolicitud,
    S.DestinoEntrega,
    S.GranjaDestino,
    S.ProductoNuevo,
    S.DatosTecnicosProducto,
    S.FechaConfirmacionTecnica,
    S.UsuarioConfirmacionTecnicaID,
    S.NumeroRequisicion,    
    S.DentroPresupuesto, 
    S.ObservacionesPresupuesto,
    S.UsuarioID,
    S.CotizacionSeleccionadaID,
    S.FechaSeleccionCotizacion,
    S.ComentariosSeleccionUsuario,
    S.UsuarioSeleccionCotizacionID,
    CASE
        WHEN PSelector.PersonaID IS NULL THEN NULL
        ELSE LTRIM(RTRIM(
            ISNULL(PSelector.Nombre, '') + ' ' +
            ISNULL(PSelector.ApellidoPaterno, '')
        ))
    END AS NombreSelectorCotizacion,
    CASE
        WHEN S.UsuarioSeleccionCotizacionID = S.UsuarioID THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END AS CotizacionSeleccionadaPorSolicitante,
    S.FechaAutorizacionCotizacion,
    S.UsuarioAutorizacionCotizacionID,
    S.ComentariosAutorizacionCotizacion,
    CASE
        WHEN PAutorizador.PersonaID IS NULL THEN NULL
        ELSE LTRIM(RTRIM(
            ISNULL(PAutorizador.Nombre, '') + ' ' +
            ISNULL(PAutorizador.ApellidoPaterno, '')
        ))
    END AS NombreAutorizadorCotizacion,
    CASE
        WHEN S.UsuarioAutorizacionCotizacionID = S.UsuarioID
             AND S.FechaAutorizacionCotizacion IS NOT NULL
        THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END AS CotizacionAutorizadaPorSolicitante,
    S.MontoPresupuestoSolicitado,
    S.FueraPresupuestoUsuario,
    S.CompradorAsignadoUsuarioID,
    CASE
        WHEN PComprador.PersonaID IS NULL THEN NULL
        ELSE LTRIM(RTRIM(
            ISNULL(PComprador.Nombre, '') + ' ' +
            ISNULL(PComprador.ApellidoPaterno, '')
        ))
    END AS CompradorAsignadoNombre,
    PComprador.Puesto AS CompradorAsignadoPuesto,

    ADesv.RutaArchivo AS ArchivoDesviacionPath,
    ADesv.NombreOriginal AS NombreArchivoDesviacion,
    ADesv.Extension AS ExtensionArchivoDesviacion,

    ADesvFirma.RutaArchivo AS ArchivoDesviacionParaFirmaPath,
    ADesvFirma.NombreOriginal AS NombreArchivoDesviacionParaFirma,
    ADesvFirma.Extension AS ExtensionArchivoDesviacionParaFirma,

AReq.RutaArchivo AS ArchivoFormatoRequisicionPath,
AReq.NombreOriginal AS NombreArchivoFormatoRequisicion,
AReq.Extension AS ExtensionArchivoFormatoRequisicion,

AUrg.RutaArchivo AS ArchivoAutorizacionUrgentePath,
AUrg.NombreOriginal AS NombreArchivoAutorizacionUrgente,
AUrg.Extension AS ExtensionArchivoAutorizacionUrgente,

    S.ArchivoReferenciaPath
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
INNER JOIN Cat_EstatusCompra Est ON S.EstatusID = Est.EstatusID
INNER JOIN Cat_Urgencia CU ON S.UrgenciaID = CU.UrgenciaID
LEFT JOIN Usuarios UComprador
    ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador
    ON UComprador.PersonaID = PComprador.PersonaID
LEFT JOIN Usuarios USelector
    ON S.UsuarioSeleccionCotizacionID = USelector.UsuarioID
LEFT JOIN Persona PSelector
    ON USelector.PersonaID = PSelector.PersonaID
LEFT JOIN Usuarios UAutorizador
    ON S.UsuarioAutorizacionCotizacionID = UAutorizador.UsuarioID
LEFT JOIN Persona PAutorizador
    ON UAutorizador.PersonaID = PAutorizador.PersonaID
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND (CA.CotizacionID = S.CotizacionSeleccionadaID OR CA.CotizacionID IS NULL)
      AND CA.TipoArchivo = 'DESVIACION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) ADesv
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'DESVIACION_CP'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) ADesvFirma
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'FORMATO_REQUISICION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) AReq
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.TipoArchivo = 'AUTORIZACION_URGENTE'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) AUrg
WHERE S.SolicitudID = @id";

                using (var cmd = new SqlCommand(sqlCabecera, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                            return NotFound();

                        vm.SolicitudID = (int)reader["SolicitudID"];
                        vm.Folio = reader["Folio"] == DBNull.Value ? "" : reader["Folio"].ToString();
                        vm.NombreSolicitante = reader["NombreCompleto"] == DBNull.Value ? "" : reader["NombreCompleto"].ToString();
                        vm.Empresa = reader["EmpresaNombre"] == DBNull.Value ? "" : reader["EmpresaNombre"].ToString();
                        vm.EstatusActual = reader["EstatusTexto"] == DBNull.Value ? "" : reader["EstatusTexto"].ToString();
                        vm.EstatusID = (int)reader["EstatusID"];
                        vm.UrgenciaID = reader["UrgenciaID"] == DBNull.Value ? 0 : Convert.ToInt32(reader["UrgenciaID"]);
                        vm.UrgenciaDescripcion = reader["UrgenciaDescripcion"] == DBNull.Value ? null : reader["UrgenciaDescripcion"].ToString();
                        vm.TiempoObjetivoCotizacion = reader["TiempoObjetivoCotizacion"] == DBNull.Value ? null : reader["TiempoObjetivoCotizacion"].ToString();
                        vm.EjemplosCategoriaCompra = reader["EjemplosCategoriaCompra"] == DBNull.Value ? null : reader["EjemplosCategoriaCompra"].ToString();
                        vm.DiasHabilesObjetivo = reader["DiasHabilesObjetivo"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasHabilesObjetivo"]);
                        vm.EsUrgente = reader["RequiereAutorizacionUrgente"] != DBNull.Value &&
                            Convert.ToBoolean(reader["RequiereAutorizacionUrgente"]);

                        vm.TipoGasto = reader["TipoGasto"] == DBNull.Value ? null : reader["TipoGasto"].ToString();
                        vm.ClasificacionSolicitud = reader["ClasificacionSolicitud"]?.ToString() ?? "COMPRA";
                        vm.DestinoEntrega = reader["DestinoEntrega"]?.ToString() ?? "ALMACEN";
                        vm.GranjaDestino = reader["GranjaDestino"] == DBNull.Value ? null : reader["GranjaDestino"].ToString();
                        vm.ProductoNuevo = reader["ProductoNuevo"] != DBNull.Value && Convert.ToBoolean(reader["ProductoNuevo"]);
                        vm.DatosTecnicosProducto = reader["DatosTecnicosProducto"] == DBNull.Value ? null : reader["DatosTecnicosProducto"].ToString();
                        vm.FechaConfirmacionTecnica = reader["FechaConfirmacionTecnica"] == DBNull.Value ? null : (DateTime?)reader["FechaConfirmacionTecnica"];
                        vm.UsuarioConfirmacionTecnicaID = reader["UsuarioConfirmacionTecnicaID"] == DBNull.Value ? null : (int?)reader["UsuarioConfirmacionTecnicaID"];
                        vm.DentroPresupuesto = reader["DentroPresupuesto"] == DBNull.Value ? null : (bool?)reader["DentroPresupuesto"];
                        vm.NumeroRequisicion = reader["NumeroRequisicion"] == DBNull.Value ? null : reader["NumeroRequisicion"].ToString();
                        vm.ObservacionesPresupuesto = reader["ObservacionesPresupuesto"] == DBNull.Value ? null : reader["ObservacionesPresupuesto"].ToString();

                        int usuarioSolicitanteId = (int)reader["UsuarioID"];
                        vm.EsSolicitante = usuarioSolicitanteId == usuarioId;

                        vm.CotizacionSeleccionadaID = reader["CotizacionSeleccionadaID"] == DBNull.Value ? null : (int?)reader["CotizacionSeleccionadaID"];

                        vm.FechaSeleccionCotizacion = reader["FechaSeleccionCotizacion"] == DBNull.Value ? null : (DateTime?)reader["FechaSeleccionCotizacion"];

                        vm.ComentariosSeleccionUsuario = reader["ComentariosSeleccionUsuario"] == DBNull.Value
                            ? null
                            : reader["ComentariosSeleccionUsuario"].ToString();

                        vm.UsuarioSeleccionCotizacionID = reader["UsuarioSeleccionCotizacionID"] == DBNull.Value
                            ? null
                            : (int?)Convert.ToInt32(reader["UsuarioSeleccionCotizacionID"]);

                        vm.NombreSelectorCotizacion = reader["NombreSelectorCotizacion"] == DBNull.Value
                            ? null
                            : reader["NombreSelectorCotizacion"].ToString();

                        vm.CotizacionSeleccionadaPorSolicitante =
                            reader["CotizacionSeleccionadaPorSolicitante"] != DBNull.Value &&
                            Convert.ToBoolean(reader["CotizacionSeleccionadaPorSolicitante"]);

                        vm.FechaAutorizacionCotizacion =
                            reader["FechaAutorizacionCotizacion"] == DBNull.Value
                                ? null
                                : (DateTime?)Convert.ToDateTime(reader["FechaAutorizacionCotizacion"]);

                        vm.UsuarioAutorizacionCotizacionID =
                            reader["UsuarioAutorizacionCotizacionID"] == DBNull.Value
                                ? null
                                : (int?)Convert.ToInt32(reader["UsuarioAutorizacionCotizacionID"]);

                        vm.ComentariosAutorizacionCotizacion =
                            reader["ComentariosAutorizacionCotizacion"] == DBNull.Value
                                ? null
                                : reader["ComentariosAutorizacionCotizacion"].ToString();

                        vm.NombreAutorizadorCotizacion =
                            reader["NombreAutorizadorCotizacion"] == DBNull.Value
                                ? null
                                : reader["NombreAutorizadorCotizacion"].ToString();

                        vm.CotizacionAutorizadaPorSolicitante =
                            reader["CotizacionAutorizadaPorSolicitante"] != DBNull.Value &&
                            Convert.ToBoolean(reader["CotizacionAutorizadaPorSolicitante"]);

                        vm.ArchivoReferenciaPath = reader["ArchivoReferenciaPath"] == DBNull.Value ? null : reader["ArchivoReferenciaPath"].ToString();

                        vm.MontoPresupuestoSolicitado = reader["MontoPresupuestoSolicitado"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(reader["MontoPresupuestoSolicitado"]);

                        vm.FueraPresupuestoUsuario = reader["FueraPresupuestoUsuario"] != DBNull.Value && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);

                        vm.CompradorAsignadoUsuarioID =
                            reader["CompradorAsignadoUsuarioID"] == DBNull.Value
                                ? null
                                : (int?)Convert.ToInt32(reader["CompradorAsignadoUsuarioID"]);

                        vm.CompradorAsignadoNombre =
                            reader["CompradorAsignadoNombre"] == DBNull.Value
                                ? null
                                : reader["CompradorAsignadoNombre"].ToString();

                        vm.CompradorAsignadoPuesto =
                            reader["CompradorAsignadoPuesto"] == DBNull.Value
                                ? null
                                : reader["CompradorAsignadoPuesto"].ToString();
                        vm.ArchivoDesviacionPath = reader["ArchivoDesviacionPath"] == DBNull.Value ? null : reader["ArchivoDesviacionPath"].ToString();

                        vm.NombreArchivoDesviacion = reader["NombreArchivoDesviacion"] == DBNull.Value ? null : reader["NombreArchivoDesviacion"].ToString();

                        vm.ExtensionArchivoDesviacion = reader["ExtensionArchivoDesviacion"] == DBNull.Value ? null : reader["ExtensionArchivoDesviacion"].ToString();

                        vm.ArchivoDesviacionParaFirmaPath =
                            reader["ArchivoDesviacionParaFirmaPath"] == DBNull.Value
                                ? null
                                : reader["ArchivoDesviacionParaFirmaPath"].ToString();

                        vm.NombreArchivoDesviacionParaFirma =
                            reader["NombreArchivoDesviacionParaFirma"] == DBNull.Value
                                ? null
                                : reader["NombreArchivoDesviacionParaFirma"].ToString();

                        vm.ExtensionArchivoDesviacionParaFirma =
                            reader["ExtensionArchivoDesviacionParaFirma"] == DBNull.Value
                                ? null
                                : reader["ExtensionArchivoDesviacionParaFirma"].ToString();

                        vm.ArchivoFormatoRequisicionPath = reader["ArchivoFormatoRequisicionPath"] == DBNull.Value ? null : reader["ArchivoFormatoRequisicionPath"].ToString();

                        vm.NombreArchivoFormatoRequisicion = reader["NombreArchivoFormatoRequisicion"] == DBNull.Value ? null : reader["NombreArchivoFormatoRequisicion"].ToString();

                        vm.ExtensionArchivoFormatoRequisicion = reader["ExtensionArchivoFormatoRequisicion"] == DBNull.Value ? null : reader["ExtensionArchivoFormatoRequisicion"].ToString();

                        vm.ArchivoAutorizacionUrgentePath = reader["ArchivoAutorizacionUrgentePath"] == DBNull.Value ? null : reader["ArchivoAutorizacionUrgentePath"].ToString();
                        vm.NombreArchivoAutorizacionUrgente = reader["NombreArchivoAutorizacionUrgente"] == DBNull.Value ? null : reader["NombreArchivoAutorizacionUrgente"].ToString();
                        vm.ExtensionArchivoAutorizacionUrgente = reader["ExtensionArchivoAutorizacionUrgente"] == DBNull.Value ? null : reader["ExtensionArchivoAutorizacionUrgente"].ToString();
                    }

                    bool esControlPresupuestal =
    await UsuarioPerteneceADepartamentoAsync(
        conn,
        usuarioId,
        "CONTROL PRESUPUESTAL",
        "PRESUPUESTOS",
        "CIS"
    );

                    vm.PuedeVerArchivoDesviacion =
                        vm.EsSolicitante || esControlPresupuestal;

                }

                string sqlCot = @"
SELECT
    CotizacionID,
    Proveedor,
    MontoTotal,
    ArchivoPath,
    NombreArchivoOriginal,
    Extension,
    EsRecomendada,
    ComentariosCompras,
    FechaEnvioAlUsuario
FROM Compras_Cotizaciones
WHERE SolicitudID = @id
ORDER BY NumeroCotizacion";

                using (var cmd = new SqlCommand(sqlCot, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int cotizacionId = (int)reader["CotizacionID"];

                            vm.Cotizaciones.Add(new CotizacionDetalleVm
                            {
                                CotizacionID = cotizacionId,
                                Proveedor = reader["Proveedor"] == DBNull.Value ? "" : reader["Proveedor"].ToString(),
                                MontoTotal = reader["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["MontoTotal"]),
                                ArchivoPath = reader["ArchivoPath"] == DBNull.Value ? "" : reader["ArchivoPath"].ToString(),
                                NombreArchivoOriginal = reader["NombreArchivoOriginal"] == DBNull.Value ? "" : reader["NombreArchivoOriginal"].ToString(),
                                Extension = reader["Extension"] == DBNull.Value ? "" : reader["Extension"].ToString(),
                                EsRecomendada = reader["EsRecomendada"] != DBNull.Value && Convert.ToBoolean(reader["EsRecomendada"]),
                                ComentariosCompras = reader["ComentariosCompras"] == DBNull.Value ? null : reader["ComentariosCompras"].ToString(),
                                FechaEnvioAlUsuario = reader["FechaEnvioAlUsuario"] == DBNull.Value ? DateTime.MinValue : (DateTime)reader["FechaEnvioAlUsuario"],
                                FueSeleccionadaPorUsuario = vm.CotizacionSeleccionadaID.HasValue &&
                                                            vm.CotizacionSeleccionadaID.Value == cotizacionId
                            });
                        }
                    }
                }

                string sqlMateriales = @"
SELECT 
    M.DetalleID,
    M.NombreMaterial,
    M.Descripcion,
    M.Cantidad,
    M.UnidadMedida,
    M.ArchivoReferenciaPath,
    M.NombreArchivoReferencia,
    M.ExtensionArchivoReferencia,
    ISNULL(R.CantidadRecibidaAlmacen, 0) AS CantidadRecibidaAlmacen,
    ISNULL(EU.CantidadEntregadaUsuario, 0) AS CantidadEntregadaUsuario
FROM Compras_Detalle_Materiales M
OUTER APPLY
(
    SELECT SUM(MD.Cantidad) AS CantidadRecibidaAlmacen
    FROM Compras_MovimientoDetalle MD
    INNER JOIN Compras_Recepciones R
        ON R.RecepcionID = MD.RecepcionID
       AND R.Activo = 1
    WHERE MD.DetalleID = M.DetalleID
      AND MD.RecepcionID IS NOT NULL
      AND MD.EntregaID IS NULL
) R
OUTER APPLY
(
    SELECT SUM(MD.Cantidad) AS CantidadEntregadaUsuario
    FROM Compras_MovimientoDetalle MD
    INNER JOIN Compras_EntregasUsuario EU
        ON EU.EntregaID = MD.EntregaID
       AND EU.Activo = 1
    WHERE MD.DetalleID = M.DetalleID
      AND MD.EntregaID IS NOT NULL
      AND MD.RecepcionID IS NULL
) EU
WHERE M.SolicitudID = @id
ORDER BY M.DetalleID";

                using (var cmdM = new SqlCommand(sqlMateriales, conn))
                {
                    cmdM.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdM.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Materiales.Add(new MaterialItem
                            {
                                DetalleID = Convert.ToInt32(reader["DetalleID"]),
                                Nombre = reader["NombreMaterial"] == DBNull.Value ? "" : reader["NombreMaterial"].ToString(),
                                Descripcion = reader["Descripcion"] == DBNull.Value ? "" : reader["Descripcion"].ToString(),
                                Cantidad = reader["Cantidad"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Cantidad"]),
                                UnidadMedida = reader["UnidadMedida"] == DBNull.Value ? "" : reader["UnidadMedida"].ToString(),
                                CantidadRecibidaAlmacen = reader["CantidadRecibidaAlmacen"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["CantidadRecibidaAlmacen"]),
                                CantidadEntregadaUsuario = reader["CantidadEntregadaUsuario"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["CantidadEntregadaUsuario"]),
                                ArchivoReferenciaPath = reader["ArchivoReferenciaPath"] == DBNull.Value ? null : reader["ArchivoReferenciaPath"].ToString(),
                                NombreArchivoReferencia = reader["NombreArchivoReferencia"] == DBNull.Value ? null : reader["NombreArchivoReferencia"].ToString(),
                                ExtensionArchivoReferencia = reader["ExtensionArchivoReferencia"] == DBNull.Value ? null : reader["ExtensionArchivoReferencia"].ToString()
                            });
                        }
                    }
                }

                vm.TieneRecepcionesAlmacen = vm.Materiales.Any(x => x.CantidadRecibidaAlmacen > 0);
                vm.RecibidaEnAlmacen = vm.Materiales.Any() && vm.Materiales.All(x => x.RecibidaCompletaAlmacen);
                vm.RecepcionAlmacenParcial = vm.TieneRecepcionesAlmacen && !vm.RecibidaEnAlmacen;

                vm.TieneEntregasUsuario = vm.Materiales.Any(x => x.CantidadEntregadaUsuario > 0);
                vm.EntregadaUsuario = vm.Materiales.Any() && vm.Materiales.All(x => x.EntregadaCompletaUsuario);
                vm.EntregaUsuarioParcial = vm.TieneEntregasUsuario && !vm.EntregadaUsuario;

                string sqlOC = @"
SELECT TOP 1 
    NumeroOC,
    Proveedor,
    Comentarios,
    FechaOC,
    FechaEnvioProveedor,
    FechaEstimadaEntrega
FROM Compras_OrdenCompra
WHERE SolicitudID = @id
  AND Activo = 1
ORDER BY OrdenCompraID DESC";

                using (var cmdOC = new SqlCommand(sqlOC, conn))
                {
                    cmdOC.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdOC.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            vm.TieneOC = true;
                            vm.NumeroOC = reader["NumeroOC"] == DBNull.Value ? "" : reader["NumeroOC"].ToString();
                            vm.ProveedorOC = reader["Proveedor"] == DBNull.Value ? null : reader["Proveedor"].ToString();
                            vm.ComentariosOC = reader["Comentarios"] == DBNull.Value ? null : reader["Comentarios"].ToString();
                            vm.FechaOC = reader["FechaOC"] == DBNull.Value ? null : (DateTime?)reader["FechaOC"];
                            vm.FechaEnvioProveedor = reader["FechaEnvioProveedor"] == DBNull.Value ? null : (DateTime?)reader["FechaEnvioProveedor"];
                            vm.FechaEstimadaEntrega = reader["FechaEstimadaEntrega"] == DBNull.Value ? null : (DateTime?)reader["FechaEstimadaEntrega"];
                            vm.OCEnviadaProveedor = vm.FechaEnvioProveedor.HasValue;
                        }
                    }
                }

                string sqlRecepcion = @"
SELECT TOP 1
    FechaRecepcion,
    Comentarios,
    EvidenciaRecepcionPath,
    NombreArchivoEvidencia,
    ExtensionArchivoEvidencia
FROM Compras_Recepciones
WHERE SolicitudID = @id
  AND Activo = 1
ORDER BY RecepcionID DESC";

                using (var cmdRecep = new SqlCommand(sqlRecepcion, conn))
                {
                    cmdRecep.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdRecep.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            vm.TieneRecepcionesAlmacen = true;

                            vm.FechaRecepcionAlmacen = reader["FechaRecepcion"] == DBNull.Value ? null : (DateTime?)reader["FechaRecepcion"];
                            vm.ComentariosRecepcionAlmacen = reader["Comentarios"] == DBNull.Value ? null : reader["Comentarios"].ToString();
                            vm.EvidenciaRecepcionPath = reader["EvidenciaRecepcionPath"] == DBNull.Value ? null : reader["EvidenciaRecepcionPath"].ToString();
                            vm.NombreArchivoEvidencia = reader["NombreArchivoEvidencia"] == DBNull.Value ? null : reader["NombreArchivoEvidencia"].ToString();
                            vm.ExtensionArchivoEvidencia = reader["ExtensionArchivoEvidencia"] == DBNull.Value ? null : reader["ExtensionArchivoEvidencia"].ToString();
                        }
                    }
                }

                string sqlEntrega = @"
SELECT TOP 1
    FechaEntrega,
    NombreRecibe,
    Comentarios,
    EvidenciaEntregaPath,
    NombreArchivoEvidencia,
    ExtensionArchivoEvidencia
FROM Compras_EntregasUsuario
WHERE SolicitudID = @id
  AND Activo = 1
ORDER BY EntregaID DESC";

                using (var cmdEntrega = new SqlCommand(sqlEntrega, conn))
                {
                    cmdEntrega.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdEntrega.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            vm.TieneEntregasUsuario = true;
                            vm.FechaEntregaUsuario = reader["FechaEntrega"] == DBNull.Value ? null : (DateTime?)reader["FechaEntrega"];
                            vm.NombreRecibeUsuario = reader["NombreRecibe"] == DBNull.Value ? null : reader["NombreRecibe"].ToString();
                            vm.ComentariosEntregaUsuario = reader["Comentarios"] == DBNull.Value ? null : reader["Comentarios"].ToString();
                            vm.EvidenciaEntregaPath =
    reader["EvidenciaEntregaPath"] == DBNull.Value
        ? null
        : reader["EvidenciaEntregaPath"].ToString();

                            vm.NombreArchivoEvidenciaEntrega =
                                reader["NombreArchivoEvidencia"] == DBNull.Value
                                    ? null
                                    : reader["NombreArchivoEvidencia"].ToString();

                            vm.ExtensionArchivoEvidenciaEntrega =
                                reader["ExtensionArchivoEvidencia"] == DBNull.Value
                                    ? null
                                    : reader["ExtensionArchivoEvidencia"].ToString();
                        }
                    }
                }

                string sqlPuesto = @"
SELECT P.Puesto
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE U.UsuarioID = @UsuarioID";

                using (var cmd = new SqlCommand(sqlPuesto, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    string puesto = (await cmd.ExecuteScalarAsync())?.ToString()?.Trim().ToUpper() ?? "";

                    vm.EsCompras =
                        puesto == "COMPRADOR NACIONAL" ||
                        puesto == "COMPRADOR INTERNACIONAL" ||
                        puesto == "DIRECCION COMPRAS" ||
                        puesto == "DIRECCIÓN COMPRAS";
                }
            }

            return View(vm);
        }





        // Confirmacion documental del requisitor antes de cotizar productos nuevos.
        // No equivale a autorizacion presupuestal ni sustituye la revision del comprador.
        [HttpPost("Compras/ConfirmarDatosTecnicos")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarDatosTecnicos(int solicitudId)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string sql = @"
UPDATE dbo.Compras_Solicitud
SET FechaConfirmacionTecnica = SYSDATETIME(),
    UsuarioConfirmacionTecnicaID = @UsuarioID
WHERE SolicitudID = @SolicitudID
  AND UsuarioID = @UsuarioID
  AND ProductoNuevo = 1
  AND FechaConfirmacionTecnica IS NULL
  AND EstatusID = 1
  AND NULLIF(LTRIM(RTRIM(DatosTecnicosProducto)), N'') IS NOT NULL;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@SolicitudID", SqlDbType.Int).Value = solicitudId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            int count = await cmd.ExecuteNonQueryAsync();
            if (count != 1) return Forbid();
            TempData["Mensaje"] = "Datos técnicos confirmados por el solicitante.";
            return RedirectToAction(nameof(Detalle), new { id = solicitudId });
        }

        [HttpGet]
        public async Task<IActionResult> VerArchivoNas(string ruta)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();
            if (string.IsNullOrWhiteSpace(ruta)) return BadRequest("Ruta no válida.");

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            if (!await UsuarioPuedeConsultarArchivoAsync(conn, usuarioId, ruta))
                return Forbid();

            try
            {
                var bytes = _sftp.DescargarBytes(ruta);
                string extension = Path.GetExtension(ruta)?.ToLowerInvariant() ?? "";
                string contentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    ".xls" => "application/vnd.ms-excel",
                    _ => "application/octet-stream"
                };
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                return File(bytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo recuperar un archivo de Compras autorizado.");
                return NotFound();
            }
        }

        [HttpGet]
        public async Task<IActionResult> DescargarArchivoNas(string ruta, string? nombre)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();
            if (string.IsNullOrWhiteSpace(ruta)) return BadRequest("Ruta no válida.");

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            if (!await UsuarioPuedeConsultarArchivoAsync(conn, usuarioId, ruta))
                return Forbid();

            try
            {
                var bytes = _sftp.DescargarBytes(ruta);
                string extension = Path.GetExtension(ruta)?.ToLowerInvariant() ?? "";
                string contentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    ".xls" => "application/vnd.ms-excel",
                    _ => "application/octet-stream"
                };
                string nombreDescarga = string.IsNullOrWhiteSpace(nombre)
                    ? Path.GetFileName(ruta) : Path.GetFileName(nombre);
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                return File(bytes, contentType, nombreDescarga);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo descargar un archivo de Compras autorizado.");
                return NotFound();
            }
        }

        private async Task CargarCatalogosAsync(int usuarioId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                ViewBag.Empresas = await ObtenerEmpresasPermitidasParaSolicitudAsync(conn, usuarioId);

                var urgencias = new List<SelectListItem>();
                var categoriasCompraInfo = new List<CategoriaCompraInfoVm>();

                using (var cmd = new SqlCommand(@"
SELECT UrgenciaID, Descripcion, TiempoObjetivo, Ejemplos, RequiereAutorizacion
FROM Cat_Urgencia
WHERE Activo = 1
ORDER BY ISNULL(OrdenVisual, 999), UrgenciaID", conn))
                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        int categoriaId = Convert.ToInt32(rd["UrgenciaID"]);
                        string descripcion = rd["Descripcion"]?.ToString() ?? string.Empty;
                        string tiempoObjetivo = rd["TiempoObjetivo"]?.ToString() ?? string.Empty;

                        urgencias.Add(new SelectListItem
                        {
                            Value = categoriaId.ToString(),
                            Text = descripcion + " — " + tiempoObjetivo
                        });

                        categoriasCompraInfo.Add(new CategoriaCompraInfoVm
                        {
                            UrgenciaID = categoriaId,
                            Descripcion = descripcion,
                            TiempoObjetivo = tiempoObjetivo,
                            Ejemplos = rd["Ejemplos"]?.ToString() ?? string.Empty,
                            RequiereAutorizacion = rd["RequiereAutorizacion"] != DBNull.Value && Convert.ToBoolean(rd["RequiereAutorizacion"])
                        });
                    }
                }

                ViewBag.Urgencias = urgencias;
                ViewBag.CategoriasCompraInfo = categoriasCompraInfo;
            }
        }

        private async Task<bool> EsUrgenciaConAutorizacionAsync(int urgenciaId)
        {
            if (urgenciaId <= 0)
                return false;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
SELECT ISNULL(RequiereAutorizacion, 0)
FROM Cat_Urgencia
WHERE UrgenciaID = @UrgenciaID;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UrgenciaID", urgenciaId);

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value && Convert.ToInt32(result) == 1;
        }

        private async Task AplicarMetasCotizacionAsync(
            SqlConnection conn,
            IEnumerable<SeguimientoCompraItemVm> solicitudes)
        {
            var metas = new Dictionary<string, (string TiempoObjetivo, int DiasMax)>(
                StringComparer.OrdinalIgnoreCase
            );

            const string sql = @"
SELECT Descripcion,
       ISNULL(TiempoObjetivo, '') AS TiempoObjetivo,
       ISNULL(DiasHabilesMax, DiasHabiles) AS DiasMax
FROM Cat_Urgencia;";

            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string descripcion = reader["Descripcion"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(descripcion))
                        continue;

                    metas[descripcion] = (
                        reader["TiempoObjetivo"]?.ToString() ?? "",
                        reader["DiasMax"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasMax"])
                    );
                }
            }

            foreach (var item in solicitudes)
            {
                string categoria = item.Urgencia?.Trim() ?? "";
                if (metas.TryGetValue(categoria, out var meta))
                {
                    item.TiempoObjetivoCotizacion = meta.TiempoObjetivo;
                    if (meta.DiasMax > 0)
                        item.DiasPermitidos = meta.DiasMax;
                }
            }
        }

        private int ObtenerUsuarioIdActual()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return (claim != null && int.TryParse(claim.Value, out int id)) ? id : 0;
        }


        [HttpGet("BandejaCompras")]
        [HttpGet("Compras/BandejaCompras")]
        public async Task<IActionResult> BandejaCompras()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var vm = new BandejaComprasDashboardVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool perteneceCompras = await UsuarioPerteneceADepartamentoAsync(
                    conn,
                    usuarioId,
                    "COMPRAS"
                );

                if (!perteneceCompras)
                    return Forbid();

                int? empresaActivaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioId);

                if (!empresaActivaId.HasValue)
                    return Forbid();

                int empresaCompradoresId = ObtenerEmpresaCompradoresId(empresaActivaId.Value);

                string puesto = "";

                using (var cmdP = new SqlCommand(@"
SELECT P.Puesto
FROM Persona P
INNER JOIN Usuarios U ON P.PersonaID = U.PersonaID
WHERE U.UsuarioID = @Uid", conn))
                {
                    cmdP.Parameters.AddWithValue("@Uid", usuarioId);
                    puesto = (await cmdP.ExecuteScalarAsync())?.ToString() ?? "";
                }

                vm.EsDireccionCompras = puesto == "DIRECCION COMPRAS";

                string filtroTipo = "";

                if (puesto == "COMPRADOR NACIONAL")
                    filtroTipo = " AND S.TipoCompra = 'Nacional'";
                else if (puesto == "COMPRADOR INTERNACIONAL")
                    filtroTipo = " AND S.TipoCompra = 'Internacional'";

                string sqlPendientes = $@"
SELECT
    S.SolicitudID,
    S.CompradorAsignadoUsuarioID,
    S.Folio,
    (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
    ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
    S.TipoCompra,
    U.Descripcion AS Urgencia,
    U.TiempoObjetivo AS TiempoObjetivoCotizacion,
    S.FechaCreacion,
    E.Nombre AS Estatus
FROM Compras_Solicitud S
INNER JOIN Usuarios US ON S.UsuarioID = US.UsuarioID
INNER JOIN Persona P ON US.PersonaID = P.PersonaID
OUTER APPLY (
    SELECT TOP (1) DP.NombreDepartamento
    FROM EmpleadoDepartamentos ED
    INNER JOIN Departamentos DP ON DP.DepartamentoID = ED.DepartamentoID AND DP.Activo = 1
    WHERE ED.UsuarioID = US.UsuarioID AND ED.Activo = 1
    ORDER BY ED.DepartamentoID
) D
INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
INNER JOIN Cat_EstatusCompra E ON S.EstatusID = E.EstatusID
WHERE S.EstatusID = 1
  AND
  (
      S.EmpresaID = @EmpresaID
      OR
      (
          @EmpresaID = @EmpresaNsEquipoId
          AND S.EmpresaID = @EmpresaNsFortId
      )
  )
  AND
  (
      @EsDireccion = 1
      OR S.CompradorAsignadoUsuarioID = @UsuarioID
  )
{filtroTipo}
ORDER BY S.FechaCreacion ASC";

                using (var cmd = new SqlCommand(sqlPendientes, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@EsDireccion", vm.EsDireccionCompras ? 1 : 0);
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);
                    cmd.Parameters.AddWithValue("@EmpresaNsEquipoId", EmpresaNsEquipoId);
                    cmd.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Pendientes.Add(new BandejaComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                CompradorAsignadoUsuarioID = reader["CompradorAsignadoUsuarioID"] == DBNull.Value
                                    ? null
                                    : (int?)reader["CompradorAsignadoUsuarioID"],
                                Solicitante = reader["Solicitante"].ToString(),
                                Departamento = reader["Departamento"].ToString(),
                                TipoCompra = reader["TipoCompra"].ToString(),
                                Urgencia = reader["Urgencia"].ToString(),
                                TiempoObjetivoCotizacion = reader["TiempoObjetivoCotizacion"] == DBNull.Value
                                    ? null
                                    : reader["TiempoObjetivoCotizacion"].ToString(),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],
                                Estatus = reader["Estatus"].ToString()
                            });
                        }
                    }
                }

                string sqlHistorico = $@"
SELECT
    S.SolicitudID,
    S.Folio,
    S.EstatusID,
    (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
    ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
    S.TipoCompra,
    U.Descripcion AS Urgencia,
    U.TiempoObjetivo AS TiempoObjetivoCotizacion,
    E.Nombre AS Estatus,
    ISNULL(C.MontoTotal, 0) AS MontoTotal,
    S.FechaCreacion,
    C.FechaEnvioAlUsuario AS FechaCotizacion
FROM Compras_Solicitud S
INNER JOIN Usuarios US ON S.UsuarioID = US.UsuarioID
INNER JOIN Persona P ON US.PersonaID = P.PersonaID
OUTER APPLY (
    SELECT TOP (1) DP.NombreDepartamento
    FROM EmpleadoDepartamentos ED
    INNER JOIN Departamentos DP ON DP.DepartamentoID = ED.DepartamentoID AND DP.Activo = 1
    WHERE ED.UsuarioID = US.UsuarioID AND ED.Activo = 1
    ORDER BY ED.DepartamentoID
) D
INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
INNER JOIN Cat_EstatusCompra E ON S.EstatusID = E.EstatusID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.EstatusID >= 2
  AND
  (
      S.EmpresaID = @EmpresaID
      OR
      (
          @EmpresaID = @EmpresaNsEquipoId
          AND S.EmpresaID = @EmpresaNsFortId
      )
  )
  AND
  (
      @EsDireccion = 1
      OR S.CompradorAsignadoUsuarioID = @UsuarioID
  )
{filtroTipo}
ORDER BY C.FechaEnvioAlUsuario DESC, S.FechaCreacion DESC";

                using (var cmd = new SqlCommand(sqlHistorico, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@EsDireccion", vm.EsDireccionCompras ? 1 : 0);
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);
                    cmd.Parameters.AddWithValue("@EmpresaNsEquipoId", EmpresaNsEquipoId);
                    cmd.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Historico.Add(new HistoricoComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                EstatusID = (int)reader["EstatusID"],
                                Solicitante = reader["Solicitante"].ToString(),
                                Departamento = reader["Departamento"].ToString(),
                                TipoCompra = reader["TipoCompra"].ToString(),
                                Urgencia = reader["Urgencia"].ToString(),
                                TiempoObjetivoCotizacion = reader["TiempoObjetivoCotizacion"] == DBNull.Value
                                    ? null
                                    : reader["TiempoObjetivoCotizacion"].ToString(),
                                Estatus = reader["Estatus"].ToString(),
                                MontoTotal = Convert.ToDecimal(reader["MontoTotal"]),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],
                                FechaCotizacion = reader["FechaCotizacion"] == DBNull.Value
                                    ? null
                                    : (DateTime?)reader["FechaCotizacion"]
                            });
                        }
                    }
                }

                if (vm.EsDireccionCompras)
                {
                    string sqlCargaCompradores = @"
SELECT
    U.UsuarioID,
    CASE
       WHEN @DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri' THEN N'ADRIAN MAZON'
       WHEN @DemoCompradores = 1 AND P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ADRIAN%' AND P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%MAZON%' THEN N'ADRIAN MAZON'
       WHEN @DemoCompradores = 1 AND P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ANA%' AND (P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%NAVARRO%' OR P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%GONZALEZ%') THEN N'ANA NAVARRO'
       ELSE (P.Nombre + ' ' + P.ApellidoPaterno)
    END AS NombreCompleto,
    P.Puesto,
    SUM(CASE WHEN S.EstatusID = 1 THEN 1 ELSE 0 END) AS Pendientes,
    SUM(CASE WHEN S.EstatusID IN (2, 3, 4, 5, 6, 7, 8, 9, 13, 14) THEN 1 ELSE 0 END) AS EnSeguimiento,
    SUM(CASE WHEN S.EstatusID IN (10, 11, 12) THEN 1 ELSE 0 END) AS Finalizadas,
    SUM(CASE WHEN S.EstatusID >= 2 THEN 1 ELSE 0 END) AS Cotizadas,
    COUNT(S.SolicitudID) AS TotalAsignadas
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
LEFT JOIN UsuariosEmpresas UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaCompradoresID
LEFT JOIN Compras_Solicitud S
    ON S.CompradorAsignadoUsuarioID = U.UsuarioID
   AND
   (
       S.EmpresaID = @EmpresaID
       OR
       (
           @EmpresaID = @EmpresaNsEquipoId
           AND S.EmpresaID = @EmpresaNsFortId
       )
   )
WHERE U.Activo = 1
  AND (
      P.EsColaboradorActivo = 1
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri')
  )
  AND (
      UE.UsuarioID IS NOT NULL
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri')
  )
  AND (UPPER(P.Puesto) IN ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri')
      OR (@DemoCompradores = 1 AND (
       (P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ADRIAN%' AND P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%MAZON%')
       OR (P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ANA%' AND
           (P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%NAVARRO%' OR P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%GONZALEZ%'))
   )))
GROUP BY
    U.UsuarioID,
    U.Username,
    P.Nombre,
    P.ApellidoPaterno,
    P.Puesto
ORDER BY
    P.Puesto,
    Pendientes ASC,
    NombreCompleto ASC";

                    using (var cmdCarga = new SqlCommand(sqlCargaCompradores, conn))
                    {
                        cmdCarga.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);
                        cmdCarga.Parameters.AddWithValue("@EmpresaCompradoresID", empresaCompradoresId);
                        cmdCarga.Parameters.AddWithValue("@DemoCompradores", CompradoresDemoHabilitados ? 1 : 0);
                        cmdCarga.Parameters.AddWithValue("@UsuarioID", usuarioId);
                        cmdCarga.Parameters.AddWithValue("@EmpresaNsEquipoId", EmpresaNsEquipoId);
                        cmdCarga.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);

                        using (var reader = await cmdCarga.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.CargaCompradores.Add(new CompradorCargaVm
                                {
                                    UsuarioID = (int)reader["UsuarioID"],
                                    NombreCompleto = reader["NombreCompleto"].ToString(),
                                    Puesto = reader["Puesto"].ToString(),
                                    Pendientes = Convert.ToInt32(reader["Pendientes"]),
                                    Cotizadas = Convert.ToInt32(reader["Cotizadas"]),
                                    EnSeguimiento = Convert.ToInt32(reader["EnSeguimiento"]),
                                    Finalizadas = Convert.ToInt32(reader["Finalizadas"]),
                                    TotalAsignadas = Convert.ToInt32(reader["TotalAsignadas"])
                                });
                            }
                        }
                    }

                    string sqlCompradores = @"
SELECT
    U.UsuarioID,
    CASE
       WHEN @DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri' THEN N'ADRIAN MAZON'
       WHEN @DemoCompradores = 1 AND P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ADRIAN%' AND P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%MAZON%' THEN N'ADRIAN MAZON'
       WHEN @DemoCompradores = 1 AND P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ANA%' AND (P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%NAVARRO%' OR P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%GONZALEZ%') THEN N'ANA NAVARRO'
       ELSE (P.Nombre + ' ' + P.ApellidoPaterno)
    END AS NombreCompleto,
    P.Puesto
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
LEFT JOIN UsuariosEmpresas UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaCompradoresID
WHERE U.Activo = 1
  AND (
      P.EsColaboradorActivo = 1
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri')
  )
  AND (
      UE.UsuarioID IS NOT NULL
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri')
  )
  AND (UPPER(P.Puesto) IN ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioID AND U.Username = N'adrian.nutri')
      OR (@DemoCompradores = 1 AND (
       (P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ADRIAN%' AND P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%MAZON%')
       OR (P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ANA%' AND
           (P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%NAVARRO%' OR P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%GONZALEZ%'))
   )))
ORDER BY
    P.Puesto,
    P.Nombre,
    P.ApellidoPaterno";

                    using (var cmdCompradores = new SqlCommand(sqlCompradores, conn))
                    {
                        cmdCompradores.Parameters.AddWithValue("@EmpresaCompradoresID", empresaCompradoresId);
                        cmdCompradores.Parameters.AddWithValue("@DemoCompradores", CompradoresDemoHabilitados ? 1 : 0);
                        cmdCompradores.Parameters.AddWithValue("@UsuarioID", usuarioId);

                        using (var reader = await cmdCompradores.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.CompradoresDisponibles.Add(new CompradorSelectVm
                                {
                                    UsuarioID = (int)reader["UsuarioID"],
                                    NombreCompleto = reader["NombreCompleto"].ToString(),
                                    Puesto = reader["Puesto"].ToString()
                                });
                            }
                        }
                    }
                }
            }

            int[] estatusSeguimiento = { 2, 3, 4, 5, 6, 7, 8, 9, 13, 14 };
            int[] estatusFinalizados = { 10, 11, 12 };

            vm.TotalPendientes = vm.Pendientes.Count;
            vm.TotalCotizadas = vm.Historico.Count(x => x.FechaCotizacion != null);
            vm.TotalAtendidas = vm.Historico.Count;
            vm.TotalEnSeguimiento = vm.Historico.Count(x => estatusSeguimiento.Contains(x.EstatusID));
            vm.TotalFinalizadas = vm.Historico.Count(x => estatusFinalizados.Contains(x.EstatusID));
            vm.TotalPendientesAutorizacionUsuario = vm.Historico.Count(x => x.EstatusID == 14);
            vm.TotalEntregasParciales = vm.Historico.Count(x => x.EstatusID == 13);
            vm.MontoCotizado = vm.Historico.Sum(x => x.MontoTotal);

            return View(vm);
        }

        [HttpPost("SubirAutorizacionUrgente")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubirAutorizacionUrgente(
            int solicitudId,
            IFormFile? archivoAutorizacionUrgente)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            if (archivoAutorizacionUrgente == null || archivoAutorizacionUrgente.Length == 0)
            {
                TempData["Error"] = "Selecciona el archivo de autorización de compra urgente.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            string extension = Path.GetExtension(archivoAutorizacionUrgente.FileName)?.ToLowerInvariant() ?? "";
            var permitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

            if (!permitidas.Contains(extension))
            {
                TempData["Error"] = "La autorización urgente solo puede ser PDF o imagen JPG, PNG o WEBP.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var trans = conn.BeginTransaction();

            try
            {
                const string sqlValidar = @"
SELECT COUNT(*)
FROM Compras_Solicitud S
INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
WHERE S.SolicitudID = @SolicitudID
  AND S.UsuarioID = @UsuarioID
  AND S.EstatusID = 1
  AND ISNULL(U.RequiereAutorizacion, 0) = 1;";

                using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                {
                    cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdVal.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    if (Convert.ToInt32(await cmdVal.ExecuteScalarAsync()) == 0)
                    {
                        trans.Rollback();
                        TempData["Error"] = "La solicitud no está disponible para cargar una autorización urgente.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }

                await GuardarArchivoCompraAsync(
                    conn,
                    trans,
                    solicitudId,
                    null,
                    "AUTORIZACION_URGENTE",
                    archivoAutorizacionUrgente,
                    usuarioId,
                    _rutaNas.ObtenerRutaSolicitudesCompras(),
                    "AutorizacionUrgente"
                );

                trans.Commit();
                TempData["Mensaje"] = "La autorización de compra urgente se cargó correctamente.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }
            catch (Exception ex)
            {
                trans.Rollback();
                _logger.LogError(ex, "Error al cargar autorización urgente. SolicitudID={SolicitudID}", solicitudId);
                TempData["Error"] = "No se pudo guardar la autorización de compra urgente.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }
        }


        [HttpPost("ProcesarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarCotizacion([FromForm] ProcesarCotizacionesVm model)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            int solicitudId = model.SolicitudID;

            var cotizaciones = (model.Cotizaciones ?? new List<CotizacionCargaItemVm>())
                .Where(c =>
                    c != null &&
                    (
                        !string.IsNullOrWhiteSpace(c.Proveedor) ||
                        c.Monto.HasValue ||
                        (c.Archivo != null && c.Archivo.Length > 0)
                    ))
                .ToList();

            if (!cotizaciones.Any())
            {
                TempData["Error"] = "Debes agregar al menos una cotización.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            for (int i = 0; i < cotizaciones.Count; i++)
            {
                var cotizacion = cotizaciones[i];

                if (string.IsNullOrWhiteSpace(cotizacion.Proveedor) ||
                    !cotizacion.Monto.HasValue ||
                    cotizacion.Monto.Value <= 0 ||
                    cotizacion.Archivo == null ||
                    cotizacion.Archivo.Length == 0)
                {
                    TempData["Error"] =
                        $"La cotización {i + 1} debe tener proveedor, un monto mayor a cero y archivo.";

                    return RedirectToAction("Detalle", new { id = solicitudId });
                }
            }

            var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var trans = conn.BeginTransaction())
                    {
                        try
                        {
                            string sqlValidarSolicitud = @"
SELECT
    CASE WHEN ISNULL(U.RequiereAutorizacion, 0) = 1 THEN 1 ELSE 0 END AS RequiereAutorizacionUrgente,
    S.ProductoNuevo,
    S.FechaConfirmacionTecnica,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM Compras_Archivos A
        WHERE A.SolicitudID = S.SolicitudID
          AND A.TipoArchivo = 'AUTORIZACION_URGENTE'
          AND A.Vigente = 1
          AND A.Activo = 1
    ) THEN 1 ELSE 0 END AS TieneAutorizacionUrgente
FROM Compras_Solicitud S
INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 1;";

                            using (var cmdVal = new SqlCommand(sqlValidarSolicitud, conn, trans))
                            {
                                cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);

                                using var rdVal = await cmdVal.ExecuteReaderAsync();

                                if (!await rdVal.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "La solicitud ya no está pendiente de cotización.";
                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }

                                if (Convert.ToBoolean(rdVal["ProductoNuevo"]) &&
                                    rdVal["FechaConfirmacionTecnica"] == DBNull.Value)
                                {
                                    TempData["Error"] =
                                        "El solicitante debe confirmar los datos técnicos del producto nuevo antes de registrar cotizaciones.";
                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }

                                bool requiereAutorizacionUrgente =
                                    Convert.ToInt32(rdVal["RequiereAutorizacionUrgente"]) == 1;

                                bool tieneAutorizacionUrgente =
                                    Convert.ToInt32(rdVal["TieneAutorizacionUrgente"]) == 1;

                                if (requiereAutorizacionUrgente && !tieneAutorizacionUrgente)
                                {
                                    trans.Rollback();
                                    TempData["Error"] =
                                        "La solicitud está marcada como urgente, pero no tiene un archivo de autorización vigente. No es posible cargar cotizaciones hasta corregirlo.";
                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }
                            }

                            int numeroCotizacion;

                            using (var cmdNumero = new SqlCommand(@"
SELECT ISNULL(MAX(NumeroCotizacion), 0)
FROM Compras_Cotizaciones
WHERE SolicitudID = @SolicitudID", conn, trans))
                            {
                                cmdNumero.Parameters.AddWithValue("@SolicitudID", solicitudId);
                                numeroCotizacion = Convert.ToInt32(await cmdNumero.ExecuteScalarAsync());
                            }

                            string rutaContenedor = _rutaNas.ObtenerRutaUnicaCompras();
                            string folioStr = solicitudId.ToString().PadLeft(5, '0');

                            _sftp.AsegurarDirectorio(rutaContenedor);

                            foreach (var cotizacion in cotizaciones)
                            {
                                numeroCotizacion++;

                                string proveedor = cotizacion.Proveedor!.Trim();
                                decimal monto = cotizacion.Monto!.Value;
                                IFormFile archivo = cotizacion.Archivo!;

                                string extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";

                                if (!extensionesPermitidas.Contains(extension))
                                {
                                    trans.Rollback();
                                    TempData["Error"] =
                                        $"La cotización {numeroCotizacion} tiene un formato no permitido. Solo se aceptan PDF, JPG, PNG o WEBP.";

                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }

                                string nombreOriginal = Path.GetFileName(archivo.FileName);
                                string nombreArchivo =
                                    $"COM-{folioStr}_Cotizacion_{numeroCotizacion}_{Guid.NewGuid()}{extension}";
                                string rutaArchivoSftp = $"{rutaContenedor}/{nombreArchivo}";

                                using (var stream = archivo.OpenReadStream())
                                {
                                    _sftp.SubirStream(stream, rutaArchivoSftp);
                                }

                                string sqlCot = @"
INSERT INTO Compras_Cotizaciones
(
    SolicitudID,
    ArchivoPath,
    MontoTotal,
    FechaEnvioAlUsuario,
    Proveedor,
    ComentariosCompras,
    EsRecomendada,
    NumeroCotizacion,
    NombreArchivoOriginal,
    Extension,
    ContentType,
    TamanoBytes,
    UsuarioSubioID
)
VALUES
(
    @SolicitudID,
    @ArchivoPath,
    @MontoTotal,
    GETDATE(),
    @Proveedor,
    @ComentariosCompras,
    @EsRecomendada,
    @NumeroCotizacion,
    @NombreArchivoOriginal,
    @Extension,
    @ContentType,
    @TamanoBytes,
    @UsuarioSubioID
)";

                                using (var cmdCot = new SqlCommand(sqlCot, conn, trans))
                                {
                                    cmdCot.Parameters.AddWithValue("@SolicitudID", solicitudId);
                                    cmdCot.Parameters.AddWithValue("@ArchivoPath", rutaArchivoSftp);

                                    var paramMonto = new SqlParameter("@MontoTotal", SqlDbType.Decimal)
                                    {
                                        Precision = 18,
                                        Scale = 2,
                                        Value = monto
                                    };

                                    cmdCot.Parameters.Add(paramMonto);
                                    cmdCot.Parameters.AddWithValue("@Proveedor", proveedor);
                                    cmdCot.Parameters.AddWithValue(
                                        "@ComentariosCompras",
                                        (object?)model.ComentariosCompras?.Trim() ?? DBNull.Value
                                    );
                                    cmdCot.Parameters.AddWithValue("@EsRecomendada", false);
                                    cmdCot.Parameters.AddWithValue("@NumeroCotizacion", numeroCotizacion);
                                    cmdCot.Parameters.AddWithValue("@NombreArchivoOriginal", nombreOriginal);
                                    cmdCot.Parameters.AddWithValue("@Extension", extension);
                                    cmdCot.Parameters.AddWithValue(
                                        "@ContentType",
                                        archivo.ContentType ?? "application/octet-stream"
                                    );
                                    cmdCot.Parameters.AddWithValue("@TamanoBytes", archivo.Length);
                                    cmdCot.Parameters.AddWithValue("@UsuarioSubioID", usuarioId);

                                    await cmdCot.ExecuteNonQueryAsync();
                                }
                            }

                            string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 2,
    FechaCotizacion = GETDATE()
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 1";

                            using (var cmdUpd = new SqlCommand(sqlUpdate, conn, trans))
                            {
                                cmdUpd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                                int filas = await cmdUpd.ExecuteNonQueryAsync();

                                if (filas == 0)
                                {
                                    trans.Rollback();
                                    TempData["Error"] =
                                        "No se pudo actualizar la solicitud. Verifica que siga en estatus 1.";

                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }
                            }

                            string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 2, GETDATE(), @Responsable)";

                            using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                            {
                                cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                                cmdHist.Parameters.AddWithValue(
                                    "@Responsable",
                                    User.Identity?.Name ?? "Sistema"
                                );

                                await cmdHist.ExecuteNonQueryAsync();
                            }

                            trans.Commit();

                            await NotificarSolicitante_CotizacionesListasAsync(solicitudId);

                            TempData["Mensaje"] =
                                $"Se guardaron {cotizaciones.Count} cotización(es) correctamente. Ahora selecciona la cotización que procede.";

                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }
                        catch (Exception ex)
                        {
                            trans.Rollback();

                            _logger.LogError(ex, "Error al guardar cotizaciones");
                            TempData["Error"] = ex.Message;

                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico en ProcesarCotizacion");
                TempData["Error"] = "Error técnico: " + ex.Message;

                return RedirectToAction("Detalle", new { id = solicitudId });
            }
        }


        //METODOS PARA NOTIFICAR POR CORREO 
        // 1. Notificar al Comprador que ya tiene una requisición aprobada para generar la O.C.
        private async Task NotificarCompra_RequisicionListaAsync(int solicitudId, string idRequisicion, bool esDesviacion)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    var sql = @"SELECT Folio, PuestoAsignado FROM Compras_Solicitud WHERE SolicitudID = @id";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", solicitudId);
                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (await rd.ReadAsync())
                            {
                                var folio = rd["Folio"].ToString();
                                var puesto = rd["PuestoAsignado"].ToString();

                                // Buscamos a las personas con ese puesto para mandarles correo
                                var compradoresIds = new List<int>();
                                var sqlCompradores = "SELECT PersonaID FROM Persona WHERE Puesto = @puesto AND EsColaboradorActivo = 1";
                                // (Lógica para llenar compradoresIds...)

                                var asunto = $"Requisición Lista - Folio {folio}";
                                var html = $@"<h2>Requisición Autorizada</h2>
                                      <p>Control Presupuestal ha liberado la requisición <b>{idRequisicion}</b>.</p>
                                      {(esDesviacion ? "<p style='color:red;'><b>Nota:</b> Esta compra incluye una desviación aprobada.</p>" : "")}
                                      <p>Favor de proceder con la creación de la Orden de Compra.</p>";

                                await _notif.EnviarABccPersonasAsync(compradoresIds, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error notificar compra"); }
        }

        // 2. Notificar al Usuario Solicitante sobre el dictamen
        private async Task NotificarUsuario_DictamenPresupuestalAsync(int solicitudId, bool aprobado, string motivo)
        {
            // Lógica similar enviando correo al UsuarioID de la solicitud
            // Indicando si fue 'Aprobado' o 'Rechazado' por Control Presupuestal.
        }






        //Metodo para que control presupuestal decida si se aprueba o no el gasto
        [HttpGet("Dictamen/{id}")]
        [HttpGet("Compras/Dictamen/{id}")]
        public async Task<IActionResult> Dictamen(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var model = new DictamenPresupuestalVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool esControlPresupuestal =
                    await UsuarioPerteneceADepartamentoAsync(
                        conn,
                        usuarioId,
                        "CONTROL PRESUPUESTAL",
                        "PRESUPUESTOS",
                        "CIS"
                    );

                if (!esControlPresupuestal)
                    return Forbid();

                string sql = @"
SELECT 
    S.SolicitudID,
    S.Folio,
    S.MontoPresupuestoSolicitado,
    S.FueraPresupuestoUsuario,

    ADesv.RutaArchivo AS ArchivoDesviacionPath,
    ADesv.NombreOriginal AS NombreArchivoDesviacion,
    ADesv.Extension AS ExtensionArchivoDesviacion,

    ADesvFirma.RutaArchivo AS ArchivoDesviacionParaFirmaPath,
    ADesvFirma.NombreOriginal AS NombreArchivoDesviacionParaFirma,
    ADesvFirma.Extension AS ExtensionArchivoDesviacionParaFirma,

AReq.RutaArchivo AS ArchivoFormatoRequisicionPath,
AReq.NombreOriginal AS NombreArchivoFormatoRequisicion,
AReq.Extension AS ExtensionArchivoFormatoRequisicion,

    C.CotizacionID,
    C.Proveedor,
    C.MontoTotal,
    C.ArchivoPath,
    C.NombreArchivoOriginal,
    C.Extension
FROM Compras_Solicitud S
INNER JOIN Compras_Cotizaciones C 
    ON S.CotizacionSeleccionadaID = C.CotizacionID
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND (CA.CotizacionID = S.CotizacionSeleccionadaID OR CA.CotizacionID IS NULL)
      AND CA.TipoArchivo = 'DESVIACION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) ADesv
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'DESVIACION_CP'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) ADesvFirma
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'FORMATO_REQUISICION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) AReq
WHERE S.SolicitudID = @id
  AND S.EstatusID = 4";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                        {
                            TempData["Error"] = "No se encontró una cotización seleccionada para dictaminar.";
                            return RedirectToAction("BandejaPresupuestos");
                        }

                        model.SolicitudID = (int)reader["SolicitudID"];
                        model.Folio = reader["Folio"] == DBNull.Value ? "" : reader["Folio"].ToString();
                        model.CotizacionID = (int)reader["CotizacionID"];
                        model.Proveedor = reader["Proveedor"] == DBNull.Value ? "" : reader["Proveedor"].ToString();
                        model.MontoCotizado = reader["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["MontoTotal"]);
                        model.ArchivoPath = reader["ArchivoPath"] == DBNull.Value ? "" : reader["ArchivoPath"].ToString();
                        model.NombreArchivoOriginal = reader["NombreArchivoOriginal"] == DBNull.Value ? "" : reader["NombreArchivoOriginal"].ToString();
                        model.Extension = reader["Extension"] == DBNull.Value ? "" : reader["Extension"].ToString();
                        model.MontoPresupuestoSolicitado = reader["MontoPresupuestoSolicitado"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(reader["MontoPresupuestoSolicitado"]);
                        model.FueraPresupuestoUsuario = reader["FueraPresupuestoUsuario"] != DBNull.Value && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);
                        model.ArchivoDesviacionPath = reader["ArchivoDesviacionPath"] == DBNull.Value ? null : reader["ArchivoDesviacionPath"].ToString();
                        model.NombreArchivoDesviacion = reader["NombreArchivoDesviacion"] == DBNull.Value ? null : reader["NombreArchivoDesviacion"].ToString();
                        model.ExtensionArchivoDesviacion = reader["ExtensionArchivoDesviacion"] == DBNull.Value ? null : reader["ExtensionArchivoDesviacion"].ToString();
                        model.ArchivoDesviacionParaFirmaPath = reader["ArchivoDesviacionParaFirmaPath"] == DBNull.Value ? null : reader["ArchivoDesviacionParaFirmaPath"].ToString();
                        model.NombreArchivoDesviacionParaFirma = reader["NombreArchivoDesviacionParaFirma"] == DBNull.Value ? null : reader["NombreArchivoDesviacionParaFirma"].ToString();
                        model.ExtensionArchivoDesviacionParaFirma = reader["ExtensionArchivoDesviacionParaFirma"] == DBNull.Value ? null : reader["ExtensionArchivoDesviacionParaFirma"].ToString();
                        model.ArchivoFormatoRequisicionPath = reader["ArchivoFormatoRequisicionPath"] == DBNull.Value ? null : reader["ArchivoFormatoRequisicionPath"].ToString();
                        model.NombreArchivoFormatoRequisicion = reader["NombreArchivoFormatoRequisicion"] == DBNull.Value ? null : reader["NombreArchivoFormatoRequisicion"].ToString();
                        model.ExtensionArchivoFormatoRequisicion = reader["ExtensionArchivoFormatoRequisicion"] == DBNull.Value ? null : reader["ExtensionArchivoFormatoRequisicion"].ToString();
                    }
                }
            }

            return View(model);
        }

        //METODO PARA ASIGNAR A UN COMPRADOR
        [HttpPost("AsignarComprador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarComprador(int solicitudId, int compradorUsuarioId)
        {
            int usuarioDireccionId = ObtenerUsuarioIdActual();
            if (usuarioDireccionId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                int? empresaActivaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioDireccionId);

                if (!empresaActivaId.HasValue)
                    return Forbid();

                string puesto = "";

                using (var cmdPuesto = new SqlCommand(@"
SELECT P.Puesto
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE U.UsuarioID = @UsuarioID", conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioDireccionId);
                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                if (puesto != "DIRECCION COMPRAS")
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int empresaSolicitudId;
                        int? anteriorId = null;

                        const string sqlSolicitud = @"
SELECT
    EmpresaID,
    CompradorAsignadoUsuarioID
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID";

                        using (var cmdSolicitud = new SqlCommand(sqlSolicitud, conn, trans))
                        {
                            cmdSolicitud.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            using (var reader = await cmdSolicitud.ExecuteReaderAsync())
                            {
                                if (!await reader.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "No se encontró la solicitud.";
                                    return RedirectToAction("BandejaCompras");
                                }

                                empresaSolicitudId = Convert.ToInt32(reader["EmpresaID"]);

                                if (reader["CompradorAsignadoUsuarioID"] != DBNull.Value)
                                    anteriorId = Convert.ToInt32(reader["CompradorAsignadoUsuarioID"]);
                            }
                        }

                        if (!EmpresaSolicitudPermitidaEnBandeja(
                            empresaActivaId.Value,
                            empresaSolicitudId))
                        {
                            trans.Rollback();
                            return Forbid();
                        }

                        int empresaCompradoresId = ObtenerEmpresaCompradoresId(empresaSolicitudId);

                        const string sqlValidarComprador = @"
SELECT COUNT(*)
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
LEFT JOIN UsuariosEmpresas UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaCompradoresID
WHERE U.UsuarioID = @CompradorID
  AND U.Activo = 1
  AND (
      P.EsColaboradorActivo = 1
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioDireccionID AND U.Username = N'adrian.nutri')
  )
  AND (
      UE.UsuarioID IS NOT NULL
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioDireccionID AND U.Username = N'adrian.nutri')
  )
  AND (UPPER(P.Puesto) IN ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')
      OR (@DemoCompradores = 1 AND U.UsuarioID = @UsuarioDireccionID AND U.Username = N'adrian.nutri')
      OR (@DemoCompradores = 1 AND (
       (P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ADRIAN%' AND P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%MAZON%')
       OR (P.Nombre COLLATE Latin1_General_CI_AI LIKE N'%ANA%' AND
           (P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%NAVARRO%' OR P.ApellidoPaterno COLLATE Latin1_General_CI_AI LIKE N'%GONZALEZ%'))
   )))";

                        using (var cmdValidar = new SqlCommand(sqlValidarComprador, conn, trans))
                        {
                            cmdValidar.Parameters.AddWithValue("@CompradorID", compradorUsuarioId);
                            cmdValidar.Parameters.AddWithValue("@EmpresaCompradoresID", empresaCompradoresId);
                            cmdValidar.Parameters.AddWithValue("@DemoCompradores", CompradoresDemoHabilitados ? 1 : 0);
                            cmdValidar.Parameters.AddWithValue("@UsuarioDireccionID", usuarioDireccionId);

                            int existe = Convert.ToInt32(await cmdValidar.ExecuteScalarAsync());

                            if (existe == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "El comprador seleccionado no pertenece a la empresa operativa de Compras.";
                                return RedirectToAction("BandejaCompras");
                            }
                        }

                        const string sqlUpdate = @"
UPDATE Compras_Solicitud
SET CompradorAsignadoUsuarioID = @NuevoCompradorID,
    FechaAsignacionComprador = GETDATE(),
    UsuarioAsignoCompradorID = @DireccionID
WHERE SolicitudID = @SolicitudID
  AND EmpresaID = @EmpresaSolicitudID";

                        using (var cmdUpdate = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmdUpdate.Parameters.AddWithValue("@NuevoCompradorID", compradorUsuarioId);
                            cmdUpdate.Parameters.AddWithValue("@DireccionID", usuarioDireccionId);
                            cmdUpdate.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdUpdate.Parameters.AddWithValue("@EmpresaSolicitudID", empresaSolicitudId);

                            int filas = await cmdUpdate.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar la asignación del comprador.";
                                return RedirectToAction("BandejaCompras");
                            }
                        }

                        const string sqlHist = @"
INSERT INTO Compras_Asignaciones_Historico
(
    SolicitudID,
    UsuarioAsignadoAnteriorID,
    UsuarioAsignadoNuevoID,
    UsuarioDireccionID,
    FechaAsignacion,
    Comentario
)
VALUES
(
    @SolicitudID,
    @AnteriorID,
    @NuevoID,
    @DireccionID,
    GETDATE(),
    @Comentario
)";

                        using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist.Parameters.AddWithValue("@AnteriorID", (object?)anteriorId ?? DBNull.Value);
                            cmdHist.Parameters.AddWithValue("@NuevoID", compradorUsuarioId);
                            cmdHist.Parameters.AddWithValue("@DireccionID", usuarioDireccionId);
                            cmdHist.Parameters.AddWithValue("@Comentario", "Asignación manual desde Dirección Compras");

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        TempData["Mensaje"] = "Comprador asignado correctamente.";
                        return RedirectToAction("BandejaCompras");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(
                            ex,
                            "Error al reasignar comprador para la solicitud {SolicitudID}",
                            solicitudId
                        );

                        TempData["Error"] = "No se pudo reasignar comprador.";
                        return RedirectToAction("BandejaCompras");
                    }
                }
            }
        }

        //metodo post para CP

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarDictamen(DictamenPresupuestalVm vm)
        {
            if (vm.SolicitudID == 0)
                return BadRequest("Error: No se recibió el ID de la solicitud.");

            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            string responsable = User.Identity?.Name ?? "Sistema";

            vm.TipoGasto = vm.TipoGasto?.Trim();
            vm.NumeroRequisicion = vm.NumeroRequisicion?.Trim();
            vm.Observaciones = vm.Observaciones?.Trim();

            if (vm.Pasa && string.IsNullOrWhiteSpace(vm.TipoGasto))
            {
                TempData["Error"] = "Debes seleccionar el tipo de gasto.";
                return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
            }

            string tipoGastoNormalizado = (vm.TipoGasto ?? "")
                .Trim()
                .ToUpper()
                .Replace("Ó", "O");

            if (vm.Pasa &&
                tipoGastoNormalizado == "REQUISICION" &&
                string.IsNullOrWhiteSpace(vm.NumeroRequisicion))
            {
                TempData["Error"] = "Debes capturar el número de requisición.";
                return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
            }

            if (!vm.Pasa && string.IsNullOrWhiteSpace(vm.Observaciones))
            {
                TempData["Error"] = "Debes capturar el motivo del rechazo.";
                return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool esControlPresupuestal =
                    await UsuarioPerteneceADepartamentoAsync(
                        conn,
                        usuarioId,
                        "CONTROL PRESUPUESTAL",
                        "PRESUPUESTOS",
                        "CIS"
                    );

                if (!esControlPresupuestal)
                    return Forbid();

                // Coherencia con la clasificación de mantenimiento capturada al crear la solicitud.
                if (vm.Pasa)
                {
                    await using var cmdClase = new SqlCommand(
                        "SELECT ClasificacionSolicitud FROM dbo.Compras_Solicitud WHERE SolicitudID=@SolicitudID", conn);
                    cmdClase.Parameters.Add("@SolicitudID", SqlDbType.Int).Value = vm.SolicitudID;
                    var clase = (await cmdClase.ExecuteScalarAsync())?.ToString();
                    if (clase == "GASTO_MANTENIMIENTO" && tipoGastoNormalizado != "GASTO")
                    {
                        TempData["Error"] = "Una solicitud de mantenimiento debe dictaminarse como GASTO.";
                        return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                    }
                }

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int nuevoEstatus = vm.Pasa ? 5 : 11;

                        bool fueraPresupuestoUsuario = false;
                        bool tieneArchivoDesviacion = false;
                        bool tieneDesviacionParaFirma = false;
                        bool tieneFormatoRequisicion = false;

                        decimal? montoPresupuestoSolicitado = null;
                        decimal montoCotizado = 0;
                        bool requiereDesviacionPorMonto = false;

                        string sqlValidarPresupuestoInicial = @"
SELECT 
    S.FueraPresupuestoUsuario,
    S.MontoPresupuestoSolicitado,
    C.MontoTotal AS MontoCotizado,

    CASE 
        WHEN EXISTS (
            SELECT 1
            FROM Compras_Archivos A
            WHERE A.SolicitudID = S.SolicitudID
              AND A.TipoArchivo = 'DESVIACION'
              AND A.Vigente = 1
              AND A.Activo = 1
              AND (
                    A.CotizacionID = S.CotizacionSeleccionadaID
                    OR A.CotizacionID IS NULL
                  )
        )
        THEN 1 ELSE 0
    END AS TieneArchivoDesviacion,

    CASE
        WHEN EXISTS (
            SELECT 1
            FROM Compras_Archivos A
            WHERE A.SolicitudID = S.SolicitudID
              AND A.TipoArchivo = 'DESVIACION_CP'
              AND A.Vigente = 1
              AND A.Activo = 1
              AND (
                    A.CotizacionID = S.CotizacionSeleccionadaID
                    OR A.CotizacionID IS NULL
                  )
        )
        THEN 1 ELSE 0
    END AS TieneDesviacionParaFirma,

    CASE 
        WHEN EXISTS (
            SELECT 1
            FROM Compras_Archivos A
            WHERE A.SolicitudID = S.SolicitudID
              AND A.TipoArchivo = 'FORMATO_REQUISICION'
              AND A.Vigente = 1
              AND A.Activo = 1
              AND (
                    A.CotizacionID = S.CotizacionSeleccionadaID
                    OR A.CotizacionID IS NULL
                  )
        )
        THEN 1 ELSE 0
    END AS TieneFormatoRequisicion

FROM Compras_Solicitud S
INNER JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 4";

                        using (var cmdValPres = new SqlCommand(sqlValidarPresupuestoInicial, conn, trans))
                        {
                            cmdValPres.Parameters.AddWithValue("@SolicitudID", vm.SolicitudID);

                            using (var reader = await cmdValPres.ExecuteReaderAsync())
                            {
                                if (!await reader.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "La solicitud ya no está pendiente de dictamen.";
                                    return RedirectToAction("BandejaPresupuestos");
                                }

                                fueraPresupuestoUsuario =
                                    reader["FueraPresupuestoUsuario"] != DBNull.Value
                                    && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);

                                montoPresupuestoSolicitado =
                                    reader["MontoPresupuestoSolicitado"] == DBNull.Value
                                        ? null
                                        : Convert.ToDecimal(reader["MontoPresupuestoSolicitado"]);

                                montoCotizado =
                                    reader["MontoCotizado"] == DBNull.Value
                                        ? 0
                                        : Convert.ToDecimal(reader["MontoCotizado"]);

                                requiereDesviacionPorMonto =
                                    montoPresupuestoSolicitado.HasValue
                                    && montoCotizado > montoPresupuestoSolicitado.Value;

                                tieneArchivoDesviacion =
                                    reader["TieneArchivoDesviacion"] != DBNull.Value
                                    && Convert.ToInt32(reader["TieneArchivoDesviacion"]) == 1;

                                tieneDesviacionParaFirma =
                                    reader["TieneDesviacionParaFirma"] != DBNull.Value
                                    && Convert.ToInt32(reader["TieneDesviacionParaFirma"]) == 1;

                                tieneFormatoRequisicion =
                                    reader["TieneFormatoRequisicion"] != DBNull.Value
                                    && Convert.ToInt32(reader["TieneFormatoRequisicion"]) == 1;
                            }
                        }

                        bool requiereDesviacion =
                            fueraPresupuestoUsuario ||
                            requiereDesviacionPorMonto ||
                            tieneDesviacionParaFirma ||
                            vm.DentroDePresupuesto == false;

                        if (vm.Pasa && requiereDesviacion && !tieneArchivoDesviacion)
                        {
                            trans.Rollback();

                            TempData["Error"] =
                                "No puedes aprobar esta solicitud porque requiere desviación y no existe archivo de desviación vigente.";

                            return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                        }

                        if (vm.Pasa && requiereDesviacion && vm.DentroDePresupuesto == true)
                        {
                            trans.Rollback();

                            TempData["Error"] =
                                "La solicitud requiere desviación. No puede dictaminarse como dentro de presupuesto.";

                            return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                        }

                        if (vm.Pasa &&
                            tipoGastoNormalizado == "REQUISICION" &&
                            !tieneFormatoRequisicion)
                        {
                            trans.Rollback();

                            TempData["Error"] =
                                "No puedes aprobar como requisición porque no existe formato de requisición vigente cargado.";

                            return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                        }

                        string queryUpdate = @"
UPDATE Compras_Solicitud 
SET EstatusID = @Est,
    FechaDictamen = GETDATE(),
    TipoGasto = @Tipo,
    DentroPresupuesto = @Dentro,
    NumeroRequisicion = @Requi,
    ObservacionesPresupuesto = @Obs
WHERE SolicitudID = @Sid
  AND EstatusID = 4";

                        using (var cmd = new SqlCommand(queryUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmd.Parameters.AddWithValue("@Sid", vm.SolicitudID);

                            cmd.Parameters.AddWithValue(
                                "@Tipo",
                                vm.Pasa ? (object)tipoGastoNormalizado : DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@Dentro",
                                vm.Pasa ? (object)vm.DentroDePresupuesto : DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@Requi",
                                (vm.Pasa && tipoGastoNormalizado == "REQUISICION")
                                    ? (object)(vm.NumeroRequisicion ?? "")
                                    : DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@Obs",
                                (object?)vm.Observaciones ?? DBNull.Value
                            );

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud ya no está pendiente de dictamen.";
                                return RedirectToAction("BandejaPresupuestos");
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@Sid, @Est, GETDATE(), @Resp)";

                        using (var cmdHist = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@Sid", vm.SolicitudID);
                            cmdHist.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmdHist.Parameters.AddWithValue("@Resp", responsable);

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_DictamenPresupuestalAsync(
                            vm.SolicitudID,
                            vm.Pasa,
                            vm.Observaciones
                        );

                        if (vm.Pasa)
                        {
                            await NotificarCompras_DictamenAprobadoAsync(vm.SolicitudID);
                        }

                        return RedirectToAction("BandejaPresupuestos");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(ex, "Error al actualizar Compras_Solicitud");

                        TempData["Error"] = "Ocurrió un error al procesar el dictamen.";
                        return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                    }
                }
            }
        }


        [HttpGet("BandejaPresupuestos")]
        [HttpGet("Compras/BandejaPresupuestos")]
        public async Task<IActionResult> BandejaPresupuestos()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var vm = new ControlPresupuestalVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool esControlPresupuestal =
                    await UsuarioPerteneceADepartamentoAsync(
                        conn,
                        usuarioId,
                        "CONTROL PRESUPUESTAL",
                        "PRESUPUESTOS",
                        "CIS"

                    );

                if (!esControlPresupuestal)
                    return Forbid();

                string sqlPendientes = @"
SELECT S.SolicitudID, S.Folio, 
       (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
       S.TipoCompra, S.FechaCreacion, Est.Nombre AS Estatus
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Cat_EstatusCompra Est ON S.EstatusID = Est.EstatusID
WHERE S.EstatusID = 4
ORDER BY S.FechaCreacion ASC";

                using (var cmd = new SqlCommand(sqlPendientes, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        vm.Pendientes.Add(new BandejaComprasVm
                        {
                            SolicitudID = (int)reader["SolicitudID"],
                            Folio = reader["Folio"].ToString(),
                            Solicitante = reader["Solicitante"].ToString(),
                            TipoCompra = reader["TipoCompra"].ToString(),
                            FechaCreacion = (DateTime)reader["FechaCreacion"],
                            Estatus = reader["Estatus"].ToString()
                        });
                    }
                }

                string sqlHistorico = @"
SELECT S.SolicitudID, S.Folio,
       (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
       S.TipoCompra,
       ISNULL(C.MontoTotal, 0) AS MontoTotal,
       CASE 
    WHEN S.EstatusID >= 5 AND S.EstatusID NOT IN (11, 12) THEN 'Aprobado'
    WHEN S.EstatusID = 11 THEN 'Rechazado'
    WHEN S.EstatusID = 12 THEN 'Cancelado'
    ELSE 'Otro'
END AS Resultado,
       S.DentroPresupuesto,
       S.NumeroRequisicion,
       S.ObservacionesPresupuesto,
       S.FechaDictamen
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
LEFT JOIN Compras_Cotizaciones C 
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.FechaDictamen IS NOT NULL
  AND S.EstatusID IN (5, 6, 7, 8, 9, 10, 11, 12)
ORDER BY S.FechaDictamen DESC";

                using (var cmd = new SqlCommand(sqlHistorico, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        vm.Historico.Add(new HistoricoPresupuestoVm
                        {
                            SolicitudID = (int)reader["SolicitudID"],
                            Folio = reader["Folio"].ToString(),
                            Solicitante = reader["Solicitante"].ToString(),
                            TipoCompra = reader["TipoCompra"].ToString(),
                            MontoTotal = Convert.ToDecimal(reader["MontoTotal"]),
                            Resultado = reader["Resultado"].ToString(),
                            DentroPresupuesto = reader["DentroPresupuesto"] as bool?,
                            NumeroRequisicion = reader["NumeroRequisicion"]?.ToString(),
                            Observaciones = reader["ObservacionesPresupuesto"]?.ToString(),
                            FechaDictamen = reader["FechaDictamen"] == DBNull.Value
                                ? DateTime.MinValue
                                : (DateTime)reader["FechaDictamen"]
                        });
                    }
                }
            }

            vm.TotalPendientes = vm.Pendientes.Count;
            vm.TotalAprobadas = vm.Historico.Count(x => x.Resultado == "Aprobado");
            vm.TotalRechazadas = vm.Historico.Count(x => x.Resultado == "Rechazado");
            vm.MontoAprobado = vm.Historico.Where(x => x.Resultado == "Aprobado").Sum(x => x.MontoTotal);
            vm.MontoRechazado = vm.Historico.Where(x => x.Resultado == "Rechazado").Sum(x => x.MontoTotal);

            return View(vm);
        }

        // Lógica para detectar si Control Presupuestal se pasó de sus 24h
        public string ObtenerAlertaPresupuesto(DateTime fechaCotizacion)
        {
            var horasTranscurridas = (DateTime.Now - fechaCotizacion).TotalHours;

            if (horasTranscurridas > 48) return "text-danger font-weight-bold"; // Muy retrasado
            if (horasTranscurridas > 24) return "text-warning"; // Al límite
            return "text-success"; // A tiempo
        }




        private async Task NotificarACompras_NuevaRequisicion(int solicitudId, string requisicion)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    // Buscamos quién es el comprador asignado (Nacional o Internacional)
                    var sql = "SELECT Folio, PuestoAsignado FROM Compras_Solicitud WHERE SolicitudID = @id";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", solicitudId);
                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (await rd.ReadAsync())
                            {
                                string puesto = rd["PuestoAsignado"].ToString();
                                string folio = rd["Folio"].ToString();

                                // Buscamos los correos de las personas con ese puesto
                                var listaIds = new List<int>();
                                using (var conn2 = new SqlConnection(_connectionString))
                                {
                                    await conn2.OpenAsync();
                                    var sqlP = "SELECT PersonaID FROM Persona WHERE Puesto = @p AND EsColaboradorActivo = 1";
                                    using (var cmdP = new SqlCommand(sqlP, conn2))
                                    {
                                        cmdP.Parameters.AddWithValue("@p", puesto);
                                        using (var rdP = await cmdP.ExecuteReaderAsync())
                                        {
                                            while (await rdP.ReadAsync()) listaIds.Add((int)rdP["PersonaID"]);
                                        }
                                    }
                                }

                                var asunto = $"Nueva Requisición lista para O.C. - {folio}";
                                var html = $"<h2>Requisición Liberada</h2><p>La solicitud {folio} ya tiene número de requisición: <b>{requisicion}</b>. Favor de generar la Orden de Compra.</p>";
                                await _notif.EnviarABccPersonasAsync(listaIds, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error al notificar a compras"); }
        }

        private async Task NotificarUsuario_Rechazo(int solicitudId, string motivo)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    // Buscamos al usuario que creó la solicitud
                    var sql = @"SELECT S.UsuarioID, S.Folio, U.PersonaID 
                        FROM Compras_Solicitud S 
                        INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID 
                        WHERE S.SolicitudID = @id";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", solicitudId);
                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (await rd.ReadAsync())
                            {
                                int personaId = (int)rd["PersonaID"];
                                string folio = rd["Folio"].ToString();

                                var asunto = $"Solicitud de Compra Rechazada - {folio}";
                                var html = $"<h2>Solicitud No Procedente</h2><p>Lo sentimos, su solicitud <b>{folio}</b> ha sido rechazada por Control Presupuestal.</p><p><b>Motivo:</b> {motivo}</p>";
                                await _notif.EnviarABccPersonasAsync(new List<int> { personaId }, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error al notificar rechazo"); }
        }

        //METODO PARA LLOS GRAFICOS
        [HttpGet]
        public async Task<IActionResult> DashboardDireccion()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();
            using (var acceso = new SqlConnection(_connectionString))
            {
                await acceso.OpenAsync();
                using var cmdPuesto = new SqlCommand(@"SELECT P.Puesto FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID = U.PersonaID WHERE U.UsuarioID=@Uid", acceso);
                cmdPuesto.Parameters.Add("@Uid", SqlDbType.Int).Value = usuarioId;
                var puestoActual = ((await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "").ToUpperInvariant().Trim();
                bool esDireccion = puestoActual.Contains("DIRECCION") || puestoActual.Contains("DIRECCIÓN") ||
                    puestoActual.Contains("DIRECTOR") || puestoActual.StartsWith("DIR.");
                if (!esDireccion) return Forbid();
            }
            var vm = new ComprasDashboardVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("sp_Compras_DataDashBoard", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        // Lectura 1: Datos del Heatmap
                        while (await reader.ReadAsync())
                        {
                            var depto = reader["NombreDepartamento"].ToString();
                            var serie = vm.HeatmapData.FirstOrDefault(s => s.name == depto);

                            if (serie == null)
                            {
                                serie = new HeatmapSeries { name = depto };
                                vm.HeatmapData.Add(serie);
                            }

                            serie.data.Add(new HeatmapDataPoint
                            {
                                x = reader["Estatus"].ToString(),
                                y = Convert.ToInt32(reader["PromedioHorasEstancado"])
                            });
                        }

                        // Lectura 2: Datos de la Dona (Cumplimiento)
                        if (await reader.NextResultAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.DonaEtiquetas.Add(reader["Estatus"].ToString());
                                vm.DonaValores.Add(Convert.ToDecimal(reader["Total"]));
                            }
                        }
                    }
                }
            }
            return View(vm);
        }

        //HELPERS PARA OBTENER EMPRESAS DEL USUARIO
        private async Task<int?> ObtenerEmpresaActivaUsuarioAsync(
    SqlConnection conn,
    int usuarioId)
        {
            string sql = @"
        SELECT TOP 1 UE.EmpresaID
        FROM UsuariosEmpresas UE
        INNER JOIN Empresas E ON UE.EmpresaID = E.EmpresaID
        WHERE UE.UsuarioID = @UsuarioID
          AND UE.Activo = 1
          AND E.Activa = 1
        ORDER BY UE.EmpresaID";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                var result = await cmd.ExecuteScalarAsync();

                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToInt32(result);
            }
        }

        private async Task<List<int>> ObtenerEmpresasActivasUsuarioAsync(SqlConnection conn, int usuarioId)
        {
            var empresas = new List<int>();

            string sql = @"
        SELECT UE.EmpresaID
        FROM UsuariosEmpresas UE
        INNER JOIN Empresas E ON UE.EmpresaID = E.EmpresaID
        WHERE UE.UsuarioID = @UsuarioID
          AND UE.Activo = 1
          AND E.Activa = 1";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        empresas.Add((int)reader["EmpresaID"]);
                    }
                }
            }

            return empresas;
        }

        private async Task<int?> ObtenerDepartamentoEmpresaIdAsync(
    SqlConnection conn,
    int empresaId,
    string nombreDepartamento)
        {
            string sql = @"
        SELECT TOP 1 DE.DepartamentoEmpresaID
        FROM DepartamentoEmpresa DE
        INNER JOIN Departamentos D ON DE.DepartamentoID = D.DepartamentoID
        WHERE DE.EmpresaID = @EmpresaID
          AND D.Activo = 1
          AND UPPER(D.NombreDepartamento) = UPPER(@NombreDepartamento)";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
                cmd.Parameters.AddWithValue("@NombreDepartamento", nombreDepartamento);

                var result = await cmd.ExecuteScalarAsync();

                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToInt32(result);
            }
        }


        //METODO PARA REGISTRAR OC
        [HttpGet("RegistrarOC/{id}")]
        [HttpGet("Compras/RegistrarOC/{id}")]
        public async Task<IActionResult> RegistrarOC(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var vm = new RegistrarOrdenCompraVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string sql = @"
        SELECT
            S.SolicitudID,
            S.Folio,
            E.Nombre AS Empresa,
            P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
        FROM Compras_Solicitud S
        INNER JOIN Empresas E
            ON S.EmpresaID = E.EmpresaID
        INNER JOIN Usuarios U
            ON S.UsuarioID = U.UsuarioID
        INNER JOIN Persona P
            ON U.PersonaID = P.PersonaID
        WHERE S.SolicitudID = @SolicitudID
          AND S.EstatusID = 5";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                            return NotFound();

                        vm.SolicitudID = (int)reader["SolicitudID"];
                        vm.Folio = reader["Folio"].ToString();
                        vm.Empresa = reader["Empresa"].ToString();
                        vm.Solicitante = reader["Solicitante"].ToString();
                    }
                }
            }

            return View(vm);
        }


        [HttpPost("RegistrarOC")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarOC(int solicitudId, string numeroOC, string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            numeroOC = numeroOC?.Trim();

            comentarios = comentarios?.Trim();

            if (string.IsNullOrWhiteSpace(numeroOC))
            {
                TempData["Error"] = "Debes capturar el número de O.C.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string sqlValidarSolicitud = @"
SELECT COUNT(*)
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 5
  AND CotizacionSeleccionadaID IS NOT NULL";

                        using (var cmdValSol = new SqlCommand(sqlValidarSolicitud, conn, trans))
                        {
                            cmdValSol.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int validaSolicitud = Convert.ToInt32(await cmdValSol.ExecuteScalarAsync());

                            if (validaSolicitud == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud no está autorizada para registrar O.C.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlExiste = @"
SELECT COUNT(*)
FROM Compras_OrdenCompra
WHERE SolicitudID = @SolicitudID
  AND Activo = 1";

                        using (var cmdExiste = new SqlCommand(sqlExiste, conn, trans))
                        {
                            cmdExiste.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int existe = Convert.ToInt32(await cmdExiste.ExecuteScalarAsync());

                            if (existe > 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "Esta solicitud ya tiene una O.C. registrada.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlCotizacionSeleccionada = @"
SELECT C.Proveedor, C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 5";

                        string proveedorCotizacion = "";
                        decimal montoCotizacion = 0;

                        using (var cmdCot = new SqlCommand(sqlCotizacionSeleccionada, conn, trans))
                        {
                            cmdCot.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            using var rd = await cmdCot.ExecuteReaderAsync();
                            if (!await rd.ReadAsync())
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se encontró la cotización seleccionada.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            proveedorCotizacion = rd["Proveedor"]?.ToString() ?? "";
                            montoCotizacion = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
                        }

                        string sqlInsert = @"
INSERT INTO Compras_OrdenCompra
(SolicitudID, NumeroOC, Proveedor, FechaOC, Comentarios, UsuarioOCID)
VALUES
(@SolicitudID, @NumeroOC, @Proveedor, GETDATE(), @Comentarios, @UsuarioOCID)";

                        using (var cmd = new SqlCommand(sqlInsert, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@NumeroOC", numeroOC);
                            cmd.Parameters.AddWithValue("@Proveedor", proveedorCotizacion);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@UsuarioOCID", usuarioId);

                            int filasInsert = await cmd.ExecuteNonQueryAsync();

                            if (filasInsert == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo registrar la O.C.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID = 6
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 5";

                        using (var cmdUpdateSol = new SqlCommand(sqlUpdateSolicitud, conn, trans))
                        {
                            cmdUpdateSol.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int filasUpdateSol = await cmdUpdateSol.ExecuteNonQueryAsync();

                            if (filasUpdateSol == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar el estatus de la solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }



                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 6, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_OCRegistradaAsync(solicitudId);

                        TempData["Mensaje"] = "O.C. registrada correctamente. Ahora puedes enviarla al proveedor.";
                        return RedirectToAction("Detalle", new { id = solicitudId });

                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al registrar O.C.");

                        TempData["Error"] = "No se pudo registrar la O.C.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }


        //METODO PARA SUBIR UNA DESVIACIÓN 



        [HttpPost("EnviarOCProveedor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarOCProveedor(
     int solicitudId,
     DateTime fechaEstimadaEntrega,
     string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            comentarios = comentarios?.Trim();

            if (fechaEstimadaEntrega.Date < DateTime.Today)
            {
                TempData["Error"] = "La fecha estimada de entrega no puede ser anterior a hoy.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string sqlValidar = @"
SELECT COUNT(*)
FROM Compras_Solicitud S
INNER JOIN Compras_OrdenCompra OC
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 6
  AND OC.FechaEnvioProveedor IS NULL";

                        using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                        {
                            cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int valido = Convert.ToInt32(await cmdVal.ExecuteScalarAsync());

                            if (valido == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud no está disponible para enviar la O.C. al proveedor.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlUpdateOC = @"
UPDATE Compras_OrdenCompra
SET FechaEnvioProveedor = GETDATE(),
    FechaEstimadaEntrega = @FechaEstimadaEntrega,
    UsuarioEnvioProveedorID = @UsuarioID,
    Comentarios = 
        CASE 
            WHEN @Comentarios IS NULL OR @Comentarios = '' 
            THEN Comentarios
            ELSE @Comentarios
        END
WHERE SolicitudID = @SolicitudID
  AND Activo = 1
  AND FechaEnvioProveedor IS NULL";

                        using (var cmd = new SqlCommand(sqlUpdateOC, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@FechaEstimadaEntrega", fechaEstimadaEntrega);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);

                            int filasOC = await cmd.ExecuteNonQueryAsync();

                            if (filasOC == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar la O.C.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHistOCEnviada = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 7, GETDATE(), @Responsable)";

                        using (var cmdHist7 = new SqlCommand(sqlHistOCEnviada, conn, trans))
                        {
                            cmdHist7.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist7.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmdHist7.ExecuteNonQueryAsync();
                        }



                        string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID =
    CASE
        WHEN UPPER(ISNULL(TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN') THEN 8
        ELSE 9
    END
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 6";

                        int nuevoEstatusHistorico;

                        using (var cmdTipo = new SqlCommand(@"
SELECT 
    CASE
        WHEN UPPER(ISNULL(TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN') THEN 8
        ELSE 9
    END
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID", conn, trans))
                        {
                            cmdTipo.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            nuevoEstatusHistorico = Convert.ToInt32(await cmdTipo.ExecuteScalarAsync());
                        }

                        using (var cmd = new SqlCommand(sqlUpdateSolicitud, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int filasSolicitud = await cmd.ExecuteNonQueryAsync();

                            if (filasSolicitud == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar el estatus de la solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, @EstatusID, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@EstatusID", nuevoEstatusHistorico);
                            cmd.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_OCEnviadaProveedorAsync(solicitudId);

                        // Cuando el flujo es con requisición, Almacén debe saber que existe
                        // una orden pendiente de recepción (estatus 8).
                        if (nuevoEstatusHistorico == 8)
                        {
                            await NotificarAlmacen_OrdenPendienteRecepcionAsync(solicitudId);
                        }

                        TempData["Mensaje"] = "O.C. marcada como enviada al proveedor.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al enviar O.C. al proveedor");

                        TempData["Error"] = "No se pudo registrar el envío al proveedor.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }

        [HttpPost("RegistrarRecepcionAlmacen")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRecepcionAlmacen(
            int solicitudId,
            List<MovimientoMaterialPostVm>? materiales,
            string? comentarios,
            IFormFile? evidenciaRecepcion)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            comentarios = comentarios?.Trim();

            if (materiales == null || materiales.Count == 0)
            {
                TempData["Error"] = "Debes indicar la cantidad recibida de al menos un artículo.";
                return RedirectToAction("BandejaAlmacen");
            }

            if (materiales.Any(x => x.Cantidad.HasValue && x.Cantidad.Value < 0))
            {
                TempData["Error"] = "Las cantidades recibidas no pueden ser negativas.";
                return RedirectToAction("BandejaAlmacen");
            }

            var cantidadesCapturadas = materiales
                .Where(x => x.DetalleID > 0 && x.Cantidad.HasValue && x.Cantidad.Value > 0)
                .GroupBy(x => x.DetalleID)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Cantidad!.Value));

            if (cantidadesCapturadas.Count == 0)
            {
                TempData["Error"] = "Captura una cantidad mayor a cero para al menos un artículo.";
                return RedirectToAction("BandejaAlmacen");
            }

            if (evidenciaRecepcion == null || evidenciaRecepcion.Length == 0)
            {
                TempData["Error"] = "Debes adjuntar evidencia de la recepción.";
                return RedirectToAction("BandejaAlmacen");
            }

            var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
            string extensionEvidencia =
                Path.GetExtension(evidenciaRecepcion.FileName)?.ToLowerInvariant() ?? "";

            if (!extensionesPermitidas.Contains(extensionEvidencia))
            {
                TempData["Error"] = "La evidencia solo puede ser PDF o imagen JPG, PNG o WEBP.";
                return RedirectToAction("BandejaAlmacen");
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            bool perteneceAlmacen = await UsuarioPerteneceADepartamentoAsync(
                conn,
                usuarioId,
                "ALMACEN",
                "ALMACÉN"
            );

            if (!perteneceAlmacen)
                return Forbid();

            using var trans = conn.BeginTransaction();

            try
            {
                int ordenCompraId;

                const string sqlValidarSolicitud = @"
SELECT TOP 1 OC.OrdenCompraID
FROM Compras_Solicitud S WITH (UPDLOCK, HOLDLOCK)
INNER JOIN Compras_OrdenCompra OC
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
   AND OC.FechaEnvioProveedor IS NOT NULL
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID IN (8, 13)
  AND UPPER(ISNULL(S.TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN')
ORDER BY OC.OrdenCompraID DESC;";

                using (var cmdVal = new SqlCommand(sqlValidarSolicitud, conn, trans))
                {
                    cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    var result = await cmdVal.ExecuteScalarAsync();

                    if (result == null || result == DBNull.Value)
                    {
                        trans.Rollback();
                        TempData["Error"] = "La solicitud no está disponible para recepción en almacén.";
                        return RedirectToAction("BandejaAlmacen");
                    }

                    ordenCompraId = Convert.ToInt32(result);
                }

                var partidas = new Dictionary<int, (decimal Solicitada, decimal Recibida)>();

                const string sqlPartidas = @"
SELECT
    M.DetalleID,
    M.Cantidad AS CantidadSolicitada,
    ISNULL(SUM(CASE WHEN R.Activo = 1 THEN MD.Cantidad ELSE 0 END), 0) AS CantidadRecibida
FROM Compras_Detalle_Materiales M
LEFT JOIN Compras_MovimientoDetalle MD
    ON MD.DetalleID = M.DetalleID
   AND MD.RecepcionID IS NOT NULL
   AND MD.EntregaID IS NULL
LEFT JOIN Compras_Recepciones R
    ON R.RecepcionID = MD.RecepcionID
WHERE M.SolicitudID = @SolicitudID
GROUP BY M.DetalleID, M.Cantidad
ORDER BY M.DetalleID;";

                using (var cmdPartidas = new SqlCommand(sqlPartidas, conn, trans))
                {
                    cmdPartidas.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var reader = await cmdPartidas.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        partidas[Convert.ToInt32(reader["DetalleID"])] = (
                            Convert.ToDecimal(reader["CantidadSolicitada"]),
                            Convert.ToDecimal(reader["CantidadRecibida"])
                        );
                    }
                }

                if (partidas.Count == 0)
                {
                    trans.Rollback();
                    TempData["Error"] = "La solicitud no tiene artículos registrados.";
                    return RedirectToAction("BandejaAlmacen");
                }

                foreach (var captura in cantidadesCapturadas)
                {
                    if (!partidas.TryGetValue(captura.Key, out var partida))
                    {
                        trans.Rollback();
                        TempData["Error"] = "Uno de los artículos seleccionados no pertenece a la solicitud.";
                        return RedirectToAction("BandejaAlmacen");
                    }

                    decimal pendiente = Math.Max(partida.Solicitada - partida.Recibida, 0);

                    if (captura.Value > pendiente)
                    {
                        trans.Rollback();
                        TempData["Error"] =
                            $"La cantidad recibida del artículo {captura.Key} supera la cantidad pendiente ({pendiente:0.##}).";
                        return RedirectToAction("BandejaAlmacen");
                    }
                }

                string rutaContenedor = _rutaNas.ObtenerRutaSolicitudesCompras();
                _sftp.AsegurarDirectorio(rutaContenedor);

                string folioStr = solicitudId.ToString().PadLeft(5, '0');
                string nombreOriginalEvidencia = Path.GetFileName(evidenciaRecepcion.FileName);
                string nombreArchivoEvidencia =
                    $"COM-{folioStr}_RecepcionAlmacen_{Guid.NewGuid()}{extensionEvidencia}";
                string rutaArchivoEvidenciaSftp = $"{rutaContenedor}/{nombreArchivoEvidencia}";

                using (var stream = evidenciaRecepcion.OpenReadStream())
                {
                    _sftp.SubirStream(stream, rutaArchivoEvidenciaSftp);
                }

                int recepcionId;

                const string sqlInsertRecepcion = @"
INSERT INTO Compras_Recepciones
(
    SolicitudID,
    OrdenCompraID,
    FechaRecepcion,
    UsuarioRecibioID,
    Comentarios,
    EvidenciaRecepcionPath,
    NombreArchivoEvidencia,
    ExtensionArchivoEvidencia,
    ContentTypeEvidencia,
    TamanoBytesEvidencia,
    FechaCargaEvidencia,
    Activo
)
VALUES
(
    @SolicitudID,
    @OrdenCompraID,
    GETDATE(),
    @UsuarioID,
    @Comentarios,
    @EvidenciaRecepcionPath,
    @NombreArchivoEvidencia,
    @ExtensionArchivoEvidencia,
    @ContentTypeEvidencia,
    @TamanoBytesEvidencia,
    GETDATE(),
    1
);
SELECT SCOPE_IDENTITY();";

                using (var cmdRecepcion = new SqlCommand(sqlInsertRecepcion, conn, trans))
                {
                    cmdRecepcion.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdRecepcion.Parameters.AddWithValue("@OrdenCompraID", ordenCompraId);
                    cmdRecepcion.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmdRecepcion.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);
                    cmdRecepcion.Parameters.AddWithValue("@EvidenciaRecepcionPath", rutaArchivoEvidenciaSftp);
                    cmdRecepcion.Parameters.AddWithValue("@NombreArchivoEvidencia", nombreOriginalEvidencia);
                    cmdRecepcion.Parameters.AddWithValue("@ExtensionArchivoEvidencia", extensionEvidencia);
                    cmdRecepcion.Parameters.AddWithValue(
                        "@ContentTypeEvidencia",
                        evidenciaRecepcion.ContentType ?? "application/octet-stream"
                    );
                    cmdRecepcion.Parameters.AddWithValue("@TamanoBytesEvidencia", evidenciaRecepcion.Length);

                    recepcionId = Convert.ToInt32(await cmdRecepcion.ExecuteScalarAsync());
                }

                const string sqlInsertDetalle = @"
INSERT INTO Compras_MovimientoDetalle
(
    RecepcionID,
    EntregaID,
    DetalleID,
    Cantidad
)
VALUES
(
    @RecepcionID,
    NULL,
    @DetalleID,
    @Cantidad
);";

                foreach (var captura in cantidadesCapturadas)
                {
                    using var cmdDetalle = new SqlCommand(sqlInsertDetalle, conn, trans);
                    cmdDetalle.Parameters.AddWithValue("@RecepcionID", recepcionId);
                    cmdDetalle.Parameters.AddWithValue("@DetalleID", captura.Key);

                    var pCantidad = new SqlParameter("@Cantidad", SqlDbType.Decimal)
                    {
                        Precision = 18,
                        Scale = 2,
                        Value = captura.Value
                    };
                    cmdDetalle.Parameters.Add(pCantidad);

                    await cmdDetalle.ExecuteNonQueryAsync();
                }

                bool recepcionCompleta = partidas.All(x =>
                {
                    decimal recibidaAhora = cantidadesCapturadas.TryGetValue(x.Key, out var cantidad)
                        ? cantidad
                        : 0;

                    return x.Value.Recibida + recibidaAhora >= x.Value.Solicitada;
                });

                int nuevoEstatus = recepcionCompleta ? 10 : 13;

                const string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID = @NuevoEstatus
WHERE SolicitudID = @SolicitudID
  AND EstatusID IN (8, 13);";

                using (var cmdUpdate = new SqlCommand(sqlUpdateSolicitud, conn, trans))
                {
                    cmdUpdate.Parameters.AddWithValue("@NuevoEstatus", nuevoEstatus);
                    cmdUpdate.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    if (await cmdUpdate.ExecuteNonQueryAsync() == 0)
                    {
                        trans.Rollback();
                        TempData["Error"] = "La solicitud cambió de estatus antes de registrar la recepción.";
                        return RedirectToAction("BandejaAlmacen");
                    }
                }

                const string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, @EstatusID, GETDATE(), @Responsable);";

                using (var cmdHist = new SqlCommand(sqlHistorico, conn, trans))
                {
                    cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdHist.Parameters.AddWithValue("@EstatusID", nuevoEstatus);
                    cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                    await cmdHist.ExecuteNonQueryAsync();
                }

                trans.Commit();

                // Informa tanto recepciones parciales (estatus 13) como completas (estatus 10).
                // El correo incluye el avance acumulado por artículo y las cantidades pendientes.
                await NotificarAvanceMaterialesAsync(
                    solicitudId,
                    esRecepcionAlmacen: true,
                    movimientoCompleto: recepcionCompleta
                );

                if (recepcionCompleta)
                {
                    TempData["Mensaje"] = "La recepción completa del pedido fue registrada correctamente.";
                }
                else
                {
                    TempData["Mensaje"] =
                        "La recepción parcial fue registrada. La solicitud permanecerá abierta hasta recibir todos los artículos.";
                }

                return RedirectToAction("BandejaAlmacen");
            }
            catch (Exception ex)
            {
                trans.Rollback();
                _logger.LogError(ex, "Error al registrar recepción parcial en almacén");
                TempData["Error"] = "No se pudo registrar la recepción en almacén: " + ex.Message;
                return RedirectToAction("BandejaAlmacen");
            }
        }

        [HttpPost("RegistrarEntregaUsuario")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarEntregaUsuario(
            int solicitudId,
            List<MovimientoMaterialPostVm>? materiales,
            string nombreRecibe,
            string? comentarios,
            IFormFile? evidenciaEntrega)
        {
            int usuarioEntregaId = ObtenerUsuarioIdActual();
            if (usuarioEntregaId == 0) return Unauthorized();

            nombreRecibe = nombreRecibe?.Trim() ?? "";
            comentarios = comentarios?.Trim();

            if (string.IsNullOrWhiteSpace(nombreRecibe))
            {
                TempData["Error"] = "Debes capturar quién recibe la mercancía.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            if (materiales == null || materiales.Count == 0)
            {
                TempData["Error"] = "Debes indicar la cantidad entregada de al menos un artículo.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            if (materiales.Any(x => x.Cantidad.HasValue && x.Cantidad.Value < 0))
            {
                TempData["Error"] = "Las cantidades entregadas no pueden ser negativas.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            var cantidadesCapturadas = materiales
                .Where(x => x.DetalleID > 0 && x.Cantidad.HasValue && x.Cantidad.Value > 0)
                .GroupBy(x => x.DetalleID)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Cantidad!.Value));

            if (cantidadesCapturadas.Count == 0)
            {
                TempData["Error"] = "Captura una cantidad mayor a cero para al menos un artículo.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            if (evidenciaEntrega == null || evidenciaEntrega.Length == 0)
            {
                TempData["Error"] = "Debes adjuntar evidencia de entrega al usuario.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
            string extensionEvidencia =
                Path.GetExtension(evidenciaEntrega.FileName)?.ToLowerInvariant() ?? "";

            if (!extensionesPermitidas.Contains(extensionEvidencia))
            {
                TempData["Error"] = "La evidencia de entrega solo puede ser PDF o imagen JPG, PNG o WEBP.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            bool perteneceCompras = await UsuarioPerteneceADepartamentoAsync(
                conn,
                usuarioEntregaId,
                "COMPRAS"
            );

            if (!perteneceCompras)
                return Forbid();

            using var trans = conn.BeginTransaction();

            try
            {
                int usuarioSolicitanteId;

                const string sqlSolicitud = @"
SELECT S.UsuarioID
FROM Compras_Solicitud S WITH (UPDLOCK, HOLDLOCK)
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID IN (9, 13)
  AND UPPER(ISNULL(S.TipoGasto, '')) NOT IN ('REQUISICION', 'REQUISICIÓN');";

                using (var cmdSol = new SqlCommand(sqlSolicitud, conn, trans))
                {
                    cmdSol.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    var result = await cmdSol.ExecuteScalarAsync();

                    if (result == null || result == DBNull.Value)
                    {
                        trans.Rollback();
                        TempData["Error"] =
                            "La solicitud no está disponible para entrega directa al usuario.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }

                    usuarioSolicitanteId = Convert.ToInt32(result);
                }

                var partidas = new Dictionary<int, (decimal Solicitada, decimal Entregada)>();

                const string sqlPartidas = @"
SELECT
    M.DetalleID,
    M.Cantidad AS CantidadSolicitada,
    ISNULL(SUM(CASE WHEN EU.Activo = 1 THEN MD.Cantidad ELSE 0 END), 0) AS CantidadEntregada
FROM Compras_Detalle_Materiales M
LEFT JOIN Compras_MovimientoDetalle MD
    ON MD.DetalleID = M.DetalleID
   AND MD.EntregaID IS NOT NULL
   AND MD.RecepcionID IS NULL
LEFT JOIN Compras_EntregasUsuario EU
    ON EU.EntregaID = MD.EntregaID
WHERE M.SolicitudID = @SolicitudID
GROUP BY M.DetalleID, M.Cantidad
ORDER BY M.DetalleID;";

                using (var cmdPartidas = new SqlCommand(sqlPartidas, conn, trans))
                {
                    cmdPartidas.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var reader = await cmdPartidas.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        partidas[Convert.ToInt32(reader["DetalleID"])] = (
                            Convert.ToDecimal(reader["CantidadSolicitada"]),
                            Convert.ToDecimal(reader["CantidadEntregada"])
                        );
                    }
                }

                if (partidas.Count == 0)
                {
                    trans.Rollback();
                    TempData["Error"] = "La solicitud no tiene artículos registrados.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                foreach (var captura in cantidadesCapturadas)
                {
                    if (!partidas.TryGetValue(captura.Key, out var partida))
                    {
                        trans.Rollback();
                        TempData["Error"] = "Uno de los artículos seleccionados no pertenece a la solicitud.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }

                    decimal pendiente = Math.Max(partida.Solicitada - partida.Entregada, 0);

                    if (captura.Value > pendiente)
                    {
                        trans.Rollback();
                        TempData["Error"] =
                            $"La cantidad entregada del artículo {captura.Key} supera la cantidad pendiente ({pendiente:0.##}).";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }

                string rutaContenedor = _rutaNas.ObtenerRutaSolicitudesCompras();
                _sftp.AsegurarDirectorio(rutaContenedor);

                string folioStr = solicitudId.ToString().PadLeft(5, '0');
                string nombreOriginalEvidencia = Path.GetFileName(evidenciaEntrega.FileName);
                string nombreArchivoEvidencia =
                    $"COM-{folioStr}_EntregaUsuario_{Guid.NewGuid()}{extensionEvidencia}";
                string rutaArchivoEvidenciaSftp = $"{rutaContenedor}/{nombreArchivoEvidencia}";

                using (var stream = evidenciaEntrega.OpenReadStream())
                {
                    _sftp.SubirStream(stream, rutaArchivoEvidenciaSftp);
                }

                int entregaId;

                const string sqlInsertEntrega = @"
INSERT INTO Compras_EntregasUsuario
(
    SolicitudID,
    FechaEntrega,
    UsuarioEntregaID,
    UsuarioRecibeID,
    NombreRecibe,
    Comentarios,
    EvidenciaEntregaPath,
    NombreArchivoEvidencia,
    ExtensionArchivoEvidencia,
    ContentTypeEvidencia,
    TamanoBytesEvidencia,
    FechaCargaEvidencia,
    Activo
)
VALUES
(
    @SolicitudID,
    GETDATE(),
    @UsuarioEntregaID,
    @UsuarioRecibeID,
    @NombreRecibe,
    @Comentarios,
    @EvidenciaEntregaPath,
    @NombreArchivoEvidencia,
    @ExtensionArchivoEvidencia,
    @ContentTypeEvidencia,
    @TamanoBytesEvidencia,
    GETDATE(),
    1
);
SELECT SCOPE_IDENTITY();";

                using (var cmdEntrega = new SqlCommand(sqlInsertEntrega, conn, trans))
                {
                    cmdEntrega.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdEntrega.Parameters.AddWithValue("@UsuarioEntregaID", usuarioEntregaId);
                    cmdEntrega.Parameters.AddWithValue("@UsuarioRecibeID", usuarioSolicitanteId);
                    cmdEntrega.Parameters.AddWithValue("@NombreRecibe", nombreRecibe);
                    cmdEntrega.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);
                    cmdEntrega.Parameters.AddWithValue("@EvidenciaEntregaPath", rutaArchivoEvidenciaSftp);
                    cmdEntrega.Parameters.AddWithValue("@NombreArchivoEvidencia", nombreOriginalEvidencia);
                    cmdEntrega.Parameters.AddWithValue("@ExtensionArchivoEvidencia", extensionEvidencia);
                    cmdEntrega.Parameters.AddWithValue(
                        "@ContentTypeEvidencia",
                        evidenciaEntrega.ContentType ?? "application/octet-stream"
                    );
                    cmdEntrega.Parameters.AddWithValue("@TamanoBytesEvidencia", evidenciaEntrega.Length);

                    entregaId = Convert.ToInt32(await cmdEntrega.ExecuteScalarAsync());
                }

                const string sqlInsertDetalle = @"
INSERT INTO Compras_MovimientoDetalle
(
    RecepcionID,
    EntregaID,
    DetalleID,
    Cantidad
)
VALUES
(
    NULL,
    @EntregaID,
    @DetalleID,
    @Cantidad
);";

                foreach (var captura in cantidadesCapturadas)
                {
                    using var cmdDetalle = new SqlCommand(sqlInsertDetalle, conn, trans);
                    cmdDetalle.Parameters.AddWithValue("@EntregaID", entregaId);
                    cmdDetalle.Parameters.AddWithValue("@DetalleID", captura.Key);

                    var pCantidad = new SqlParameter("@Cantidad", SqlDbType.Decimal)
                    {
                        Precision = 18,
                        Scale = 2,
                        Value = captura.Value
                    };
                    cmdDetalle.Parameters.Add(pCantidad);

                    await cmdDetalle.ExecuteNonQueryAsync();
                }

                bool entregaCompleta = partidas.All(x =>
                {
                    decimal entregadaAhora = cantidadesCapturadas.TryGetValue(x.Key, out var cantidad)
                        ? cantidad
                        : 0;

                    return x.Value.Entregada + entregadaAhora >= x.Value.Solicitada;
                });

                int nuevoEstatus = entregaCompleta ? 10 : 13;

                const string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID = @NuevoEstatus
WHERE SolicitudID = @SolicitudID
  AND EstatusID IN (9, 13);";

                using (var cmdUpdate = new SqlCommand(sqlUpdateSolicitud, conn, trans))
                {
                    cmdUpdate.Parameters.AddWithValue("@NuevoEstatus", nuevoEstatus);
                    cmdUpdate.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    if (await cmdUpdate.ExecuteNonQueryAsync() == 0)
                    {
                        trans.Rollback();
                        TempData["Error"] = "La solicitud cambió de estatus antes de registrar la entrega.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }

                const string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, @EstatusID, GETDATE(), @Responsable);";

                using (var cmdHist = new SqlCommand(sqlHistorico, conn, trans))
                {
                    cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdHist.Parameters.AddWithValue("@EstatusID", nuevoEstatus);
                    cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                    await cmdHist.ExecuteNonQueryAsync();
                }

                trans.Commit();

                // Antes no existía una notificación para la entrega directa al usuario.
                // Ahora se informa tanto el avance parcial (estatus 13) como el cierre (estatus 10).
                await NotificarAvanceMaterialesAsync(
                    solicitudId,
                    esRecepcionAlmacen: false,
                    movimientoCompleto: entregaCompleta
                );

                TempData["Mensaje"] = entregaCompleta
                    ? "Todos los artículos fueron entregados al usuario. La solicitud fue cerrada."
                    : "La entrega parcial fue registrada. La solicitud permanecerá abierta hasta completar el pedido.";

                return RedirectToAction("Detalle", new { id = solicitudId });
            }
            catch (Exception ex)
            {
                trans.Rollback();
                _logger.LogError(ex, "Error al registrar entrega parcial al usuario");
                TempData["Error"] = "No se pudo registrar la entrega al usuario: " + ex.Message;
                return RedirectToAction("Detalle", new { id = solicitudId });
            }
        }


        [HttpGet("Seguimiento")]
        [HttpGet("Compras/Seguimiento")]
        public async Task<IActionResult> Seguimiento(
    int? estatus,
    string? departamento,
    string? comprador,
    bool? soloRetrasadas
)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var vm = new SeguimientoDireccionVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // VALIDAR PUESTO
                string puesto = "";

                string sqlPuesto = @"
        SELECT P.Puesto
        FROM Usuarios U
        INNER JOIN Persona P ON U.PersonaID = P.PersonaID
        WHERE U.UsuarioID = @UsuarioID";

                using (var cmdPuesto = new SqlCommand(sqlPuesto, conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                // SOLO DIRECCIÓN
                puesto = (puesto ?? "").ToUpper().Trim();

                bool esDireccion =
                    puesto.Contains("DIRECCION") ||
                    puesto.Contains("DIRECCIÓN") ||
                    puesto.Contains("DIRECTOR") ||
                    puesto.Contains("DIRECTORA") ||
                    puesto.StartsWith("DIR.");

                if (!esDireccion)
                {
                    return Forbid();
                }

                // EJECUTAR SP
                using (var cmd = new SqlCommand("sp_Compras_SeguimientoDireccion", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Solicitudes.Add(new SeguimientoCompraItemVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],

                                Solicitante = reader["Solicitante"].ToString(),
                                Departamento = reader["Departamento"].ToString(),
                                Empresa = reader["Empresa"].ToString(),

                                TipoCompra = reader["TipoCompra"].ToString(),
                                Urgencia = reader["Urgencia"].ToString(),

                                EstatusID = (int)reader["EstatusID"],
                                Estatus = reader["Estatus"].ToString(),

                                CompradorAsignado = reader["CompradorAsignado"].ToString(),

                                FechaAsignacionComprador =
                                    reader["FechaAsignacionComprador"] == DBNull.Value
                                        ? null
                                        : (DateTime?)reader["FechaAsignacionComprador"],

                                FechaUltimoMovimiento =
                                    reader["FechaUltimoMovimiento"] == DBNull.Value
                                        ? null
                                        : (DateTime?)reader["FechaUltimoMovimiento"],

                                DiasEnEstatus = Convert.ToInt32(reader["DiasEnEstatus"]),

                                DiasCotizando = Convert.ToInt32(reader["DiasCotizando"]),

                                MontoCotizado = Convert.ToDecimal(reader["MontoCotizado"]),

                                DiasPermitidos = Convert.ToInt32(reader["DiasPermitidos"]),
                                DiasHabilesTranscurridos = Convert.ToInt32(reader["DiasHabilesTranscurridos"]),
                                SemaforoTexto = reader["SemaforoTexto"].ToString(),

                                DiasCompras = reader["DiasCompras"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasCompras"]),

                                DiasPresupuesto = reader["DiasPresupuesto"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasPresupuesto"]),

                                DiasOC = reader["DiasOC"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasOC"]),

                                DiasProveedor = reader["DiasProveedor"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasProveedor"]),

                                DiasAlmacen = reader["DiasAlmacen"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasAlmacen"]),


                            });
                        }
                    }
                }

                await AplicarMetasCotizacionAsync(conn, vm.Solicitudes);
            }
            if (estatus.HasValue)
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x => x.EstatusID == estatus.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(departamento))
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x =>
                        x.Departamento.Contains(
                            departamento,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(comprador))
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x =>
                        x.CompradorAsignado.Contains(
                            comprador,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
            }

            if (soloRetrasadas == true)
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x => x.SemaforoTexto == "Retrasada")
                    .ToList();
            }


            return View(vm);
        }


        [HttpGet("BandejaAlmacen")]
        [HttpGet("Compras/BandejaAlmacen")]
        public async Task<IActionResult> BandejaAlmacen()
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var vm = new BandejaAlmacenVm();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            bool perteneceAlmacen = await UsuarioPerteneceADepartamentoAsync(
                conn,
                usuarioId,
                "ALMACEN",
                "ALMACÉN"
            );

            if (!perteneceAlmacen)
                return Forbid();

            const string sqlSolicitudes = @"
SELECT
    S.SolicitudID,
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
    E.Nombre AS Empresa,
    OC.NumeroOC,
    OC.Proveedor,
    S.FechaCreacion,
    OC.FechaEnvioProveedor,
    OC.FechaEstimadaEntrega,
    CE.Nombre AS Estatus,
    S.EstatusID,
    DATEDIFF(DAY, OC.FechaEnvioProveedor, GETDATE()) AS DiasDesdeEnvioProveedor
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
INNER JOIN Cat_EstatusCompra CE ON S.EstatusID = CE.EstatusID
INNER JOIN Compras_OrdenCompra OC
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
   AND OC.FechaEnvioProveedor IS NOT NULL
OUTER APPLY (
    SELECT TOP (1) DP.NombreDepartamento
    FROM EmpleadoDepartamentos ED
    INNER JOIN Departamentos DP ON DP.DepartamentoID = ED.DepartamentoID AND DP.Activo = 1
    WHERE ED.UsuarioID = U.UsuarioID AND ED.Activo = 1
    ORDER BY ED.DepartamentoID
) D
OUTER APPLY
(
    SELECT
        SUM(M.Cantidad) AS TotalSolicitado,
        SUM(ISNULL(RC.CantidadRecibida, 0)) AS TotalRecibido
    FROM Compras_Detalle_Materiales M
    OUTER APPLY
    (
        SELECT SUM(MD.Cantidad) AS CantidadRecibida
        FROM Compras_MovimientoDetalle MD
        INNER JOIN Compras_Recepciones R
            ON R.RecepcionID = MD.RecepcionID
           AND R.Activo = 1
        WHERE MD.DetalleID = M.DetalleID
          AND MD.RecepcionID IS NOT NULL
          AND MD.EntregaID IS NULL
    ) RC
    WHERE M.SolicitudID = S.SolicitudID
) T
WHERE S.EstatusID IN (8, 13)
  AND UPPER(ISNULL(S.TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN')
  AND ISNULL(T.TotalRecibido, 0) < ISNULL(T.TotalSolicitado, 0)
ORDER BY OC.FechaEstimadaEntrega ASC, OC.FechaEnvioProveedor ASC;";

            using (var cmd = new SqlCommand(sqlSolicitudes, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    vm.PendientesRecepcion.Add(new AlmacenItemVm
                    {
                        SolicitudID = Convert.ToInt32(reader["SolicitudID"]),
                        Folio = reader["Folio"] == DBNull.Value ? "" : reader["Folio"].ToString(),
                        Solicitante = reader["Solicitante"] == DBNull.Value ? "" : reader["Solicitante"].ToString(),
                        Departamento = reader["Departamento"] == DBNull.Value ? "" : reader["Departamento"].ToString(),
                        Empresa = reader["Empresa"] == DBNull.Value ? "" : reader["Empresa"].ToString(),
                        NumeroOC = reader["NumeroOC"] == DBNull.Value ? "" : reader["NumeroOC"].ToString(),
                        Proveedor = reader["Proveedor"] == DBNull.Value ? "" : reader["Proveedor"].ToString(),
                        FechaCreacion = Convert.ToDateTime(reader["FechaCreacion"]),
                        FechaEnvioProveedor = reader["FechaEnvioProveedor"] == DBNull.Value
                            ? null
                            : (DateTime?)Convert.ToDateTime(reader["FechaEnvioProveedor"]),
                        FechaEstimadaEntrega = reader["FechaEstimadaEntrega"] == DBNull.Value
                            ? null
                            : (DateTime?)Convert.ToDateTime(reader["FechaEstimadaEntrega"]),
                        Estatus = reader["Estatus"] == DBNull.Value ? "" : reader["Estatus"].ToString(),
                        EstatusID = Convert.ToInt32(reader["EstatusID"]),
                        DiasDesdeEnvioProveedor = reader["DiasDesdeEnvioProveedor"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["DiasDesdeEnvioProveedor"])
                    });
                }
            }

            if (vm.PendientesRecepcion.Count == 0)
                return View(vm);

            // La bandeja representa una solicitud por fila. Las uniones con varias OC
            // pueden devolver mas de una fila de la misma solicitud.
            vm.PendientesRecepcion = vm.PendientesRecepcion
                .GroupBy(x => x.SolicitudID)
                .Select(g => g.OrderByDescending(x => x.FechaEnvioProveedor ?? DateTime.MinValue).First())
                .ToList();
            var solicitudesPorId = vm.PendientesRecepcion.ToDictionary(x => x.SolicitudID);

            const string sqlMateriales = @"
SELECT
    S.SolicitudID,
    M.DetalleID,
    M.NombreMaterial,
    M.Descripcion,
    M.Cantidad,
    M.UnidadMedida,
    ISNULL(RC.CantidadRecibida, 0) AS CantidadRecibidaAlmacen
FROM Compras_Solicitud S
INNER JOIN Compras_Detalle_Materiales M
    ON M.SolicitudID = S.SolicitudID
OUTER APPLY
(
    SELECT SUM(MD.Cantidad) AS CantidadRecibida
    FROM Compras_MovimientoDetalle MD
    INNER JOIN Compras_Recepciones R
        ON R.RecepcionID = MD.RecepcionID
       AND R.Activo = 1
    WHERE MD.DetalleID = M.DetalleID
      AND MD.RecepcionID IS NOT NULL
      AND MD.EntregaID IS NULL
) RC
WHERE S.EstatusID IN (8, 13)
  AND UPPER(ISNULL(S.TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN')
ORDER BY S.SolicitudID, M.DetalleID;";

            using (var cmdMateriales = new SqlCommand(sqlMateriales, conn))
            using (var reader = await cmdMateriales.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    int solicitudId = Convert.ToInt32(reader["SolicitudID"]);

                    if (!solicitudesPorId.TryGetValue(solicitudId, out var solicitud))
                        continue;

                    solicitud.Materiales.Add(new MaterialItem
                    {
                        DetalleID = Convert.ToInt32(reader["DetalleID"]),
                        Nombre = reader["NombreMaterial"] == DBNull.Value ? "" : reader["NombreMaterial"].ToString(),
                        Descripcion = reader["Descripcion"] == DBNull.Value ? "" : reader["Descripcion"].ToString(),
                        Cantidad = reader["Cantidad"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Cantidad"]),
                        UnidadMedida = reader["UnidadMedida"] == DBNull.Value ? "" : reader["UnidadMedida"].ToString(),
                        CantidadRecibidaAlmacen = reader["CantidadRecibidaAlmacen"] == DBNull.Value
                            ? 0
                            : Convert.ToDecimal(reader["CantidadRecibidaAlmacen"])
                    });
                }
            }

            return View(vm);
        }


        //HELPER PARA SABER A QUE DEPARTAMENTO PERTENECE UN USUARIO
        private async Task<bool> UsuarioPerteneceADepartamentoAsync(
    SqlConnection conn,
    int usuarioId,
    params string[] palabrasClaveDepartamento)
        {
            string sql = @"
SELECT D.NombreDepartamento
FROM EmpleadoDepartamentos ED
INNER JOIN Departamentos D
    ON ED.DepartamentoID = D.DepartamentoID
WHERE ED.UsuarioID = @UsuarioID
  AND ED.Activo = 1
  AND D.Activo = 1";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string depto = reader["NombreDepartamento"]?.ToString()?.ToUpper().Trim() ?? "";

                        foreach (var palabra in palabrasClaveDepartamento)
                        {
                            if (depto.Contains(palabra.ToUpper()))
                                return true;
                        }
                    }
                }
            }

            return false;
        }


        [HttpPost("SeleccionarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeleccionarCotizacion(
            int solicitudId,
            int cotizacionId,
            string? comentarios,
            string? motivoCambio)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            comentarios = comentarios?.Trim();
            motivoCambio = motivoCambio?.Trim();

            if (!string.IsNullOrWhiteSpace(comentarios) && comentarios.Length > 500)
            {
                TempData["Error"] = "El comentario no puede superar los 500 caracteres.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            if (!string.IsNullOrWhiteSpace(motivoCambio) && motivoCambio.Length > 500)
            {
                TempData["Error"] = "El motivo del cambio no puede superar los 500 caracteres.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            bool perteneceCompras =
                await UsuarioPerteneceADepartamentoAsync(conn, usuarioId, "COMPRAS");

            if (!perteneceCompras)
                return Forbid();

            using var trans = conn.BeginTransaction();

            try
            {
                int estatusActual;
                int? cotizacionAnteriorId;

                const string sqlDatosSolicitud = @"
SELECT
    S.EstatusID,
    S.CotizacionSeleccionadaID
FROM Compras_Solicitud S WITH (UPDLOCK, ROWLOCK)
WHERE S.SolicitudID = @SolicitudID;";

                using (var cmdDatos = new SqlCommand(sqlDatosSolicitud, conn, trans))
                {
                    cmdDatos.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var reader = await cmdDatos.ExecuteReaderAsync();

                    if (!await reader.ReadAsync())
                    {
                        trans.Rollback();
                        TempData["Error"] = "No se encontró la solicitud.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }

                    estatusActual = Convert.ToInt32(reader["EstatusID"]);
                    cotizacionAnteriorId = reader["CotizacionSeleccionadaID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(reader["CotizacionSeleccionadaID"]);
                }

                if (estatusActual != 2 && estatusActual != 14)
                {
                    trans.Rollback();
                    TempData["Error"] =
                        "Compras solo puede dictaminar una cotización cuando existen cotizaciones cargadas o mientras está pendiente la autorización del solicitante.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                bool esCambio = cotizacionAnteriorId.HasValue &&
                                cotizacionAnteriorId.Value != cotizacionId;

                if (cotizacionAnteriorId.HasValue &&
                    cotizacionAnteriorId.Value == cotizacionId)
                {
                    trans.Rollback();
                    TempData["Error"] = "Esta cotización ya es la dictaminada para la solicitud.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                if (esCambio && string.IsNullOrWhiteSpace(motivoCambio))
                {
                    trans.Rollback();
                    TempData["Error"] = "Debes capturar el motivo del cambio de cotización.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                const string sqlValidarCotizacion = @"
SELECT COUNT(1)
FROM Compras_Cotizaciones
WHERE SolicitudID = @SolicitudID
  AND CotizacionID = @CotizacionID;";

                using (var cmdVal = new SqlCommand(sqlValidarCotizacion, conn, trans))
                {
                    cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdVal.Parameters.AddWithValue("@CotizacionID", cotizacionId);

                    int valido = Convert.ToInt32(await cmdVal.ExecuteScalarAsync() ?? 0);

                    if (valido == 0)
                    {
                        trans.Rollback();
                        TempData["Error"] = "La cotización dictaminada no pertenece a esta solicitud.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }

                string? comentarioFinal = comentarios;

                if (esCambio && !string.IsNullOrWhiteSpace(motivoCambio))
                {
                    comentarioFinal = string.IsNullOrWhiteSpace(comentarioFinal)
                        ? $"Motivo del cambio: {motivoCambio}"
                        : $"{comentarioFinal}\nMotivo del cambio: {motivoCambio}";
                }

                const string sqlUpdate = @"
UPDATE Compras_Solicitud
SET CotizacionSeleccionadaID = @CotizacionID,
    FechaSeleccionCotizacion = GETDATE(),
    UsuarioSeleccionCotizacionID = @UsuarioID,
    ComentariosSeleccionUsuario = @Comentarios,
    FechaAutorizacionCotizacion = NULL,
    UsuarioAutorizacionCotizacionID = NULL,
    ComentariosAutorizacionCotizacion = NULL,
    EstatusID = 14,
    FechaDictamen = NULL,
    TipoGasto = NULL,
    DentroPresupuesto = NULL,
    NumeroRequisicion = NULL,
    ObservacionesPresupuesto = NULL
WHERE SolicitudID = @SolicitudID
  AND EstatusID IN (2, 14)
  AND
  (
      (@CotizacionAnteriorID IS NULL AND CotizacionSeleccionadaID IS NULL)
      OR CotizacionSeleccionadaID = @CotizacionAnteriorID
  );";

                using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                {
                    cmd.Parameters.AddWithValue("@CotizacionID", cotizacionId);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarioFinal ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    cmd.Parameters.Add(new SqlParameter("@CotizacionAnteriorID", SqlDbType.Int)
                    {
                        Value = cotizacionAnteriorId.HasValue
                            ? cotizacionAnteriorId.Value
                            : DBNull.Value
                    });

                    int filas = await cmd.ExecuteNonQueryAsync();

                    if (filas == 0)
                    {
                        trans.Rollback();
                        TempData["Error"] =
                            "La solicitud fue modificada por otro usuario. Actualiza la pantalla e inténtalo nuevamente.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }

                if (esCambio)
                {
                    const string sqlHistCambio = @"
INSERT INTO Compras_CotizacionCambios_Historico
(
    SolicitudID,
    CotizacionAnteriorID,
    CotizacionNuevaID,
    UsuarioCambioID,
    FechaCambio,
    MotivoCambio,
    EstatusAnteriorID,
    EstatusNuevoID
)
VALUES
(
    @SolicitudID,
    @CotizacionAnteriorID,
    @CotizacionNuevaID,
    @UsuarioCambioID,
    GETDATE(),
    @MotivoCambio,
    @EstatusAnteriorID,
    14
);";

                    using (var cmdHistCambio = new SqlCommand(sqlHistCambio, conn, trans))
                    {
                        cmdHistCambio.Parameters.AddWithValue("@SolicitudID", solicitudId);
                        cmdHistCambio.Parameters.AddWithValue("@CotizacionAnteriorID", cotizacionAnteriorId!.Value);
                        cmdHistCambio.Parameters.AddWithValue("@CotizacionNuevaID", cotizacionId);
                        cmdHistCambio.Parameters.AddWithValue("@UsuarioCambioID", usuarioId);
                        cmdHistCambio.Parameters.AddWithValue("@MotivoCambio", motivoCambio!);
                        cmdHistCambio.Parameters.AddWithValue("@EstatusAnteriorID", estatusActual);

                        await cmdHistCambio.ExecuteNonQueryAsync();
                    }

                    const string sqlInvalidarDocs = @"
UPDATE Compras_Archivos
SET Vigente = 0
WHERE SolicitudID = @SolicitudID
  AND TipoArchivo IN ('DESVIACION', 'DESVIACION_CP', 'FORMATO_REQUISICION')
  AND Vigente = 1
  AND Activo = 1;";

                    using var cmdInvalidarDocs = new SqlCommand(sqlInvalidarDocs, conn, trans);
                    cmdInvalidarDocs.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    await cmdInvalidarDocs.ExecuteNonQueryAsync();
                }

                const string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(
    SolicitudID,
    EstatusID,
    FechaMovimiento,
    UsuarioResponsable
)
VALUES
(
    @SolicitudID,
    14,
    GETDATE(),
    @Responsable
);";

                using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                {
                    cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdHist.Parameters.AddWithValue(
                        "@Responsable",
                        $"Compras dictaminó cotización: {User.Identity?.Name ?? "Usuario"}"
                    );
                    await cmdHist.ExecuteNonQueryAsync();
                }

                trans.Commit();

                await NotificarSolicitante_CotizacionDictaminadaAsync(solicitudId, esCambio);

                TempData["Mensaje"] = esCambio
                    ? "El dictamen de cotización se actualizó. El solicitante deberá revisar y autorizar nuevamente."
                    : "Cotización dictaminada correctamente. La solicitud quedó pendiente de autorización del solicitante.";

                return RedirectToAction("Detalle", new { id = solicitudId });
            }
            catch (Exception ex)
            {
                trans.Rollback();
                _logger.LogError(
                    ex,
                    "Error al dictaminar cotización. SolicitudID={SolicitudID}, UsuarioID={UsuarioID}",
                    solicitudId,
                    usuarioId
                );

                TempData["Error"] = "Ocurrió un error al dictaminar la cotización.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }
        }

        [HttpPost("AutorizarCotizacionUsuario")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarCotizacionUsuario(
            int solicitudId,
            string? comentariosAutorizacion)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            comentariosAutorizacion = comentariosAutorizacion?.Trim();

            if (!string.IsNullOrWhiteSpace(comentariosAutorizacion) &&
                comentariosAutorizacion.Length > 500)
            {
                TempData["Error"] = "El comentario de autorización no puede superar los 500 caracteres.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var trans = conn.BeginTransaction();

            try
            {
                int estatusActual;
                int usuarioSolicitanteId;
                int? cotizacionSeleccionadaId;

                const string sqlValidar = @"
SELECT
    EstatusID,
    UsuarioID,
    CotizacionSeleccionadaID
FROM Compras_Solicitud WITH (UPDLOCK, ROWLOCK)
WHERE SolicitudID = @SolicitudID;";

                using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                {
                    cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var reader = await cmdVal.ExecuteReaderAsync();

                    if (!await reader.ReadAsync())
                    {
                        trans.Rollback();
                        TempData["Error"] = "No se encontró la solicitud.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }

                    estatusActual = Convert.ToInt32(reader["EstatusID"]);
                    usuarioSolicitanteId = Convert.ToInt32(reader["UsuarioID"]);
                    cotizacionSeleccionadaId = reader["CotizacionSeleccionadaID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(reader["CotizacionSeleccionadaID"]);
                }

                if (usuarioSolicitanteId != usuarioId)
                {
                    trans.Rollback();
                    return Forbid();
                }

                if (estatusActual != 14 || !cotizacionSeleccionadaId.HasValue)
                {
                    trans.Rollback();
                    TempData["Error"] =
                        "La solicitud no está pendiente de autorización de cotización.";
                    return RedirectToAction("Detalle", new { id = solicitudId });
                }

                const string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 3,
    FechaAutorizacionCotizacion = GETDATE(),
    UsuarioAutorizacionCotizacionID = @UsuarioID,
    ComentariosAutorizacionCotizacion = @Comentarios
WHERE SolicitudID = @SolicitudID
  AND UsuarioID = @UsuarioID
  AND EstatusID = 14
  AND CotizacionSeleccionadaID = @CotizacionID;";

                using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@CotizacionID", cotizacionSeleccionadaId.Value);
                    cmd.Parameters.AddWithValue(
                        "@Comentarios",
                        (object?)comentariosAutorizacion ?? DBNull.Value
                    );

                    int filas = await cmd.ExecuteNonQueryAsync();

                    if (filas == 0)
                    {
                        trans.Rollback();
                        TempData["Error"] =
                            "La solicitud fue modificada por otro usuario. Actualiza la pantalla e inténtalo nuevamente.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }

                const string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(
    SolicitudID,
    EstatusID,
    FechaMovimiento,
    UsuarioResponsable
)
VALUES
(
    @SolicitudID,
    3,
    GETDATE(),
    @Responsable
);";

                using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                {
                    cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                    cmdHist.Parameters.AddWithValue(
                        "@Responsable",
                        $"Solicitante autorizó cotización: {User.Identity?.Name ?? "Usuario"}"
                    );
                    await cmdHist.ExecuteNonQueryAsync();
                }

                trans.Commit();

                await NotificarCompras_CotizacionAutorizadaAsync(solicitudId);

                TempData["Mensaje"] =
                    "Cotización autorizada correctamente. Ahora puedes completar la documentación presupuestal.";

                return RedirectToAction("Detalle", new { id = solicitudId });
            }
            catch (Exception ex)
            {
                trans.Rollback();
                _logger.LogError(
                    ex,
                    "Error al autorizar cotización. SolicitudID={SolicitudID}, UsuarioID={UsuarioID}",
                    solicitudId,
                    usuarioId
                );

                TempData["Error"] = "Ocurrió un error al autorizar la cotización.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }
        }

        //METODOS PARA LA CONFIRMACION  DE DOCUMENTOS

        [HttpPost("ConfirmarDocumentacionPresupuestal")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarDocumentacionPresupuestal(
    int solicitudId,
    IFormFile? archivoDesviacion,
    IFormFile? archivoFormatoRequisicion)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int cotizacionSeleccionadaId;
                        bool fueraPresupuestoUsuario;
                        bool tieneDesviacionParaFirma;

                        string sqlValidar = @"
SELECT 
    S.CotizacionSeleccionadaID,
    S.FueraPresupuestoUsuario,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM Compras_Archivos A
        WHERE A.SolicitudID = S.SolicitudID
          AND A.CotizacionID = S.CotizacionSeleccionadaID
          AND A.TipoArchivo = 'DESVIACION_CP'
          AND A.Vigente = 1
          AND A.Activo = 1
    ) THEN 1 ELSE 0 END AS TieneDesviacionParaFirma
FROM Compras_Solicitud S
WHERE S.SolicitudID = @SolicitudID
  AND UsuarioID = @UsuarioID
  AND EstatusID = 3
  AND CotizacionSeleccionadaID IS NOT NULL
  AND FechaAutorizacionCotizacion IS NOT NULL
  AND UsuarioAutorizacionCotizacionID = UsuarioID";

                        using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                        {
                            cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdVal.Parameters.AddWithValue("@UsuarioID", usuarioId);

                            using (var reader = await cmdVal.ExecuteReaderAsync())
                            {
                                if (!await reader.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "La solicitud no está disponible para enviar a Control Presupuestal.";
                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }

                                cotizacionSeleccionadaId = Convert.ToInt32(reader["CotizacionSeleccionadaID"]);

                                fueraPresupuestoUsuario =
                                    reader["FueraPresupuestoUsuario"] != DBNull.Value
                                    && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);

                                tieneDesviacionParaFirma =
                                    reader["TieneDesviacionParaFirma"] != DBNull.Value
                                    && Convert.ToInt32(reader["TieneDesviacionParaFirma"]) == 1;
                            }
                        }

                        if ((fueraPresupuestoUsuario || tieneDesviacionParaFirma)
                            && (archivoDesviacion == null || archivoDesviacion.Length == 0))
                        {
                            trans.Rollback();
                            TempData["Error"] = "Debes descargar, firmar y adjuntar la desviación presupuestal escaneada antes de enviar a Control Presupuestal.";
                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }


                        if (archivoDesviacion != null && archivoDesviacion.Length > 0)
                        {
                            string extensionDesviacion =
                                Path.GetExtension(archivoDesviacion.FileName)?.ToLowerInvariant() ?? "";

                            if (!ExtensionPermitida(extensionDesviacion, ".pdf", ".jpg", ".jpeg", ".png", ".webp"))
                            {
                                trans.Rollback();
                                TempData["Error"] = "La desviación solo puede ser PDF o imagen JPG, PNG o WEBP.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            await GuardarArchivoCompraAsync(
                                conn,
                                trans,
                                solicitudId,
                                cotizacionSeleccionadaId,
                                "DESVIACION",
                                archivoDesviacion,
                                usuarioId,
                                _rutaNas.ObtenerRutaComprasDesviaciones(),
                                "Desviacion"
                            );
                        }

                        if (archivoFormatoRequisicion != null && archivoFormatoRequisicion.Length > 0)
                        {
                            string extensionRequisicion =
                                Path.GetExtension(archivoFormatoRequisicion.FileName)?.ToLowerInvariant() ?? "";

                            if (!ExtensionPermitida(extensionRequisicion, ".xlsx", ".xls", ".pdf"))
                            {
                                trans.Rollback();
                                TempData["Error"] = "El formato de requisición solo puede ser Excel o PDF.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            await GuardarArchivoCompraAsync(
                                conn,
                                trans,
                                solicitudId,
                                cotizacionSeleccionadaId,
                                "FORMATO_REQUISICION",
                                archivoFormatoRequisicion,
                                usuarioId,
                                _rutaNas.ObtenerRutaComprasRequisiciones(),
                                "FormatoRequisicion"
                            );
                        }

                        string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 4
WHERE SolicitudID = @SolicitudID
  AND UsuarioID = @UsuarioID
  AND EstatusID = 3";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar la solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 4, GETDATE(), @Responsable)";

                        using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarControlPresupuestal_CotizacionSeleccionadaAsync(solicitudId);

                        TempData["Mensaje"] = "Documentación confirmada. La solicitud pasó a Control Presupuestal.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(ex, "Error al confirmar documentación presupuestal");

                        TempData["Error"] = "No se pudo enviar la solicitud a Control Presupuestal.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }

        //METODOS PARA LOS CORREOS
        private async Task NotificarComprador_NuevaSolicitudAsync(int solicitudId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string sql = @"
SELECT 
    S.SolicitudID,
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.TipoCompra,
    S.FechaCreacion,
    S.ComentariosExtra,
    S.CompradorAsignadoUsuarioID,
    PComprador.PersonaID AS PersonaCompradorID,
    PSolicitante.Nombre + ' ' + PSolicitante.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    U.Descripcion AS Urgencia,
    U.TiempoObjetivo AS TiempoObjetivoCotizacion
FROM Compras_Solicitud S
INNER JOIN Usuarios USolicitante ON S.UsuarioID = USolicitante.UsuarioID
INNER JOIN Persona PSolicitante ON USolicitante.PersonaID = PSolicitante.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
LEFT JOIN Usuarios UComprador ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador ON UComprador.PersonaID = PComprador.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (!await rd.ReadAsync())
                                return;

                            if (rd["PersonaCompradorID"] == DBNull.Value)
                                return;

                            int personaCompradorId = Convert.ToInt32(rd["PersonaCompradorID"]);

                            string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                            string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";
                            string empresa = rd["Empresa"]?.ToString() ?? "";
                            string tipoCompra = rd["TipoCompra"]?.ToString() ?? "";
                            string urgencia = rd["Urgencia"]?.ToString() ?? "";
                            string tiempoObjetivoCotizacion = rd["TiempoObjetivoCotizacion"] == DBNull.Value ? "" : rd["TiempoObjetivoCotizacion"].ToString() ?? "";
                            DateTime fechaCreacion = Convert.ToDateTime(rd["FechaCreacion"]);
                            string comentarios = rd["ComentariosExtra"] == DBNull.Value ? "" : rd["ComentariosExtra"].ToString();

                            string asunto = $"Nueva solicitud de compra asignada - {folio}";

                            string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#2563eb; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Nueva solicitud de compra</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Se te ha asignado una nueva solicitud de compra.</p>

      <div style='background:#f8f9fa; border-left:4px solid #2563eb; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Tipo:</strong> {System.Net.WebUtility.HtmlEncode(tipoCompra)}</p>
        <p style='margin:0 0 6px;'><strong>Categoría de compra:</strong> {System.Net.WebUtility.HtmlEncode(urgencia)}</p>
        {(string.IsNullOrWhiteSpace(tiempoObjetivoCotizacion) ? "" : $"<p style='margin:0 0 6px;'><strong>Tiempo objetivo de cotización:</strong> {System.Net.WebUtility.HtmlEncode(tiempoObjetivoCotizacion)}</p>")}
        <p style='margin:0;'><strong>Fecha:</strong> {fechaCreacion:dd/MM/yyyy HH:mm}</p>
      </div>

      {(string.IsNullOrWhiteSpace(comentarios) ? "" : $"<p><strong>Comentarios:</strong> {System.Net.WebUtility.HtmlEncode(comentarios)}</p>")}

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, para cotizar la solicitud.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                            await _notif.EnviarABccPersonasAsync(
                                new List<int> { personaCompradorId },
                                asunto,
                                html
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de nueva solicitud de compra. SolicitudID={SolicitudID}", solicitudId);
            }
        }


        private async Task NotificarSolicitante_CotizacionesListasAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    S.SolicitudID,
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.UsuarioID,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";

                string asunto = $"Cotizaciones cargadas por Compras - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Cotizaciones cargadas</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

     <p>Compras ha cargado cotizaciones para tu solicitud.</p>
<p>El área de Compras revisará las opciones y dictaminará la cotización que procede. Por el momento no necesitas realizar ninguna acción.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
      </div>

      <p>Puedes consultar el avance en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de cotizaciones listas. SolicitudID={SolicitudID}", solicitudId);
            }
        }


        private async Task NotificarSolicitante_CotizacionDictaminadaAsync(
    int solicitudId,
    bool esCambio)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    C.Proveedor,
    C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Usuarios U 
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P 
    ON U.PersonaID = P.PersonaID
LEFT JOIN Compras_Cotizaciones C 
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);

                string folio = rd["Folio"]?.ToString()
                    ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";

                string solicitante = rd["Solicitante"]?.ToString()
                    ?? "Solicitante";

                string proveedor = rd["Proveedor"] == DBNull.Value
                    ? "N/A"
                    : rd["Proveedor"].ToString();

                decimal monto = rd["MontoTotal"] == DBNull.Value
                    ? 0
                    : Convert.ToDecimal(rd["MontoTotal"]);

                string asunto = esCambio
                    ? $"Cambio de cotización dictaminada por Compras - {folio}"
                    : $"Cotización dictaminada por Compras - {folio}";

                string titulo = esCambio
                    ? "Cotización actualizada por Compras"
                    : "Cotización dictaminada por Compras";

                string mensaje = esCambio
                    ? "Compras ha cambiado la cotización dictaminada para tu solicitud."
                    : "Compras ha dictaminado la cotización que procede para tu solicitud.";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#2563eb; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>{titulo}</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>{System.Net.WebUtility.HtmlEncode(mensaje)}</p>

      <div style='background:#eff6ff; border-left:4px solid #2563eb; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0;'><strong>Monto:</strong> {monto:C}</p>
      </div>

      <p><strong>Acción requerida:</strong> revisa la cotización dictaminada y autorízala desde el detalle de tu solicitud.</p>
      <p>Después de autorizarla, el sistema habilitará la sección de documentación presupuestal para continuar el proceso.</p>

      <p>Puedes consultar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error enviando correo de cotización dictaminada al solicitante. SolicitudID={SolicitudID}",
                    solicitudId
                );
            }
        }




        private async Task NotificarCompras_CotizacionSugeridaAsync(
            int solicitudId,
            bool esCambio)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var personaIds = new List<int>();
                string folio = $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = "Solicitante";
                string proveedor = "N/A";
                decimal monto = 0;
                string comentarios = "Sin comentarios";

                const string sqlSolicitud = @"
SELECT
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    PComprador.PersonaID AS PersonaCompradorID,
    C.Proveedor,
    C.MontoTotal,
    S.ComentariosSeleccionUsuario
FROM Compras_Solicitud S
INNER JOIN Usuarios U
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
LEFT JOIN Usuarios UComprador
    ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador
    ON UComprador.PersonaID = PComprador.PersonaID
LEFT JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID;";

                using (var cmd = new SqlCommand(sqlSolicitud, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                        return;

                    folio = rd["Folio"]?.ToString() ?? folio;
                    solicitante = rd["Solicitante"]?.ToString() ?? solicitante;
                    proveedor = rd["Proveedor"] == DBNull.Value
                        ? "N/A"
                        : rd["Proveedor"].ToString() ?? "N/A";
                    monto = rd["MontoTotal"] == DBNull.Value
                        ? 0
                        : Convert.ToDecimal(rd["MontoTotal"]);
                    comentarios = rd["ComentariosSeleccionUsuario"] == DBNull.Value
                        ? "Sin comentarios"
                        : rd["ComentariosSeleccionUsuario"].ToString() ?? "Sin comentarios";

                    if (rd["PersonaCompradorID"] != DBNull.Value)
                        personaIds.Add(Convert.ToInt32(rd["PersonaCompradorID"]));
                }

                if (!personaIds.Any())
                {
                    const string sqlCompras = @"
SELECT DISTINCT P.PersonaID
FROM Usuarios U
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
INNER JOIN EmpleadoDepartamentos ED
    ON U.UsuarioID = ED.UsuarioID
   AND ED.Activo = 1
INNER JOIN Departamentos D
    ON ED.DepartamentoID = D.DepartamentoID
   AND D.Activo = 1
WHERE UPPER(D.NombreDepartamento) LIKE '%COMPRAS%'
  AND P.Correo IS NOT NULL
  AND LTRIM(RTRIM(P.Correo)) <> '';";

                    using var cmdCompras = new SqlCommand(sqlCompras, conn);
                    using var rdCompras = await cmdCompras.ExecuteReaderAsync();

                    while (await rdCompras.ReadAsync())
                        personaIds.Add(Convert.ToInt32(rdCompras["PersonaID"]));
                }

                personaIds = personaIds.Distinct().ToList();

                if (!personaIds.Any())
                    return;

                string titulo = esCambio
                    ? "El solicitante cambió su selección de cotización"
                    : "El solicitante seleccionó una cotización";

                string asunto = $"{titulo} - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>{System.Net.WebUtility.HtmlEncode(titulo)}</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p><strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong> indicó la cotización que prefiere para su solicitud.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0 0 6px;'><strong>Monto:</strong> {monto:C}</p>
        <p style='margin:0;'><strong>Comentario:</strong> {System.Net.WebUtility.HtmlEncode(comentarios)}</p>
      </div>

      <p>Compras puede revisar la selección y cambiarla cuando sea necesario antes de continuar el proceso.</p>
      <p>Consulta el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error notificando a Compras la selección del solicitante. SolicitudID={SolicitudID}",
                    solicitudId
                );
            }
        }


        private async Task NotificarCompras_CotizacionAutorizadaAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var personaIds = new List<int>();
                string folio = $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = "Solicitante";
                string proveedor = "N/A";
                decimal monto = 0;

                const string sqlSolicitud = @"
SELECT
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    PComprador.PersonaID AS PersonaCompradorID,
    C.Proveedor,
    C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Usuarios U
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
LEFT JOIN Usuarios UComprador
    ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador
    ON UComprador.PersonaID = PComprador.PersonaID
LEFT JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID;";

                using (var cmd = new SqlCommand(sqlSolicitud, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                        return;

                    folio = rd["Folio"]?.ToString() ?? folio;
                    solicitante = rd["Solicitante"]?.ToString() ?? solicitante;
                    proveedor = rd["Proveedor"] == DBNull.Value
                        ? "N/A"
                        : rd["Proveedor"].ToString() ?? "N/A";
                    monto = rd["MontoTotal"] == DBNull.Value
                        ? 0
                        : Convert.ToDecimal(rd["MontoTotal"]);

                    if (rd["PersonaCompradorID"] != DBNull.Value)
                        personaIds.Add(Convert.ToInt32(rd["PersonaCompradorID"]));
                }

                if (!personaIds.Any())
                {
                    const string sqlCompras = @"
SELECT DISTINCT P.PersonaID
FROM Usuarios U
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
INNER JOIN EmpleadoDepartamentos ED
    ON U.UsuarioID = ED.UsuarioID
   AND ED.Activo = 1
INNER JOIN Departamentos D
    ON ED.DepartamentoID = D.DepartamentoID
   AND D.Activo = 1
WHERE UPPER(D.NombreDepartamento) LIKE '%COMPRAS%'
  AND P.Correo IS NOT NULL
  AND LTRIM(RTRIM(P.Correo)) <> '';";

                    using var cmdCompras = new SqlCommand(sqlCompras, conn);
                    using var rdCompras = await cmdCompras.ExecuteReaderAsync();

                    while (await rdCompras.ReadAsync())
                        personaIds.Add(Convert.ToInt32(rdCompras["PersonaID"]));
                }

                personaIds = personaIds.Distinct().ToList();

                if (!personaIds.Any())
                    return;

                string asunto = $"Cotización autorizada por el solicitante - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#16a34a; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Cotización autorizada</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p><strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong> autorizó la cotización dictaminada por Compras.</p>

      <div style='background:#f0fdf4; border-left:4px solid #16a34a; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0;'><strong>Monto:</strong> {monto:C}</p>
      </div>

      <p>La solicitud avanzó a documentación presupuestal del usuario.</p>
      <p>Consulta el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error notificando a Compras la autorización de cotización. SolicitudID={SolicitudID}",
                    solicitudId
                );
            }
        }


        private async Task NotificarControlPresupuestal_CotizacionSeleccionadaAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var personaIds = new List<int>();

                string sqlPersonas = @"
SELECT DISTINCT P.PersonaID
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN EmpleadoDepartamentos ED ON U.UsuarioID = ED.UsuarioID AND ED.Activo = 1
INNER JOIN Departamentos D ON ED.DepartamentoID = D.DepartamentoID AND D.Activo = 1
WHERE P.Correo IS NOT NULL
  AND LTRIM(RTRIM(P.Correo)) <> ''
  AND (
        UPPER(D.NombreDepartamento) LIKE '%CONTROL PRESUPUESTAL%'
        OR UPPER(D.NombreDepartamento) LIKE '%PRESUPUESTOS%'
        OR UPPER(D.NombreDepartamento) LIKE '%CIS%'
      )";

                using (var cmdPersonas = new SqlCommand(sqlPersonas, conn))
                using (var rdPersonas = await cmdPersonas.ExecuteReaderAsync())
                {
                    while (await rdPersonas.ReadAsync())
                    {
                        personaIds.Add(Convert.ToInt32(rdPersonas["PersonaID"]));
                    }
                }

                if (!personaIds.Any())
                    return;

                string sqlSolicitud = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    S.TipoCompra,
    C.Proveedor,
    C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID";

                string folio = $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = "";
                string empresa = "";
                string tipoCompra = "";
                string proveedor = "";
                decimal monto = 0;

                using (var cmd = new SqlCommand(sqlSolicitud, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();

                    if (await rd.ReadAsync())
                    {
                        folio = rd["Folio"]?.ToString() ?? folio;
                        solicitante = rd["Solicitante"]?.ToString() ?? "";
                        empresa = rd["Empresa"]?.ToString() ?? "";
                        tipoCompra = rd["TipoCompra"]?.ToString() ?? "";
                        proveedor = rd["Proveedor"]?.ToString() ?? "";
                        monto = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
                    }
                }

                string asunto = $"Solicitud pendiente de dictamen presupuestal - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0891b2; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Dictamen presupuestal pendiente</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Compras ha dictaminado la cotización que procede. La solicitud está lista para revisión de Control Presupuestal.</p>

      <div style='background:#f0f9ff; border-left:4px solid #0891b2; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Tipo:</strong> {System.Net.WebUtility.HtmlEncode(tipoCompra)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0;'><strong>Monto:</strong> {monto:C}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, bandeja de <strong>Control Presupuestal</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo a Control Presupuestal. SolicitudID={SolicitudID}", solicitudId);
            }
        }



        private async Task NotificarSolicitante_DictamenPresupuestalAsync(
    int solicitudId,
    bool aprobado,
    string? motivo)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.TipoGasto,
    S.DentroPresupuesto,
    S.NumeroRequisicion,
    S.ObservacionesPresupuesto,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";

                string tipoGasto = rd["TipoGasto"] == DBNull.Value ? "" : rd["TipoGasto"].ToString();
                bool? dentroPresupuesto = rd["DentroPresupuesto"] == DBNull.Value ? null : (bool?)rd["DentroPresupuesto"];
                string requisicion = rd["NumeroRequisicion"] == DBNull.Value ? "" : rd["NumeroRequisicion"].ToString();
                string observaciones = rd["ObservacionesPresupuesto"] == DBNull.Value ? motivo ?? "" : rd["ObservacionesPresupuesto"].ToString();

                string estadoTexto = aprobado ? "APROBADA" : "RECHAZADA";
                string color = aprobado ? "#16a34a" : "#dc2626";
                string fondo = aprobado ? "#f0fdf4" : "#fef2f2";

                string detalleAprobacion = "";

                if (aprobado)
                {
                    detalleAprobacion = $@"
              <div style='background:{fondo}; border-left:4px solid {color}; padding:12px 14px; border-radius:6px; margin:14px 0;'>
                <p style='margin:0 0 6px;'><strong>Tipo de gasto:</strong> {System.Net.WebUtility.HtmlEncode(tipoGasto)}</p>
                <p style='margin:0 0 6px;'><strong>Presupuesto:</strong> {(dentroPresupuesto == true ? "Dentro de presupuesto" : "Desviación autorizada")}</p>
                <p style='margin:0;'><strong>Requisición:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(requisicion) ? "N/A" : requisicion)}</p>
              </div>";
                }

                string motivoHtml = string.IsNullOrWhiteSpace(observaciones)
                    ? ""
                    : $"<p><strong>Observaciones:</strong> {System.Net.WebUtility.HtmlEncode(observaciones)}</p>";

                string asunto = $"Dictamen presupuestal {estadoTexto.ToLower()} - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:{color}; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud {estadoTexto}</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Control Presupuestal ha emitido dictamen para tu solicitud de compra.</p>

      <div style='background:#f8f9fa; border-left:4px solid {color}; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
      </div>

      {detalleAprobacion}
      {motivoHtml}

      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de dictamen al solicitante. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        private async Task NotificarCompras_DictamenAprobadoAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.TipoGasto,
    S.DentroPresupuesto,
    S.NumeroRequisicion,
    S.CompradorAsignadoUsuarioID,
    PComprador.PersonaID AS PersonaCompradorID,
    PSolicitante.Nombre + ' ' + PSolicitante.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    C.Proveedor,
    C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Usuarios USolicitante ON S.UsuarioID = USolicitante.UsuarioID
INNER JOIN Persona PSolicitante ON USolicitante.PersonaID = PSolicitante.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
LEFT JOIN Usuarios UComprador ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador ON UComprador.PersonaID = PComprador.PersonaID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                if (rd["PersonaCompradorID"] == DBNull.Value)
                    return;

                int personaCompradorId = Convert.ToInt32(rd["PersonaCompradorID"]);

                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "";
                string empresa = rd["Empresa"]?.ToString() ?? "";
                string tipoGasto = rd["TipoGasto"]?.ToString() ?? "";
                string requisicion = rd["NumeroRequisicion"] == DBNull.Value ? "" : rd["NumeroRequisicion"].ToString();
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                decimal monto = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
                bool? dentroPresupuesto = rd["DentroPresupuesto"] == DBNull.Value ? null : (bool?)rd["DentroPresupuesto"];

                string presupuestoTexto =
                    dentroPresupuesto == true
                        ? "Dentro de presupuesto"
                        : "Desviación autorizada";

                string asunto = $"Solicitud aprobada para generar O.C. - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#16a34a; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud aprobada</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Control Presupuestal aprobó la solicitud. Ya puedes continuar con la generación de la O.C.</p>

      <div style='background:#f0fdf4; border-left:4px solid #16a34a; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0 0 6px;'><strong>Monto:</strong> {monto:C}</p>
        <p style='margin:0 0 6px;'><strong>Tipo de gasto:</strong> {System.Net.WebUtility.HtmlEncode(tipoGasto)}</p>
        <p style='margin:0 0 6px;'><strong>Presupuesto:</strong> {System.Net.WebUtility.HtmlEncode(presupuestoTexto)}</p>
        <p style='margin:0;'><strong>Requisición:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(requisicion) ? "N/A" : requisicion)}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, para registrar la Orden de Compra.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaCompradorId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de dictamen aprobado a compras. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        private async Task NotificarSolicitante_OCRegistradaAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    OC.NumeroOC,
    OC.Proveedor,
    OC.FechaOC
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Compras_OrdenCompra OC 
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";
                string numeroOC = rd["NumeroOC"]?.ToString() ?? "";
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                DateTime? fechaOC = rd["FechaOC"] == DBNull.Value ? null : (DateTime?)rd["FechaOC"];

                string asunto = $"Orden de compra registrada - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#2563eb; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Orden de compra registrada</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Compras ha registrado la Orden de Compra de tu solicitud.</p>

      <div style='background:#eff6ff; border-left:4px solid #2563eb; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(proveedor) ? "No capturado" : proveedor)}</p>
        <p style='margin:0;'><strong>Fecha:</strong> {(fechaOC.HasValue ? fechaOC.Value.ToString("dd/MM/yyyy HH:mm") : "N/A")}</p>
      </div>

      <p>El siguiente paso será el envío de la O.C. al proveedor.</p>
      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de O.C. registrada. SolicitudID={SolicitudID}", solicitudId);
            }
        }


        private async Task NotificarSolicitante_OCEnviadaProveedorAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    OC.NumeroOC,
    OC.Proveedor,
    OC.FechaEnvioProveedor,
    OC.FechaEstimadaEntrega
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Compras_OrdenCompra OC 
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";
                string numeroOC = rd["NumeroOC"]?.ToString() ?? "";
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                DateTime? fechaEnvio = rd["FechaEnvioProveedor"] == DBNull.Value ? null : (DateTime?)rd["FechaEnvioProveedor"];
                DateTime? fechaEstimada = rd["FechaEstimadaEntrega"] == DBNull.Value ? null : (DateTime?)rd["FechaEstimadaEntrega"];

                string asunto = $"O.C. enviada al proveedor - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>O.C. enviada al proveedor</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>La Orden de Compra de tu solicitud fue enviada al proveedor.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(proveedor) ? "No capturado" : proveedor)}</p>
        <p style='margin:0 0 6px;'><strong>Fecha de envío:</strong> {(fechaEnvio.HasValue ? fechaEnvio.Value.ToString("dd/MM/yyyy HH:mm") : "N/A")}</p>
        <p style='margin:0;'><strong>Entrega estimada:</strong> {(fechaEstimada.HasValue ? fechaEstimada.Value.ToString("dd/MM/yyyy") : "N/A")}</p>
      </div>

      <p>El siguiente paso será la recepción en almacén.</p>
      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de O.C. enviada a proveedor. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        /// <summary>
        /// Notifica al personal de Almacén de la empresa correspondiente cuando una O.C.
        /// con requisición queda pendiente de recepción (estatus 8).
        /// </summary>
        private async Task NotificarAlmacen_OrdenPendienteRecepcionAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                int empresaId;
                string folio = $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = "Solicitante";
                string empresa = "";
                string numeroOC = "";
                string proveedor = "";
                DateTime? fechaEstimadaEntrega = null;

                const string sqlSolicitud = @"
SELECT
    S.EmpresaID,
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    OC.NumeroOC,
    OC.Proveedor,
    OC.FechaEstimadaEntrega
FROM Compras_Solicitud S
INNER JOIN Usuarios U
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
INNER JOIN Empresas E
    ON S.EmpresaID = E.EmpresaID
OUTER APPLY
(
    SELECT TOP 1
        NumeroOC,
        Proveedor,
        FechaEstimadaEntrega
    FROM Compras_OrdenCompra
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY OrdenCompraID DESC
) OC
WHERE S.SolicitudID = @SolicitudID;";

                using (var cmd = new SqlCommand(sqlSolicitud, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                        return;

                    empresaId = Convert.ToInt32(rd["EmpresaID"]);
                    folio = rd["Folio"]?.ToString() ?? folio;
                    solicitante = rd["Solicitante"]?.ToString() ?? solicitante;
                    empresa = rd["Empresa"]?.ToString() ?? "";
                    numeroOC = rd["NumeroOC"] == DBNull.Value ? "" : rd["NumeroOC"].ToString() ?? "";
                    proveedor = rd["Proveedor"] == DBNull.Value ? "" : rd["Proveedor"].ToString() ?? "";
                    fechaEstimadaEntrega = rd["FechaEstimadaEntrega"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaEstimadaEntrega"]);
                }

                var personaIds = new List<int>();

                const string sqlAlmacen = @"
SELECT DISTINCT P.PersonaID
FROM Usuarios U
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
INNER JOIN EmpleadoDepartamentos ED
    ON U.UsuarioID = ED.UsuarioID
   AND ED.Activo = 1
INNER JOIN Departamentos D
    ON ED.DepartamentoID = D.DepartamentoID
   AND D.Activo = 1
INNER JOIN UsuariosEmpresas UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaID
WHERE P.EsColaboradorActivo = 1
  AND P.Correo IS NOT NULL
  AND LTRIM(RTRIM(P.Correo)) <> ''
  AND
  (
      UPPER(D.NombreDepartamento) LIKE '%ALMACEN%'
      OR UPPER(D.NombreDepartamento) LIKE '%ALMACÉN%'
  );";

                using (var cmd = new SqlCommand(sqlAlmacen, conn))
                {
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaId);

                    using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync())
                        personaIds.Add(Convert.ToInt32(rd["PersonaID"]));
                }

                personaIds = personaIds.Distinct().ToList();

                if (!personaIds.Any())
                {
                    _logger.LogWarning(
                        "No se encontraron destinatarios de Almacén para la solicitud {SolicitudID}, EmpresaID={EmpresaID}",
                        solicitudId,
                        empresaId
                    );
                    return;
                }

                string fechaTexto = fechaEstimadaEntrega.HasValue
                    ? fechaEstimadaEntrega.Value.ToString("dd/MM/yyyy")
                    : "No capturada";

                string asunto = $"O.C. pendiente de recepción en Almacén - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0891b2; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Pedido pendiente de recepción</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Se registró el envío al proveedor de una O.C. con requisición. El pedido quedó pendiente de recepción en Almacén.</p>

      <div style='background:#f0f9ff; border-left:4px solid #0891b2; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0;'><strong>Entrega estimada:</strong> {fechaTexto}</p>
      </div>

      <p>Cuando llegue mercancía, registra las cantidades recibidas desde la bandeja de Almacén.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error notificando a Almacén la O.C. pendiente de recepción. SolicitudID={SolicitudID}",
                    solicitudId
                );
            }
        }


        /// <summary>
        /// Notifica el avance acumulado de una recepción en Almacén o una entrega directa
        /// al usuario. Cubre los nuevos movimientos parciales (estatus 13) y el cierre
        /// completo del pedido (estatus 10).
        /// </summary>
        private async Task NotificarAvanceMaterialesAsync(
            int solicitudId,
            bool esRecepcionAlmacen,
            bool movimientoCompleto)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var personaIds = new List<int>();
                string folio = $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = "Solicitante";
                string numeroOC = "";
                string proveedor = "";
                DateTime? fechaMovimiento = null;
                string comentarios = "";
                string nombreRecibe = "";

                string sqlCabecera = esRecepcionAlmacen
                    ? @"
SELECT
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    PSolicitante.PersonaID AS PersonaSolicitanteID,
    PSolicitante.Nombre + ' ' + PSolicitante.ApellidoPaterno AS Solicitante,
    PComprador.PersonaID AS PersonaCompradorID,
    OC.NumeroOC,
    OC.Proveedor,
    Mov.FechaRecepcion AS FechaMovimiento,
    Mov.Comentarios,
    CAST(NULL AS NVARCHAR(400)) AS NombreRecibe
FROM Compras_Solicitud S
INNER JOIN Usuarios USolicitante
    ON S.UsuarioID = USolicitante.UsuarioID
INNER JOIN Persona PSolicitante
    ON USolicitante.PersonaID = PSolicitante.PersonaID
LEFT JOIN Usuarios UComprador
    ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador
    ON UComprador.PersonaID = PComprador.PersonaID
OUTER APPLY
(
    SELECT TOP 1 NumeroOC, Proveedor
    FROM Compras_OrdenCompra
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY OrdenCompraID DESC
) OC
OUTER APPLY
(
    SELECT TOP 1 FechaRecepcion, Comentarios
    FROM Compras_Recepciones
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY RecepcionID DESC
) Mov
WHERE S.SolicitudID = @SolicitudID;"
                    : @"
SELECT
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    PSolicitante.PersonaID AS PersonaSolicitanteID,
    PSolicitante.Nombre + ' ' + PSolicitante.ApellidoPaterno AS Solicitante,
    PComprador.PersonaID AS PersonaCompradorID,
    OC.NumeroOC,
    OC.Proveedor,
    Mov.FechaEntrega AS FechaMovimiento,
    Mov.Comentarios,
    Mov.NombreRecibe
FROM Compras_Solicitud S
INNER JOIN Usuarios USolicitante
    ON S.UsuarioID = USolicitante.UsuarioID
INNER JOIN Persona PSolicitante
    ON USolicitante.PersonaID = PSolicitante.PersonaID
LEFT JOIN Usuarios UComprador
    ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador
    ON UComprador.PersonaID = PComprador.PersonaID
OUTER APPLY
(
    SELECT TOP 1 NumeroOC, Proveedor
    FROM Compras_OrdenCompra
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY OrdenCompraID DESC
) OC
OUTER APPLY
(
    SELECT TOP 1 FechaEntrega, Comentarios, NombreRecibe
    FROM Compras_EntregasUsuario
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY EntregaID DESC
) Mov
WHERE S.SolicitudID = @SolicitudID;";

                using (var cmd = new SqlCommand(sqlCabecera, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                        return;

                    folio = rd["Folio"]?.ToString() ?? folio;
                    solicitante = rd["Solicitante"]?.ToString() ?? solicitante;
                    numeroOC = rd["NumeroOC"] == DBNull.Value ? "" : rd["NumeroOC"].ToString() ?? "";
                    proveedor = rd["Proveedor"] == DBNull.Value ? "" : rd["Proveedor"].ToString() ?? "";
                    fechaMovimiento = rd["FechaMovimiento"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaMovimiento"]);
                    comentarios = rd["Comentarios"] == DBNull.Value ? "" : rd["Comentarios"].ToString() ?? "";
                    nombreRecibe = rd["NombreRecibe"] == DBNull.Value ? "" : rd["NombreRecibe"].ToString() ?? "";

                    if (rd["PersonaSolicitanteID"] != DBNull.Value)
                        personaIds.Add(Convert.ToInt32(rd["PersonaSolicitanteID"]));

                    if (rd["PersonaCompradorID"] != DBNull.Value)
                        personaIds.Add(Convert.ToInt32(rd["PersonaCompradorID"]));
                }

                personaIds = personaIds.Distinct().ToList();

                if (!personaIds.Any())
                {
                    _logger.LogWarning(
                        "No se encontraron destinatarios para notificar el avance de materiales. SolicitudID={SolicitudID}",
                        solicitudId
                    );
                    return;
                }

                string sqlMateriales = esRecepcionAlmacen
                    ? @"
SELECT
    M.NombreMaterial,
    M.Cantidad AS CantidadSolicitada,
    ISNULL(SUM(CASE WHEN R.Activo = 1 THEN MD.Cantidad ELSE 0 END), 0) AS CantidadAvance
FROM Compras_Detalle_Materiales M
LEFT JOIN Compras_MovimientoDetalle MD
    ON MD.DetalleID = M.DetalleID
   AND MD.RecepcionID IS NOT NULL
   AND MD.EntregaID IS NULL
LEFT JOIN Compras_Recepciones R
    ON R.RecepcionID = MD.RecepcionID
WHERE M.SolicitudID = @SolicitudID
GROUP BY M.DetalleID, M.NombreMaterial, M.Cantidad
ORDER BY M.DetalleID;"
                    : @"
SELECT
    M.NombreMaterial,
    M.Cantidad AS CantidadSolicitada,
    ISNULL(SUM(CASE WHEN EU.Activo = 1 THEN MD.Cantidad ELSE 0 END), 0) AS CantidadAvance
FROM Compras_Detalle_Materiales M
LEFT JOIN Compras_MovimientoDetalle MD
    ON MD.DetalleID = M.DetalleID
   AND MD.EntregaID IS NOT NULL
   AND MD.RecepcionID IS NULL
LEFT JOIN Compras_EntregasUsuario EU
    ON EU.EntregaID = MD.EntregaID
WHERE M.SolicitudID = @SolicitudID
GROUP BY M.DetalleID, M.NombreMaterial, M.Cantidad
ORDER BY M.DetalleID;";

                var filas = new System.Text.StringBuilder();
                decimal totalSolicitado = 0;
                decimal totalAvance = 0;

                using (var cmd = new SqlCommand(sqlMateriales, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync())
                    {
                        string material = rd["NombreMaterial"] == DBNull.Value
                            ? "Artículo"
                            : rd["NombreMaterial"].ToString() ?? "Artículo";

                        decimal solicitada = rd["CantidadSolicitada"] == DBNull.Value
                            ? 0
                            : Convert.ToDecimal(rd["CantidadSolicitada"]);

                        decimal avance = rd["CantidadAvance"] == DBNull.Value
                            ? 0
                            : Convert.ToDecimal(rd["CantidadAvance"]);

                        decimal pendiente = Math.Max(solicitada - avance, 0);

                        totalSolicitado += solicitada;
                        totalAvance += Math.Min(avance, solicitada);

                        string estatusPartida = pendiente <= 0
                            ? "Completo"
                            : avance > 0
                                ? "Parcial"
                                : "Pendiente";

                        filas.Append($@"
<tr>
  <td style='padding:8px; border-bottom:1px solid #e2e8f0;'>{System.Net.WebUtility.HtmlEncode(material)}</td>
  <td style='padding:8px; border-bottom:1px solid #e2e8f0; text-align:center;'>{solicitada:0.##}</td>
  <td style='padding:8px; border-bottom:1px solid #e2e8f0; text-align:center;'>{avance:0.##}</td>
  <td style='padding:8px; border-bottom:1px solid #e2e8f0; text-align:center;'>{pendiente:0.##}</td>
  <td style='padding:8px; border-bottom:1px solid #e2e8f0; text-align:center;'>{estatusPartida}</td>
</tr>");
                    }
                }

                decimal porcentaje = totalSolicitado <= 0
                    ? 0
                    : Math.Round((totalAvance * 100m) / totalSolicitado, 2);

                string tipoMovimiento = esRecepcionAlmacen
                    ? "recepción en Almacén"
                    : "entrega al usuario";

                string titulo = movimientoCompleto
                    ? esRecepcionAlmacen
                        ? "Pedido recibido completamente en Almacén"
                        : "Pedido entregado completamente al usuario"
                    : esRecepcionAlmacen
                        ? "Recepción parcial registrada en Almacén"
                        : "Entrega parcial registrada al usuario";

                string asunto = $"{titulo} - {folio}";
                string color = movimientoCompleto ? "#16a34a" : "#f97316";
                string fondo = movimientoCompleto ? "#f0fdf4" : "#fff7ed";

                string comentariosHtml = string.IsNullOrWhiteSpace(comentarios)
                    ? ""
                    : $"<p><strong>Comentarios:</strong> {System.Net.WebUtility.HtmlEncode(comentarios)}</p>";

                string recibeHtml = !esRecepcionAlmacen && !string.IsNullOrWhiteSpace(nombreRecibe)
                    ? $"<p style='margin:0 0 6px;'><strong>Recibe:</strong> {System.Net.WebUtility.HtmlEncode(nombreRecibe)}</p>"
                    : "";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:760px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:{color}; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>{System.Net.WebUtility.HtmlEncode(titulo)}</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>
      <p>Se registró una {System.Net.WebUtility.HtmlEncode(tipoMovimiento)} para la solicitud indicada.</p>

      <div style='background:{fondo}; border-left:4px solid {color}; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        {recibeHtml}
        <p style='margin:0 0 6px;'><strong>Fecha:</strong> {(fechaMovimiento.HasValue ? fechaMovimiento.Value.ToString("dd/MM/yyyy HH:mm") : "N/A")}</p>
        <p style='margin:0;'><strong>Avance acumulado:</strong> {porcentaje:0.##}%</p>
      </div>

      {comentariosHtml}

      <div style='overflow-x:auto;'>
        <table style='width:100%; border-collapse:collapse; margin-top:14px; font-size:13px;'>
          <thead>
            <tr style='background:#f8fafc;'>
              <th style='padding:8px; text-align:left;'>Artículo</th>
              <th style='padding:8px; text-align:center;'>Solicitado</th>
              <th style='padding:8px; text-align:center;'>{(esRecepcionAlmacen ? "Recibido" : "Entregado")}</th>
              <th style='padding:8px; text-align:center;'>Pendiente</th>
              <th style='padding:8px; text-align:center;'>Estatus</th>
            </tr>
          </thead>
          <tbody>
            {filas}
          </tbody>
        </table>
      </div>

      <p style='margin-top:18px;'>
        {(movimientoCompleto
            ? "El pedido quedó completado y la solicitud pasó a estatus Cerrada."
            : "El pedido permanece en estatus Pedido entregado parcialmente hasta completar todas las partidas.")}
      </p>

      <p>Consulta el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error enviando notificación de avance de materiales. SolicitudID={SolicitudID}, EsRecepcionAlmacen={EsRecepcionAlmacen}, Completo={Completo}",
                    solicitudId,
                    esRecepcionAlmacen,
                    movimientoCompleto
                );
            }
        }


        //METODO PARA EXPORTAR A EXEL+
        [HttpGet("ExportarSeguimientoExcel")]
        [HttpGet("Compras/ExportarSeguimientoExcel")]
        public async Task<IActionResult> ExportarSeguimientoExcel(
    int? estatus,
    string? departamento,
    string? comprador,
    bool? soloRetrasadas)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var solicitudes = new List<SeguimientoCompraItemVm>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string puesto = "";

                string sqlPuesto = @"
SELECT P.Puesto
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE U.UsuarioID = @UsuarioID";

                using (var cmdPuesto = new SqlCommand(sqlPuesto, conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                puesto = puesto.ToUpper().Trim();

                bool esDireccion =
                    puesto.Contains("DIRECCION") ||
                    puesto.Contains("DIRECCIÓN") ||
                    puesto.Contains("DIRECTOR") ||
                    puesto.Contains("DIRECTORA") ||
                    puesto.StartsWith("DIR.");

                if (!esDireccion)
                    return Forbid();

                using (var cmd = new SqlCommand("sp_Compras_SeguimientoDireccion", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            solicitudes.Add(new SeguimientoCompraItemVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],

                                Solicitante = reader["Solicitante"].ToString(),
                                Departamento = reader["Departamento"].ToString(),
                                Empresa = reader["Empresa"].ToString(),

                                TipoCompra = reader["TipoCompra"].ToString(),
                                Urgencia = reader["Urgencia"].ToString(),

                                EstatusID = (int)reader["EstatusID"],
                                Estatus = reader["Estatus"].ToString(),

                                CompradorAsignado = reader["CompradorAsignado"].ToString(),

                                FechaAsignacionComprador =
                                    reader["FechaAsignacionComprador"] == DBNull.Value
                                        ? null
                                        : (DateTime?)reader["FechaAsignacionComprador"],

                                FechaUltimoMovimiento =
                                    reader["FechaUltimoMovimiento"] == DBNull.Value
                                        ? null
                                        : (DateTime?)reader["FechaUltimoMovimiento"],

                                DiasEnEstatus = Convert.ToInt32(reader["DiasEnEstatus"]),
                                DiasCotizando = Convert.ToInt32(reader["DiasCotizando"]),
                                MontoCotizado = Convert.ToDecimal(reader["MontoCotizado"]),
                                DiasPermitidos = Convert.ToInt32(reader["DiasPermitidos"]),
                                DiasHabilesTranscurridos = Convert.ToInt32(reader["DiasHabilesTranscurridos"]),
                                SemaforoTexto = reader["SemaforoTexto"].ToString(),

                                DiasCompras = reader["DiasCompras"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasCompras"]),
                                DiasPresupuesto = reader["DiasPresupuesto"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasPresupuesto"]),
                                DiasOC = reader["DiasOC"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasOC"]),
                                DiasProveedor = reader["DiasProveedor"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasProveedor"]),
                                DiasAlmacen = reader["DiasAlmacen"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DiasAlmacen"])

                            });
                        }
                    }
                }

                await AplicarMetasCotizacionAsync(conn, solicitudes);
            }

            if (estatus.HasValue)
            {
                solicitudes = solicitudes
                    .Where(x => x.EstatusID == estatus.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(departamento))
            {
                solicitudes = solicitudes
                    .Where(x =>
                        x.Departamento != null &&
                        x.Departamento.Contains(departamento, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(comprador))
            {
                solicitudes = solicitudes
                    .Where(x =>
                        x.CompradorAsignado != null &&
                        x.CompradorAsignado.Contains(comprador, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (soloRetrasadas == true)
            {
                solicitudes = solicitudes
                    .Where(x => x.SemaforoTexto == "Retrasada")
                    .ToList();
            }

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Seguimiento");

                string[] headers =
                {
            "Folio",
            "Fecha creación",
            "Solicitante",
            "Departamento",
            "Empresa",
            "Tipo compra",
            "Comprador asignado",
            "Estatus",
            "Categoría de compra",
            "Tiempo objetivo",
            "Días transcurridos",
            "SLA máximo (días)",
            "Semáforo",
            "Monto cotizado",
            "Días Compras",
            "Días Presupuesto",
            "Días O.C.",
            "Días Proveedor",
            "Días Almacén"
        };

                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cell(1, i + 1).Value = headers[i];
                }

                int row = 2;

                foreach (var item in solicitudes)
                {
                    ws.Cell(row, 1).Value = item.Folio;
                    ws.Cell(row, 2).Value = item.FechaCreacion;
                    ws.Cell(row, 3).Value = item.Solicitante;
                    ws.Cell(row, 4).Value = item.Departamento;
                    ws.Cell(row, 5).Value = item.Empresa;
                    ws.Cell(row, 6).Value = item.TipoCompra;
                    ws.Cell(row, 7).Value = item.CompradorAsignado;
                    ws.Cell(row, 8).Value = item.Estatus;
                    ws.Cell(row, 9).Value = item.Urgencia;
                    ws.Cell(row, 10).Value = item.TiempoObjetivoCotizacion ?? "";
                    ws.Cell(row, 11).Value = item.DiasHabilesTranscurridos;
                    ws.Cell(row, 12).Value = item.DiasPermitidos;
                    ws.Cell(row, 13).Value = item.SemaforoTexto;
                    ws.Cell(row, 14).Value = item.MontoCotizado;
                    ws.Cell(row, 15).Value = item.DiasCompras;
                    ws.Cell(row, 16).Value = item.DiasPresupuesto;
                    ws.Cell(row, 17).Value = item.DiasOC;
                    ws.Cell(row, 18).Value = item.DiasProveedor;
                    ws.Cell(row, 19).Value = item.DiasAlmacen;


                    row++;
                }

                var rango = ws.Range(1, 1, Math.Max(row - 1, 1), headers.Length);
                rango.CreateTable();

                ws.Row(1).Style.Font.Bold = true;
                ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                ws.Row(1).Style.Font.FontColor = XLColor.White;

                ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
                ws.Column(13).Style.NumberFormat.Format = "$#,##0.00";

                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var contenido = stream.ToArray();

                    string nombreArchivo = $"Seguimiento_Compras_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

                    return File(
                        contenido,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        nombreArchivo
                    );
                }
            }
        }


        //ENDPOINT JASON PARA OBTENER LA CARGA POR COMPRADOR
        [HttpGet("CargaCompradoresSeguimiento")]
        [HttpGet("Compras/CargaCompradoresSeguimiento")]
        public async Task<IActionResult> CargaCompradoresSeguimiento(int anio, int mes)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            if (anio < 2020 || mes < 1 || mes > 12)
                return BadRequest("Mes o año inválido.");

            var inicioMes = new DateTime(anio, mes, 1);
            var inicioMesSiguiente = inicioMes.AddMonths(1);

            var data = new List<object>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string puesto = "";

                string sqlPuesto = @"
SELECT P.Puesto
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE U.UsuarioID = @UsuarioID";

                using (var cmdPuesto = new SqlCommand(sqlPuesto, conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                puesto = puesto.ToUpper().Trim();

                bool esDireccion =
                    puesto.Contains("DIRECCION") ||
                    puesto.Contains("DIRECCIÓN") ||
                    puesto.Contains("DIRECTOR") ||
                    puesto.Contains("DIRECTORA") ||
                    puesto.StartsWith("DIR.");

                if (!esDireccion)
                    return Forbid();

                string sql = @"
SELECT
    ISNULL(P.Nombre + ' ' + P.ApellidoPaterno, 'Sin asignar') AS Comprador,
    SUM(CASE WHEN S.FechaCotizacion IS NOT NULL THEN 1 ELSE 0 END) AS Atendidas,
    SUM(CASE WHEN S.EstatusID = 1 THEN 1 ELSE 0 END) AS Pendientes,
    COUNT(S.SolicitudID) AS Total
FROM Compras_Solicitud S
LEFT JOIN Usuarios U
    ON S.CompradorAsignadoUsuarioID = U.UsuarioID
LEFT JOIN Persona P
    ON U.PersonaID = P.PersonaID
WHERE S.FechaCotizacion >= @InicioMes
  AND S.FechaCotizacion < @InicioMesSiguiente
GROUP BY
    ISNULL(P.Nombre + ' ' + P.ApellidoPaterno, 'Sin asignar')
ORDER BY Atendidas DESC";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@InicioMes", inicioMes);
                    cmd.Parameters.AddWithValue("@InicioMesSiguiente", inicioMesSiguiente);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new
                            {
                                comprador = reader["Comprador"].ToString(),
                                atendidas = Convert.ToInt32(reader["Atendidas"]),
                                pendientes = Convert.ToInt32(reader["Pendientes"]),
                                total = Convert.ToInt32(reader["Total"])
                            });
                        }
                    }
                }
            }

            return Json(data);
        }

        private async Task<int> GuardarArchivoCompraAsync(
            SqlConnection conn,
            SqlTransaction trans,
            int solicitudId,
            int? cotizacionId,
            string tipoArchivo,
            IFormFile archivo,
            int usuarioSubioId,
            string rutaContenedor,
            string prefijoNombre)
        {
            if (archivo == null || archivo.Length == 0)
                throw new Exception("No se recibió archivo para guardar.");

            string extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";

            string nombreOriginal = Path.GetFileName(archivo.FileName);

            string folioStr = solicitudId.ToString().PadLeft(5, '0');

            string nombreSistema =
                $"COM-{folioStr}_{prefijoNombre}_{Guid.NewGuid()}{extension}";

            _sftp.AsegurarDirectorio(rutaContenedor);

            string rutaArchivoSftp = $"{rutaContenedor}/{nombreSistema}";

            using (var stream = archivo.OpenReadStream())
            {
                _sftp.SubirStream(stream, rutaArchivoSftp);
            }

            string sqlVersion = @"
SELECT ISNULL(MAX(VersionArchivo), 0) + 1
FROM Compras_Archivos
WHERE SolicitudID = @SolicitudID
  AND TipoArchivo = @TipoArchivo";

            int versionArchivo;

            using (var cmdVersion = new SqlCommand(sqlVersion, conn, trans))
            {
                cmdVersion.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmdVersion.Parameters.AddWithValue("@TipoArchivo", tipoArchivo);

                versionArchivo = Convert.ToInt32(await cmdVersion.ExecuteScalarAsync());
            }

            string sqlDesactivarAnteriores = @"
UPDATE Compras_Archivos
SET Vigente = 0
WHERE SolicitudID = @SolicitudID
  AND TipoArchivo = @TipoArchivo
  AND Vigente = 1
  AND Activo = 1";

            using (var cmdDesactivar = new SqlCommand(sqlDesactivarAnteriores, conn, trans))
            {
                cmdDesactivar.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmdDesactivar.Parameters.AddWithValue("@TipoArchivo", tipoArchivo);

                await cmdDesactivar.ExecuteNonQueryAsync();
            }

            string sqlInsert = @"
INSERT INTO Compras_Archivos
(
    SolicitudID,
    CotizacionID,
    TipoArchivo,
    RutaArchivo,
    NombreOriginal,
    NombreSistema,
    Extension,
    ContentType,
    TamanoBytes,
    VersionArchivo,
    Vigente,
    Activo,
    UsuarioSubioID,
    FechaCarga
)
VALUES
(
    @SolicitudID,
    @CotizacionID,
    @TipoArchivo,
    @RutaArchivo,
    @NombreOriginal,
    @NombreSistema,
    @Extension,
    @ContentType,
    @TamanoBytes,
    @VersionArchivo,
    1,
    1,
    @UsuarioSubioID,
    GETDATE()
);

SELECT SCOPE_IDENTITY();";

            using (var cmdInsert = new SqlCommand(sqlInsert, conn, trans))
            {
                cmdInsert.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmdInsert.Parameters.AddWithValue("@CotizacionID", (object?)cotizacionId ?? DBNull.Value);
                cmdInsert.Parameters.AddWithValue("@TipoArchivo", tipoArchivo);
                cmdInsert.Parameters.AddWithValue("@RutaArchivo", rutaArchivoSftp);
                cmdInsert.Parameters.AddWithValue("@NombreOriginal", nombreOriginal);
                cmdInsert.Parameters.AddWithValue("@NombreSistema", nombreSistema);
                cmdInsert.Parameters.AddWithValue("@Extension", extension);
                cmdInsert.Parameters.AddWithValue("@ContentType", archivo.ContentType ?? "application/octet-stream");
                cmdInsert.Parameters.AddWithValue("@TamanoBytes", archivo.Length);
                cmdInsert.Parameters.AddWithValue("@VersionArchivo", versionArchivo);
                cmdInsert.Parameters.AddWithValue("@UsuarioSubioID", usuarioSubioId);

                return Convert.ToInt32(await cmdInsert.ExecuteScalarAsync());
            }
        }

        private bool ExtensionPermitida(string extension, params string[] permitidas)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            return permitidas.Contains(extension.ToLowerInvariant());
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarDocumentosPresupuesto(
            int solicitudId,
            string? observaciones,
            IFormFile? archivoDesviacionPresupuestal)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            if (solicitudId <= 0)
            {
                TempData["Error"] = "No se recibió la solicitud.";
                return RedirectToAction("BandejaPresupuestos");
            }

            bool incluyeDesviacion =
                archivoDesviacionPresupuestal != null &&
                archivoDesviacionPresupuestal.Length > 0;

            observaciones = observaciones?.Trim();

            if (!incluyeDesviacion && string.IsNullOrWhiteSpace(observaciones))
            {
                TempData["Error"] =
                    "Debes indicar qué documentación falta o adjuntar la desviación presupuestal para firma.";

                return RedirectToAction("Dictamen", new { id = solicitudId });
            }

            if (incluyeDesviacion)
            {
                string extension =
                    Path.GetExtension(archivoDesviacionPresupuestal!.FileName)?.ToLowerInvariant() ?? "";

                if (!ExtensionPermitida(extension, ".pdf", ".jpg", ".jpeg", ".png", ".webp"))
                {
                    TempData["Error"] =
                        "La desviación presupuestal solo puede ser PDF o imagen JPG, PNG o WEBP.";

                    return RedirectToAction("Dictamen", new { id = solicitudId });
                }
            }

            string mensajeUsuario = string.IsNullOrWhiteSpace(observaciones)
                ? "Control Presupuestal cargó una desviación. Descárgala, fírmala y vuelve a subirla escaneada."
                : observaciones;

            string responsable = User.Identity?.Name ?? "Control Presupuestal";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool esControlPresupuestal =
                    await UsuarioPerteneceADepartamentoAsync(
                        conn,
                        usuarioId,
                        "CONTROL PRESUPUESTAL",
                        "PRESUPUESTOS",
                        "CIS"
                    );

                if (!esControlPresupuestal)
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int cotizacionSeleccionadaId;

                        string sqlValidar = @"
SELECT CotizacionSeleccionadaID
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 4
  AND CotizacionSeleccionadaID IS NOT NULL";

                        using (var cmdValidar = new SqlCommand(sqlValidar, conn, trans))
                        {
                            cmdValidar.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            var result = await cmdValidar.ExecuteScalarAsync();

                            if (result == null || result == DBNull.Value)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud ya no está en Control Presupuestal.";
                                return RedirectToAction("BandejaPresupuestos");
                            }

                            cotizacionSeleccionadaId = Convert.ToInt32(result);
                        }

                        if (incluyeDesviacion)
                        {
                            await GuardarArchivoCompraAsync(
                                conn,
                                trans,
                                solicitudId,
                                cotizacionSeleccionadaId,
                                "DESVIACION_CP",
                                archivoDesviacionPresupuestal!,
                                usuarioId,
                                _rutaNas.ObtenerRutaComprasDesviaciones(),
                                "DesviacionParaFirma"
                            );
                        }

                        string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 3,
    ObservacionesPresupuesto = @Observaciones
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 4";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Observaciones", mensajeUsuario);

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud ya no está en Control Presupuestal.";
                                return RedirectToAction("BandejaPresupuestos");
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(
    SolicitudID,
    EstatusID,
    FechaMovimiento,
    UsuarioResponsable
)
VALUES
(
    @SolicitudID,
    3,
    GETDATE(),
    @Responsable
)";

                        using (var cmdHist = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist.Parameters.AddWithValue("@Responsable", responsable);

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_DocumentosSolicitadosAsync(
                            solicitudId,
                            mensajeUsuario
                        );

                        TempData["Mensaje"] = incluyeDesviacion
                            ? "La desviación fue enviada al usuario para firma y carga del documento escaneado."
                            : "Se solicitó documentación adicional al usuario.";

                        return RedirectToAction("BandejaPresupuestos");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(
                            ex,
                            "Error al solicitar documentos para la solicitud {SolicitudID}",
                            solicitudId
                        );

                        TempData["Error"] = "Ocurrió un error al solicitar documentos.";
                        return RedirectToAction("Dictamen", new { id = solicitudId });
                    }
                }
            }
        }

        //METODO PARA SOLICITAR DOCUMENTOS

        private async Task NotificarSolicitante_DocumentosSolicitadosAsync(
    int solicitudId,
    string observaciones)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
FROM Compras_Solicitud S
INNER JOIN Usuarios U 
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P 
    ON U.PersonaID = P.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);

                string folio = rd["Folio"]?.ToString()
                    ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";

                string solicitante = rd["Solicitante"]?.ToString()
                    ?? "Solicitante";

                string asunto = $"Documentación requerida para tu solicitud - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Documentación requerida</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Control Presupuestal revisó tu solicitud y requiere documentación adicional o corrección de documentos antes de emitir dictamen.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0;'><strong>Observaciones:</strong> {System.Net.WebUtility.HtmlEncode(observaciones)}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, revisa el detalle de la solicitud y vuelve a confirmar la documentación presupuestal.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error enviando correo de solicitud de documentos. SolicitudID={SolicitudID}",
                    solicitudId
                );
            }
        }

    }
}
