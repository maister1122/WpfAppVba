using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using SistemaGestion.Data;

namespace SistemaGestion
{
    /// <summary>
    /// Pestaña-selector para validar un pedido desde una factura: arriba la lista
    /// de pedidos (con buscador por código / cliente-proveedor / referencia),
    /// abajo las líneas del pedido elegido con un check por línea, y al pie
    /// "Validar pedido" / "Cancelar".
    ///
    /// Solo se listan los pedidos SIN factura todavía: un pedido ya apuntado por
    /// algún `documentosF.relacion` no vuelve a aparecer (salvo el propio pedido
    /// de la factura desde la que se abrió la pestaña, para poder re-validarlo).
    ///
    /// Al validar deja el resultado en <see cref="PedidoValidado"/> (id del
    /// documentosP, que va a `documentosF.relacion`) y en
    /// <see cref="LineasValidadas"/>: las líneas tildadas ya sumadas **por
    /// categoría** — una línea de factura por categoría, no una por artículo.
    /// Si se cancela, ambas quedan en null.
    /// </summary>
    public partial class ValidarPedido : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        public event Action? Cerrando;

        /// <summary>Id del documentosP validado (null si se canceló).</summary>
        public static string? PedidoValidado { get; set; }

        /// <summary>
        /// Líneas de factura resultantes: las líneas tildadas agrupadas por
        /// categoría (null si se canceló).
        /// </summary>
        public static List<FacturaLineaValidada>? LineasValidadas { get; set; }

        private readonly List<ValidarPedidoFila> _pedidos = new();
        private readonly List<ValidarItemFila>   _items   = new();
        private bool _iniciado = false;

        // Movimiento de la sección desde la que se abrió ("venta"/"compra"): solo
        // se listan pedidos de ese movimiento. Vacío = todos.
        private readonly string _movimiento;

        // Pedido que ya trae la factura: se deja seleccionado al abrir.
        private readonly string _pedidoPreseleccionado;

        // Vínculo pedido ↔ factura, recalculado al abrir y con "Actualizar" (no en
        // cada tecla del buscador: PedidosYaFacturados consulta SQL).
        private HashSet<string>            _pedidosYaFacturados = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _facturadoPorPedido  = new();

        public ValidarPedido() : this("") { }

        public ValidarPedido(string movimiento, string pedidoPreseleccionado = "")
        {
            InitializeComponent();
            _movimiento = (movimiento ?? "").ToLower();
            _pedidoPreseleccionado = pedidoPreseleccionado ?? "";
            Loaded += (_, _) =>
            {
                if (_iniciado) return;
                _iniciado = true;
                RecargarVinculosFactura();
                CargarPedidos();
                Preseleccionar();
            };
        }

        /// <summary>
        /// Rehace el vínculo pedido ↔ factura: qué pedidos ya fueron validados y
        /// cuánto se les facturó. Una sola pasada por documentosF más la consulta
        /// a SQL, así el buscador después solo filtra en memoria.
        /// </summary>
        private void RecargarVinculosFactura()
        {
            var relacionPorFactura = CalcularRelacionPorFactura();
            _pedidosYaFacturados   = PedidosYaFacturados(relacionPorFactura);
            _facturadoPorPedido    = CalcularFacturadoPorPedido(relacionPorFactura);
        }

        private void Preseleccionar()
        {
            if (_pedidoPreseleccionado == "") return;
            var fila = _pedidos.FirstOrDefault(f => f.DocumentoP == _pedidoPreseleccionado);
            if (fila == null) return;
            GridPedidos.SelectedItem = fila;
            GridPedidos.ScrollIntoView(fila);
        }

        public void IntentarCerrar() => Cerrando?.Invoke();

        public static void OpenAsDialog(Window owner, string contexto = "",
                                        Action? onCerrado = null, UIElement? llamador = null,
                                        string movimiento = "", string pedidoPreseleccionado = "")
        {
            if (owner is not ConsolaMovimientos consola) return;

            var ctrl = new ValidarPedido(movimiento, pedidoPreseleccionado);
            ctrl.Cerrando += () => { consola.CerrarPestaña(ctrl); onCerrado?.Invoke(); consola.SeleccionarPestaña(llamador); };

            string titulo = string.IsNullOrEmpty(contexto)
                            ? "Validar pedido"
                            : $"Validar pedido ({contexto})";
            // Una sola pestaña por llamador (mismo criterio que los demás selectores).
            string clave = string.IsNullOrEmpty(contexto) ? "validar-pedido" : $"validar-pedido|{contexto}";
            consola.CerrarPestañaPorClave(clave);
            consola.AbrirPestaña(titulo, ctrl, clave);
        }

        // ─── Pedidos ──────────────────────────────────────────────────────────
        private void CargarPedidos()
        {
            string busqueda = TxtBuscar.Text.Trim().ToLower();

            // Solo se listan los pedidos que todavía NO fueron validados: los que ya
            // están apuntados por algún documentosF.relacion quedan fuera. La única
            // excepción es el pedido que ya trae la factura desde la que se abrió
            // esta pestaña (si no, no se podría re-validar ni ver el propio pedido).
            _pedidos.Clear();
            int linea = 1;
            int uf = Sql.DocumentosPObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosPObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (Sql.DocumentosPObj.ObtenerItem("sucursal", id)?.ToString() != AppState.SucursalActiva) continue;

                if (_pedidosYaFacturados.Contains(id) &&
                    !string.Equals(id, _pedidoPreseleccionado, StringComparison.OrdinalIgnoreCase)) continue;

                string movDoc = Sql.DocumentosPObj.ObtenerItem("movimiento", id)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(_movimiento) &&
                    !string.Equals(movDoc, _movimiento, StringComparison.OrdinalIgnoreCase)) continue;

                string codigo      = Sql.DocumentosPObj.ObtenerItem("codigo",     id)?.ToString() ?? "";
                string referencia  = Sql.DocumentosPObj.ObtenerItem("referencia", id)?.ToString() ?? "";
                string terceroId   = Sql.DocumentosPObj.ObtenerItem("tercero",    id)?.ToString() ?? "";
                string terceroDesc = Sql.TercerosObj.ObtenerItem("descripcion", terceroId)?.ToString() ?? "";

                if (!string.IsNullOrEmpty(busqueda) &&
                    !codigo.ToLower().Contains(busqueda) &&
                    !terceroDesc.ToLower().Contains(busqueda) &&
                    !referencia.ToLower().Contains(busqueda))
                    continue;

                var fechaObj = Sql.DocumentosPObj.ObtenerItem("fecha", id);
                DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;

                _pedidos.Add(new ValidarPedidoFila
                {
                    Linea       = linea++,
                    DocumentoP  = id,
                    Codigo      = codigo,
                    Fecha       = fecha,
                    FechaStr    = fecha != default ? $"{fecha:d} {fecha:HH:mm:ss}" : "",
                    Movimiento  = movDoc,
                    TerceroDesc = terceroDesc,
                    Referencia  = referencia,
                    Importe     = CalcularImportePedido(id),
                    Facturado   = _facturadoPorPedido.TryGetValue(id, out double fac) ? fac : 0
                });
            }

            GridPedidos.ItemsSource = null;
            GridPedidos.ItemsSource = _pedidos;
            OcultarDetalle();
        }

        /// <summary>
        /// Pedidos que YA fueron validados en alguna factura: el conjunto de
        /// `documentosF.relacion` de todas las facturas vivas.
        ///
        /// Se consulta a SQL directo, sin filtro de sucursal ni de período, porque
        /// la caché `DocumentosFObj` solo trae las facturas de la sucursal activa
        /// dentro del período abierto: un pedido validado en una factura de otra
        /// sucursal o de otro período no aparecería y se podría volver a facturar.
        /// A eso se le suma lo que haya en la caché (incluye lo recién guardado en
        /// esta sesión), y si la consulta falla —sin conexión— queda solo la caché.
        /// </summary>
        private static HashSet<string> PedidosYaFacturados(Dictionary<string, string> relacionPorFactura)
        {
            var res = new HashSet<string>(relacionPorFactura.Values, StringComparer.OrdinalIgnoreCase);
            try
            {
                SqlRetry.Ejecutar(() =>
                {
                    var conn = DatabaseConnection.ObtenerConexion();
                    using var cmd = new SqlCommand(
                        "SELECT relacion FROM documentosF " +
                        "WHERE estadof = 'normal' AND relacion IS NOT NULL", conn);
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        string rel = rd[0]?.ToString() ?? "";
                        if (rel != "") res.Add(rel);
                    }
                });
            }
            catch
            {
                // Sin conexión: queda lo de la caché, que es lo que se usaba antes.
            }
            return res;
        }

        /// <summary>
        /// Vínculo factura → pedido de una sola pasada por documentosF: la clave es
        /// el id de la factura y el valor el del pedido que factura
        /// (documentosF.relacion). Sirve para las dos cosas que necesita la grilla:
        /// saber qué pedidos ya están facturados (los valores) y cuánto se les
        /// facturó (ver <see cref="CalcularFacturadoPorPedido"/>).
        /// </summary>
        private static Dictionary<string, string> CalcularRelacionPorFactura()
        {
            var relacionPorFactura = new Dictionary<string, string>();
            int ufD = Sql.DocumentosFObj.ContarFilas;
            for (int i = 1; i <= ufD; i++)
            {
                var idObj = Sql.DocumentosFObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                string relacion = Sql.DocumentosFObj.ObtenerItem("relacion", id)?.ToString() ?? "";
                if (relacion != "") relacionPorFactura[id] = relacion;
            }
            return relacionPorFactura;
        }

        /// <summary>
        /// Importe ya facturado de cada pedido: suma de las líneas de las facturas
        /// cuyo documentosF.relacion apunta a ese pedido.
        /// </summary>
        private static Dictionary<string, double> CalcularFacturadoPorPedido(
            Dictionary<string, string> relacionPorFactura)
        {
            var total = new Dictionary<string, double>();
            int ufF = Sql.FacturasObj.ContarFilas;
            for (int i = 1; i <= ufF; i++)
            {
                var idObj = Sql.FacturasObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                string docF = Sql.FacturasObj.ObtenerItem("documentoF", id)?.ToString() ?? "";
                if (docF == "" || !relacionPorFactura.TryGetValue(docF, out string? pedido)) continue;

                double importe = Convert.ToDouble(Sql.FacturasObj.ObtenerItem("importe", id) ?? 0);
                total[pedido] = (total.TryGetValue(pedido, out double acum) ? acum : 0) + importe;
            }
            return total;
        }

        private static double CalcularImportePedido(string documentoP)
        {
            double importe = 0;
            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() != documentoP) continue;
                importe += Convert.ToDouble(Sql.PedidosObj.ObtenerItem("importe", id) ?? 0);
            }
            return importe;
        }

        // ─── Líneas del pedido seleccionado ───────────────────────────────────
        private void GridPedidos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridPedidos.SelectedItem is ValidarPedidoFila fila)
                MostrarDetalle(fila);
            else
                OcultarDetalle();
        }

        private void MostrarDetalle(ValidarPedidoFila pedido)
        {
            LblDetalleHeader.Text = $"Artículos del pedido {pedido.Codigo}";

            _items.Clear();
            int linea = 1;
            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() != pedido.DocumentoP) continue;

                string artId = Sql.PedidosObj.ObtenerItem("articulo", id)?.ToString() ?? "";

                _items.Add(new ValidarItemFila
                {
                    Seleccionado = true,   // por defecto se factura todo el pedido
                    Linea        = linea++,
                    ArticuloId   = artId,
                    Codigo       = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? artId,
                    Descripcion  = ObtenerDescripcionArticulo(artId),
                    CategoriaId  = Sql.ArticulosObj.ObtenerItem("categoria", artId)?.ToString() ?? "",
                    Cantidad     = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("cantidad", id) ?? 0),
                    Importe      = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("importe",  id) ?? 0)
                });
            }

            GridItems.ItemsSource = null;
            GridItems.ItemsSource = _items;
            ActualizarTotalSeleccionado();
        }

        private void OcultarDetalle()
        {
            LblDetalleHeader.Text = "Artículos del pedido";
            _items.Clear();
            GridItems.ItemsSource = null;
            ActualizarTotalSeleccionado();
        }

        private static string ObtenerDescripcionArticulo(string artId)
        {
            string desc    = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
            string famId   = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
            string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
            string modelo  = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
            return FuncionesComunes.UnirVariables(desc, famDesc, modelo);
        }

        // ─── Selección de líneas ──────────────────────────────────────────────
        private void ChkItem_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not CheckBox chk || chk.DataContext is not ValidarItemFila fila) return;

            fila.Seleccionado = !fila.Seleccionado;
            GridItems.SelectedItem = fila;
            GridItems.Items.Refresh();
            ActualizarTotalSeleccionado();
        }

        private void BtnMarcarTodos_Click(object sender, RoutedEventArgs e)
            => MarcarTodos(true);

        private void BtnDesmarcarTodos_Click(object sender, RoutedEventArgs e)
            => MarcarTodos(false);

        private void MarcarTodos(bool valor)
        {
            foreach (var item in _items) item.Seleccionado = valor;
            GridItems.Items.Refresh();
            ActualizarTotalSeleccionado();
        }

        private void ActualizarTotalSeleccionado()
        {
            var marcados = _items.Where(i => i.Seleccionado).ToList();
            LblTotalSeleccionado.Text  = marcados.Sum(i => i.Importe).ToString("N2");
            LblItemsSeleccionados.Text = _items.Count == 0
                                         ? ""
                                         : $"({marcados.Count} de {_items.Count} líneas)";
        }

        // ─── Buscador / actualizar ────────────────────────────────────────────
        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_iniciado) return;
            CargarPedidos();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;
            Sql.DocumentosPObj.Actualizar();
            Sql.PedidosObj.Actualizar();
            Sql.DocumentosFObj.Actualizar();
            Sql.FacturasObj.Actualizar();
            RecargarVinculosFactura();
            CargarPedidos();
        }

        // ─── Validar / cancelar ───────────────────────────────────────────────
        private void BtnValidar_Click(object sender, RoutedEventArgs e)
        {
            if (GridPedidos.SelectedItem is not ValidarPedidoFila pedido)
            {
                MessageBox.Show("Elegí un pedido de la lista.", "Consola",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var marcados = _items.Where(i => i.Seleccionado).ToList();
            if (marcados.Count == 0)
            {
                MessageBox.Show("Tildá al menos una línea para facturar.", "Consola",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Una línea de factura por categoría: se suman los importes de todas
            // las líneas tildadas que comparten categoría (dos ítems de 200 de la
            // misma categoría entran como una sola línea de 400).
            PedidoValidado  = pedido.DocumentoP;
            LineasValidadas = marcados
                .GroupBy(i => i.CategoriaId)
                .Select(g => new FacturaLineaValidada
                {
                    CategoriaId = g.Key,
                    // Concepto en blanco a propósito: la línea llega con su categoría
                    // y su importe, y el texto lo escribe el usuario en la factura.
                    Concepto    = "",
                    Importe     = g.Sum(i => i.Importe)
                })
                // Una categoría que suma cero no se factura: no aporta nada a la
                // factura y solo ensucia la grilla con una línea en 0.
                .Where(l => l.Importe != 0)
                .ToList();

            if (LineasValidadas.Count == 0)
            {
                MessageBox.Show("Lo tildado suma cero: no hay nada para facturar.", "Consola",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                PedidoValidado  = null;
                LineasValidadas = null;
                return;
            }

            Cerrando?.Invoke();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            PedidoValidado  = null;
            LineasValidadas = null;
            Cerrando?.Invoke();
        }

        /// <summary>
        /// Todas las líneas de un pedido agrupadas por categoría, con el mismo
        /// criterio que "Validar pedido" (una línea por categoría, sin las que
        /// suman cero). La usa el botón "Actualizar" de FacturasDetalle para
        /// rehacer la factura sin volver a tildar línea por línea.
        /// </summary>
        public static List<FacturaLineaValidada> LineasDePedido(string documentoP)
        {
            var porCategoria = new Dictionary<string, double>();
            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() != documentoP) continue;

                string artId = Sql.PedidosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string catId = Sql.ArticulosObj.ObtenerItem("categoria", artId)?.ToString() ?? "";
                double importe = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("importe", id) ?? 0);
                porCategoria[catId] = (porCategoria.TryGetValue(catId, out double acum) ? acum : 0) + importe;
            }

            return porCategoria
                .Where(kv => kv.Value != 0)
                .Select(kv => new FacturaLineaValidada
                {
                    CategoriaId = kv.Key,
                    Concepto    = "",   // en blanco, igual que al validar
                    Importe     = kv.Value
                })
                .ToList();
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class ValidarPedidoFila
    {
        public int    Linea       { get; set; }
        public string DocumentoP  { get; set; } = "";
        public string Codigo      { get; set; } = "";
        public DateTime Fecha     { get; set; }
        public string FechaStr    { get; set; } = "";
        public string Movimiento  { get; set; } = "";
        public string TerceroDesc { get; set; } = "";
        public string Referencia  { get; set; } = "";
        public double Importe     { get; set; }
        public double Facturado   { get; set; }
    }

    public class ValidarItemFila
    {
        public bool   Seleccionado { get; set; }
        public int    Linea        { get; set; }
        public string ArticuloId   { get; set; } = "";
        public string Codigo       { get; set; } = "";
        public string Descripcion  { get; set; } = "";
        public string CategoriaId  { get; set; } = "";
        // Se resuelve en vivo contra la caché de Categorias (igual que en FacturasDetalle).
        public string CategoriaDescripcion =>
            string.IsNullOrEmpty(CategoriaId)
                ? "Sin categoría"
                : SqlData.Instance.CategoriasObj.ObtenerItem("descripcion", CategoriaId)?.ToString() ?? "Sin categoría";
        public double Cantidad     { get; set; }
        public double Importe      { get; set; }
    }

    /// <summary>
    /// Línea de factura resultante de validar un pedido: una por categoría, con
    /// el importe ya sumado. El concepto llega SIEMPRE en blanco: lo escribe el
    /// usuario en la factura (antes se rellenaba con el nombre de la categoría).
    /// </summary>
    public class FacturaLineaValidada
    {
        public string CategoriaId { get; set; } = "";
        public string Concepto    { get; set; } = "";
        public double Importe     { get; set; }
    }
}
