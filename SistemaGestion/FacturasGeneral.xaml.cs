using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class FacturasGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private string _mesActivo  = "";
        private string _modoFiltro = "filtros"; // "filtros" = Tree1 | "busquedas" = TxtBuscar

        /// <summary>
        /// Tipo de movimiento fijo para este control ("ingreso" o "egreso"). Lo fija
        /// la sección del panel lateral (Ingresos / Egresos): el listado queda
        /// filtrado y las facturas nuevas nacen con ese movimiento. Vacío = la
        /// pantalla lista ingresos y egresos juntos.
        /// </summary>
        public string TipoMovimiento { get; set; } = "";

        private bool _iniciado = false;

        public FacturasGeneral() : this("") { }

        public FacturasGeneral(string tipoMovimiento)
        {
            InitializeComponent();
            TipoMovimiento = (tipoMovimiento ?? "").ToLower();
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; ConfigurarModo(); CargarMeses(); CargarFacturas(); };
        }

        private void ConfigurarModo()
        {
            if (!AppState.EsAdmin) BtnEliminar.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(TipoMovimiento)) return;

            LblTitulo.Text = TipoMovimiento == "egreso" ? "Facturas de Egresos" : "Facturas de Ingresos";
        }

        // ─── Carga el árbol de meses ──────────────────────────────────────────
        private void CargarMeses()
        {
            // Qué estaba seleccionado ANTES de reconstruir el árbol: null = todavía no se
            // seleccionó nada (primera carga); "" = nodo raíz "Todos"; nombre de mes = ese mes.
            object? tagPrevio = (Tree1.SelectedItem as TreeViewItem)?.Tag;

            Tree1.Items.Clear();
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };

            int año = AppState.DataFechaFinal.Year > 2000
                ? AppState.DataFechaFinal.Year
                : DateTime.Now.Year;

            // Solo los meses que tienen documentos cargados (igual que CorreccionesGeneral).
            var mesesConDatos = new SortedSet<int>();
            int uf = Sql.DocumentosFObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosFObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // MISMO filtro de movimiento que CargarFacturas: sin esto, la sección
                // Egresos arma el árbol con los meses de los ingresos (y al revés),
                // quedando el árbol lleno con el grid vacío.
                string movArbol = NormalizarMovimiento(Sql.DocumentosFObj.ObtenerItem("movimiento", id)?.ToString());
                if (!string.IsNullOrEmpty(ObtenerFiltroTipo()) &&
                    !string.Equals(movArbol, ObtenerFiltroTipo(), StringComparison.OrdinalIgnoreCase)) continue;

                string suc = Sql.DocumentosFObj.ObtenerItem("sucursal", id)?.ToString() ?? "";
                if (suc != AppState.SucursalActiva) continue;
                var fechaObj = Sql.DocumentosFObj.ObtenerItem("fecha", id);
                if (fechaObj == null) continue;
                mesesConDatos.Add(Convert.ToDateTime(fechaObj).Month);
            }

            // Si no hay documentos en el período, el árbol queda vacío (no se muestra el año).
            if (mesesConDatos.Count == 0) { _mesActivo = ""; return; }

            // Nodo padre con el año/período activo → muestra todos los meses (Tag vacío = sin filtro)
            var nodoGeneral = new TreeViewItem
            {
                Header     = año.ToString(),
                Tag        = "",
                IsExpanded = true
            };
            foreach (int mes in mesesConDatos)
                nodoGeneral.Items.Add(new TreeViewItem { Header = meses[mes - 1], Tag = meses[mes - 1] });

            Tree1.Items.Add(nodoGeneral);

            bool SeleccionarMes(string nombreMes)
            {
                foreach (var item in nodoGeneral.Items)
                {
                    if (item is not TreeViewItem ti || (string)ti.Tag != nombreMes) continue;
                    ti.IsSelected = true;
                    _mesActivo = nombreMes;
                    return true;
                }
                return false;
            }

            string? mesActualNombre = mesesConDatos.Contains(DateTime.Now.Month)
                ? meses[DateTime.Now.Month - 1]
                : null;

            if (tagPrevio is string tagVacio && tagVacio == "")
            {
                // Se estaba viendo "Todos" (nodo raíz): conservarlo tal cual.
                nodoGeneral.IsSelected = true;
                _mesActivo = "";
            }
            else if (tagPrevio is string mesPrevio && SeleccionarMes(mesPrevio))
            {
                // Mes previamente activo conservado (p. ej. al volver de guardar/editar).
            }
            else if (mesActualNombre != null)
            {
                // Primera carga, o el mes previo ya no tiene documentos: usar el mes actual.
                SeleccionarMes(mesActualNombre);
            }
            else
            {
                _mesActivo = "";
            }
        }

        // ─── Carga la lista de facturas ────────────────────────────────────────
        public void CargarFacturas()
        {
            if (TxtBuscar == null || Grid1 == null) return;

            var lista = new List<FacturaFila>();
            int linea = 1;
            double totalMonto = 0;
            // Fila más reciente del listado: es la que queda seleccionada al terminar
            // de cargar (ver SeleccionarFilaMasReciente).
            DateTime fechaMax = DateTime.MinValue;
            FacturaFila? filaMasReciente = null;
            string busqueda  = _modoFiltro == "busquedas" ? TxtBuscar.Text.Trim().ToLower() : "";
            string mesFiltro = _modoFiltro == "filtros"   ? _mesActivo : "";
            string filtroEstado = ObtenerFiltroEstado();
            string filtroCuenta = ObtenerFiltroCuenta();
            string filtroTipo   = ObtenerFiltroTipo();

            int uf = Sql.DocumentosFObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosFObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // Filtrar por tipo de movimiento y sucursal activa
                string movDoc = NormalizarMovimiento(Sql.DocumentosFObj.ObtenerItem("movimiento", id)?.ToString());
                if (!string.IsNullOrEmpty(filtroTipo) &&
                    !string.Equals(movDoc, filtroTipo, StringComparison.OrdinalIgnoreCase)) continue;

                string suc = Sql.DocumentosFObj.ObtenerItem("sucursal", id)?.ToString() ?? "";
                if (suc != AppState.SucursalActiva) continue;

                // Filtro por mes (solo en modo "filtros", independiente de TxtBuscar)
                var fechaDocObj = Sql.DocumentosFObj.ObtenerItem("fecha", id);
                DateTime fechaDoc = fechaDocObj != null ? Convert.ToDateTime(fechaDocObj) : default;
                if (!string.IsNullOrEmpty(mesFiltro))
                {
                    if (fechaDocObj == null) continue;
                    string mesDoc = ObtenerNombreMes(fechaDoc.Month);
                    if (!string.Equals(mesDoc, mesFiltro, StringComparison.OrdinalIgnoreCase)) continue;
                }

                string estado  = (Sql.DocumentosFObj.ObtenerItem("estado",  id)?.ToString() ?? "pendiente").ToLower();
                string estadoC = (Sql.DocumentosFObj.ObtenerItem("estadoC", id)?.ToString() ?? "pendiente").ToLower();

                // Filtro por estado
                if (!string.IsNullOrEmpty(filtroEstado) &&
                    !string.Equals(estado, filtroEstado, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filtro por cuenta (estadoC)
                if (!string.IsNullOrEmpty(filtroCuenta) &&
                    !string.Equals(estadoC, filtroCuenta, StringComparison.OrdinalIgnoreCase))
                    continue;

                string codigo      = Sql.DocumentosFObj.ObtenerItem("codigo", id)?.ToString() ?? "";
                string referencia  = Sql.DocumentosFObj.ObtenerItem("referencia", id)?.ToString() ?? "";
                string terceroId   = Sql.DocumentosFObj.ObtenerItem("tercero", id)?.ToString() ?? "";
                string terceroDesc = Sql.TercerosObj.ObtenerItem("descripcion", terceroId)?.ToString() ?? "";

                // Filtro por búsqueda
                if (!string.IsNullOrEmpty(busqueda))
                    if (!codigo.ToLower().Contains(busqueda) && !referencia.ToLower().Contains(busqueda)
                        && !terceroDesc.ToLower().Contains(busqueda))
                        continue;

                double monto = CalcularMonto(id);

                // Pedido de origen: documentosF.relacion apunta al documentosP facturado.
                string relacion  = Sql.DocumentosFObj.ObtenerItem("relacion", id)?.ToString() ?? "";
                string pedidoCod = relacion == ""
                                   ? ""
                                   : Sql.DocumentosPObj.ObtenerItem("codigo", relacion)?.ToString() ?? "";

                lista.Add(new FacturaFila
                {
                    Linea        = linea++,
                    Id           = id,
                    Codigo       = codigo,
                    Fecha        = fechaDoc,
                    FechaStr     = fechaDoc != default ? $"{fechaDoc:d} {fechaDoc:HH:mm:ss}" : "",
                    Referencia   = referencia,
                    PedidoCodigo = pedidoCod,
                    TerceroDesc  = terceroDesc,
                    Movimiento   = movDoc,
                    MontoTotal   = monto,
                    Estado       = estado,
                    EstadoC      = estadoC
                });

                if (fechaDoc >= fechaMax) { fechaMax = fechaDoc; filaMasReciente = lista[^1]; }

                totalMonto += monto;
            }

            Grid1.ItemsSource        = lista;
            TxtTotalImporte.Text     = totalMonto.ToString("N2");
            TxtTotalDocumentos.Text  = lista.Count.ToString("N0");
            TxtEstadosPendientes.Text = lista.Count(f => f.Estado == "pendiente").ToString();
            TxtCuentasPendientes.Text = lista.Count(f => f.EstadoC == "pendiente" || f.EstadoC == "pendiente parcial").ToString();
            int año = AppState.DataFechaFinal.Year > 2000
                ? AppState.DataFechaFinal.Year
                : DateTime.Now.Year;
            LblSubtitulo.Text = string.IsNullOrEmpty(_mesActivo)
                ? año.ToString()
                : $"{_mesActivo} {año}";

            OcultarDetalle();
            SeleccionarFilaMasReciente(filaMasReciente);
        }

        // ─── Filtros ──────────────────────────────────────────────────────────
        private string ObtenerFiltroEstado()
        {
            if (BtnFiltroPendiente?.IsChecked == true) return "pendiente";
            if (BtnFiltroEntregado?.IsChecked == true) return "entregado";
            return "";
        }

        private string ObtenerFiltroCuenta()
        {
            if (BtnCuentaPendiente?.IsChecked == true) return "pendiente";
            if (BtnCuentaCancelado?.IsChecked == true) return "cancelado";
            if (BtnCuentaParcial?.IsChecked   == true) return "pendiente parcial";
            return "";
        }

        private void FiltroEstado_Checked(object sender, RoutedEventArgs e)
            => CargarFacturas();

        private void FiltroCuenta_Checked(object sender, RoutedEventArgs e)
            => CargarFacturas();

        // El movimiento lo fija la sección del panel lateral; vacío = ingresos y egresos.
        private string ObtenerFiltroTipo() => TipoMovimiento;

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
        // Las selecciones explícitas de guardar/editar/eliminar corren DESPUÉS de
        // CargarFacturas(), así que siguen ganando sobre ésta.
        private void SeleccionarFilaMasReciente(FacturaFila? fila)
        {
            if (fila == null) return;
            Grid1.SelectedItem = fila;
            Grid1.ScrollIntoView(fila);

            // Foco de teclado en la fila: seleccionarla solo la pinta, sin foco las
            // flechas siguen moviendo otro control. Mismo helper que ya usan
            // Nuevo/Editar/Eliminar (GridFocusHelper: difiere a prioridad Background
            // y saltea columnas ocultas). NO roba el foco si el usuario está
            // tecleando en la búsqueda o navegando el árbol de meses con el teclado.
            if (TxtBuscar.IsKeyboardFocusWithin || Tree1.IsKeyboardFocusWithin) return;
            GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
        }

        /// <summary>
        /// Movimiento de una factura, normalizado a "ingreso"/"egreso". Las
        /// facturas creadas antes de este cambio guardaron "venta"/"compra": se
        /// leen como su equivalente (la mercadería de una compra entra, la de una
        /// venta sale) para que sigan apareciendo en su listado.
        /// </summary>
        private static string NormalizarMovimiento(string? mov) =>
            (mov ?? "").ToLower() switch
            {
                "egreso" or "venta" => "egreso",
                _                   => "ingreso"
            };

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
        }

        // ─── Helpers de actualización incremental del Grid1 ───────────────────
        private List<FacturaFila> FilasGrid =>
            Grid1.ItemsSource as List<FacturaFila> ?? new List<FacturaFila>();

        // ─── Suma el monto total de las líneas de un documento ────────────────
        private static double CalcularMonto(string documentoF)
        {
            double monto = 0;
            int uf = Sql.FacturasObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.FacturasObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.FacturasObj.ObtenerItem("documentoF", id)?.ToString() != documentoF) continue;
                monto += Convert.ToDouble(Sql.FacturasObj.ObtenerItem("monto", id) ?? 0);
            }
            return monto;
        }

        // ─── Panel de detalle de líneas (Lista2) ──────────────────────────────
        private void MostrarDetalle(string documentoF)
        {
            string codigoDoc = Sql.DocumentosFObj.ObtenerItem("codigo", documentoF)?.ToString() ?? documentoF;
            LblDetalleHeader.Text = $"Líneas del documento {codigoDoc}";
            var detalles = new List<FacturaDetalleFila>();
            int linea = 1;

            int uf = Sql.FacturasObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.FacturasObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.FacturasObj.ObtenerItem("documentoF", id)?.ToString() != documentoF) continue;

                string categoriaId = Sql.FacturasObj.ObtenerItem("categoria", id)?.ToString() ?? "";
                string categoriaDesc = string.IsNullOrEmpty(categoriaId)
                    ? ""
                    : Sql.CategoriasObj.ObtenerItem("descripcion", categoriaId)?.ToString() ?? "";

                detalles.Add(new FacturaDetalleFila
                {
                    Linea     = linea++,
                    Concepto  = Sql.FacturasObj.ObtenerItem("concepto", id)?.ToString() ?? "",
                    Categoria = categoriaDesc,
                    Importe   = Convert.ToDouble(Sql.FacturasObj.ObtenerItem("importe", id) ?? 0)
                });
            }

            Lista2.ItemsSource = detalles;
        }

        private void OcultarDetalle()
        {
            LblDetalleHeader.Text = "Líneas del documento";
            Lista2.ItemsSource    = null;
        }

        // ─── Selección en Grid1 → mostrar detalle ────────────────────────────
        private void Grid1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Grid1.SelectedItem is FacturaFila fila)
                MostrarDetalle(fila.Id);
            else
                OcultarDetalle();
        }

        // ─── Eventos de árbol ─────────────────────────────────────────────────
        private void Tree1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (Tree1.SelectedItem is TreeViewItem ti)
                _mesActivo = ti.Tag?.ToString() ?? "";
            _modoFiltro = "filtros";   // Tree1 activo → ignora TxtBuscar
            CargarFacturas();
        }

        private void Tree1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not TreeViewItem)
                source = VisualTreeHelper.GetParent(source);

            if (source is TreeViewItem tvi && tvi.IsSelected)
            {
                _modoFiltro = "filtros";
                CargarFacturas();
            }
        }

        // ─── Búsqueda (independiente del Tree1) ──────────────────────────────
        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _modoFiltro = "busquedas";
            CargarFacturas();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            _modoFiltro = "busquedas";
            CargarFacturas();
        }

        // ─── Doble clic / Enter = editar ──────────────────────────────────────
        private void Grid1_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AbrirEditar();
        }

        private void Grid1_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            AbrirEditar();
        }

        // ─── Botones ──────────────────────────────────────────────────────────
        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            AbrirNuevaFactura(consola, desdePedidoId: "", lineas: null);
        }

        // Abre la pestaña "Validar pedido"; al validar, crea una factura nueva con
        // los datos generales copiados del pedido y una línea por categoría (los
        // ítems tildados se suman por categoría — ver ValidarPedido).
        private void BtnValidarPedido_Click(object sender, RoutedEventArgs e)
        {
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;

            ValidarPedido.PedidoValidado  = null;
            ValidarPedido.LineasValidadas = null;
            string contexto = TipoMovimiento == "egreso"  ? "Facturas de Egresos"
                              : TipoMovimiento == "ingreso" ? "Facturas de Ingresos" : "Facturas";
            // Los pedidos siguen siendo venta/compra. El criterio es el de la
            // mercadería, no el del dinero: una compra entra (ingreso) y una venta
            // sale (egreso).
            string movPedido = TipoMovimiento switch
            {
                "ingreso" => "compra",
                "egreso"  => "venta",
                _         => ""
            };
            ValidarPedido.OpenAsDialog(consola, contexto: contexto, llamador: this,
                                       movimiento: movPedido, onCerrado: () =>
            {
                string pedidoId = ValidarPedido.PedidoValidado ?? "";
                var lineas      = ValidarPedido.LineasValidadas;
                if (pedidoId == "" || lineas == null || lineas.Count == 0) return;

                AbrirNuevaFactura(consola, pedidoId, lineas);
            });
        }

        private void AbrirNuevaFactura(ConsolaMovimientos consola, string desdePedidoId,
                                       List<FacturaLineaValidada>? lineas)
        {
            string titulo = "Nueva Factura";
            string clave  = "nueva-factura";
            var dlg = new FacturasDetalle(this, tituloTab: titulo,
                                          desdePedidoId: desdePedidoId,
                                          lineasDesdePedido: lineas,
                                          movimientoInicial: TipoMovimiento);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.ItemCreadoId == null) return;
                // Recarga completa (no incremental): así el árbol de meses refleja el
                // mes del documento recién creado y el listado respeta los filtros activos.
                CargarMeses();
                CargarFacturas();
                var creada = FilasGrid.FirstOrDefault(f => f.Id == dlg.ItemCreadoId);
                if (creada != null) { Grid1.SelectedItem = creada; Grid1.ScrollIntoView(creada); }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(titulo, dlg, clave);
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
            => AbrirEditar();

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (Grid1.SelectedItem is not FacturaFila fila) return;

            // Verificación de conexión en 2 capas antes de persistir el borrado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            var res = MessageBox.Show("¿Eliminar esta factura y todas sus líneas?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                int idxPrevio = FilasGrid.IndexOf(fila);

                // Ocultar todas las líneas de este documentoF
                int uf = Sql.FacturasObj.ContarFilas;
                var idsOcultar = new List<string>();
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.FacturasObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    if (Sql.FacturasObj.ObtenerItem("documentoF", id)?.ToString() == fila.Id)
                        idsOcultar.Add(id);
                }
                foreach (string id in idsOcultar)
                    Sql.FacturasObj.Ocultar(id);

                // Ocultar el documento de factura
                Sql.DocumentosFObj.EstablecerItem("edicion",  fila.Id, DateTime.Now);
                Sql.DocumentosFObj.EstablecerItem("usuarioE", fila.Id, AppState.UsuarioActivo);
                Sql.DocumentosFObj.Ocultar(fila.Id);

                Sql.FacturasObj.OrdenarData(("documentoF", false), ("indice", false));
                Sql.DocumentosFObj.OrdenarData(("fecha", false));

                // Recarga completa: el documento eliminado pudo ser el último de su mes,
                // así que el árbol y el listado deben rehacerse (igual que nuevo/editar).
                CargarMeses();
                CargarFacturas();

                var lista = FilasGrid;
                if (lista.Count > 0)
                {
                    var sel = lista[Math.Min(idxPrevio, lista.Count - 1)];
                    Grid1.SelectedItem = sel; Grid1.ScrollIntoView(sel);
                }
                else OcultarDetalle();
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.DocumentosFObj.Actualizar();
            Sql.FacturasObj.Actualizar();
            CargarFacturas();
        }

        // ─── Helper ───────────────────────────────────────────────────────────
        private void AbrirEditar()
        {
            if (Grid1.SelectedItem is not FacturaFila fila) return;

            string idSel = fila.Id;

            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = $"Factura {fila.Codigo}";
            var dlg = new FacturasDetalle(this, idSel, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                // Recarga completa (no incremental): la edición pudo cambiar el mes
                // (fecha) del documento, así que el árbol y el listado deben rehacerse.
                CargarMeses();
                CargarFacturas();
                var actualizada = FilasGrid.FirstOrDefault(f => f.Id == idSel);
                if (actualizada != null) { Grid1.SelectedItem = actualizada; Grid1.ScrollIntoView(actualizada); }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(titulo, dlg, $"factura-{idSel}");
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class FacturaFila
    {
        public int      Linea        { get; set; }
        public string   Id           { get; set; } = "";
        public string   Codigo       { get; set; } = "";
        public DateTime Fecha        { get; set; }
        public string   FechaStr     { get; set; } = "";
        public string   Referencia   { get; set; } = "";
        public string   TerceroDesc  { get; set; } = "";
        public string   PedidoCodigo { get; set; } = "";
        public string   Movimiento   { get; set; } = "ingreso";
        public double   MontoTotal   { get; set; }
        public string   Estado       { get; set; } = "pendiente";
        public string   EstadoC      { get; set; } = "pendiente";
    }

    public class FacturaDetalleFila
    {
        public int    Linea     { get; set; }
        public string Concepto  { get; set; } = "";
        public string Categoria { get; set; } = "";
        public double Importe   { get; set; }
    }
}
