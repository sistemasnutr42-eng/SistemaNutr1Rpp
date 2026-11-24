using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SistemaNutri.Models
{
    public class CoordinacionPrecioMercadoCerdoPaginaVm
    {
        public CoordinacionPrecioMercadoCerdoVm? PrecioActual { get; set; }
        public CoordinacionPrecioMercadoCerdoCapturaVm Captura { get; set; } = new();
        public CoordinacionPrecioMercadoCerdoResumenVm Resumen { get; set; } = new();
        public CoordinacionPrecioMercadoCerdoFiltroVm Filtros { get; set; } = new();
        public List<CoordinacionPrecioMercadoCerdoHistoricoVm> Historico { get; set; } = new();
        public List<int> AniosDisponibles { get; set; } = new();
    }

    public class CoordinacionPrecioMercadoCerdoVm
    {
        public int PrecioMercadoCerdoID { get; set; }

        [DataType(DataType.Date)]
        public DateTime Fecha { get; set; }

        [Display(Name = "Precio por kg")]
        public decimal PrecioKg { get; set; }

        public string FuenteCodigo { get; set; } = string.Empty;

        public string? ContactoReferencia { get; set; }

        public string? Observaciones { get; set; }

        public int UsuarioCapturaID { get; set; }

        public DateTime FechaCaptura { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public bool EsPrecioDelDia { get; set; }

        public bool Activo { get; set; }

        public string FuenteNombre =>
            FuenteCodigo?.Trim().ToUpperInvariant() switch
            {
                "LLAMADA" => "Llamada",
                "MERCADO" => "Mercado",
                "MANUAL" => "Captura manual",
                _ => FuenteCodigo ?? string.Empty
            };
    }

    public class CoordinacionPrecioMercadoCerdoCapturaVm
    {
        public int? PrecioMercadoCerdoID { get; set; }

        [Required(ErrorMessage = "La fecha es obligatoria.")]
        [DataType(DataType.Date)]
        [Display(Name = "Fecha")]
        public DateTime Fecha { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "El precio por kilogramo es obligatorio.")]
        [Range(typeof(decimal), "0.01", "999999.99", ErrorMessage = "El precio por kilogramo debe ser mayor a cero.")]
        [Display(Name = "Precio por kg")]
        public decimal PrecioKg { get; set; }

        [StringLength(150, ErrorMessage = "La referencia del contacto no puede exceder 150 caracteres.")]
        [Display(Name = "Persona consultada")]
        public string? ContactoReferencia { get; set; }

        [StringLength(500, ErrorMessage = "Las observaciones no pueden exceder 500 caracteres.")]
        [Display(Name = "Observaciones")]
        public string? Observaciones { get; set; }

        [StringLength(500, ErrorMessage = "El motivo del cambio no puede exceder 500 caracteres.")]
        [Display(Name = "Motivo del cambio")]
        public string? MotivoCambio { get; set; }

        public bool EsEdicion { get; set; }
    }

    public class CoordinacionPrecioMercadoCerdoFiltroVm
    {
        [Range(2000, 2100, ErrorMessage = "El año seleccionado no es válido.")]
        public int? Anio { get; set; }

        [Range(1, 12, ErrorMessage = "El mes seleccionado no es válido.")]
        public int? Mes { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaDesde { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaHasta { get; set; }

        public void Normalizar()
        {
            if (Anio.HasValue && (Anio.Value < 2000 || Anio.Value > 2100))
                Anio = null;

            if (Mes.HasValue && (Mes.Value < 1 || Mes.Value > 12))
                Mes = null;

            FechaDesde = FechaDesde?.Date;
            FechaHasta = FechaHasta?.Date;
        }
    }

    public class CoordinacionPrecioMercadoCerdoHistoricoVm
    {
        public int PrecioMercadoCerdoID { get; set; }

        [DataType(DataType.Date)]
        public DateTime Fecha { get; set; }

        public string DiaSemana { get; set; } = string.Empty;

        public decimal PrecioKg { get; set; }

        public decimal? PrecioAnterior { get; set; }

        public decimal? VariacionPrecio { get; set; }

        public decimal? VariacionPorcentaje { get; set; }

        public string FuenteCodigo { get; set; } = string.Empty;

        public string? ContactoReferencia { get; set; }

        public string? Observaciones { get; set; }

        public int UsuarioCapturaID { get; set; }

        public DateTime FechaCaptura { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public bool Subio =>
            VariacionPrecio.HasValue &&
            VariacionPrecio.Value > 0;

        public bool Bajo =>
            VariacionPrecio.HasValue &&
            VariacionPrecio.Value < 0;

        public bool SinCambio =>
            VariacionPrecio.HasValue &&
            VariacionPrecio.Value == 0;

        public string TipoVariacion =>
            !VariacionPrecio.HasValue
                ? "SIN_REFERENCIA"
                : VariacionPrecio.Value > 0
                    ? "SUBIO"
                    : VariacionPrecio.Value < 0
                        ? "BAJO"
                        : "SIN_CAMBIO";
    }

    public class CoordinacionPrecioMercadoCerdoResumenVm
    {
        public int Anio { get; set; }

        public int DiasCapturados { get; set; }

        public decimal? PrecioMinimo { get; set; }

        public decimal? PrecioMaximo { get; set; }

        public decimal? PrecioPromedio { get; set; }

        public decimal? PrecioInicioAnio { get; set; }

        public decimal? PrecioUltimo { get; set; }

        public DateTime? FechaUltimoPrecio { get; set; }

        public decimal? VariacionAnual =>
            PrecioInicioAnio.HasValue &&
            PrecioUltimo.HasValue
                ? PrecioUltimo.Value - PrecioInicioAnio.Value
                : null;

        public decimal? VariacionAnualPorcentaje =>
            PrecioInicioAnio.HasValue &&
            PrecioInicioAnio.Value != 0 &&
            PrecioUltimo.HasValue
                ? ((PrecioUltimo.Value - PrecioInicioAnio.Value) / PrecioInicioAnio.Value) * 100m
                : null;
    }

    public class CoordinacionPrecioMercadoCerdoHistorialCambioVm
    {
        public long PrecioMercadoCerdoHistorialID { get; set; }

        public int PrecioMercadoCerdoID { get; set; }

        [DataType(DataType.Date)]
        public DateTime Fecha { get; set; }

        public decimal? PrecioAnterior { get; set; }

        public decimal PrecioNuevo { get; set; }

        public string? Motivo { get; set; }

        public int UsuarioID { get; set; }

        public string? Usuario { get; set; }

        public DateTime FechaMovimiento { get; set; }

        public decimal? Diferencia =>
            PrecioAnterior.HasValue
                ? PrecioNuevo - PrecioAnterior.Value
                : null;
    }

    public class CoordinacionPrecioMercadoCerdoResultadoGuardarVm
    {
        public int PrecioMercadoCerdoID { get; set; }

        public DateTime Fecha { get; set; }

        public decimal PrecioKg { get; set; }

        public string FuenteCodigo { get; set; } = string.Empty;

        public string? ContactoReferencia { get; set; }

        public string? Observaciones { get; set; }

        public int UsuarioCapturaID { get; set; }

        public DateTime FechaCaptura { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; }
    }

    public class CoordinacionPrecioMercadoCerdoGraficaVm
    {
        public DateTime Fecha { get; set; }

        public decimal PrecioKg { get; set; }

        public string EtiquetaFecha => Fecha.ToString("dd/MM/yyyy");
    }

    public class CoordinacionPrecioMercadoCerdoMesVm
    {
        public int Numero { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public decimal? PrecioMinimo { get; set; }

        public decimal? PrecioMaximo { get; set; }

        public decimal? PrecioPromedio { get; set; }

        public decimal? PrecioInicio { get; set; }

        public decimal? PrecioFin { get; set; }

        public int DiasCapturados { get; set; }

        public decimal? Variacion =>
            PrecioInicio.HasValue &&
            PrecioFin.HasValue
                ? PrecioFin.Value - PrecioInicio.Value
                : null;
    }
}