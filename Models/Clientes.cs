using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SistemaNutri.Models
{
    public class ClientesIndexVm
    {
        public int EmpresaID { get; set; }
        public string EmpresaNombre { get; set; } = string.Empty;
        public ClientesFiltroVm Filtros { get; set; } = new();
        public List<ClientesCatalogoItemVm> Clientes { get; set; } = new();
        public ClientesCatalogosVm Catalogos { get; set; } = new();
        public bool PuedeCrear { get; set; }
        public bool PuedeEditar { get; set; }
        public bool PuedeEliminar { get; set; }
    }

    public class ClientesFiltroVm
    {
        [StringLength(200)]
        public string? Buscar { get; set; }
        public bool IncluirInactivos { get; set; }
    }

    public class ClientesCatalogoItemVm
    {
        public int ClienteID { get; set; }
        public string? CodigoCliente { get; set; }
        public string NombreComercial { get; set; } = string.Empty;
        public string? RazonSocial { get; set; }
        public string? RFC { get; set; }
        public bool EsBioHypor { get; set; }
        public string? Telefono { get; set; }
        public string? Correo { get; set; }
        public string? Observaciones { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public int TotalUbicaciones { get; set; }
        public int TotalAlias { get; set; }
        public string? Empresas { get; set; }
    }

    public class ClientesCatalogosVm
    {
        public List<ClientesEmpresaVm> Empresas { get; set; } = new();
        public List<ClientesTipoUbicacionVm> TiposUbicacion { get; set; } = new();
    }

    public class ClientesDetallePaginaVm
    {
        public ClientesDetalleVm Cliente { get; set; } = new();
        public List<ClientesEmpresaVm> Empresas { get; set; } = new();
        public List<ClientesUbicacionVm> Ubicaciones { get; set; } = new();
        public List<ClientesAliasVm> Alias { get; set; } = new();
        public List<ClientesTipoUbicacionVm> TiposUbicacion { get; set; } = new();
        public bool PuedeEditar { get; set; }
        public bool PuedeEliminar { get; set; }
    }

    public class ClientesDetalleVm
    {
        public int ClienteID { get; set; }
        public string? CodigoCliente { get; set; }
        public string NombreComercial { get; set; } = string.Empty;
        public string? RazonSocial { get; set; }
        public string? RFC { get; set; }
        public bool EsBioHypor { get; set; }
        public string? Telefono { get; set; }
        public string? Correo { get; set; }
        public string? Observaciones { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    public class ClientesEmpresaVm
    {
        public int EmpresaID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool EsEmpresaActual { get; set; }
        public bool Seleccionada { get; set; }
    }

    public class ClientesTipoUbicacionVm
    {
        public int TipoUbicacionClienteID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public bool Activo { get; set; }
    }

    public class ClientesUbicacionVm
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
        public string? Correo { get; set; }
        public string? Observaciones { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    public class ClientesAliasVm
    {
        public int ClienteAliasID { get; set; }
        public int ClienteID { get; set; }
        public string Alias { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    public class ClientesGuardarVm
    {
        public int? ClienteID { get; set; }

        [StringLength(50, ErrorMessage = "El código no puede exceder 50 caracteres.")]
        public string? CodigoCliente { get; set; }

        [Required(ErrorMessage = "El nombre comercial es obligatorio.")]
        [StringLength(200, ErrorMessage = "El nombre comercial no puede exceder 200 caracteres.")]
        public string NombreComercial { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "La razón social no puede exceder 200 caracteres.")]
        public string? RazonSocial { get; set; }

        [StringLength(20, ErrorMessage = "El RFC no puede exceder 20 caracteres.")]
        public string? RFC { get; set; }

        public bool EsBioHypor { get; set; }

        [StringLength(30, ErrorMessage = "El teléfono no puede exceder 30 caracteres.")]
        public string? Telefono { get; set; }

        [StringLength(150, ErrorMessage = "El correo no puede exceder 150 caracteres.")]
        [EmailAddress(ErrorMessage = "El correo electrónico no tiene un formato válido.")]
        public string? Correo { get; set; }

        [StringLength(500, ErrorMessage = "Las observaciones no pueden exceder 500 caracteres.")]
        public string? Observaciones { get; set; }

        public List<int> EmpresasSeleccionadas { get; set; } = new();
    }

    public class ClientesGuardarUbicacionVm
    {
        public int? ClienteUbicacionID { get; set; }

        [Required(ErrorMessage = "El cliente es obligatorio.")]
        [Range(1, int.MaxValue, ErrorMessage = "El cliente no es válido.")]
        public int ClienteID { get; set; }

        public int? TipoUbicacionClienteID { get; set; }

        [StringLength(50, ErrorMessage = "El código no puede exceder 50 caracteres.")]
        public string? Codigo { get; set; }

        [Required(ErrorMessage = "El nombre de la ubicación es obligatorio.")]
        [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres.")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(300, ErrorMessage = "La dirección no puede exceder 300 caracteres.")]
        public string? Direccion { get; set; }

        [StringLength(100, ErrorMessage = "El municipio no puede exceder 100 caracteres.")]
        public string? Municipio { get; set; }

        [StringLength(100, ErrorMessage = "El estado no puede exceder 100 caracteres.")]
        public string? Estado { get; set; }

        [StringLength(10, ErrorMessage = "El código postal no puede exceder 10 caracteres.")]
        public string? CodigoPostal { get; set; }

        [Range(typeof(decimal), "-90", "90", ErrorMessage = "La latitud debe estar entre -90 y 90.")]
        public decimal? Latitud { get; set; }

        [Range(typeof(decimal), "-180", "180", ErrorMessage = "La longitud debe estar entre -180 y 180.")]
        public decimal? Longitud { get; set; }

        [StringLength(500, ErrorMessage = "La URL no puede exceder 500 caracteres.")]
        public string? UrlMaps { get; set; }

        [StringLength(150, ErrorMessage = "El contacto no puede exceder 150 caracteres.")]
        public string? Contacto { get; set; }

        [StringLength(30, ErrorMessage = "El teléfono no puede exceder 30 caracteres.")]
        public string? Telefono { get; set; }

        [StringLength(150, ErrorMessage = "El correo no puede exceder 150 caracteres.")]
        [EmailAddress(ErrorMessage = "El correo electrónico no tiene un formato válido.")]
        public string? Correo { get; set; }

        [StringLength(500, ErrorMessage = "Las observaciones no pueden exceder 500 caracteres.")]
        public string? Observaciones { get; set; }
    }

    public class ClientesGuardarAliasVm
    {
        public int? ClienteAliasID { get; set; }

        [Required(ErrorMessage = "El cliente es obligatorio.")]
        [Range(1, int.MaxValue, ErrorMessage = "El cliente no es válido.")]
        public int ClienteID { get; set; }

        [Required(ErrorMessage = "El alias es obligatorio.")]
        [StringLength(200, ErrorMessage = "El alias no puede exceder 200 caracteres.")]
        public string Alias { get; set; } = string.Empty;
    }

    public class ClientesCambiarActivoVm
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "El cliente no es válido.")]
        public int ClienteID { get; set; }

        public bool Activo { get; set; }
    }

    public class ClientesCambiarActivoUbicacionVm
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "La ubicación no es válida.")]
        public int ClienteUbicacionID { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "El cliente no es válido.")]
        public int ClienteID { get; set; }

        public bool Activo { get; set; }
    }

    public class ClientesCambiarActivoAliasVm
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "El alias no es válido.")]
        public int ClienteAliasID { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "El cliente no es válido.")]
        public int ClienteID { get; set; }

        public bool Activo { get; set; }
    }

    public class ClientesBusquedaVinculoVm
    {
        public int ClienteID { get; set; }
        public string? CodigoCliente { get; set; }
        public string NombreComercial { get; set; } = string.Empty;
        public string? RazonSocial { get; set; }
        public bool EsBioHypor { get; set; }
        public string? NombreCoincidente { get; set; }
        public string? TipoNombre { get; set; }
    }

    public class ClientesGuardarResultadoVm
    {
        public int ClienteID { get; set; }
        public string? CodigoCliente { get; set; }
        public string NombreComercial { get; set; } = string.Empty;
        public string? RazonSocial { get; set; }
        public string? RFC { get; set; }
        public bool EsBioHypor { get; set; }
        public string? Telefono { get; set; }
        public string? Correo { get; set; }
        public string? Observaciones { get; set; }
        public bool Activo { get; set; }
    }

    public class ClientesCambiarActivoResultadoVm
    {
        public int ClienteID { get; set; }
        public string NombreComercial { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }

    public class ClientesCambiarActivoUbicacionResultadoVm
    {
        public int ClienteUbicacionID { get; set; }
        public int ClienteID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }

    public class ClientesCambiarActivoAliasResultadoVm
    {
        public int ClienteAliasID { get; set; }
        public int ClienteID { get; set; }
        public string Alias { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }
}