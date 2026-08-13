using System;
using System.Text;
using Microsoft.Data.SqlClient;

namespace SistemaGestion.Data
{
    /// <summary>
    /// Regenera la columna <c>codigo</c> de las tablas del sistema, para la
    /// EMPRESA ACTIVA (<see cref="AppState.EmpresaActiva"/>) — nunca toca
    /// documentos de otra empresa aunque compartan la misma base:
    ///   • Maestras → numeración secuencial 1..N, ordenada por 'descripcion'
    ///     (excepto 'usuarios', que usa 'apellidos, nombres'). Son catálogos
    ///     globales de toda la base: no se filtran por empresa.
    ///   • documentosP/documentosC/documentosF (pedidos/correcciones/facturas)
    ///     → sin signo: codigo = correlativo 1..N, agrupado por tipo de
    ///     movimiento (venta/compra, repuesta/retirado, ingreso/egreso
    ///     respectivamente — normalizando el vocabulario viejo al nuevo),
    ///     ordenado por fecha, filtrado a la empresa activa (cascada
    ///     sucursal → sucursales.empresa). Antes usaban signo de sucursal +
    ///     correlativo por sucursal, sin distinguir movimiento.
    ///   • documentosT/documentosL/documentosI (traspasos/precios/inventarios)
    ///     → sin signo: codigo = correlativo 1..N dentro de la empresa activa,
    ///     ordenado por fecha. Antes usaban signo de empresa (traspasos,
    ///     precios) o signo de sucursal (inventarios).
    /// Trabaja directamente sobre SQL Server y reescribe las filas de la
    /// empresa activa en una única transacción.
    /// </summary>
    public static class CodigoRegenerator
    {
        // Tablas maestras → código entero secuencial, ordenadas por 'descripcion'.
        // "usuarios" es la única sin columna 'descripcion' (tiene nombres/apellidos
        // en su lugar) así que se ordena por "apellidos, nombres". Catálogos
        // globales: no se filtran por empresa aunque varias tengan columna
        // 'empresa' propia.
        private static readonly (string tabla, string colOrden)[] Maestras =
        {
            ("usuarios",   "apellidos, nombres"),
            ("familias",   "descripcion"),
            ("productos",  "descripcion"),
            ("Categorias", "descripcion"),
            ("industrias", "descripcion"),
            ("terceros",   "descripcion"),
            ("sucursales", "descripcion"),
            ("regiones",   "descripcion"),
            ("empresas",   "descripcion"),
        };

        /// <summary>
        /// Regenera todos los códigos de la empresa activa en una única
        /// transacción. Devuelve un resumen (tabla → filas afectadas).
        /// </summary>
        public static string RegenerarTodos()
        {
            string empresaId = AppState.EmpresaActiva;
            if (string.IsNullOrEmpty(empresaId))
                throw new InvalidOperationException(
                    "No hay una empresa activa: inicia sesión antes de regenerar códigos.");

            var conn = DatabaseConnection.ObtenerConexion();
            var resumen = new StringBuilder();

            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var (t, colOrden) in Maestras)
                    resumen.AppendLine($"{t}: {RenumerarMaestra(conn, tx, t, colOrden)}");

                resumen.AppendLine($"documentosP: {RenumerarPedidos(conn, tx, empresaId)}");
                resumen.AppendLine($"documentosC: {RenumerarCorrecciones(conn, tx, empresaId)}");
                resumen.AppendLine($"documentosF: {RenumerarFacturas(conn, tx, empresaId)}");
                resumen.AppendLine($"documentosI: {RenumerarInventarios(conn, tx, empresaId)}");
                resumen.AppendLine($"documentosT: {RenumerarTraspasos(conn, tx, empresaId)}");
                resumen.AppendLine($"documentosL: {RenumerarPrecios(conn, tx, empresaId)}");

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            return resumen.ToString().TrimEnd();
        }

        // ─── Maestras: codigo = 1..N (orden por colOrden) ─────────────────────
        // codigo es INT en TODAS las tablas que renumera este archivo (maestras y
        // documentos): los documentosP/C/F/I/L/T pasaron de NVARCHAR(100) a INT
        // (ver codigo-a-int.sql), porque el correlativo siempre fue un entero.
        // articulos.codigo sigue siendo NVARCHAR y NO lo toca el regenerador.
        private static int RenumerarMaestra(SqlConnection conn, SqlTransaction tx, string tabla, string colOrden)
        {
            string sql =
                $";WITH cte AS (" +
                $"  SELECT codigo, ROW_NUMBER() OVER (ORDER BY {colOrden}, id) AS rn " +
                $"  FROM {tabla}" +
                $") UPDATE cte SET codigo = CAST(rn AS INT);";
            return Ejecutar(conn, tx, sql);
        }

        // ─── Pedidos: codigo = correlativo 1..N por movimiento (venta/compra) ─
        // Sin signo. Filtrado a la empresa activa vía sucursal → sucursales.empresa.
        private static int RenumerarPedidos(SqlConnection conn, SqlTransaction tx, string empresaId)
        {
            string sql =
                ";WITH cte AS (" +
                "  SELECT d.codigo AS codigo, " +
                "         ROW_NUMBER() OVER (" +
                $"           PARTITION BY {MovimientoSql.CasoPedidos("d")} " +
                "           ORDER BY d.fecha, d.id" +
                "         ) AS rn " +
                "  FROM documentosP AS d " +
                "  INNER JOIN sucursales AS s ON s.id = d.sucursal " +
                "  WHERE s.empresa = @emp" +
                ") UPDATE cte SET codigo = CAST(rn AS INT);";
            return Ejecutar(conn, tx, sql, empresaId);
        }

        // ─── Correcciones: codigo = correlativo 1..N por movimiento ──────────
        // (repuesta/retirado). "ingreso" cuenta como repuesta y "descarga"/
        // "egreso" como retirado — mismo criterio que NormalizarMovimiento en
        // CorreccionesGeneral, para no separar documentos viejos y nuevos del
        // mismo tipo real en dos correlativos distintos.
        private static int RenumerarCorrecciones(SqlConnection conn, SqlTransaction tx, string empresaId)
        {
            string sql =
                ";WITH cte AS (" +
                "  SELECT d.codigo AS codigo, " +
                "         ROW_NUMBER() OVER (" +
                $"           PARTITION BY {MovimientoSql.CasoCorrecciones("d")} " +
                "           ORDER BY d.fecha, d.id" +
                "         ) AS rn " +
                "  FROM documentosC AS d " +
                "  INNER JOIN sucursales AS s ON s.id = d.sucursal " +
                "  WHERE s.empresa = @emp" +
                ") UPDATE cte SET codigo = CAST(rn AS INT);";
            return Ejecutar(conn, tx, sql, empresaId);
        }

        // ─── Facturas: codigo = correlativo 1..N por movimiento ──────────────
        // (ingreso/egreso). "venta" cuenta como egreso y "compra" como ingreso —
        // mismo criterio que NormalizarMovimiento en FacturasGeneral.
        private static int RenumerarFacturas(SqlConnection conn, SqlTransaction tx, string empresaId)
        {
            string sql =
                ";WITH cte AS (" +
                "  SELECT d.codigo AS codigo, " +
                "         ROW_NUMBER() OVER (" +
                $"           PARTITION BY {MovimientoSql.CasoFacturas("d")} " +
                "           ORDER BY d.fecha, d.id" +
                "         ) AS rn " +
                "  FROM documentosF AS d " +
                "  INNER JOIN sucursales AS s ON s.id = d.sucursal " +
                "  WHERE s.empresa = @emp" +
                ") UPDATE cte SET codigo = CAST(rn AS INT);";
            return Ejecutar(conn, tx, sql, empresaId);
        }

        // ─── Inventarios: codigo = correlativo 1..N dentro de la empresa ─────
        // Sin signo (antes usaba signo de sucursal + correlativo por sucursal).
        private static int RenumerarInventarios(SqlConnection conn, SqlTransaction tx, string empresaId)
        {
            string sql =
                ";WITH cte AS (" +
                "  SELECT d.codigo AS codigo, " +
                "         ROW_NUMBER() OVER (ORDER BY d.fecha, d.id) AS rn " +
                "  FROM documentosI AS d " +
                "  INNER JOIN sucursales AS s ON s.id = d.sucursal " +
                "  WHERE s.empresa = @emp" +
                ") UPDATE cte SET codigo = CAST(rn AS INT);";
            return Ejecutar(conn, tx, sql, empresaId);
        }

        // ─── Traspasos: codigo = correlativo 1..N dentro de la empresa ───────
        // Sin signo (antes signo de empresa + correlativo por empresa). La
        // empresa se obtiene por la cascada emitido (sucursal) → sucursales.empresa.
        private static int RenumerarTraspasos(SqlConnection conn, SqlTransaction tx, string empresaId)
        {
            string sql =
                ";WITH cte AS (" +
                "  SELECT d.codigo AS codigo, " +
                "         ROW_NUMBER() OVER (ORDER BY d.fecha, d.id) AS rn " +
                "  FROM documentosT AS d " +
                "  INNER JOIN sucursales AS s ON s.id = d.emitido " +
                "  WHERE s.empresa = @emp" +
                ") UPDATE cte SET codigo = CAST(rn AS INT);";
            return Ejecutar(conn, tx, sql, empresaId);
        }

        // ─── Precios (documentosL): codigo = correlativo 1..N por empresa ────
        // Sin signo (antes signo de empresa + correlativo por empresa).
        // documentosL.empresa es nvarchar (no uniqueidentifier) pero guarda el id
        // como texto, así que compara directo contra @emp.
        private static int RenumerarPrecios(SqlConnection conn, SqlTransaction tx, string empresaId)
        {
            string sql =
                ";WITH cte AS (" +
                "  SELECT d.codigo AS codigo, " +
                "         ROW_NUMBER() OVER (ORDER BY d.fecha, d.id) AS rn " +
                "  FROM documentosL AS d " +
                "  WHERE d.empresa = @emp" +
                ") UPDATE cte SET codigo = CAST(rn AS INT);";
            return Ejecutar(conn, tx, sql, empresaId);
        }

        private static int Ejecutar(SqlConnection conn, SqlTransaction tx, string sql, string? empresaId = null)
        {
            using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 300 };
            if (empresaId != null) cmd.Parameters.AddWithValue("@emp", empresaId);
            return cmd.ExecuteNonQuery();
        }
    }
}
