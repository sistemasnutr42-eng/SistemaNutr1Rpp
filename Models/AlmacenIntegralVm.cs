using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace SistemaNutri.Models;

public sealed class AlmSedeVm
{
    public int SedeID { get; set; }
    public string Codigo { get; set; } = "";
    public string Grupo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
}
public sealed class AlmArticuloVm
{
    public int ArticuloID { get; set; }
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Unidad { get; set; } = "";
    public decimal Existencia { get; set; }
    public decimal? InventarioHistorico { get; set; }
    public decimal PuntoReorden { get; set; }
}
public sealed class AlmMovimientoVm
{
    public long MovimientoID { get; set; }
    public DateTime FechaUTC { get; set; }
    public string Sede { get; set; } = "";
    public string Producto { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Tipo { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Usuario { get; set; } = "";
    public Guid OperacionID { get; set; }
}
public sealed class AlmReferenciaVm
{
    public long ReferenciaID { get; set; }
    public string Archivo { get; set; } = "";
    public string Hoja { get; set; } = "";
    public int FilaExcel { get; set; }
    public string Tipo { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string Unidad { get; set; } = "";
    public DateTime? Fecha { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? Entradas { get; set; }
    public decimal? Salidas { get; set; }
    public decimal? Saldo { get; set; }
    public string Nota { get; set; } = "";
}
public sealed class AlmResumenTipoVm
{
    public string Tipo { get; set; } = "";
    public int Registros { get; set; }
}
public sealed class AlmMovimientoForm
{
    public Guid OperacionID { get; set; }
    public int SedeID { get; set; }
    public int? DestinoSedeID { get; set; }
    public int ArticuloID { get; set; }
    public string Tipo { get; set; } = "ENTRADA";
    public decimal Cantidad { get; set; }
    public string Referencia { get; set; } = "";
    public string? Observaciones { get; set; }
    public IFormFile? Evidencia { get; set; }
}
public sealed class AlmEvidenciaVm
{
    public string ArchivoInterno { get; set; } = "";
    public string NombreOriginal { get; set; } = "";
    public string Mime { get; set; } = "application/octet-stream";
}
public sealed class AlmEvidenciaListaVm
{
    public long EvidenciaID { get; set; }
    public string NombreOriginal { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Usuario { get; set; } = "";
    public DateTime FechaUTC { get; set; }
}
public sealed class AlmDashboardVm
{
    public string Seccion { get; set; } = "Index";
    public string Titulo { get; set; } = "Centro de Almacen";
    public string Subtitulo { get; set; } = "BSP, GPN y Nutriservicios en un mismo espacio de trabajo";
    public string? Buscar { get; set; }
    public string? TipoFiltro { get; set; }
    public int? SedeFiltro { get; set; }
    public string GrupoFiltro { get; set; } = "Todos";
    public bool PuedeRegistrar { get; set; }
    public int CantidadSedes { get; set; }
    public int CantidadArticulos { get; set; }
    public int CantidadReferencias { get; set; }
    public int CantidadMovimientos { get; set; }
    public int CantidadAlertas { get; set; }
    public Guid ProximaOperacion { get; set; } = Guid.NewGuid();
    public List<AlmSedeVm> Sedes { get; set; } = new();
    public List<AlmArticuloVm> Articulos { get; set; } = new();
    public List<AlmMovimientoVm> Movimientos { get; set; } = new();
    public List<AlmReferenciaVm> Referencias { get; set; } = new();
    public List<AlmResumenTipoVm> Resumen { get; set; } = new();
    public List<AlmEvidenciaListaVm> Evidencias { get; set; } = new();
}
