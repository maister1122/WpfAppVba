using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class MovimientosGeneral : UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private string _identificadorArticulo = "";
        private bool _iniciado = false;

        // Cuando se abre desde ArticulosDetalle, el código viene prefijado y los controles de búsqueda se bloquean.
        private readonly string _codigoFijo;

        public MovimientosGeneral(string codigoFijo = "")
        {
            InitializeComponent();
            _codigoFijo = codigoFijo;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_iniciado) return;
            _iniciado = true;

            if (!string.IsNullOrEmpty(_codigoFijo))
            {
                TxtCodigo.Text       = _codigoFijo;
                TxtCodigo.IsEnabled  = false;
                BtnBuscar.IsEnabled  = false;
                BuscarArticulo();
            }
        }

        // ─── Método estático para abrir como pestaña ──────────────────────────
        public static void OpenAsTab(Window ventana, string codigoArticulo = "")
        {
            if (ventana is not ConsolaMovimientos consola) return;

            string clave = string.IsNullOrEmpty(codigoArticulo)
                ? "movimientos-general"
                : $"movimientos-{codigoArticulo}";

            string titulo = string.IsNullOrEmpty(codigoArticulo)
                ? "Movimientos"
                : $"Movimientos · {codigoArticulo}";

            var panel = new MovimientosGeneral(codigoArticulo);
            consola.AbrirPestaña(titulo, panel, clave);
        }

        // ─── Buscar artículo por código ───────────────────────────────────────
        private void BuscarArticulo()
        {
            string codigo = TxtCodigo.Text.Trim();
            if (string.IsNullOrEmpty(codigo)) return;

            string id = Sql.ArticulosObj.BuscarIdentificador("codigo", codigo);
            if (string.IsNullOrEmpty(id))
            {
                LblDescripcion.Text = "Artículo no encontrado.";
                _identificadorArticulo = "";
                Grid1.ItemsSource = null;
                return;
            }

            _identificadorArticulo = id;

            string desc    = Sql.ArticulosObj.ObtenerItem("descripcion", _identificadorArticulo)?.ToString() ?? "";
            string modelo  = Sql.ArticulosObj.ObtenerItem("modelo",      _identificadorArticulo)?.ToString() ?? "";
            string famId   = Sql.ArticulosObj.ObtenerItem("familia",      _identificadorArticulo)?.ToString() ?? "";
            string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";

            LblDescripcion.Text = FuncionesComunes.UnirVariables(desc, famDesc, modelo);
            CargarMovimientos();
        }

        // ─── Carga y muestra todos los movimientos del artículo ───────────────
        private void CargarMovimientos()
        {
            if (string.IsNullOrEmpty(_identificadorArticulo)) return;

            var datos = new List<MovimientoDato>();

            // 1. Apertura activa
            foreach (var item in AppState.AperturaActiva)
            {
                if (item == null) continue;
                if (item.ArticuloId != _identificadorArticulo) continue;

                datos.Add(new MovimientoDato
                {
                    Fecha      = item.Fecha,
                    Documento  = "0",
                    Movimiento = "apertura",
                    Estado     = "-",
                    Cantidad   = item.Cantidad,
                    Unitario   = "-",
                    SubTotal   = "-"
                });
            }

            // 2. Pedidos — solo los entregados
            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (Sql.PedidosObj.ObtenerItem("articulo", id)?.ToString() != _identificadorArticulo) continue;

                string docP   = Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() ?? "";
                string estado = Sql.DocumentosPObj.ObtenerItem("estado", docP)?.ToString() ?? "";
                if (estado != "entregado") continue;

                double cantidad = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("cantidad", id) ?? 0);
                double importe  = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("importe",  id) ?? 0);
                double unitario = cantidad != 0 ? importe / cantidad : 0;
                string movDoc   = Sql.DocumentosPObj.ObtenerItem("movimiento", docP)?.ToString() ?? "";
                var   fechaObj  = Sql.DocumentosPObj.ObtenerItem("fecha", docP);
                DateTime fecha  = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;

                datos.Add(new MovimientoDato
                {
                    Fecha      = fecha,
                    Documento  = docP,
                    DocumentoCodigo = Sql.DocumentosPObj.ObtenerItem("codigo", docP)?.ToString() ?? "",
                    Movimiento = movDoc,
                    Estado     = estado,
                    Cantidad   = cantidad,
                    Unitario   = unitario.ToString("N2"),
                    SubTotal   = importe.ToString("N2")
                });
            }

            // 3. Traspasos
            uf = Sql.TraspasosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.TraspasosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (Sql.TraspasosObj.ObtenerItem("articulo", id)?.ToString() != _identificadorArticulo) continue;

                string docT    = Sql.TraspasosObj.ObtenerItem("documentoT", id)?.ToString() ?? "";
                string estado  = Sql.DocumentosTObj.ObtenerItem("estado",   docT)?.ToString() ?? "";
                string origen  = Sql.DocumentosTObj.ObtenerItem("origen",   docT)?.ToString() ?? "";
                string destino = Sql.DocumentosTObj.ObtenerItem("destino",  docT)?.ToString() ?? "";
                string emitido = Sql.DocumentosTObj.ObtenerItem("emitido",  docT)?.ToString() ?? "";

                if (emitido != AppState.SucursalActiva && estado == "pendiente")
                    estado = "pendiente revisar";

                string movimiento = (origen == AppState.SucursalActiva &&
                                     destino != AppState.SucursalActiva)
                                    ? "salida" : "entrada";

                var fechaObj   = Sql.DocumentosTObj.ObtenerItem("fecha", docT);
                DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;
                double cantidad = Convert.ToDouble(Sql.TraspasosObj.ObtenerItem("cantidad", id) ?? 0);

                datos.Add(new MovimientoDato
                {
                    Fecha      = fecha,
                    Documento  = docT,
                    DocumentoCodigo = Sql.DocumentosTObj.ObtenerItem("codigo", docT)?.ToString() ?? "",
                    Movimiento = movimiento,
                    Estado     = estado,
                    Cantidad   = cantidad,
                    Unitario   = "-",
                    SubTotal   = "-"
                });
            }

            // 4. Correcciones
            uf = Sql.CorreccionesObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.CorreccionesObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (Sql.CorreccionesObj.ObtenerItem("articulo", id)?.ToString() != _identificadorArticulo) continue;

                string docC       = Sql.CorreccionesObj.ObtenerItem("documentoC", id)?.ToString() ?? "";
                string movimiento = Sql.DocumentosCObj.ObtenerItem("movimiento",  docC)?.ToString()?.ToLower() ?? "";
                string motivo     = Sql.DocumentosCObj.ObtenerItem("motivo",      docC)?.ToString() ?? "";

                var fechaObj   = Sql.DocumentosCObj.ObtenerItem("fecha", docC);
                DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;
                double cantidad = Convert.ToDouble(Sql.CorreccionesObj.ObtenerItem("cantidad", id) ?? 0);

                datos.Add(new MovimientoDato
                {
                    Fecha      = fecha,
                    Documento  = docC,
                    DocumentoCodigo = Sql.DocumentosCObj.ObtenerItem("codigo", docC)?.ToString() ?? "",
                    Movimiento = movimiento,
                    Estado     = motivo,
                    Cantidad   = cantidad,
                    Unitario   = "-",
                    SubTotal   = "-"
                });
            }

            datos = datos.OrderBy(d => d.Fecha).ThenBy(d => d.Documento).ToList();

            var lista = new List<MovimientoFila>();
            int linea = 1;
            double stock = 0;
            double totalCompras = 0, totalVentas = 0, totalEntradas = 0, totalSalidas = 0;
            double totalIngresos = 0, totalEgresos = 0;

            foreach (var d in datos)
            {
                switch (d.Movimiento)
                {
                    case "apertura": stock += d.Cantidad; break;
                    case "compra":   stock += d.Cantidad; totalCompras  += d.Cantidad; break;
                    case "venta":    stock -= d.Cantidad; totalVentas   += d.Cantidad; break;
                    case "entrada":  stock += d.Cantidad; totalEntradas += d.Cantidad; break;
                    case "salida":   stock -= d.Cantidad; totalSalidas  += d.Cantidad; break;
                    // Correcciones: "repuesta"/"retirado" es el vocabulario actual,
                    // "ingreso"/"egreso" el anterior (documentos ya cargados).
                    case "repuesta":
                    case "ingreso":  stock += d.Cantidad; totalIngresos += d.Cantidad; break;
                    case "retirado":
                    case "descarga":
                    case "egreso":   stock -= d.Cantidad; totalEgresos  += d.Cantidad; break;
                }

                lista.Add(new MovimientoFila
                {
                    Linea      = linea++,
                    Fecha      = d.Fecha,
                    FechaStr   = d.Fecha != default ? $"{d.Fecha:d} {d.Fecha:HH:mm:ss}" : "-",
                    Movimiento = string.IsNullOrEmpty(d.Documento) || d.Documento == "0"
                                 ? d.Movimiento
                                 : $"{(string.IsNullOrEmpty(d.DocumentoCodigo) ? d.Documento : d.DocumentoCodigo)}-{d.Movimiento}",
                    Estado     = d.Estado,
                    Cantidad   = d.Cantidad,
                    Unitario   = d.Unitario,
                    SubTotal   = d.SubTotal,
                    Stock      = stock
                });
            }

            Grid1.ItemsSource = lista;

            TxtTotalCompras.Text  = totalCompras.ToString("N0");
            TxtTotalVentas.Text   = totalVentas.ToString("N0");
            TxtTotalEntradas.Text = totalEntradas.ToString("N0");
            TxtTotalSalidas.Text  = totalSalidas.ToString("N0");
            TxtTotalIngresos.Text = totalIngresos.ToString("N0");
            TxtTotalEgresos.Text  = totalEgresos.ToString("N0");
        }

        // ─── Eventos ─────────────────────────────────────────────────────────

        // Abre el selector de artículo (igual que BtnBuscarArticulo en TraspasosDetalle)
        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            var ventana = Window.GetWindow(this);
            if (ventana == null) return;

            ArticulosGeneral.OpenAsTab(ventana, null, art =>
            {
                TxtCodigo.Text = art.Codigo;
                BuscarArticulo();
                return true;
            }, contexto: "movimientos-buscar", llamador: this);
        }

        private void TxtCodigo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { BuscarArticulo(); e.Handled = true; }
        }

        private void BtnProcesar_Click(object sender, RoutedEventArgs e)
            => CargarMovimientos();
    }

    // ─── Dato interno (antes de ordenar) ─────────────────────────────────────
    internal class MovimientoDato
    {
        public DateTime Fecha           { get; set; }
        public string   Documento       { get; set; } = "";
        public string   DocumentoCodigo { get; set; } = "";
        public string   Movimiento      { get; set; } = "";
        public string   Estado          { get; set; } = "";
        public double   Cantidad        { get; set; }
        public string   Unitario        { get; set; } = "";
        public string   SubTotal        { get; set; } = "";
    }

    // ─── Fila del DataGrid ────────────────────────────────────────────────────
    public class MovimientoFila
    {
        public int    Linea      { get; set; }
        public DateTime Fecha    { get; set; }
        public string FechaStr   { get; set; } = "";
        public string Movimiento { get; set; } = "";
        public string Estado     { get; set; } = "";
        public double Cantidad   { get; set; }
        public string Unitario   { get; set; } = "";
        public string SubTotal   { get; set; } = "";
        public double Stock      { get; set; }
    }
}
