namespace SistemaNutri.Models
{
    using System.Linq;

    public class ComprasModel
    {
    }

    public class CompraViewModel
    {
        // Datos Generales
        public int EmpresaID { get; set; }
        public string TipoCompra { get; set; } // "Nacional" o "Internacional"
        public bool EsProyecto { get; set; }
        public string? NombreProyecto { get; set; }
        public int UrgenciaID { get; set; }
        public string? Comentarios { get; set; }

        // Datos del flujo operativo aprobados para captura; no implican autorización automática.
        public string ClasificacionSolicitud { get; set; } = "COMPRA";
        public string DestinoEntrega { get; set; } = "ALMACEN";
        public string? GranjaDestino { get; set; }
        public bool ProductoNuevo { get; set; }
        public string? DatosTecnicosProducto { get; set; }

        // Solo para Internacional
        public int? TransporteID { get; set; }

        public decimal? MontoPresupuestoSolicitado { get; set; }

        public bool FueraPresupuestoUsuario { get; set; }

        public IFormFile? ArchivoDesviacion { get; set; }

        // Autorización requerida únicamente cuando la prioridad seleccionada es urgente.
        public IFormFile? ArchivoAutorizacionUrgente { get; set; }

        // Listado de materiales
        public List<MaterialItem> Materiales { get; set; } = new List<MaterialItem>();
    }

    public class CategoriaCompraInfoVm
    {
        public int UrgenciaID { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string TiempoObjetivo { get; set; } = string.Empty;
        public string Ejemplos { get; set; } = string.Empty;
        public bool RequiereAutorizacion { get; set; }
    }

    public class MaterialItem
    {
        public int DetalleID { get; set; }
        public string? Nombre { get; set; }
        public string? Descripcion { get; set; }
        public decimal Cantidad { get; set; }
        public string? UnidadMedida { get; set; }

        public IFormFile? ArchivoReferencia { get; set; }

        public string? ArchivoReferenciaPath { get; set; }
        public string? NombreArchivoReferencia { get; set; }
        public string? ExtensionArchivoReferencia { get; set; }

        // Seguimiento por partida.
        public decimal CantidadRecibidaAlmacen { get; set; }
        public decimal CantidadEntregadaUsuario { get; set; }

        public decimal CantidadPendienteAlmacen =>
            Math.Max(Cantidad - CantidadRecibidaAlmacen, 0);

        public decimal CantidadPendienteUsuario =>
            Math.Max(Cantidad - CantidadEntregadaUsuario, 0);

        public bool RecibidaCompletaAlmacen =>
            Cantidad > 0 && CantidadRecibidaAlmacen >= Cantidad;

        public bool EntregadaCompletaUsuario =>
            Cantidad > 0 && CantidadEntregadaUsuario >= Cantidad;
    }

    public class MisComprasVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Tipo { get; set; }
        public string Empresa { get; set; }
        public DateTime Fecha { get; set; }
        public string Estatus { get; set; }
        public int EstatusID { get; set; }

      
        public int Porcentaje
        {
            get
            {
                return EstatusID switch
                {
                    1 => 10,   
                    2 => 25,   
                    14 => 32,  
                    3 => 40,   
                    4 => 55,   
                    5 => 70,   
                    6 => 80,   
                    7 => 82,   
                    8 => 85,   
                    9 => 90,   
                    13 => 95,
                    10 => 100, 
                    11 => 100, 
                    12 => 100, 
                    _ => 0
                };
            }
        }

        public string ColorProgreso => EstatusID switch
        {
            1 => "bg-primary",
            2 => "bg-info",
            14 => "bg-warning",
            3 => "bg-info",
            4 => "bg-info",
            5 => "bg-primary",
            6 => "bg-primary",
            7 => "bg-primary",
            8 => "bg-warning",
            9 => "bg-warning",
            13 => "bg-warning",
            10 => "bg-success",
            11 => "bg-danger",
            12 => "bg-secondary",
            _ => "bg-primary"
        };
    }

    public class BandejaComprasVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string TipoCompra { get; set; }
        public string Urgencia { get; set; }
        public string? TiempoObjetivoCotizacion { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Estatus { get; set; }

        public int? CompradorAsignadoUsuarioID { get; set; }
    }

    public class DataHeatmapVm
    {
        public string Departamento { get; set; }
        public string Estatus { get; set; }
        public int Horas { get; set; }
    }

    public class ComprasDashboardVm
    {
        public List<HeatmapSeries> HeatmapData { get; set; } = new List<HeatmapSeries>();

        public List<decimal> DonaValores { get; set; } = new();
        public List<string> DonaEtiquetas { get; set; } = new();
        public List<string> DonaColores { get; set; } = new();
        public List<int> DonaEstatusIds { get; set; } = new();

        public List<double> TiemposPromedio { get; set; } = new();

        public List<string> EtiquetasDepartamentos { get; set; } = new()
        {
            "Compras",
            "Autorización usuario",
            "Control Presupuestal",
            "Orden de Compra",
            "Proveedor",
            "Almacén"
        };

        public int CriticosVencidos { get; set; }
        public double PromedioTotal { get; set; }
        public int SolicitudesActivas { get; set; }
        public int PendientesAutorizacionUsuario { get; set; }
        public int EntregasParciales { get; set; }
    }

    public class HeatmapSeries
    {
        public string name { get; set; }
        public List<HeatmapDataPoint> data { get; set; } = new List<HeatmapDataPoint>();
    }

    public class HeatmapDataPoint
    {
        public string x { get; set; }
        public int y { get; set; }
    }

    public class DictamenPresupuestalVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public decimal MontoCotizado { get; set; }

        public bool Pasa { get; set; }
        public string TipoGasto { get; set; }
        public bool DentroDePresupuesto { get; set; }
        public string NumeroRequisicion { get; set; }
        public string Observaciones { get; set; }

        public int CotizacionID { get; set; }
        public string? Proveedor { get; set; }
        public string? ArchivoPath { get; set; }
        public string? NombreArchivoOriginal { get; set; }
        public string? Extension { get; set; }

        public decimal? MontoPresupuestoSolicitado { get; set; }

        public bool FueraPresupuestoUsuario { get; set; }

        public string? ArchivoDesviacionPath { get; set; }
        public string? NombreArchivoDesviacion { get; set; }
        public string? ExtensionArchivoDesviacion { get; set; }

        public string? ArchivoDesviacionParaFirmaPath { get; set; }
        public string? NombreArchivoDesviacionParaFirma { get; set; }
        public string? ExtensionArchivoDesviacionParaFirma { get; set; }

        public string? ArchivoFormatoRequisicionPath { get; set; }
        public string? NombreArchivoFormatoRequisicion { get; set; }
        public string? ExtensionArchivoFormatoRequisicion { get; set; }
    }

    public class DetalleCompraVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string NombreSolicitante { get; set; }
        public string ClasificacionSolicitud { get; set; } = "COMPRA";
        public string DestinoEntrega { get; set; } = "ALMACEN";
        public string? GranjaDestino { get; set; }
        public bool ProductoNuevo { get; set; }
        public string? DatosTecnicosProducto { get; set; }
        public DateTime? FechaConfirmacionTecnica { get; set; }
        public int? UsuarioConfirmacionTecnicaID { get; set; }
        public string Empresa { get; set; }

        public int? CompradorAsignadoUsuarioID { get; set; }
        public string? CompradorAsignadoNombre { get; set; }
        public string? CompradorAsignadoPuesto { get; set; }

        public int UrgenciaID { get; set; }
        public string? UrgenciaDescripcion { get; set; }
        public string? TiempoObjetivoCotizacion { get; set; }
        public string? EjemplosCategoriaCompra { get; set; }
        public int DiasHabilesObjetivo { get; set; }
        public bool EsUrgente { get; set; }

        public string? ArchivoAutorizacionUrgentePath { get; set; }
        public string? NombreArchivoAutorizacionUrgente { get; set; }
        public string? ExtensionArchivoAutorizacionUrgente { get; set; }

        public bool TieneAutorizacionUrgente =>
            !string.IsNullOrWhiteSpace(ArchivoAutorizacionUrgentePath);

        public string EstatusActual { get; set; }
        public int EstatusID { get; set; }

        public List<MaterialItem> Materiales { get; set; } = new List<MaterialItem>();

        public decimal TotalCantidadSolicitada =>
            Materiales.Sum(x => x.Cantidad);

        public decimal TotalCantidadRecibidaAlmacen =>
            Materiales.Sum(x => x.CantidadRecibidaAlmacen);

        public decimal TotalCantidadEntregadaUsuario =>
            Materiales.Sum(x => x.CantidadEntregadaUsuario);

        public decimal TotalPendienteAlmacen =>
            Materiales.Sum(x => x.CantidadPendienteAlmacen);

        public decimal TotalPendienteUsuario =>
            Materiales.Sum(x => x.CantidadPendienteUsuario);

        public bool TieneRecepcionesAlmacen { get; set; }
        public bool RecepcionAlmacenParcial { get; set; }

        public bool TieneEntregasUsuario { get; set; }
        public bool EntregaUsuarioParcial { get; set; }

        public string TipoGasto { get; set; }

        public bool EsRequisicion =>
            string.Equals(
                TipoGasto?.Trim(),
                "REQUISICION",
                StringComparison.OrdinalIgnoreCase
            ) ||
            string.Equals(
                TipoGasto?.Trim(),
                "REQUISICIÓN",
                StringComparison.OrdinalIgnoreCase
            );

        public bool? DentroPresupuesto { get; set; }
        public string NumeroRequisicion { get; set; }
        public string ObservacionesPresupuesto { get; set; }

        public string? NumeroOC { get; set; }
        public string? ProveedorOC { get; set; }
        public string? ComentariosOC { get; set; }
        public DateTime? FechaOC { get; set; }

        public bool EsCompras { get; set; }

        public bool TieneOC { get; set; }

        public DateTime? FechaEnvioProveedor { get; set; }
        public DateTime? FechaEstimadaEntrega { get; set; }
        public string? ComentariosEnvioProveedor { get; set; }
        public bool OCEnviadaProveedor { get; set; }

        public bool RecibidaEnAlmacen { get; set; }
        public DateTime? FechaRecepcionAlmacen { get; set; }
        public string? ComentariosRecepcionAlmacen { get; set; }

        public bool EntregadaUsuario { get; set; }
        public DateTime? FechaEntregaUsuario { get; set; }
        public string? NombreRecibeUsuario { get; set; }
        public string? ComentariosEntregaUsuario { get; set; }

        public int? CotizacionSeleccionadaID { get; set; }
        public DateTime? FechaSeleccionCotizacion { get; set; }
        public string? ComentariosSeleccionUsuario { get; set; }
        public int? UsuarioSeleccionCotizacionID { get; set; }
        public string? NombreSelectorCotizacion { get; set; }
        public bool CotizacionSeleccionadaPorSolicitante { get; set; }

        public DateTime? FechaAutorizacionCotizacion { get; set; }
        public int? UsuarioAutorizacionCotizacionID { get; set; }
        public string? NombreAutorizadorCotizacion { get; set; }
        public string? ComentariosAutorizacionCotizacion { get; set; }
        public bool CotizacionAutorizadaPorSolicitante { get; set; }

        public bool EsSolicitante { get; set; }

        public string? ArchivoReferenciaPath { get; set; }

        // Flujo completo:
        // 1 -> 2 -> 14 -> 3 -> 4 -> 5 -> 6 -> 7
        // REQUISICION: 7 -> 8 -> 13 (si parcial) -> 10
        // GASTO:       7 -> 9 -> 13 (si parcial) -> 10
        public int Porcentaje
        {
            get
            {
                return EstatusID switch
                {
                    1 => 10,
                    2 => 25,
                    14 => 32,
                    3 => 40,
                    4 => 55,
                    5 => 70,
                    6 => 80,
                    7 => 82,
                    8 => 85,
                    9 => 90,
                    13 => 95,
                    10 => 100,
                    11 => 100,
                    12 => 100,
                    _ => 0
                };
            }
        }

        public string ColorProgreso => EstatusID switch
        {
            1 => "bg-primary",
            2 => "bg-info",
            14 => "bg-warning",
            3 => "bg-info",
            4 => "bg-info",
            5 => "bg-primary",
            6 => "bg-primary",
            7 => "bg-primary",
            8 => "bg-warning",
            9 => "bg-warning",
            13 => "bg-warning",
            10 => "bg-success",
            11 => "bg-danger",
            12 => "bg-secondary",
            _ => "bg-primary"
        };

        public decimal? MontoPresupuestoSolicitado { get; set; }

        public bool FueraPresupuestoUsuario { get; set; }

        public string? ArchivoDesviacionPath { get; set; }
        public string? NombreArchivoDesviacion { get; set; }
        public string? ExtensionArchivoDesviacion { get; set; }

        public string? ArchivoDesviacionParaFirmaPath { get; set; }
        public string? NombreArchivoDesviacionParaFirma { get; set; }
        public string? ExtensionArchivoDesviacionParaFirma { get; set; }

        public bool TieneDesviacionParaFirma =>
            !string.IsNullOrWhiteSpace(ArchivoDesviacionParaFirmaPath);

        public bool PuedeVerArchivoDesviacion { get; set; }

        public string? EvidenciaRecepcionPath { get; set; }
        public string? NombreArchivoEvidencia { get; set; }
        public string? ExtensionArchivoEvidencia { get; set; }

        public bool TieneEvidenciaRecepcion =>
            !string.IsNullOrWhiteSpace(EvidenciaRecepcionPath);

        public string? EvidenciaEntregaPath { get; set; }
        public string? NombreArchivoEvidenciaEntrega { get; set; }
        public string? ExtensionArchivoEvidenciaEntrega { get; set; }

        public List<CotizacionDetalleVm> Cotizaciones { get; set; } = new();

        public string? ArchivoFormatoRequisicionPath { get; set; }
        public string? NombreArchivoFormatoRequisicion { get; set; }
        public string? ExtensionArchivoFormatoRequisicion { get; set; }
    }

    public class TiempoProcesoVm
    {
        public string Etapa { get; set; }
        public double PromedioHoras { get; set; }
        public string Responsable { get; set; }
    }

    public class CuelloBotellaVm
    {
        public string Departamento { get; set; }
        public double HorasPromedio { get; set; }

        public string EstatusSLA =>
            HorasPromedio <= 24
                ? "A Tiempo"
                : "Retrasado";
    }

    public class IndexComprasVm
    {
        public IEnumerable<MisComprasVm> MisCompras { get; set; }
        public ComprasDashboardVm Estadisticas { get; set; }
    }

    public class BandejaPresupuestosDashboardVm
    {
        public int Pendientes { get; set; }
        public int Aprobadas { get; set; }
        public int Rechazadas { get; set; }
        public decimal MontoAprobado { get; set; }
        public decimal MontoRechazado { get; set; }
        public int Desviaciones { get; set; }
    }

    public class ControlPresupuestalVm
    {
        public int TotalPendientes { get; set; }
        public int TotalAprobadas { get; set; }
        public int TotalRechazadas { get; set; }
        public decimal MontoAprobado { get; set; }
        public decimal MontoRechazado { get; set; }

        public List<BandejaComprasVm> Pendientes { get; set; } = new();
        public List<HistoricoPresupuestoVm> Historico { get; set; } = new();
    }

    public class HistoricoPresupuestoVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string TipoCompra { get; set; }
        public decimal MontoTotal { get; set; }
        public string Resultado { get; set; }
        public bool? DentroPresupuesto { get; set; }
        public string NumeroRequisicion { get; set; }
        public string Observaciones { get; set; }
        public DateTime FechaDictamen { get; set; }
    }

    public class BandejaComprasDashboardVm
    {
        public int TotalPendientes { get; set; }
        public int TotalCotizadas { get; set; }
        public int TotalAtendidas { get; set; }
        public int TotalEnSeguimiento { get; set; }
        public int TotalFinalizadas { get; set; }
        public int TotalPendientesAutorizacionUsuario { get; set; }
        public int TotalEntregasParciales { get; set; }

        public decimal MontoCotizado { get; set; }

        public bool EsDireccionCompras { get; set; }

        public List<CompradorCargaVm> CargaCompradores { get; set; } = new();

        public List<CompradorSelectVm> CompradoresDisponibles { get; set; } = new();

        public List<BandejaComprasVm> Pendientes { get; set; } = new();

        public List<HistoricoComprasVm> Historico { get; set; } = new();
    }

    public class CompradorSelectVm
    {
        public int UsuarioID { get; set; }
        public string NombreCompleto { get; set; }
        public string Puesto { get; set; }
    }

    public class HistoricoComprasVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string TipoCompra { get; set; }
        public string Urgencia { get; set; }
        public string? TiempoObjetivoCotizacion { get; set; }
        public string Estatus { get; set; }
        public decimal MontoTotal { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaCotizacion { get; set; }
        public int EstatusID { get; set; }
    }

    public class CompradorCargaVm
    {
        public int UsuarioID { get; set; }
        public string NombreCompleto { get; set; }
        public string Puesto { get; set; }
        public int Pendientes { get; set; }
        public int Cotizadas { get; set; }
        public int EnSeguimiento { get; set; }
        public int Finalizadas { get; set; }
        public int TotalAsignadas { get; set; }
    }

    public class RegistrarOrdenCompraVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Empresa { get; set; }

        public string NumeroOC { get; set; }
        public string? Proveedor { get; set; }
        public string? Comentarios { get; set; }
    }

    public class SeguimientoDireccionVm
    {
        public List<SeguimientoCompraItemVm> Solicitudes { get; set; } = new();

        public int TotalSolicitudes =>
            Solicitudes.Count;

        public int TotalActivas =>
            Solicitudes.Count(x =>
                x.EstatusID != 10 &&
                x.EstatusID != 11 &&
                x.EstatusID != 12
            );

        public int TotalCerradas =>
            Solicitudes.Count(x =>
                x.EstatusID == 10
            );

        public int TotalRechazadas =>
            Solicitudes.Count(x =>
                x.EstatusID == 11
            );

        public int TotalRetrasadas =>
            Solicitudes.Count(x =>
                x.DiasEnEstatus >= 2 &&
                x.EstatusID != 10 &&
                x.EstatusID != 11 &&
                x.EstatusID != 12
            );
    }

    public class SeguimientoCompraItemVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public DateTime FechaCreacion { get; set; }

        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string Empresa { get; set; }

        public string TipoCompra { get; set; }
        public string Urgencia { get; set; }
        public string? TiempoObjetivoCotizacion { get; set; }

        public int EstatusID { get; set; }
        public string Estatus { get; set; }

        public string CompradorAsignado { get; set; }
        public DateTime? FechaAsignacionComprador { get; set; }

        public DateTime? FechaUltimoMovimiento { get; set; }

        public int DiasEnEstatus { get; set; }
        public int DiasDesdeSolicitud { get; set; }
        public int DiasCotizando { get; set; }

        public decimal MontoCotizado { get; set; }

        public int DiasPermitidos { get; set; }
        public int DiasHabilesTranscurridos { get; set; }
        public string SemaforoTexto { get; set; }

        public string SemaforoCss => EstatusID switch
        {
            10 => "badge bg-success",
            11 => "badge bg-danger",
            12 => "badge bg-secondary",
            8 => "badge bg-info text-dark",
            9 => "badge bg-warning text-dark",
            13 => "badge bg-warning text-dark",
            14 => "badge bg-warning text-dark",
            _ => SemaforoTexto switch
            {
                "Retrasada" => "badge bg-danger",
                "Por vencer" => "badge bg-warning text-dark",
                "A tiempo" => "badge bg-success",
                _ => "badge bg-secondary"
            }
        };

        public List<TiempoDepartamentoVm> TiemposDepartamento { get; set; } = new();

        public int DiasCompras { get; set; }
        public int DiasPresupuesto { get; set; }
        public int DiasOC { get; set; }
        public int DiasProveedor { get; set; }
        public int DiasAlmacen { get; set; }
    }

    public class BandejaAlmacenVm
    {
        public List<AlmacenItemVm> PendientesRecepcion { get; set; } = new();

        public int TotalPendientesRecepcion =>
            PendientesRecepcion.Count;

        public int TotalRecepcionesParciales =>
            PendientesRecepcion.Count(x =>
                x.EstatusID == 13 ||
                x.Materiales.Any(m =>
                    m.CantidadRecibidaAlmacen > 0 &&
                    !m.RecibidaCompletaAlmacen
                )
            );
    }

    public class AlmacenItemVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }

        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string Empresa { get; set; }

        public string NumeroOC { get; set; }
        public string Proveedor { get; set; }

        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaEnvioProveedor { get; set; }
        public DateTime? FechaEstimadaEntrega { get; set; }
        public DateTime? FechaRecepcionAlmacen { get; set; }

        public string Estatus { get; set; }
        public int EstatusID { get; set; }

        public int DiasDesdeEnvioProveedor { get; set; }

        public List<MaterialItem> Materiales { get; set; } = new();

        public decimal TotalSolicitado =>
            Materiales.Sum(x => x.Cantidad);

        public decimal TotalRecibido =>
            Materiales.Sum(x => x.CantidadRecibidaAlmacen);

        public decimal TotalPendiente =>
            Materiales.Sum(x => x.CantidadPendienteAlmacen);

        public decimal PorcentajeRecibido =>
            TotalSolicitado <= 0
                ? 0
                : Math.Round(
                    (TotalRecibido * 100m) / TotalSolicitado,
                    2
                );
    }

    public class MovimientoMaterialPostVm
    {
        public int DetalleID { get; set; }
        public decimal? Cantidad { get; set; }
    }

    public class ProcesarCotizacionesVm
    {
        public int SolicitudID { get; set; }

        public string? ComentariosCompras { get; set; }

        public List<CotizacionCargaItemVm> Cotizaciones { get; set; } = new();
    }

    public class CotizacionCargaItemVm
    {
        public string? Proveedor { get; set; }

        public decimal? Monto { get; set; }

        public IFormFile? Archivo { get; set; }
    }

    public class CotizacionDetalleVm
    {
        public int CotizacionID { get; set; }

        public string Proveedor { get; set; }

        public decimal MontoTotal { get; set; }

        public string ArchivoPath { get; set; }

        public string NombreArchivoOriginal { get; set; }

        public string Extension { get; set; }

        public bool EsRecomendada { get; set; }

        public string ComentariosCompras { get; set; }

        public DateTime FechaEnvioAlUsuario { get; set; }

        public bool FueSeleccionadaPorUsuario { get; set; }
    }

    public class CuentasPorPagarVm
    {
        public List<CuentasPorPagarItemVm> Pendientes { get; set; } = new();
        public List<CuentasPorPagarItemVm> Historico { get; set; } = new();
    }

    public class CuentasPorPagarItemVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Empresa { get; set; }
        public string Departamento { get; set; }
        public string Proveedor { get; set; }
        public decimal MontoTotal { get; set; }
        public string NumeroOC { get; set; }
        public DateTime? FechaEntregaUsuario { get; set; }
        public string NombreRecibeUsuario { get; set; }

        public string TipoGasto { get; set; }
        public string NumeroRequisicion { get; set; }
        public DateTime? FechaDictamen { get; set; }

        public int EstatusID { get; set; }
        public string Estatus { get; set; }
        public string? ComentariosCxP { get; set; }
        public DateTime? FechaCierreCxP { get; set; }
    }

    public class TiempoDepartamentoVm
    {
        public string Departamento { get; set; } = "";
        public string Estatus { get; set; } = "";
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int Dias { get; set; }
        public int Horas { get; set; }
    }
}
