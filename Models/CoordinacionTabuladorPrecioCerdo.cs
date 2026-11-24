using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SistemaNutri.Models
{
    public class CoordinacionTabuladorPrecioCerdoPaginaVm
    {
        public CoordinacionTabuladorPrecioCerdoConfiguracionVm Configuracion { get; set; } = new();

        public CoordinacionTabuladorPrecioCerdoCapturaVm Captura { get; set; } = new();

        public CoordinacionTabuladorPrecioCerdoResultadoVm Resultado { get; set; } = new();

        public CoordinacionPrecioMercadoCerdoVm? PrecioMercadoActual { get; set; }

        public List<CoordinacionTabuladorPrecioCerdoFilaVm> Filas { get; set; } = new();

        public List<CoordinacionTabuladorPrecioCerdoConfiguracionVm> Versiones { get; set; } = new();

        public bool TieneConfiguracionVigente =>
            Configuracion != null &&
            Configuracion.TabuladorPrecioCerdoID > 0;

        public bool TienePrecioMercado =>
            PrecioMercadoActual != null &&
            PrecioMercadoActual.PrecioKg > 0;
    }

    public class CoordinacionTabuladorPrecioCerdoConfiguracionVm
    {
        public int TabuladorPrecioCerdoID { get; set; }

        [Required(ErrorMessage = "El código es obligatorio.")]
        [StringLength(50)]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha de inicio de vigencia es obligatoria.")]
        [DataType(DataType.Date)]
        public DateTime VigenciaDesde { get; set; } = DateTime.Today;

        [DataType(DataType.Date)]
        public DateTime? VigenciaHasta { get; set; }

        [Required(ErrorMessage = "El peso base es obligatorio.")]
        [Range(typeof(decimal), "0.01", "999999.99",
            ErrorMessage = "El peso base debe ser mayor a cero.")]
        [Display(Name = "Peso base")]
        public decimal PesoBaseKg { get; set; } = 115m;

        [Required(ErrorMessage = "El precio base destino es obligatorio.")]
        [Range(typeof(decimal), "0.01", "999999.99",
            ErrorMessage = "El precio base destino debe ser mayor a cero.")]
        [Display(Name = "Precio base destino")]
        public decimal PrecioBaseDestinoKg { get; set; } = 29m;

        [Required(ErrorMessage = "El ajuste por kilogramo es obligatorio.")]
        [Range(typeof(decimal), "0.00", "999999.99",
            ErrorMessage = "El ajuste por kilogramo no es válido.")]
        [Display(Name = "Ajuste por kg")]
        public decimal AjustePorKg { get; set; } = 0.20m;

        [Required(ErrorMessage = "El peso mínimo es obligatorio.")]
        [Range(typeof(decimal), "0.01", "999999.99",
            ErrorMessage = "El peso mínimo debe ser mayor a cero.")]
        [Display(Name = "Peso mínimo")]
        public decimal PesoMinimoKg { get; set; } = 70m;

        [Required(ErrorMessage = "El peso máximo es obligatorio.")]
        [Range(typeof(decimal), "0.01", "999999.99",
            ErrorMessage = "El peso máximo debe ser mayor a cero.")]
        [Display(Name = "Peso máximo")]
        public decimal PesoMaximoKg { get; set; } = 115m;

        [Display(Name = "Incrementar precio arriba del peso base")]
        public bool AjustarSobrePesoBase { get; set; }

        [Required]
        [StringLength(10)]
        public string MonedaCodigo { get; set; } = "MXN";

        public bool Activo { get; set; } = true;

        public int UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public string VigenciaTexto
        {
            get
            {
                if (!VigenciaHasta.HasValue)
                    return $"Desde {VigenciaDesde:dd/MM/yyyy}";

                return $"{VigenciaDesde:dd/MM/yyyy} - {VigenciaHasta:dd/MM/yyyy}";
            }
        }

        public bool EsVigente
        {
            get
            {
                var hoy = DateTime.Today;

                return Activo &&
                       VigenciaDesde.Date <= hoy &&
                       (!VigenciaHasta.HasValue ||
                        VigenciaHasta.Value.Date >= hoy);
            }
        }
    }

    public class CoordinacionTabuladorPrecioCerdoCapturaVm
    {
        public int? PedidoID { get; set; }

        public int? PedidoDetalleID { get; set; }

        public int? CotizacionRastroID { get; set; }

        public int? TabuladorPrecioCerdoID { get; set; }

        [Required(ErrorMessage = "La fecha de cotización es obligatoria.")]
        [DataType(DataType.Date)]
        [Display(Name = "Fecha de cotización")]
        public DateTime FechaCotizacion { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "El número de cerdos es obligatorio.")]
        [Range(1, 1000000,
            ErrorMessage = "El número de cerdos debe ser mayor a cero.")]
        [Display(Name = "Número de cerdos")]
        public int NumeroCerdos { get; set; }

        [Required(ErrorMessage = "El peso ofrecido es obligatorio.")]
        [Range(typeof(decimal), "0.01", "999999.99",
            ErrorMessage = "El peso ofrecido debe ser mayor a cero.")]
        [Display(Name = "Peso ofrecido")]
        public decimal PesoOfrecidoKg { get; set; } = 115m;

        [Range(typeof(decimal), "0.01", "999999.99",
            ErrorMessage = "El peso real debe ser mayor a cero.")]
        [Display(Name = "Peso real")]
        public decimal? PesoRealKg { get; set; }

        [Range(typeof(decimal), "0", "999999999999.99",
            ErrorMessage = "El flete total no es válido.")]
        [Display(Name = "Flete total")]
        public decimal FleteTotal { get; set; }

        [Range(0, 1000000,
            ErrorMessage = "Los animales entregados no son válidos.")]
        [Display(Name = "Animales entregados")]
        public int? NumeroCerdosReales { get; set; }

        [StringLength(500)]
        [Display(Name = "Observaciones")]
        public string? Observaciones { get; set; }

        public bool GuardarCotizacion { get; set; }
    }

    public class CoordinacionTabuladorPrecioCerdoResultadoVm
    {
        public int? PedidoID { get; set; }

        public int? PedidoDetalleID { get; set; }

        public int? CotizacionRastroID { get; set; }

        public int TabuladorPrecioCerdoID { get; set; }

        public DateTime FechaCotizacion { get; set; }

        public int NumeroCerdos { get; set; }

        public decimal PesoBaseKg { get; set; }

        public decimal PesoOfrecidoKg { get; set; }

        public decimal? PesoRealKg { get; set; }

        public int? NumeroCerdosReales { get; set; }

        public decimal PrecioBaseDestinoKg { get; set; }

        public decimal AjustePorKg { get; set; }

        public decimal FleteTotal { get; set; }

        public decimal KilosEstimados { get; set; }

        public decimal? KilosReales { get; set; }

        public decimal FletePorCerdo { get; set; }

        public decimal FletePorKgEstimado { get; set; }

        public decimal? FletePorKgReal { get; set; }

        public decimal PrecioDestinoCalculadoKg { get; set; }

        public decimal PrecioOrigenCalculadoKg { get; set; }

        public decimal? PrecioDestinoRealKg { get; set; }

        public decimal? PrecioOrigenRealKg { get; set; }

        public decimal ImporteDestinoEstimado { get; set; }

        public decimal ImporteOrigenEstimado { get; set; }

        public decimal? ImporteDestinoReal { get; set; }

        public decimal? ImporteOrigenReal { get; set; }

        public decimal? PrecioMercadoKg { get; set; }

        public DateTime? FechaPrecioMercado { get; set; }

        public decimal? DiferenciaVsMercadoKg { get; set; }

        public decimal? DiferenciaVsMercadoPorcentaje { get; set; }

        public bool PrecioMercadoEsDelDia { get; set; }

        public string? Observaciones { get; set; }

        public decimal DiferenciaPesoVsBaseKg =>
            PesoOfrecidoKg - PesoBaseKg;

        public decimal? DiferenciaPesoRealVsBaseKg =>
            PesoRealKg.HasValue
                ? PesoRealKg.Value - PesoBaseKg
                : null;

        public bool PesoOfrecidoDebajoBase =>
            PesoOfrecidoKg < PesoBaseKg;

        public bool PesoOfrecidoEnBase =>
            PesoOfrecidoKg == PesoBaseKg;

        public bool PesoOfrecidoSobreBase =>
            PesoOfrecidoKg > PesoBaseKg;

        public string EstadoPesoOfrecido
        {
            get
            {
                if (PesoOfrecidoKg < PesoBaseKg)
                    return "DEBAJO_BASE";

                if (PesoOfrecidoKg > PesoBaseKg)
                    return "SOBRE_BASE";

                return "EN_BASE";
            }
        }

        public string EstadoMercado
        {
            get
            {
                if (!PrecioMercadoKg.HasValue)
                    return "SIN_MERCADO";

                if (PrecioDestinoCalculadoKg < PrecioMercadoKg.Value)
                    return "DEBAJO_MERCADO";

                if (PrecioDestinoCalculadoKg > PrecioMercadoKg.Value)
                    return "SOBRE_MERCADO";

                return "IGUAL_MERCADO";
            }
        }
    }

    public class CoordinacionTabuladorPrecioCerdoFilaVm
    {
        public decimal PesoKg { get; set; }

        public decimal DiferenciaPesoKg { get; set; }

        public decimal AjusteTotal { get; set; }

        public decimal PrecioDestinoKg { get; set; }

        public decimal PrecioOrigenKg { get; set; }

        public decimal? PrecioMercadoKg { get; set; }

        public decimal? DiferenciaDestinoMercadoKg { get; set; }

        public decimal? DiferenciaOrigenMercadoKg { get; set; }

        public bool EsPesoBase { get; set; }

        public bool EstaDebajoPesoBase { get; set; }

        public bool EstaSobrePesoBase { get; set; }

        public string TipoFila
        {
            get
            {
                if (EsPesoBase)
                    return "BASE";

                if (EstaDebajoPesoBase)
                    return "DEBAJO";

                if (EstaSobrePesoBase)
                    return "SOBRE";

                return "NORMAL";
            }
        }
    }

    public class CoordinacionTabuladorPrecioCerdoGuardarConfiguracionVm
    {
        public int? TabuladorPrecioCerdoID { get; set; }

        [Required]
        [StringLength(50)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Date)]
        public DateTime VigenciaDesde { get; set; }

        [DataType(DataType.Date)]
        public DateTime? VigenciaHasta { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999.99")]
        public decimal PesoBaseKg { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999.99")]
        public decimal PrecioBaseDestinoKg { get; set; }

        [Required]
        [Range(typeof(decimal), "0.00", "999999.99")]
        public decimal AjustePorKg { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999.99")]
        public decimal PesoMinimoKg { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999.99")]
        public decimal PesoMaximoKg { get; set; }

        public bool AjustarSobrePesoBase { get; set; }

        [Required]
        [StringLength(10)]
        public string MonedaCodigo { get; set; } = "MXN";

        public bool Activo { get; set; } = true;
    }

    public class CoordinacionTabuladorPrecioCerdoCotizacionVm
    {
        public int CotizacionRastroID { get; set; }

        public int PedidoID { get; set; }

        public int? PedidoDetalleID { get; set; }

        public int TabuladorPrecioCerdoID { get; set; }

        public DateTime FechaCotizacion { get; set; }

        public int NumeroCerdos { get; set; }

        public int? NumeroCerdosReales { get; set; }

        public decimal PesoOfrecidoKg { get; set; }

        public decimal? PesoRealKg { get; set; }

        public decimal FleteTotal { get; set; }

        public decimal FletePorCerdo { get; set; }

        public decimal FletePorKgEstimado { get; set; }

        public decimal? FletePorKgReal { get; set; }

        public decimal PrecioBaseDestinoKg { get; set; }

        public decimal AjustePorKg { get; set; }

        public decimal PrecioDestinoCalculadoKg { get; set; }

        public decimal PrecioOrigenCalculadoKg { get; set; }

        public decimal? PrecioDestinoRealKg { get; set; }

        public decimal? PrecioOrigenRealKg { get; set; }

        public decimal KilosEstimados { get; set; }

        public decimal? KilosReales { get; set; }

        public decimal ImporteDestinoEstimado { get; set; }

        public decimal ImporteOrigenEstimado { get; set; }

        public decimal? ImporteDestinoReal { get; set; }

        public decimal? ImporteOrigenReal { get; set; }

        public decimal? PrecioMercadoKg { get; set; }

        public DateTime? FechaPrecioMercado { get; set; }

        public string? Observaciones { get; set; }

        public string EstatusCodigo { get; set; } = string.Empty;

        public int UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }
    }

    public class CoordinacionTabuladorPrecioCerdoResumenVm
    {
        public decimal PesoBaseKg { get; set; }

        public decimal PrecioBaseDestinoKg { get; set; }

        public decimal AjustePorKg { get; set; }

        public decimal PesoMinimoKg { get; set; }

        public decimal PesoMaximoKg { get; set; }

        public decimal? PrecioMercadoKg { get; set; }

        public DateTime? FechaPrecioMercado { get; set; }

        public decimal? DiferenciaPrecioBaseVsMercado { get; set; }

        public decimal? DiferenciaPrecioBaseVsMercadoPorcentaje { get; set; }

        public bool TienePrecioMercado =>
            PrecioMercadoKg.HasValue &&
            PrecioMercadoKg.Value > 0;
    }

    public class CoordinacionTabuladorPrecioCerdoCalculoRequestVm
    {
        [Required]
        [Range(1, 1000000)]
        public int NumeroCerdos { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999.99")]
        public decimal PesoOfrecidoKg { get; set; }

        [Range(typeof(decimal), "0.01", "999999.99")]
        public decimal? PesoRealKg { get; set; }

        [Range(typeof(decimal), "0", "999999999999.99")]
        public decimal FleteTotal { get; set; }

        [Range(0, 1000000)]
        public int? NumeroCerdosReales { get; set; }

        public int? PedidoID { get; set; }

        public int? PedidoDetalleID { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaCotizacion { get; set; }
    }

    public class CoordinacionTabuladorPrecioCerdoCalculoResponseVm
    {
        public bool Ok { get; set; }

        public string Mensaje { get; set; } = string.Empty;

        public CoordinacionTabuladorPrecioCerdoResultadoVm Resultado { get; set; } = new();

        public List<CoordinacionTabuladorPrecioCerdoFilaVm> Filas { get; set; } = new();
    }
}