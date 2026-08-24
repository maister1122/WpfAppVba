using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaGestion;
using VisorEmpresa.Data;

namespace VisorEmpresa
{
    /// <summary>
    /// Duplicado de SistemaGestion.PedidosGeneral para el visor: misma UI/lógica de
    /// listado (Tree1 de meses, filtros, grilla, detalle), pero de SOLO LECTURA
    /// (sin Nuevo/Editar/Eliminar) y con un combo de Sucursal (Todas las
    /// sucursales o una puntual) en vez de depender de AppState.SucursalActiva.
    /// La caché (Sql.DocumentosPObj/PedidosObj) se puebla vía
    /// ConsultasEmpresa.ConectarCachePedidos para TODA la empresa (precalentada
    /// al loguear); el combo de Sucursal filtra en memoria, sin volver a SQL.
    /// </summary>
    public partial class PedidosGeneral : UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private string _mesActivo      = "";
        private string _modoFiltro     = "filtros"; // "filtros" = Tree1 | "busquedas" = TxtBuscar
        private string _sucursalFiltro = "";         // "" = Todas las sucursales

        private bool _iniciado;
        private bool _cargandoFiltros;

        private class Opcion
        {
            public string Id    { get; }
            public string Texto { get; }
            public Opcion(string id, string texto) { Id = id; Texto = texto; }
            public override string ToString() => Texto;
        }

        /// <summary>
        /// Tipo de movimiento fijo para este control ("venta" o "compra"). Lo fija la
        /// sección del panel lateral (Ventas / Compras), igual que en SistemaGestión.
        /// Vacío = la pantalla lista los dos juntos.
        /// </summary>
        public string TipoMovimiento { get; set; } = "";

        public PedidosGeneral() : this("") { }

        public PedidosGeneral(string tipoMovimiento)
        {
            InitializeComponent();
            TipoMovimiento = (tipoMovimiento ?? "").ToLower();
            Loaded += async (_, _) =>
            {
                if (_iniciado) return;
                _iniciado = true;
                await IniciarAsync();
            };
        }

        // ─── Carga inicial: combo de sucursales + primera consulta ───────────
        private async Task IniciarAsync()
        {
            _cargandoFiltros = true;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                string emp = AppState.EmpresaActiva;
                var sucursales = await Task.Run(() => ConsultasEmpresa.CargarSucursalesEmpresa(emp));
                var opciones = new List<Opcion> { new("", "Todas las sucursales") };
                opciones.AddRange(sucursales.Select(s => new Opcion(s.Id, s.Descripcion)));
                CmbSucursal.ItemsSource   = opciones;
                CmbSucursal.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron cargar las sucursales:\n{ex.Message}",
                                "Visor Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _cargandoFiltros = false;
            }

            await RecargarCacheYVistaAsync();
        }

        /// <summary>
        /// Recarga con los filtros globales actuales (empresa/año de la top bar).
        /// La llama la consola al cambiar el año; si el panel aún no se abrió,
        /// cargará solo con los valores vigentes en su primer Loaded.
        /// </summary>
        public async void RefrescarDatos()
        {
            if (!_iniciado) return;
            await RecargarCacheYVistaAsync();
        }

        // Siempre carga TODA la empresa (sin filtro de sucursal): al loguear ya se
        // precalienta con esta misma clave (empresa, año) — ver LoginVisorWindow —
        // así que, salvo la primerísima vez, esto es un no-op (memoización en
        // ConsultasEmpresa) y solo faltan CargarMeses/CargarPedidos. El combo de
        // Sucursal ya NO dispara esta recarga (ver Filtro_SelectionChanged):
        // filtra en memoria sobre los datos de toda la empresa ya cargados.
        private async Task RecargarCacheYVistaAsync()
        {
            string emp  = AppState.EmpresaActiva;
            int    anio = VisorState.AnioActivo;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => ConsultasEmpresa.ConectarCachePedidos(emp, anio));
                CargarMeses();
                CargarPedidos();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron cargar los pedidos:\n{ex.Message}",
                                "Visor Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Cambiar el combo de Sucursal ya NO vuelve a consultar SQL: Sql.DocumentosPObj
        // ya tiene TODA la empresa cargada, así que solo hace falta refiltrar en
        // memoria (CargarMeses/CargarPedidos ya consideran _sucursalFiltro).
        private void Filtro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros || !_iniciado) return;
            _sucursalFiltro = (CmbSucursal.SelectedItem as Opcion)?.Id ?? "";
            CargarMeses();
            CargarPedidos();
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

            int año = VisorState.AnioActivo;

            // Solo los meses que tienen documentos cargados (de la sucursal filtrada,
            // o de toda la empresa si _sucursalFiltro está vacío).
            var mesesConDatos = new SortedSet<int>();
            int uf = Sql.DocumentosPObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosPObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (!string.IsNullOrEmpty(_sucursalFiltro) &&
                    Sql.DocumentosPObj.ObtenerItem("sucursal", id)?.ToString() != _sucursalFiltro)
                    continue;

                // MISMO filtro de movimiento que CargarPedidos: sin esto, la sección
                // Compras arma el árbol con los meses de las ventas y queda el árbol
                // lleno con el grid vacío.
                string movDoc = Sql.DocumentosPObj.ObtenerItem("movimiento", id)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(ObtenerFiltroTipo()) &&
                    !string.Equals(movDoc, ObtenerFiltroTipo(), StringComparison.OrdinalIgnoreCase)) continue;

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
                // Mes previamente activo conservado.
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
            string busqueda  = _modoFiltro == "busquedas" ? TxtBuscar.Text.Trim().ToLower() : "";
            string mesFiltro = _modoFiltro == "filtros"  ? _mesActivo : "";
            string tipoMov = ObtenerFiltroTipo();

            int uf = Sql.DocumentosPObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosPObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // Filtrar por tipo de movimiento
                string movDoc = Sql.DocumentosPObj.ObtenerItem("movimiento", id)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(tipoMov) &&
                    !string.Equals(movDoc, tipoMov, StringComparison.OrdinalIgnoreCase)) continue;

                string sucursal = Sql.DocumentosPObj.ObtenerItem("sucursal", id)?.ToString() ?? "";

                // Filtro por sucursal (Sql.DocumentosPObj trae TODA la empresa: ver
                // ConsultasEmpresa.ConectarCachePedidos y RecargarCacheYVistaAsync).
                if (!string.IsNullOrEmpty(_sucursalFiltro) && sucursal != _sucursalFiltro) continue;

                string sucursalDesc = Sql.SucursalesObj.ObtenerItem("descripcion", sucursal)?.ToString() ?? sucursal;

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
                    Linea        = linea++,
                    DocumentoP   = id,
                    Codigo       = Sql.DocumentosPObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                    Fecha        = fechaDoc,
                    FechaStr     = $"{fechaDoc:d} {fechaDoc:HH:mm:ss}",
                    Movimiento   = movDoc,
                    SucursalDesc = sucursalDesc,
                    TerceroDesc  = terceroDesc,
                    Estado       = estado,
                    Cuenta       = estadoC,
                    Cantidad     = cant,
                    Importe      = importe
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
            int año = VisorState.AnioActivo;
            LblSubtitulo.Text = string.IsNullOrEmpty(_mesActivo)
                ? año.ToString()
                : $"{_mesActivo} {año}";

            // Ocultar el panel de detalle al recargar
            OcultarDetalle();
            SeleccionarFilaMasReciente(filaMasReciente);
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
            if (BtnCuentaParcial?.IsChecked   == true) return "pendiente parcial";
            return "";
        }

        /// <summary>Nombre del documento a partir de su movimiento real (para pestañas).</summary>
        private static string NombreDocDe(string movimiento) => movimiento switch
        {
            "venta"  => "Venta",
            "compra" => "Compra",
            _        => "Pedido"
        };

        // El movimiento lo fija la sección del panel lateral; vacío = los dos juntos.
        private string ObtenerFiltroTipo() => TipoMovimiento;

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
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

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
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

        // ─── Doble clic / Enter → ver documento completo (solo lectura) ──────
        private void Grid1_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AbrirVerDetalle();
        }

        private void Grid1_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            AbrirVerDetalle();
        }

        // Abre PedidosDetalle (formulario real de la app principal, vinculado) en
        // modo solo lectura: mismos campos/tabs (Artículos, Cobros, Entregas) que
        // la app principal, sin Guardar ni edición. AppState.TipoMovimiento/
        // TipoPedido se leen del propio documento (igual que AbrirEditar en la app
        // principal), ya que el visor no tiene un único "tipo" activo de sesión.
        private void AbrirVerDetalle()
        {
            if (Grid1.SelectedItem is not PedidoFila fila) return;
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;

            AppState.EventoFormularioM = "editar";
            AppState.TipoMovimiento = Sql.DocumentosPObj.ObtenerItem("movimiento", fila.DocumentoP)?.ToString() ?? "venta";
            AppState.TipoPedido     = Sql.DocumentosPObj.ObtenerItem("tipo",       fila.DocumentoP)?.ToString() ?? "rapido";

            // El título sigue al movimiento REAL del documento, no al filtro: el
            // listado combinado mezcla ventas y compras.
            string titulo = $"{NombreDocDe(AppState.TipoMovimiento)} {fila.Codigo}";
            var dlg = new PedidosDetalle(null, fila.DocumentoP, tituloTab: titulo);
            dlg.Cerrando += () => consola.CerrarPestaña(dlg);
            consola.AbrirPestaña(titulo, dlg, $"pedido-{fila.DocumentoP}");
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

        // ─── Actualizar (única acción disponible: solo lectura) ───────────────
        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.DocumentosPObj.Actualizar();
            Sql.PedidosObj.Actualizar();
            CargarPedidos();
        }
    }

    // ─── Modelo de fila principal ─────────────────────────────────────────────
    public class PedidoFila
    {
        public int    Linea        { get; set; }
        public string DocumentoP   { get; set; } = "";
        public string Codigo       { get; set; } = "";
        public DateTime Fecha      { get; set; }
        public string FechaStr     { get; set; } = "";
        public string Movimiento   { get; set; } = "";
        public string SucursalDesc { get; set; } = "";
        public string TerceroDesc  { get; set; } = "";
        public string Estado       { get; set; } = "";
        public string Cuenta       { get; set; } = "";
        public double Cantidad     { get; set; }
        public double Importe      { get; set; }
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
