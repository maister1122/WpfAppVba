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
    public partial class PedidosGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private string _mesActivo  = "";
        private string _modoFiltro = "filtros"; // "filtros" = Tree1 | "busquedas" = TxtBuscar

        /// <summary>
        /// Tipo de movimiento fijo para este control ("venta" o "compra"). Lo fija
        /// la sección del panel lateral (Ventas / Compras)
        /// y es la única forma de elegirlo: ya no hay combo "Movimiento" en pantalla.
        /// Vacío = la pantalla lista ventas y compras juntas.
        /// </summary>
        public string TipoMovimiento { get; set; } = "";

        private bool _iniciado = false;

        public PedidosGeneral() : this("") { }

        public PedidosGeneral(string tipoMovimiento)
        {
            InitializeComponent();
            TipoMovimiento = (tipoMovimiento ?? "").ToLower();
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; ConfigurarModo(); CargarMeses(); CargarPedidos(); };
        }

        // BtnEliminar queda visible para todos: además del administrador, el
        // usuario que creó el documento también puede eliminarlo/ocultarlo — el
        // permiso se valida por documento en BtnEliminar_Click (ver ES_CREADOR).
        private void ConfigurarModo() { }

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

            // Solo los meses que tienen documentos cargados (igual que PreciosGeneral).
            var mesesConDatos = new SortedSet<int>();
            int uf = Sql.DocumentosPObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosPObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // MISMO filtro de movimiento que CargarPedidos: sin esto, la sección
                // Compras arma el árbol con los meses de las ventas y queda el árbol
                // lleno con el grid vacío.
                string movDoc = Sql.DocumentosPObj.ObtenerItem("movimiento", id)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(ObtenerFiltroTipo()) &&
                    !string.Equals(movDoc, ObtenerFiltroTipo(), StringComparison.OrdinalIgnoreCase)) continue;

                string sucursal = Sql.DocumentosPObj.ObtenerItem("sucursal", id)?.ToString() ?? "";
                if (sucursal != AppState.SucursalActiva) continue;
                var fechaObj = Sql.DocumentosPObj.ObtenerItem("fecha", id);
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
                // Mes previamente activo conservado (p. ej. al volver de guardar/editar/eliminar).
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

        // ─── Carga la lista de pedidos ────────────────────────────────────────
        public void CargarPedidos()
        {
            // Guard: evita correr antes de que todos los controles estén inicializados
            if (TxtBuscar == null || Grid1 == null) return;

            var lista = new List<PedidoFila>();
            int linea = 1;
            double totalCant = 0, totalImporte = 0;
            // Fila más reciente del listado: es la que queda seleccionada al terminar
            // de cargar (ver SeleccionarFilaMasReciente).
            DateTime fechaMax = DateTime.MinValue;
            PedidoFila? filaMasReciente = null;

            string filtroEstado  = ObtenerFiltroEstado();
            string filtroCuenta  = ObtenerFiltroCuenta();
            // Cada modo aplica solo su propio filtro de contenido (igual que VBA llaveActualisar)
            string busqueda = _modoFiltro == "busquedas" ? TxtBuscar.Text.Trim().ToLower() : "";
            string mesFiltro = _modoFiltro == "filtros"  ? _mesActivo : "";
            string tipoMov = ObtenerFiltroTipo();

            int uf = Sql.DocumentosPObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosPObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // Filtrar por tipo de movimiento y sucursal activa
                string movDoc = Sql.DocumentosPObj.ObtenerItem("movimiento", id)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(tipoMov) &&
                    !string.Equals(movDoc, tipoMov, StringComparison.OrdinalIgnoreCase)) continue;

                string sucursal = Sql.DocumentosPObj.ObtenerItem("sucursal", id)?.ToString() ?? "";
                if (sucursal != AppState.SucursalActiva) continue;

                // Filtro por mes (solo en modo "filtros", independiente de TxtBuscar)
                if (!string.IsNullOrEmpty(mesFiltro))
                {
                    var fechaObj2 = Sql.DocumentosPObj.ObtenerItem("fecha", id);
                    if (fechaObj2 == null) continue;
                    DateTime fecha2 = Convert.ToDateTime(fechaObj2);
                    string mesDoc = ObtenerNombreMes(fecha2.Month);
                    if (!string.Equals(mesDoc, mesFiltro, StringComparison.OrdinalIgnoreCase)) continue;
                }

                var fechaDocObj = Sql.DocumentosPObj.ObtenerItem("fecha", id);
                DateTime fechaDoc = fechaDocObj != null ? Convert.ToDateTime(fechaDocObj) : default;

                string terceroId   = Sql.DocumentosPObj.ObtenerItem("tercero", id)?.ToString() ?? "";
                string terceroDesc = Sql.TercerosObj.ObtenerItem("descripcion", terceroId)?.ToString() ?? terceroId;

                string estado = (Sql.DocumentosPObj.ObtenerItem("estado", id)?.ToString() ?? "").ToLower();

                // ── Usar estadoC (campo correcto en VBA) para cuenta ──────────
                string estadoC = (Sql.DocumentosPObj.ObtenerItem("estadoC", id)?.ToString() ?? "").ToLower();

                // Filtro por estado
                if (!string.IsNullOrEmpty(filtroEstado) &&
                    !string.Equals(estado, filtroEstado, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filtro por cuenta (estadoC)
                if (!string.IsNullOrEmpty(filtroCuenta) &&
                    !string.Equals(estadoC, filtroCuenta, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filtro por búsqueda
                if (!string.IsNullOrEmpty(busqueda))
                    if (!id.Contains(busqueda) && !terceroDesc.ToLower().Contains(busqueda))
                        continue;

                double cant    = CalcularCantidad(id);
                double importe = CalcularImporte(id);

                lista.Add(new PedidoFila
                {
                    Linea       = linea++,
                    DocumentoP  = id,
                    Codigo      = Sql.DocumentosPObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                    Fecha       = fechaDoc,
                    FechaStr    = $"{fechaDoc:d} {fechaDoc:HH:mm:ss}",
                    Movimiento  = movDoc,
                    TerceroDesc = terceroDesc,
                    Referencia  = Sql.DocumentosPObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                    Estado      = estado,
                    Cuenta      = estadoC,
                    UsuarioCreador = Sql.DocumentosPObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                    Cantidad    = cant,
                    Importe     = importe
                });

                if (fechaDoc >= fechaMax) { fechaMax = fechaDoc; filaMasReciente = lista[^1]; }

                totalCant    += cant;
                totalImporte += importe;
            }

            Grid1.ItemsSource        = lista;
            TxtTotalCantidad.Text    = totalCant.ToString("N0");
            TxtTotalImporte.Text     = totalImporte.ToString("N2");
            TxtTotalDocumentos.Text  = lista.Count.ToString("N0");
            TxtTotalPendientes.Text  = lista.Count(f => f.Estado == "pendiente"
                                                     || f.Estado == "pendiente parcial").ToString();
            TxtCuentaPendientes.Text = lista.Count(f => f.Cuenta == "pendiente"
                                                     || f.Cuenta == "pendiente parcial").ToString();

            // ── Título correcto según VBA ─────────────────────────────────────
            LblTipoMovimiento.Text = tipoMov switch
            {
                "venta"  => "Ventas de Productos",
                "compra" => "Compras de Productos",
                _        => "Pedidos (Ventas y Compras)"
            };
            ActualizarNomenclatura();
            int año = AppState.DataFechaFinal.Year > 2000
                ? AppState.DataFechaFinal.Year
                : DateTime.Now.Year;
            LblSubtitulo.Text = string.IsNullOrEmpty(_mesActivo)
                ? año.ToString()
                : $"{_mesActivo} {año}";

            // Ocultar el panel de detalle al recargar
            OcultarDetalle();
            SeleccionarFilaMasReciente(filaMasReciente);
        }

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha:
        // es el documento que el usuario acaba de cargar y el que va a mirar.
        // Las selecciones explícitas de guardar/editar/eliminar corren DESPUÉS de
        // CargarPedidos(), así que siguen ganando sobre ésta.
        private void SeleccionarFilaMasReciente(PedidoFila? fila)
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

        // ─── Filtros ──────────────────────────────────────────────────────────
        private string ObtenerFiltroEstado()
        {
            if (BtnFiltroPendiente?.IsChecked == true)      return "pendiente";
            if (BtnFiltroEntregado?.IsChecked == true)      return "entregado";
            if (BtnFiltroEntregaParcial?.IsChecked == true) return "pendiente parcial";
            return "";
        }

        private string ObtenerFiltroCuenta()
        {
            if (BtnCuentaPendiente?.IsChecked == true) return "pendiente";
            if (BtnCuentaCancelado?.IsChecked == true) return "cancelado";
            if (BtnCuentaParcial?.IsChecked   == true) return "pendiente parcial";  // ← correcto
            return "";
        }

        // El movimiento lo fija la sección del panel lateral; vacío = ventas y compras.
        private string ObtenerFiltroTipo() => TipoMovimiento;

        // ─── Nomenclatura visible según la sección activa ─────────────────────
        // La barra lateral fija la sección (Ventas / Compras): ahí los textos deben
        // hablar de "venta"/"compra", no del genérico "pedido". Solo el listado
        // combinado sigue diciendo "pedido". "Venta"/"Compra" son femeninos y
        // "Pedido" masculino, así que también cambian el artículo y el adjetivo.
        private string NombreDoc => ObtenerFiltroTipo() switch
        {
            "venta"  => "Venta",
            "compra" => "Compra",
            _        => "Pedido"
        };

        private bool EsFemenino => NombreDoc != "Pedido";

        /// <summary>"Nueva Venta Rápida" / "Nuevo Pedido Rápido" / …</summary>
        private string TituloNuevoDoc(string tipoPedido)
        {
            string articulo = EsFemenino ? "Nueva" : "Nuevo";
            // "Normal" es invariable; "Rápido/Rápida" concuerda con el sustantivo.
            string variante = tipoPedido == "rapido"
                ? (EsFemenino ? "Rápida" : "Rápido")
                : "Normal";
            return $"{articulo} {NombreDoc} {variante}";
        }

        /// <summary>"esta venta" / "esta compra" / "este pedido".</summary>
        private string EsteDocMinus => $"{(EsFemenino ? "esta" : "este")} {NombreDoc.ToLower()}";

        /// <summary>Nombre del documento a partir de su movimiento real (para pestañas).</summary>
        private static string NombreDocDe(string movimiento) => movimiento switch
        {
            "venta"  => "Venta",
            "compra" => "Compra",
            _        => "Pedido"
        };

        // Refresca los textos que dependen de la sección activa.
        private void ActualizarNomenclatura()
        {
            if (BtnNuevoRapido != null) BtnNuevoRapido.Content = TituloNuevoDoc("rapido");
            if (BtnNuevoNormal != null) BtnNuevoNormal.Content = TituloNuevoDoc("normal");
        }

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
        }

        // ─── Helpers de actualización incremental del Grid1 ───────────────────
        private List<PedidoFila> FilasGrid =>
            Grid1.ItemsSource as List<PedidoFila> ?? new List<PedidoFila>();

        private PedidoFila ConstruirFilaPedido(string id, int linea)
        {
            var fechaDocObj = Sql.DocumentosPObj.ObtenerItem("fecha", id);
            DateTime fechaDoc = fechaDocObj != null ? Convert.ToDateTime(fechaDocObj) : default;
            string terceroId   = Sql.DocumentosPObj.ObtenerItem("tercero", id)?.ToString() ?? "";
            string terceroDesc = Sql.TercerosObj.ObtenerItem("descripcion", terceroId)?.ToString() ?? terceroId;
            string estado  = (Sql.DocumentosPObj.ObtenerItem("estado",  id)?.ToString() ?? "").ToLower();
            string estadoC = (Sql.DocumentosPObj.ObtenerItem("estadoC", id)?.ToString() ?? "").ToLower();
            string movDoc  = Sql.DocumentosPObj.ObtenerItem("movimiento", id)?.ToString() ?? "";

            return new PedidoFila
            {
                Linea       = linea,
                DocumentoP  = id,
                Codigo      = Sql.DocumentosPObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                Fecha       = fechaDoc,
                FechaStr    = $"{fechaDoc:d} {fechaDoc:HH:mm:ss}",
                Movimiento  = movDoc,
                TerceroDesc = terceroDesc,
                Referencia  = Sql.DocumentosPObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                Estado      = estado,
                Cuenta      = estadoC,
                UsuarioCreador = Sql.DocumentosPObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                Cantidad    = CalcularCantidad(id),
                Importe     = CalcularImporte(id)
            };
        }

        private void RenumerarYTotales()
        {
            var lista = FilasGrid;
            int n = 1;
            double totalCant = 0, totalImporte = 0;
            foreach (var f in lista)
            {
                f.Linea       = n++;
                totalCant    += f.Cantidad;
                totalImporte += f.Importe;
            }
            TxtTotalCantidad.Text    = totalCant.ToString("N0");
            TxtTotalImporte.Text     = totalImporte.ToString("N2");
            TxtTotalDocumentos.Text  = lista.Count.ToString("N0");
            TxtTotalPendientes.Text  = lista.Count(f => f.Estado == "pendiente"
                                                     || f.Estado == "pendiente parcial").ToString();
            TxtCuentaPendientes.Text = lista.Count(f => f.Cuenta == "pendiente"
                                                     || f.Cuenta == "pendiente parcial").ToString();
            Grid1.Items.Refresh();
        }

        // ─── Calcular totales de un documentoP ───────────────────────────────
        private static double CalcularCantidad(string documentoP)
        {
            double cant = 0;
            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() != documentoP) continue;
                cant += Convert.ToDouble(Sql.PedidosObj.ObtenerItem("cantidad", id) ?? 0);
            }
            return cant;
        }

        private static double CalcularImporte(string documentoP)
        {
            double importe = 0;
            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() != documentoP) continue;
                // ── Usar la columna importe directamente (igual que VBA) ──────
                importe += Convert.ToDouble(Sql.PedidosObj.ObtenerItem("importe", id) ?? 0);
            }
            return importe;
        }

        // ─── Panel de detalle (Lista2) ────────────────────────────────────────

        private void MostrarDetalle(string documentoP)
        {
            string codigoDoc = Sql.DocumentosPObj.ObtenerItem("codigo", documentoP)?.ToString() ?? documentoP;
            LblDetalleHeader.Text = $"Artículos del documento {codigoDoc}";
            var detalles = new List<PedidoDetalleFila>();
            int linea = 1;

            int uf = Sql.PedidosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PedidosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() != documentoP) continue;

                string articuloId  = Sql.PedidosObj.ObtenerItem("articulo",  id)?.ToString() ?? "";
                string codigo      = Sql.ArticulosObj.ObtenerItem("codigo",      articuloId)?.ToString() ?? articuloId;
                string desc        = Sql.ArticulosObj.ObtenerItem("descripcion", articuloId)?.ToString() ?? "";
                string famId       = Sql.ArticulosObj.ObtenerItem("familia",     articuloId)?.ToString() ?? "";
                string famDesc     = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
                string modelo      = Sql.ArticulosObj.ObtenerItem("modelo",      articuloId)?.ToString() ?? "";
                string descripcion = FuncionesComunes.UnirVariables(desc, famDesc, modelo);
                double cantidad    = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("cantidad", id) ?? 0);
                double importe     = Convert.ToDouble(Sql.PedidosObj.ObtenerItem("importe",  id) ?? 0);

                detalles.Add(new PedidoDetalleFila
                {
                    Linea      = linea++,
                    Codigo     = codigo,
                    Descripcion = descripcion,
                    Cantidad   = cantidad,
                    Importe    = importe.ToString("N2")
                });
            }

            Lista2.ItemsSource = detalles;
        }

        private void OcultarDetalle()
        {
            LblDetalleHeader.Text  = "Artículos del documento";
            Lista2.ItemsSource     = null;
        }

        // ─── Eventos árbol y filtros ──────────────────────────────────────────
        private void Tree1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (Tree1.SelectedItem is TreeViewItem ti)
                _mesActivo = ti.Tag?.ToString() ?? "";
            _modoFiltro = "filtros";   // Tree1 activo → ignora TxtBuscar
            CargarPedidos();
        }

        private void Tree1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Si el usuario hace clic en el item ya seleccionado, SelectedItemChanged no se dispara.
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not TreeViewItem)
                source = VisualTreeHelper.GetParent(source);

            if (source is TreeViewItem tvi && tvi.IsSelected)
            {
                _modoFiltro = "filtros";
                CargarPedidos();
            }
        }

        private void FiltroEstado_Checked(object sender, RoutedEventArgs e)
            => CargarPedidos();

        private void FiltroCuenta_Checked(object sender, RoutedEventArgs e)
            => CargarPedidos();

        // ─── Selección en Grid1 → mostrar detalle ────────────────────────────
        private void Grid1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Grid1.SelectedItem is PedidoFila fila)
                MostrarDetalle(fila.DocumentoP);
            else
                OcultarDetalle();
        }

        // ─── Búsqueda (independiente del Tree1) ──────────────────────────────
        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _modoFiltro = "busquedas"; // TxtBuscar activo → ignora Tree1
            CargarPedidos();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            _modoFiltro = "busquedas"; // TxtBuscar activo → ignora Tree1
            CargarPedidos();
        }

        // ─── Doble clic ───────────────────────────────────────────────────────
        private void Grid1_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AbrirEditar();
        }

        // ─── Tecla Enter ──────────────────────────────────────────────────────
        private void Grid1_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            AbrirEditar();
        }

        // ─── Botones ──────────────────────────────────────────────────────────
        private void BtnNuevoRapido_Click(object sender, RoutedEventArgs e)
            => AbrirNuevoPedido("rapido");

        private void BtnNuevoNormal_Click(object sender, RoutedEventArgs e)
            => AbrirNuevoPedido("normal");

        // tipoMovimiento: si se indica ("venta"/"compra") fuerza el movimiento;
        // si es null se toma del filtro activo. Público para accesos rápidos del top bar.
        public void AbrirNuevoPedido(string tipoPedido, string? tipoMovimiento = null)
        {
            AppState.EventoFormularioM = "nuevo";
            AppState.TipoPedido        = tipoPedido;
            string filtroTipo = tipoMovimiento ?? ObtenerFiltroTipo();
            AppState.TipoMovimiento = string.IsNullOrEmpty(filtroTipo) ? "venta" : filtroTipo;
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            // El título sigue al movimiento REAL del documento que se está creando
            // (AppState.TipoMovimiento), no al filtro: si se llega por un acceso
            // rápido del top bar, el tipoMovimiento recibido manda.
            string nombreMov = NombreDocDe(AppState.TipoMovimiento);
            bool   femenino  = nombreMov != "Pedido";
            string variante  = tipoPedido == "rapido" ? (femenino ? "Rápida" : "Rápido") : "Normal";
            string titulo = $"{(femenino ? "Nueva" : "Nuevo")} {nombreMov} {variante}";
            // La clave incluye movimiento y tipo: con una sola clave compartida,
            // abrir una venta teniendo abierta una compra (o el normal teniendo
            // abierto el rápido) reutilizaba esa pestaña —AbrirPestaña matchea por
            // clave— y caías en el formulario equivocado.
            string clave  = $"nuevo-pedido-{AppState.TipoMovimiento}-{tipoPedido}";
            var dlg = new PedidosDetalle(this, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.DocumentoCreadoId == null) return;
                var nueva = ConstruirFilaPedido(dlg.DocumentoCreadoId, 0);
                FilasGrid.Add(nueva);
                RenumerarYTotales();
                Grid1.SelectedItem = nueva; Grid1.ScrollIntoView(nueva);
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(titulo, dlg, clave);
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
            => AbrirEditar();

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (Grid1.SelectedItem is not PedidoFila fila) return;

            // Solo el administrador o el usuario que creó el documento puede eliminarlo.
            if (!AppState.EsAdmin && fila.UsuarioCreador != AppState.UsuarioActivo)
            {
                MessageBox.Show("Solo un administrador o el usuario que creó este documento puede eliminarlo.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verificación de conexión en 2 capas antes de persistir el borrado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            var res = MessageBox.Show($"¿Eliminar {EsteDocMinus} y todos sus artículos?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                int idxPrevio = FilasGrid.IndexOf(fila);

                Sql.DocumentosPObj.EstablecerItem("edicion",  fila.DocumentoP, DateTime.Now);
                Sql.DocumentosPObj.EstablecerItem("usuarioE", fila.DocumentoP, AppState.UsuarioActivo);
                Sql.DocumentosPObj.Ocultar(fila.DocumentoP);

                int uf = Sql.PedidosObj.ContarFilas;
                var idsOcultar = new List<string>();
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.PedidosObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    if (Sql.PedidosObj.ObtenerItem("documentoP", id)?.ToString() == fila.DocumentoP)
                        idsOcultar.Add(id);
                }
                foreach (var id in idsOcultar)
                    Sql.PedidosObj.Ocultar(id);

                Sql.DocumentosPObj.OrdenarData(("fecha", false));
                Sql.PedidosObj.OrdenarData(("documentoP", false), ("indice", false));

                // Recarga completa: el documento eliminado pudo ser el último de su mes,
                // así que el árbol y el listado deben rehacerse (igual que nuevo/editar).
                CargarMeses();
                CargarPedidos();

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
                MessageBox.Show($"Error al eliminar: {ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.DocumentosPObj.Actualizar();
            Sql.PedidosObj.Actualizar();
            CargarPedidos();
        }

        private void AbrirEditar()
        {
            if (Grid1.SelectedItem is not PedidoFila fila) return;
            string docSel  = fila.DocumentoP;
            int    linea   = fila.Linea;
            AppState.EventoFormularioM = "editar";
            AppState.TipoMovimiento = Sql.DocumentosPObj.ObtenerItem("movimiento", docSel)?.ToString() ?? "venta";
            AppState.TipoPedido     = Sql.DocumentosPObj.ObtenerItem("tipo",       docSel)?.ToString() ?? "rapido";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            // El título usa el movimiento REAL del documento (ya resuelto arriba en
            // AppState.TipoMovimiento), no el filtro activo: en el listado combinado
            // conviven ventas y compras y cada pestaña debe decir lo que es.
            string tituloDoc = $"{NombreDocDe(AppState.TipoMovimiento)} {fila.Codigo}";
            var dlg = new PedidosDetalle(this, docSel, tituloTab: tituloDoc);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0)
                {
                    var actualizada = ConstruirFilaPedido(docSel, linea);
                    lista[idx] = actualizada;
                    RenumerarYTotales();
                    Grid1.SelectedItem = actualizada; Grid1.ScrollIntoView(actualizada);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(tituloDoc, dlg, $"pedido-{docSel}");
        }
    }

    // ─── Modelo de fila principal ─────────────────────────────────────────────
    public class PedidoFila
    {
        public int    Linea       { get; set; }
        public string DocumentoP  { get; set; } = "";
        public string Codigo      { get; set; } = "";
        public DateTime Fecha     { get; set; }
        public string FechaStr    { get; set; } = "";
        public string Movimiento  { get; set; } = "";
        public string TerceroDesc { get; set; } = "";
        public string Referencia  { get; set; } = "";
        public string Estado      { get; set; } = "";
        public string Cuenta      { get; set; } = "";
        public string UsuarioCreador { get; set; } = "";
        public double Cantidad    { get; set; }
        public double Importe     { get; set; }
    }

    // ─── Modelo de fila de detalle (Lista2) ──────────────────────────────────
    public class PedidoDetalleFila
    {
        public int    Linea       { get; set; }
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public double Cantidad    { get; set; }
        public string Importe     { get; set; } = "";
    }
}
