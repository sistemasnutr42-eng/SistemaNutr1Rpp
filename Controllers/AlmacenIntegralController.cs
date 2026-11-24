using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using SistemaNutri.Models;

namespace SistemaNutri.Controllers;

[Route("AlmacenIntegral")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenIntegralController : Controller
{
    private readonly string _connectionString;
    private readonly ILogger<AlmacenIntegralController> _logger;
    private readonly IWebHostEnvironment _env;

    public AlmacenIntegralController(IConfiguration config, ILogger<AlmacenIntegralController> logger, IWebHostEnvironment env)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta DefaultConnection.");
        _logger = logger;
        _env = env;
    }

    private int UsuarioID => HttpContext.Session.GetInt32("UsuarioID") ?? 0;
    private int EmpresaID
    {
        get
        {
            int? id = HttpContext.Session.GetInt32("EmpresaID");
            if (id.HasValue && id.Value > 0) return id.Value;
            var raw = HttpContext.Session.GetString("EmpresaId") ?? HttpContext.Session.GetString("EmpresaID");
            return int.TryParse(raw, out var parsed) ? parsed : 0;
        }
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private async Task<(IActionResult? Error, bool Edita)> ValidarAsync(SqlConnection connection, CancellationToken ct)
    {
        if (UsuarioID <= 0) return (RedirectToAction("Login", "Login"), false);
        if (EmpresaID <= 0) return (RedirectToAction("Index", "Menu"), false);

        // Explicit warehouse grant AND active assignment to the current company.
        const string q = @"
SELECT TOP 1 AO.PuedeRegistrar
FROM dbo.Alm_Operadores AO
JOIN dbo.Usuarios U ON U.UsuarioID=AO.UsuarioID AND U.Activo=1
JOIN dbo.UsuariosEmpresas UE ON UE.UsuarioID=AO.UsuarioID AND UE.EmpresaID=AO.EmpresaID AND UE.Activo=1
WHERE AO.UsuarioID=@UsuarioID AND AO.EmpresaID=@EmpresaID AND AO.PuedeVer=1;";
        var granted = await connection.QueryFirstOrDefaultAsync<bool?>(new CommandDefinition(q,
            new { UsuarioID, EmpresaID }, cancellationToken: ct));
        if (!granted.HasValue)
            return (StatusCode(StatusCodes.Status403Forbidden,
                "Tu usuario no tiene acceso al modulo Almacen para la empresa activa."), false);
        return (null, granted.Value);
    }

    private static readonly Dictionary<string, (string Title, string Subtitle)> Sections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Index"] = ("Centro de almacen", "Vista general de BSP, GPN y Nutriservicios"),
        ["Inventario"] = ("Inventario y catalogo", "Existencia operativa confirmada vs. referencia historica de Excel"),
        ["Movimientos"] = ("Movimientos de almacen", "Entradas, salidas, transferencias y ajustes con trazabilidad"),
        ["Recepciones"] = ("Recepciones y evidencias", "Entradas documentadas y enlace con Compras"),
        ["Bsp"] = ("BSP | Sementales y cuarentena", "Consumos, insumos y ubicaciones de Tlaxcala"),
        ["Dosis"] = ("Dosis y entregas", "Remisiones y geneticas del libro de entregas"),
        ["Alimento"] = ("Alimento y consumos", "Dietas, movimientos y existencias historicas"),
        ["Animales"] = ("Animales y produccion", "Indicadores semanales de inventario y produccion"),
        ["Referencias"] = ("Historial de Excel", "Observaciones importadas con libro, hoja y fila de origen"),
        ["Indicadores"] = ("Indicadores de almacen", "Cobertura de referencias, movimientos y alertas operativas")
    };

    private async Task<AlmDashboardVm> CrearModeloAsync(SqlConnection db, string seccion,
        string? buscar, string? tipo, int? sedeId, CancellationToken ct)
    {
        if (!Sections.TryGetValue(seccion, out var labels))
            labels = Sections["Index"];
        var vm = new AlmDashboardVm
        {
            Seccion = seccion, Titulo = labels.Title, Subtitulo = labels.Subtitle,
            Buscar = buscar?.Trim(), TipoFiltro = tipo?.Trim(), SedeFiltro = sedeId
        };
        vm.Sedes = (await db.QueryAsync<AlmSedeVm>(new CommandDefinition(
            "SELECT SedeID,Codigo,Grupo,Nombre,Descripcion FROM dbo.Alm_Sedes WHERE Activo=1 ORDER BY Grupo,Nombre",
            cancellationToken: ct))).AsList();
        const string stats = @"
SELECT (SELECT COUNT(*) FROM dbo.Alm_Sedes WHERE Activo=1) AS CantidadSedes,
       (SELECT COUNT(*) FROM dbo.Alm_Articulos WHERE Activo=1) AS CantidadArticulos,
       (SELECT COUNT(*) FROM dbo.Alm_ReferenciasExcel) AS CantidadReferencias,
       (SELECT COUNT(*) FROM dbo.Alm_Movimientos) AS CantidadMovimientos,
       (SELECT COUNT(*) FROM dbo.Alm_Existencias E JOIN dbo.Alm_Articulos A ON A.ArticuloID=E.ArticuloID
         WHERE A.PuntoReorden>0 AND E.Cantidad<=A.PuntoReorden) AS CantidadAlertas;";
        var counters = await db.QuerySingleAsync<AlmDashboardVm>(new CommandDefinition(stats, cancellationToken:ct));
        vm.CantidadSedes=counters.CantidadSedes;
        vm.CantidadArticulos=counters.CantidadArticulos;
        vm.CantidadReferencias=counters.CantidadReferencias;
        vm.CantidadMovimientos=counters.CantidadMovimientos;
        vm.CantidadAlertas=counters.CantidadAlertas;
        vm.Resumen = (await db.QueryAsync<AlmResumenTipoVm>(new CommandDefinition(
            "SELECT Tipo,COUNT(*) AS Registros FROM dbo.Alm_ReferenciasExcel GROUP BY Tipo ORDER BY Tipo",
            cancellationToken:ct))).AsList();

        if (seccion is "Index" or "Inventario" or "Movimientos" or "Recepciones" or "Indicadores")
        {
            const string products = @"
SELECT TOP (160) A.ArticuloID,A.Codigo,A.Descripcion,A.Categoria,A.Unidad,A.PuntoReorden,
       COALESCE(SUM(E.Cantidad),0) AS Existencia,
       (SELECT TOP (1) R.Saldo FROM dbo.Alm_ReferenciasExcel R
        WHERE R.Tipo='INVENTARIO' AND R.Codigo=A.Codigo AND R.Saldo IS NOT NULL
        ORDER BY R.Fecha DESC,R.ReferenciaID DESC) AS InventarioHistorico
FROM dbo.Alm_Articulos A
LEFT JOIN dbo.Alm_Existencias E ON E.ArticuloID=A.ArticuloID AND (@SedeID IS NULL OR E.SedeID=@SedeID)
WHERE A.Activo=1 AND (@Buscar IS NULL OR A.Codigo LIKE '%' + @Buscar + '%' OR A.Descripcion LIKE '%' + @Buscar + '%')
GROUP BY A.ArticuloID,A.Codigo,A.Descripcion,A.Categoria,A.Unidad,A.PuntoReorden
ORDER BY CASE WHEN SUM(E.Cantidad)>0 THEN 0 ELSE 1 END,A.Descripcion;";
            vm.Articulos=(await db.QueryAsync<AlmArticuloVm>(new CommandDefinition(products,
                new { SedeID=sedeId,Buscar=string.IsNullOrWhiteSpace(buscar)?null:buscar.Trim() },
                cancellationToken: ct))).AsList();
        }

        if (seccion is "Index" or "Movimientos" or "Recepciones" or "Indicadores")
        {
            const string movements = @"
SELECT TOP (100) M.MovimientoID,M.OperacionID,M.FechaUTC,S.Nombre AS Sede,A.Descripcion AS Producto,
 A.Codigo,A.Unidad,M.Tipo,M.Cantidad,M.Referencia,U.Username AS Usuario
FROM dbo.Alm_Movimientos M
JOIN dbo.Alm_Operaciones OP ON OP.OperacionID=M.OperacionID AND OP.EmpresaID=@EmpresaID
JOIN dbo.Alm_Sedes S ON S.SedeID=M.SedeID
JOIN dbo.Alm_Articulos A ON A.ArticuloID=M.ArticuloID
JOIN dbo.Usuarios U ON U.UsuarioID=M.UsuarioID
WHERE (@SedeID IS NULL OR S.SedeID=@SedeID)
  AND (@Recepciones=0 OR M.Tipo IN('ENTRADA','AJUSTE_ENTRADA','TRANSFERENCIA_ENTRADA'))
ORDER BY M.MovimientoID DESC;";
            vm.Movimientos=(await db.QueryAsync<AlmMovimientoVm>(new CommandDefinition(movements,
                new { SedeID=sedeId, Recepciones=seccion=="Recepciones"?1:0, EmpresaID },cancellationToken:ct))).AsList();
            if (seccion is "Movimientos" or "Recepciones")
            {
                const string evq=@"SELECT TOP (30) E.EvidenciaID,E.NombreOriginal,E.FechaUTC,O.Referencia,U.Username AS Usuario
FROM dbo.Alm_Evidencias E
JOIN dbo.Alm_Operaciones O ON O.OperacionID=E.OperacionID
JOIN dbo.Usuarios U ON U.UsuarioID=E.UsuarioID
WHERE O.EmpresaID=@EmpresaID ORDER BY E.EvidenciaID DESC";
                vm.Evidencias=(await db.QueryAsync<AlmEvidenciaListaVm>(new CommandDefinition(evq,
                    new { EmpresaID },cancellationToken:ct))).AsList();
            }
        }
        if (seccion is "Index" or "Bsp" or "Dosis" or "Alimento" or "Animales" or "Referencias" or "Indicadores")
        {
            string? selectedType = seccion switch
            {
                "Bsp" => null, "Dosis" => "DOSIS", "Animales" => "ANIMALES", "Alimento" => null,
                "Index" => null, "Indicadores" => null, _ => tipo
            };
            var food = seccion == "Alimento" ? 1 : 0;
            var bsp = seccion == "Bsp" ? 1 : 0;
            const string refs = @"
SELECT TOP (180) ReferenciaID,Archivo,Hoja,FilaExcel,Tipo,Ubicacion,Codigo,Concepto,Unidad,
 Fecha,Cantidad,Entradas,Salidas,Saldo,Nota
FROM dbo.Alm_ReferenciasExcel
WHERE (@Tipo IS NULL OR Tipo=@Tipo)
  AND (@Bsp=0 OR Tipo IN('BSP','SEMENTALES','CUARENTENA'))
  AND (@Alimento=0 OR Tipo IN('ALIMENTO','ALIMENTO_COMPRA'))
  AND (@Buscar IS NULL OR Concepto LIKE '%' + @Buscar + '%' OR Codigo LIKE '%' + @Buscar + '%' OR Ubicacion LIKE '%' + @Buscar + '%' OR Archivo LIKE '%' + @Buscar + '%')
ORDER BY COALESCE(Fecha,CONVERT(date,'19000101')) DESC,ReferenciaID DESC;";
            vm.Referencias=(await db.QueryAsync<AlmReferenciaVm>(new CommandDefinition(refs,
                new { Tipo=string.IsNullOrWhiteSpace(selectedType)?null:selectedType,
                      Alimento=food, Bsp=bsp, Buscar=string.IsNullOrWhiteSpace(buscar)?null:buscar.Trim() },
                cancellationToken: ct))).AsList();
        }
        return vm;
    }

    private async Task<IActionResult> PaginaAsync(string seccion, string? buscar, string? tipo,
        int? sedeId, CancellationToken ct)
    {
        await using var db=await AbrirAsync(ct);
        var auth=await ValidarAsync(db,ct);
        if (auth.Error is not null) return auth.Error;
        var vm=await CrearModeloAsync(db,seccion,buscar,tipo,sedeId,ct);
        vm.PuedeRegistrar=auth.Edita;
        return View(seccion,vm);
    }

    [HttpGet("")][HttpGet("Index")]
    public Task<IActionResult> Index(string? buscar=null,CancellationToken ct=default) => PaginaAsync("Index",buscar,null,null,ct);
    [HttpGet("Inventario")]
    public Task<IActionResult> Inventario(string? buscar=null,int? sedeId=null,CancellationToken ct=default) => PaginaAsync("Inventario",buscar,null,sedeId,ct);
    [HttpGet("Movimientos")]
    public Task<IActionResult> Movimientos(int? sedeId=null,CancellationToken ct=default) => PaginaAsync("Movimientos",null,null,sedeId,ct);
    [HttpGet("Recepciones")]
    public Task<IActionResult> Recepciones(int? sedeId=null,CancellationToken ct=default) => PaginaAsync("Recepciones",null,null,sedeId,ct);
    [HttpGet("Bsp")]
    public Task<IActionResult> Bsp(string? buscar=null,CancellationToken ct=default) => PaginaAsync("Bsp",buscar,null,null,ct);
    [HttpGet("Dosis")]
    public Task<IActionResult> Dosis(string? buscar=null,CancellationToken ct=default) => PaginaAsync("Dosis",buscar,null,null,ct);
    [HttpGet("Alimento")]
    public Task<IActionResult> Alimento(string? buscar=null,CancellationToken ct=default) => PaginaAsync("Alimento",buscar,null,null,ct);
    [HttpGet("Animales")]
    public Task<IActionResult> Animales(string? buscar=null,CancellationToken ct=default) => PaginaAsync("Animales",buscar,null,null,ct);
    [HttpGet("Referencias")]
    public Task<IActionResult> Referencias(string? buscar=null,string? tipo=null,CancellationToken ct=default) => PaginaAsync("Referencias",buscar,tipo,null,ct);
    [HttpGet("Indicadores")]
    public Task<IActionResult> Indicadores(CancellationToken ct=default) => PaginaAsync("Indicadores",null,null,null,ct);

    [HttpPost("RegistrarMovimiento")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> RegistrarMovimiento(AlmMovimientoForm form,CancellationToken ct)
    {
        await using var db=await AbrirAsync(ct);
        var auth=await ValidarAsync(db,ct);
        if (auth.Error is not null) return auth.Error;
        if (!auth.Edita) return StatusCode(403,"No tienes permiso para registrar movimientos.");
        if (form.OperacionID==Guid.Empty || form.Cantidad<=0 || form.Cantidad>100_000_000 ||
            string.IsNullOrWhiteSpace(form.Referencia) || form.Referencia.Length>90 || form.Observaciones?.Length>400)
        {
            TempData["ErrorMessage"]="Verifica la cantidad y la referencia.";
            return RedirectToAction(nameof(Movimientos));
        }
        var types = new[] { "ENTRADA", "SALIDA", "TRANSFERENCIA", "AJUSTE_ENTRADA", "AJUSTE_SALIDA" };
        if (!types.Contains(form.Tipo)) return BadRequest("Tipo de movimiento no valido.");
        if (form.Evidencia is not null && form.Evidencia.Length>0)
        {
            if (form.Evidencia.Length>10_000_000 ||
                !new[]{".pdf",".png",".jpg",".jpeg"}.Contains(Path.GetExtension(form.Evidencia.FileName).ToLowerInvariant()))
                return BadRequest("La evidencia debe ser PDF, PNG o JPG y pesar menos de 10 MB.");
        }
        try
        {
            await db.ExecuteAsync(new CommandDefinition("dbo.Alm_RegistrarMovimiento",
                new { form.OperacionID, UsuarioID, EmpresaID, form.SedeID, form.ArticuloID, form.Tipo,
                      form.Cantidad, Referencia=form.Referencia!.Trim(), form.Observaciones,
                      form.DestinoSedeID }, commandType: CommandType.StoredProcedure,cancellationToken:ct));
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex,"Movimiento Almacen rechazado");
            TempData["ErrorMessage"] = ex.Number>=52000 ? ex.Message : "No se registro el movimiento. Revisa la referencia y el saldo.";
            return RedirectToAction(nameof(Movimientos));
        }
        if (form.Evidencia is { Length: >0 })
        {
            string? saved=null;
            try
            {
                var dir=Path.Combine(_env.ContentRootPath,"App_Data","almacen-evidencias");
                Directory.CreateDirectory(dir);
                var extension=Path.GetExtension(form.Evidencia.FileName).ToLowerInvariant();
                var internalName=Guid.NewGuid().ToString("N")+extension;
                saved=Path.Combine(dir,internalName);
                await using(var stream=new FileStream(saved,FileMode.CreateNew,FileAccess.Write))
                    await form.Evidencia.CopyToAsync(stream,ct);
                await db.ExecuteAsync(new CommandDefinition(@"
INSERT dbo.Alm_Evidencias(OperacionID,NombreOriginal,ArchivoInterno,Mime,Tamano,UsuarioID)
VALUES(@OperacionID,@NombreOriginal,@ArchivoInterno,@Mime,@Tamano,@UsuarioID);",
                    new { form.OperacionID,
                          NombreOriginal=Path.GetFileName(form.Evidencia.FileName),ArchivoInterno=internalName,
                          Mime=extension==".pdf"?"application/pdf":extension==".png"?"image/png":"image/jpeg",
                          Tamano=form.Evidencia.Length,UsuarioID },cancellationToken:ct));
            }
            catch(Exception ex)
            {
                _logger.LogError(ex,"Movimiento registrado, pero fallo evidencia");
                if (saved is not null && System.IO.File.Exists(saved)) System.IO.File.Delete(saved);
                TempData["InfoMessage"]="El movimiento se registro, pero la evidencia no pudo guardarse. Consulta a TI.";
                return RedirectToAction(nameof(Movimientos));
            }
        }
        TempData["SuccessMessage"]="Movimiento registrado y trazado correctamente.";
        return RedirectToAction(nameof(Movimientos));
    }

    [HttpGet("Evidencia/{id:long}")]
    public async Task<IActionResult> Evidencia(long id,CancellationToken ct)
    {
        await using var db=await AbrirAsync(ct);
        var auth=await ValidarAsync(db,ct);
        if (auth.Error is not null) return auth.Error;
        var data=await db.QueryFirstOrDefaultAsync<AlmEvidenciaVm>(
            new CommandDefinition("SELECT E.ArchivoInterno,E.NombreOriginal,E.Mime FROM dbo.Alm_Evidencias E JOIN dbo.Alm_Operaciones O ON O.OperacionID=E.OperacionID WHERE E.EvidenciaID=@id AND O.EmpresaID=@EmpresaID",
                new{id, EmpresaID},cancellationToken:ct));
        if (data is null || string.IsNullOrWhiteSpace(data.ArchivoInterno)) return NotFound();
        var path=Path.Combine(_env.ContentRootPath,"App_Data","almacen-evidencias",Path.GetFileName(data.ArchivoInterno));
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path,data.Mime,data.NombreOriginal);
    }

    [HttpGet("ExportarCsv")]
    public async Task<IActionResult> ExportarCsv(string? tipo=null,CancellationToken ct=default)
    {
        await using var db=await AbrirAsync(ct);
        var auth=await ValidarAsync(db,ct);
        if (auth.Error is not null) return auth.Error;
        const string query=@"
SELECT TOP (10000) Tipo,Ubicacion,Codigo,Concepto,Unidad,Fecha,Cantidad,Entradas,Salidas,Saldo,Archivo,Hoja,FilaExcel
FROM dbo.Alm_ReferenciasExcel WHERE (@Tipo IS NULL OR Tipo=@Tipo) ORDER BY ReferenciaID DESC;";
        var rows=await db.QueryAsync(new CommandDefinition(query,new { Tipo=string.IsNullOrWhiteSpace(tipo)?null:tipo },cancellationToken:ct));
        static string Csv(object? obj)
        {
            var text=Convert.ToString(obj,System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (text.Length>0 && "=+-@".Contains(text[0])) text="'"+text; // avoid spreadsheet formula execution
            return "\""+text.Replace("\"","\"\"")+"\"";
        }
        var sb=new StringBuilder("Tipo,Ubicacion,Codigo,Concepto,Unidad,Fecha,Cantidad,Entradas,Salidas,Saldo,Archivo,Hoja,FilaExcel\r\n");
        foreach(var item in rows)
        {
            var r=(IDictionary<string,object>)item;
            sb.AppendLine(string.Join(",",r.Values.Select(Csv)));
        }
        var bytes=new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes,"text/csv","Almacen_Nutri_Referencias.csv");
    }
}
