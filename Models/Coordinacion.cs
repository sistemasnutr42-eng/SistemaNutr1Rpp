using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SistemaNutri.Models
{
    public class CoordinacionPedidosIndexVm
    {
        public CoordinacionResumenPedidosVm Resumen { get; set; } = new();
        public CoordinacionFiltroPedidosVm Filtros { get; set; } = new();
        public List<CoordinacionPedidoBandejaVm> Pedidos { get; set; } = new();
        public List<CoordinacionTipoPedidoVm> TiposPedido { get; set; } = new();
        public List<CoordinacionEstatusPedidoVm> EstatusPedido { get; set; } = new();
        public List<CoordinacionClienteVm> Clientes { get; set; } = new();
    }

    public class CoordinacionResumenPedidosVm
    {
        public int TotalPedidos { get; set; }
        public int Recibidos { get; set; }
        public int PendientesValidacion { get; set; }
        public int PendientesProgramar { get; set; }
        public int Programados { get; set; }
        public int FueraDeTiempo { get; set; }
    }

    public class CoordinacionFiltroPedidosVm
    {
        public int? TipoPedidoID { get; set; }
        public int? EstatusPedidoID { get; set; }
        public int? ClienteID { get; set; }
        [DataType(DataType.Date)]
        public DateTime? FechaDesde { get; set; }
        [DataType(DataType.Date)]
        public DateTime? FechaHasta { get; set; }
        public bool? SoloFueraDeTiempo { get; set; }
        [StringLength(150)]
        public string? Buscar { get; set; }
    }

    public class CoordinacionPedidoBandejaVm
    {
        public int PedidoID { get; set; }
        public string Folio { get; set; } = string.Empty;
        public int EmpresaID { get; set; }
        public int TipoPedidoID { get; set; }
        public string TipoPedidoCodigo { get; set; } = string.Empty;
        public string TipoPedido { get; set; } = string.Empty;
        public int? ClienteID { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public int? ClienteUbicacionID { get; set; }
        public int? CentroDestinoID { get; set; }
        public string Destino { get; set; } = string.Empty;
        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }
        public int PrioridadPedidoID { get; set; }
        public string PrioridadCodigo { get; set; } = string.Empty;
        public string Prioridad { get; set; } = string.Empty;
        public int EstatusPedidoID { get; set; }
        public string EstatusCodigo { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public string? ColorHex { get; set; }
        public string OrigenCodigo { get; set; } = string.Empty;
        public string Origen { get; set; } = string.Empty;
        public string? ReferenciaExterna { get; set; }
        public bool FueraDeTiempo { get; set; }
        public string? MotivoFueraDeTiempo { get; set; }
        public string? Observaciones { get; set; }
        public int TotalRenglones { get; set; }
        public decimal CantidadTotal { get; set; }
        public bool TieneProgramacion { get; set; }
        public int? ProgramacionID { get; set; }
        public DateTime? ProximaFechaProgramada { get; set; }
    }

    public class CoordinacionCrearPedidoVm
    {
        public int EmpresaID { get; set; }
      

        [Required(ErrorMessage = "Selecciona el tipo de pedido.")]
        [Range(1, int.MaxValue, ErrorMessage = "Selecciona el tipo de pedido.")]
        public int TipoPedidoID { get; set; }

        [Required(ErrorMessage = "Selecciona el origen del pedido.")]
        [Range(1, int.MaxValue, ErrorMessage = "Selecciona el origen del pedido.")]
        public int OrigenPedidoID { get; set; }

        public int? ClienteID { get; set; }
        public int? ClienteUbicacionID { get; set; }
        public int? CentroDestinoID { get; set; }
        public int? UsuarioSolicitanteID { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime? FechaSolicitud { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaRequerida { get; set; }

        public int? PrioridadPedidoID { get; set; }

        [StringLength(100)]
        public string? ReferenciaExterna { get; set; }

        public bool FueraDeTiempo { get; set; }

        [StringLength(500)]
        public string? MotivoFueraDeTiempo { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        [StringLength(30)]
        public string? Folio { get; set; }

        public bool GuardarComoBorrador { get; set; }

        [MinLength(1, ErrorMessage = "Agrega al menos un renglón al pedido.")]
        public List<CoordinacionPedidoRenglonCapturaVm> Detalles { get; set; } = new();

        public CoordinacionCatalogosPedidoVm Catalogos { get; set; } = new();

        public int? PedidoID { get; set; }

        public bool EsEdicion => PedidoID.HasValue && PedidoID.Value > 0;

    }

    public class CoordinacionPedidoRenglonCapturaVm
    {
        public int Renglon { get; set; }

        [Required(ErrorMessage = "Selecciona el producto.")]
        [Range(1, int.MaxValue, ErrorMessage = "Selecciona el producto.")]
        public int ProductoID { get; set; }

        public int? LineaGeneticaID { get; set; }

        [Range(typeof(decimal), "0.001", "999999999999999.999", ErrorMessage = "La cantidad debe ser mayor a cero.")]
        public decimal CantidadSolicitada { get; set; }

        [Range(typeof(decimal), "0", "999999999.999", ErrorMessage = "El peso mínimo no es válido.")]
        public decimal? PesoMinimoKg { get; set; }

        [Range(typeof(decimal), "0", "999999999.999", ErrorMessage = "El peso máximo no es válido.")]
        public decimal? PesoMaximoKg { get; set; }

        [StringLength(1000)]
        public string? Especificaciones { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaProduccionSolicitada { get; set; }

        [StringLength(1000)]
        public string? EspecificacionesDosis { get; set; }

        public int? TipoHembraID { get; set; }

        [StringLength(150)]
        public string? ReferenciaProyecto { get; set; }

        [StringLength(1000)]
        public string? CaracteristicasHembras { get; set; }

        public bool? EsImportacion { get; set; }

        [StringLength(100)]
        public string? PaisOrigen { get; set; }

        [StringLength(150)]
        public string? ReferenciaImportacion { get; set; }

        [StringLength(1000)]
        public string? CaracteristicasSemental { get; set; }

        [Range(typeof(decimal), "0.001", "999999999999999.999", ErrorMessage = "Los kilogramos solicitados deben ser mayores a cero.")]
        public decimal? KilogramosSolicitados { get; set; }

        [Range(typeof(decimal), "0.001", "999999999.999", ErrorMessage = "El peso promedio objetivo debe ser mayor a cero.")]
        public decimal? PesoPromedioObjetivoKg { get; set; }

        [StringLength(1000)]
        public string? EspecificacionesRastro { get; set; }

        public int? PedidoDetalleID { get; set; }
    }

    public class CoordinacionCatalogosPedidoVm
    {
        public List<CoordinacionTipoPedidoVm> TiposPedido { get; set; } = new();
        public List<CoordinacionPrioridadPedidoVm> Prioridades { get; set; } = new();
        public List<CoordinacionOrigenPedidoVm> Origenes { get; set; } = new();
        public List<CoordinacionClienteVm> Clientes { get; set; } = new();
        public List<CoordinacionLineaGeneticaVm> LineasGeneticas { get; set; } = new();
        public List<CoordinacionTipoHembraVm> TiposHembra { get; set; } = new();
        public List<CoordinacionCentroOperacionVm> CentrosOperacion { get; set; } = new();
        public List<CoordinacionProductoVm> Productos { get; set; } = new();
        public List<CoordinacionUbicacionClienteVm> UbicacionesCliente { get; set; } = new();
    }

    public class CoordinacionTipoPedidoVm
    {
        public int TipoPedidoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int? TipoProductoID { get; set; }
        public bool RequiereLineaGenetica { get; set; }
    }

    public class CoordinacionPrioridadPedidoVm
    {
        public int PrioridadPedidoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int Orden { get; set; }
    }

    public class CoordinacionOrigenPedidoVm
    {
        public int OrigenPedidoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    public class CoordinacionEstatusPedidoVm
    {
        public int EstatusPedidoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int Orden { get; set; }
        public bool EsFinal { get; set; }
        public string? ColorHex { get; set; }
    }

    public class CoordinacionClienteVm
    {
        public int ClienteID { get; set; }
        public string? CodigoCliente { get; set; }
        public string NombreComercial { get; set; } = string.Empty;
        public string? RazonSocial { get; set; }
        public bool EsBioHypor { get; set; }
    }

    public class CoordinacionLineaGeneticaVm
    {
        public int LineaGeneticaID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    public class CoordinacionTipoHembraVm
    {
        public int TipoHembraID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
    }

    public class CoordinacionCentroOperacionVm
    {
        public int CentroOperacionID { get; set; }
        public int EmpresaID { get; set; }
        public int TipoCentroOperacionID { get; set; }
        public string TipoCentroCodigo { get; set; } = string.Empty;
        public string TipoCentro { get; set; } = string.Empty;
        public string? Codigo { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Municipio { get; set; }
        public string? Estado { get; set; }
    }

    public class CoordinacionUbicacionClienteVm
    {
        public int ClienteUbicacionID { get; set; }
        public int ClienteID { get; set; }
        public int? TipoUbicacionClienteID { get; set; }
        public string? TipoCodigo { get; set; }
        public string? Tipo { get; set; }
        public string? Codigo { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Direccion { get; set; }
        public string? Municipio { get; set; }
        public string? Estado { get; set; }
        public string? CodigoPostal { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public string? UrlMaps { get; set; }
        public string? Contacto { get; set; }
        public string? Telefono { get; set; }
    }

    public class CoordinacionProductoVm
    {
        public int ProductoID { get; set; }
        public int TipoProductoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string? UnidadMedida { get; set; }
        public bool RequiereLineaGenetica { get; set; }
    }

    public class CoordinacionPedidoDetallePaginaVm
    {
        public CoordinacionPedidoEncabezadoVm Pedido { get; set; } = new();
        public List<CoordinacionPedidoDetalleRenglonVm> Detalles { get; set; } = new();
        public List<CoordinacionPedidoHistorialVm> Historial { get; set; } = new();
        public List<CoordinacionPedidoProgramacionVm> Programacion { get; set; } = new();
        public List<CoordinacionSolicitudCambioVm> SolicitudesCambio { get; set; } = new();
    }

    public class CoordinacionPedidoEncabezadoVm
    {
        public int PedidoID { get; set; }
        public string Folio { get; set; } = string.Empty;
        public int EmpresaID { get; set; }
        public string Empresa { get; set; } = string.Empty;
        public int TipoPedidoID { get; set; }
        public string TipoPedidoCodigo { get; set; } = string.Empty;
        public string TipoPedido { get; set; } = string.Empty;
        public int OrigenPedidoID { get; set; }
        public string OrigenCodigo { get; set; } = string.Empty;
        public string Origen { get; set; } = string.Empty;
        public int EstatusPedidoID { get; set; }
        public string EstatusCodigo { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public string? ColorHex { get; set; }
        public int PrioridadPedidoID { get; set; }
        public string PrioridadCodigo { get; set; } = string.Empty;
        public string Prioridad { get; set; } = string.Empty;
        public int? ClienteID { get; set; }
        public string? Cliente { get; set; }
        public string? RazonSocial { get; set; }
        public bool? EsBioHypor { get; set; }
        public int? ClienteUbicacionID { get; set; }
        public int? CentroDestinoID { get; set; }
        public string? Destino { get; set; }
        public string? DireccionCliente { get; set; }
        public string? MunicipioCliente { get; set; }
        public string? EstadoCliente { get; set; }
        public string? DireccionCentro { get; set; }
        public string? MunicipioCentro { get; set; }
        public string? EstadoCentro { get; set; }
        public int? UsuarioSolicitanteID { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }
        public string? ReferenciaExterna { get; set; }
        public bool FueraDeTiempo { get; set; }
        public string? MotivoFueraDeTiempo { get; set; }
        public string? Observaciones { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    public class CoordinacionPedidoDetalleRenglonVm
    {
        public int PedidoDetalleID { get; set; }
        public int Renglon { get; set; }
        public int ProductoID { get; set; }
        public string Producto { get; set; } = string.Empty;
        public int? LineaGeneticaID { get; set; }
        public string? LineaGenetica { get; set; }
        public decimal CantidadSolicitada { get; set; }
        public string? UnidadMedidaSnapshot { get; set; }
        public decimal? PesoMinimoKg { get; set; }
        public decimal? PesoMaximoKg { get; set; }
        public string? Especificaciones { get; set; }
        public string? Observaciones { get; set; }
        public DateTime? FechaProduccionSolicitada { get; set; }
        public string? EspecificacionesDosis { get; set; }
        public int? TipoHembraID { get; set; }
        public string? TipoHembraCodigo { get; set; }
        public string? TipoHembra { get; set; }
        public string? ReferenciaProyecto { get; set; }
        public string? CaracteristicasHembras { get; set; }
        public bool? EsImportacion { get; set; }
        public string? PaisOrigen { get; set; }
        public string? ReferenciaImportacion { get; set; }
        public string? CaracteristicasSemental { get; set; }
        public decimal? KilogramosSolicitados { get; set; }
        public decimal? PesoPromedioObjetivoKg { get; set; }
        public string? EspecificacionesRastro { get; set; }
        public bool Activo { get; set; }
    }

    public class CoordinacionPedidoHistorialVm
    {
        public long PedidoHistorialID { get; set; }
        public int? PedidoDetalleID { get; set; }
        public string Evento { get; set; } = string.Empty;
        public int? EstatusAnteriorID { get; set; }
        public string? EstatusAnterior { get; set; }
        public int? EstatusNuevoID { get; set; }
        public string? EstatusNuevo { get; set; }
        public string? Observaciones { get; set; }
        public string? DatosAdicionales { get; set; }
        public int? UsuarioID { get; set; }
        public string? UsuarioNombreSnapshot { get; set; }
        public DateTime FechaEvento { get; set; }
    }

    public class CoordinacionPedidoProgramacionVm
    {
        public int ProgramacionID { get; set; }
        public int Anio { get; set; }
        public int? Semana { get; set; }
        public int Version { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string EstatusProgramacionCodigo { get; set; } = string.Empty;
        public string EstatusProgramacion { get; set; } = string.Empty;
        public int ProgramacionDetalleID { get; set; }
        public int PedidoDetalleID { get; set; }
        public int? CentroOrigenID { get; set; }
        public string? CentroOrigen { get; set; }
        public DateTime? FechaProduccion { get; set; }
        public DateTime FechaEntregaProgramada { get; set; }
        public TimeSpan? HoraEntregaProgramada { get; set; }
        public decimal CantidadProgramada { get; set; }
        public int? SecuenciaEntrega { get; set; }
        public string? ReferenciaRemision { get; set; }
        public int? ProgramacionRutaID { get; set; }
        public int? RutaID { get; set; }
        public string? Ruta { get; set; }
        public DateTime? FechaRuta { get; set; }
        public TimeSpan? HoraSalidaProgramada { get; set; }
        public string EstatusDetalle { get; set; } = string.Empty;
    }

    public class CoordinacionSolicitudCambioVm
    {
        public long SolicitudCambioID { get; set; }
        public int? PedidoDetalleID { get; set; }
        public int? ProgramacionID { get; set; }
        public int? ProgramacionDetalleID { get; set; }
        public string TipoCambio { get; set; } = string.Empty;
        public bool EsFueraDeTiempo { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public string? DatosAnteriores { get; set; }
        public string? DatosNuevos { get; set; }
        public string EstatusCodigo { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public int UsuarioSolicitaID { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public int? UsuarioResuelveID { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string? ObservacionesResolucion { get; set; }
        public DateTime? FechaAplicacion { get; set; }
    }

    public class CoordinacionCambiarEstatusPedidoVm
    {
        [Required]
        [Range(1, int.MaxValue)]
        public int PedidoID { get; set; }

        [Required]
        [StringLength(40)]
        public string NuevoEstatusCodigo { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CoordinacionResultadoCrearPedidoVm
    {
        public int PedidoID { get; set; }
        public string Folio { get; set; } = string.Empty;
        public string TipoPedidoCodigo { get; set; } = string.Empty;
        public string TipoPedido { get; set; } = string.Empty;
        public string EstatusCodigo { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }
        public bool FueraDeTiempo { get; set; }
        public List<CoordinacionResultadoCrearPedidoDetalleVm> Detalles { get; set; } = new();
    }

    public class CoordinacionResultadoCrearPedidoDetalleVm
    {
        public int PedidoDetalleID { get; set; }
        public int Renglon { get; set; }
        public int ProductoID { get; set; }
        public string Producto { get; set; } = string.Empty;
        public int? LineaGeneticaID { get; set; }
        public string? LineaGenetica { get; set; }
        public decimal CantidadSolicitada { get; set; }
        public string? UnidadMedidaSnapshot { get; set; }
    }
}
