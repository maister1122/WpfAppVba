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
    public partial class TraspasosGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private string _mesActivo  = "";
        private string _modoFiltro = "filtros"; // "filtros" = Tree1 | "busquedas" = TxtBuscar

        /// <summary>
        /// Tipo de movimiento fijo para este control ("entrada" o "salida"). Lo fija
        /// la sección del panel lateral (Entradas → Traspasos / Salidas → Traspasos):
        /// el listado queda filtrado y el combo "Movimiento" se oculta. Vacío = la
        /// pantalla lista entradas y salidas y el combo queda a la vista.
        /// </summary>
        public string TipoMovimiento { get; set; } = "";

        private bool _iniciado = false;

        public TraspasosGeneral() : this("") { }

        public TraspasosGeneral(string tipoMovimiento)
        {
            InitializeComponent();
            TipoMovimiento = (tipoMovimiento ?? "").ToLower();
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; ConfigurarModo(); CargarMeses(); CargarTraspasos(); };
        }

        // BtnEliminar queda visible para todos: además del administrador, el
        // usuario que creó el documento también puede eliminarlo/ocultarlo — el
        // permiso se valida por documento en BtnEliminar_Click.
        private void ConfigurarModo()
        {
            if (string.IsNullOrEmpty(TipoMovimiento)) return;

            // Movimiento fijado por la sección: el combo queda en el valor
            // correspondiente y se oculta (acá no es una opción del usuario).
            CboTipoMovimiento.SelectedIndex = TipoMovimiento == "salida" ? 2 : 1;
            PanelTipoMovimiento.Visibility  = Visibility.Collapsed;
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

            // Solo los meses que tienen documentos cargados (igual que PreciosGeneral).
            var mesesConDatos = new SortedSet<int>();
            int uf = Sql.DocumentosTObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosTObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // MISMO filtro que CargarTraspasos (sucursal activa + dirección): sin
                // esto el árbol arma los meses con TODOS los traspasos de la base, y
                // una sección sin documentos queda con el árbol lleno y el grid vacío.
                string origen  = Sql.DocumentosTObj.ObtenerItem("origen",  id)?.ToString() ?? "";
                string destino = Sql.DocumentosTObj.ObtenerItem("destino", id)?.ToString() ?? "";
                bool esSalida  = origen  == AppState.SucursalActiva;
                bool esEntrada = destino == AppState.SucursalActiva;
                string tipoMovArbol = ObtenerFiltroTipo();
                if (tipoMovArbol == "salida")       { if (!esSalida)  continue; }
                else if (tipoMovArbol == "entrada") { if (!esEntrada) continue; }
                else if (!esSalida && !esEntrada)   continue;

                var fechaObj = Sql.DocumentosTObj.ObtenerItem("fecha", id);
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

        // ─── Carga la lista de traspasos ──────────────────────────────────────
        public void CargarTraspasos()
        {
            if (TxtBuscar == null || Grid1 == null) return;

            var lista = new List<TraspasoFila>();
            int linea = 1;
            double totalCant = 0;
            // Fila más reciente del listado: es la que queda seleccionada al terminar
            // de cargar (ver SeleccionarFilaMasReciente).
            DateTime fechaMax = DateTime.MinValue;
            TraspasoFila? filaMasReciente = null;
            string filtroEstado = ObtenerFiltroEstado();
            // Cada modo aplica solo su propio filtro de contenido (igual que VBA llaveActualisar)
            string busqueda  = _modoFiltro == "busquedas" ? TxtBuscar.Text.Trim().ToLower() : "";
            string mesFiltro = _modoFiltro == "filtros"   ? _mesActivo : "";
            string tipoMov = ObtenerFiltroTipo();

            int uf = Sql.DocumentosTObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosTObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // Filtrar por sucursal activa según tipo de movimiento
                string origen  = Sql.DocumentosTObj.ObtenerItem("origen",  id)?.ToString() ?? "";
                string destino = Sql.DocumentosTObj.ObtenerItem("destino", id)?.ToString() ?? "";
                bool esSalida  = origen  == AppState.SucursalActiva;
                bool esEntrada = destino == AppState.SucursalActiva;

                string movActual;
                if (tipoMov == "salida")
                {
                    if (!esSalida) continue;
                    movActual = "salida";
                }
                else if (tipoMov == "entrada")
                {
                    if (!esEntrada) continue;
                    movActual = "entrada";
                }
                else
                {
                    if (!esSalida && !esEntrada) continue;
                    movActual = esSalida ? "salida" : "entrada";
                }
                // Filtro por mes (solo en modo "filtros", independiente de TxtBuscar)
                if (!string.IsNullOrEmpty(mesFiltro))
                {
                    var fechaObj2 = Sql.DocumentosTObj.ObtenerItem("fecha", id);
                    if (fechaObj2 == null) continue;
                    DateTime fecha2 = Convert.ToDateTime(fechaObj2);
                    string mesDoc = ObtenerNombreMes(fecha2.Month);
                    if (!string.Equals(mesDoc, mesFiltro, StringComparison.OrdinalIgnoreCase)) continue;
                }

                var fechaDocObj = Sql.DocumentosTObj.ObtenerItem("fecha", id);
                DateTime fechaDoc = fechaDocObj != null ? Convert.ToDateTime(fechaDocObj) : default;

                // Columna Origen/Destino: la contraparte relativa a la sucursal activa —
                // si la activa es el origen, se muestra el destino; caso contrario, el origen.
                string contraparteId   = origen == AppState.SucursalActiva ? destino : origen;
                string contraparteDesc = Sql.SucursalesObj.ObtenerItem("descripcion", contraparteId)?.ToString() ?? contraparteId;

                string estado  = Sql.DocumentosTObj.ObtenerItem("estado", id)?.ToString() ?? "";
                string emitido = Sql.DocumentosTObj.ObtenerItem("emitido", id)?.ToString() ?? "";

                // Si fue emitido por otra sucursal y está "pendiente" → "pendiente revisar"
                if (emitido != AppState.SucursalActiva && estado == "pendiente")
                    estado = "pendiente revisar";

                // Filtro por estado
                if (!string.IsNullOrEmpty(filtroEstado) &&
                    !string.Equals(estado, filtroEstado, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filtro por búsqueda (la contraparte)
                if (!string.IsNullOrEmpty(busqueda))
                    if (!id.Contains(busqueda) &&
                        !contraparteDesc.ToLower().Contains(busqueda))
                        continue;

                double cant = CalcularCantidad(id);
                lista.Add(new TraspasoFila
                {
                    Linea           = linea++,
                    DocumentoT      = id,
                    Codigo          = Sql.DocumentosTObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                    Fecha           = fechaDoc,
                    FechaStr        = $"{fechaDoc:d} {fechaDoc:HH:mm:ss}",
                    Movimiento      = movActual,
                    ContraparteDesc = contraparteDesc,
                    Referencia      = Sql.DocumentosTObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                    Estado          = estado,
                    UsuarioCreador  = Sql.DocumentosTObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                    Cantidad        = cant
                });

                if (fechaDoc >= fechaMax) { fechaMax = fechaDoc; filaMasReciente = lista[^1]; }

                totalCant += cant;
            }

            Grid1.ItemsSource        = lista;
            TxtTotalCantidad.Text    = totalCant.ToString("N0");
            TxtTotalDocumentos.Text  = lista.Count.ToString("N0");
            TxtTotalPendientes.Text  = lista.Count(f => f.Estado == "pendiente").ToString();
            TxtTotalEntregados.Text  = lista.Count(f => f.Estado == "pendiente revisar").ToString();
            LblTipoMovimiento.Text = tipoMov switch
            {
                "salida"  => "Salidas de Productos",
                "entrada" => "Entradas de Productos",
                _         => "Traspasos (Entradas y Salidas)"
            };
            int año = AppState.DataFechaFinal.Year > 2000
                ? AppState.DataFechaFinal.Year
                : DateTime.Now.Year;
            LblSubtitulo.Text = string.IsNullOrEmpty(_mesActivo)
                ? año.ToString()
                : $"{_mesActivo} {año}";

            // Limpiar el panel de detalle al recargar
            OcultarDetalle();
            SeleccionarFilaMasReciente(filaMasReciente);
        }

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
        // Las selecciones explícitas de guardar/editar/eliminar corren DESPUÉS de
        // CargarTraspasos(), así que siguen ganando sobre ésta.
        private void SeleccionarFilaMasReciente(TraspasoFila? fila)
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

        private string ObtenerFiltroTipo()
        {
            if (!string.IsNullOrEmpty(TipoMovimiento)) return TipoMovimiento;

            return (CboTipoMovimiento?.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLower() switch
            {
                "entradas" => "entrada",
                "salidas"  => "salida",
                _          => ""
            };
        }

        // ─── Filtro de estado (4 opciones igual que VBA) ─────────────────────
        private string ObtenerFiltroEstado()
        {
            if (BtnFiltroPendiente?.IsChecked        == true) return "pendiente";
            if (BtnFiltroPendienteRevisar?.IsChecked == true) return "pendiente revisar";
            if (BtnFiltroEntregado?.IsChecked        == true) return "entregado";
            return ""; // todos
        }

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
        }

        // ─── Helpers de actualización incremental del Grid1 ───────────────────
        private List<TraspasoFila> FilasGrid =>
            Grid1.ItemsSource as List<TraspasoFila> ?? new List<TraspasoFila>();

        private TraspasoFila ConstruirFilaTraspaso(string id, int linea)
        {
            string origen  = Sql.DocumentosTObj.ObtenerItem("origen",  id)?.ToString() ?? "";
            string destino = Sql.DocumentosTObj.ObtenerItem("destino", id)?.ToString() ?? "";
            bool esSalida  = origen == AppState.SucursalActiva;

            var fechaDocObj = Sql.DocumentosTObj.ObtenerItem("fecha", id);
            DateTime fechaDoc = fechaDocObj != null ? Convert.ToDateTime(fechaDocObj) : default;

            // Columna Origen/Destino: la contraparte relativa a la sucursal activa —
            // si la activa es el origen, se muestra el destino; caso contrario, el origen.
            string contraparteId   = origen == AppState.SucursalActiva ? destino : origen;
            string contraparteDesc = Sql.SucursalesObj.ObtenerItem("descripcion", contraparteId)?.ToString() ?? contraparteId;

            string estado  = Sql.DocumentosTObj.ObtenerItem("estado",  id)?.ToString() ?? "";
            string emitido = Sql.DocumentosTObj.ObtenerItem("emitido", id)?.ToString() ?? "";
            if (emitido != AppState.SucursalActiva && estado == "pendiente")
                estado = "pendiente revisar";

            return new TraspasoFila
            {
                Linea           = linea,
                DocumentoT      = id,
                Codigo          = Sql.DocumentosTObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                Fecha           = fechaDoc,
                FechaStr        = $"{fechaDoc:d} {fechaDoc:HH:mm:ss}",
                Movimiento      = esSalida ? "salida" : "entrada",
                ContraparteDesc = contraparteDesc,
                Referencia      = Sql.DocumentosTObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                Estado          = estado,
                UsuarioCreador  = Sql.DocumentosTObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                Cantidad        = CalcularCantidad(id)
            };
        }

        private void RenumerarYTotales()
        {
            var lista = FilasGrid;
            int n = 1;
            double totalCant = 0;
            foreach (var f in lista)
            {
                f.Linea    = n++;
                totalCant += f.Cantidad;
            }
            TxtTotalCantidad.Text    = totalCant.ToString("N0");
            TxtTotalDocumentos.Text  = lista.Count.ToString("N0");
            TxtTotalPendientes.Text  = lista.Count(f => f.Estado == "pendiente").ToString();
            TxtTotalEntregados.Text  = lista.Count(f => f.Estado == "pendiente revisar").ToString();
            Grid1.Items.Refresh();
        }

        // ─── Sumar cantidad de artículos del documentoT ───────────────────────
        private static double CalcularCantidad(string documentoT)
        {
            double cant = 0;
            int uf = Sql.TraspasosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.TraspasosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.TraspasosObj.ObtenerItem("documentoT", id)?.ToString() != documentoT) continue;
                cant += Convert.ToDouble(Sql.TraspasosObj.ObtenerItem("cantidad", id) ?? 0);
            }
            return cant;
        }

        // ─── Panel de detalle artículos (Lista2) ─────────────────────────────

        private void MostrarDetalle(string documentoT)
        {
            string codigoDoc = Sql.DocumentosTObj.ObtenerItem("codigo", documentoT)?.ToString() ?? documentoT;
            LblDetalleHeader.Text = $"Artículos del documento {codigoDoc}";
            var detalles = new List<TraspasoDetalleFila>();
            int linea = 1;

            int uf = Sql.TraspasosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.TraspasosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.TraspasosObj.ObtenerItem("documentoT", id)?.ToString() != documentoT) continue;

                string artId = Sql.TraspasosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string codigo = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? artId;

                string desc      = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
                string famId     = Sql.ArticulosObj.ObtenerItem("familia", artId)?.ToString() ?? "";
                string famDesc   = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? "";
                string modelo    = Sql.ArticulosObj.ObtenerItem("modelo", artId)?.ToString() ?? "";
                string descFull  = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                double cantidad = Convert.ToDouble(Sql.TraspasosObj.ObtenerItem("cantidad", id) ?? 0);

                detalles.Add(new TraspasoDetalleFila
                {
                    Linea       = linea++,
                    Codigo      = codigo,
                    Descripcion = descFull,
                    Cantidad    = cantidad
                });
            }

            Lista2.ItemsSource = detalles;
        }

        private void OcultarDetalle()
        {
            LblDetalleHeader.Text = "Artículos del documento";
            Lista2.ItemsSource    = null;
        }

        // ─── Selección en Grid1 → mostrar detalle ────────────────────────────
        private void Grid1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Grid1.SelectedItem is TraspasoFila fila)
                MostrarDetalle(fila.DocumentoT);
            else
                OcultarDetalle();
        }

        // ─── Eventos de árbol y filtros ───────────────────────────────────────
        private void Tree1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (Tree1.SelectedItem is TreeViewItem ti)
                _mesActivo = ti.Tag?.ToString() ?? "";
            _modoFiltro = "filtros";   // Tree1 activo → ignora TxtBuscar
            CargarTraspasos();
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
                CargarTraspasos();
            }
        }

        private void FiltroEstado_Checked(object sender, RoutedEventArgs e)
            => CargarTraspasos();

        private void CboTipoMovimiento_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => CargarTraspasos();

        // ─── Búsqueda (independiente del Tree1) ──────────────────────────────
        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _modoFiltro = "busquedas"; // TxtBuscar activo → ignora Tree1
            CargarTraspasos();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            _modoFiltro = "busquedas"; // TxtBuscar activo → ignora Tree1
            CargarTraspasos();
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
        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
            => AbrirNuevoTraspaso();

        // tipoMovimiento: si se indica ("salida"/"entrada") fuerza el movimiento;
        // si es null se toma del filtro activo. Público para accesos rápidos del top bar.
        public void AbrirNuevoTraspaso(string? tipoMovimiento = null)
        {
            AppState.EventoFormularioM = "nuevo";
            string filtroTipo = tipoMovimiento ?? ObtenerFiltroTipo();
            AppState.TipoMovimiento = string.IsNullOrEmpty(filtroTipo) ? "entrada" : filtroTipo;
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = "Nuevo Traspaso";
            var dlg = new TraspasosDetalle(this, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.DocumentoCreadoId == null) return;   // cancelado
                var nueva = ConstruirFilaTraspaso(dlg.DocumentoCreadoId, 0);
                FilasGrid.Add(nueva);
                RenumerarYTotales();
                Grid1.SelectedItem = nueva; Grid1.ScrollIntoView(nueva);
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(titulo, dlg, "nuevo-traspaso");
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
            => AbrirEditar();

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (Grid1.SelectedItem is not TraspasoFila fila) return;

            // Solo el administrador o el usuario que creó el documento puede eliminarlo.
            if (!AppState.EsAdmin && fila.UsuarioCreador != AppState.UsuarioActivo)
            {
                MessageBox.Show("Solo un administrador o el usuario que creó este documento puede eliminarlo.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verificación de conexión en 2 capas antes de persistir el borrado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            var res = MessageBox.Show("¿Eliminar este traspaso y todos sus artículos?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                int idxPrevio = FilasGrid.IndexOf(fila);

                Sql.DocumentosTObj.EstablecerItem("edicion",  fila.DocumentoT, DateTime.Now);
                Sql.DocumentosTObj.EstablecerItem("usuarioE", fila.DocumentoT, AppState.UsuarioActivo);
                Sql.DocumentosTObj.Ocultar(fila.DocumentoT);

                int uf = Sql.TraspasosObj.ContarFilas;
                var idsOcultar = new List<string>();
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.TraspasosObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    if (Sql.TraspasosObj.ObtenerItem("documentoT", id)?.ToString() == fila.DocumentoT)
                        idsOcultar.Add(id);
                }
                foreach (var id in idsOcultar)
                    Sql.TraspasosObj.Ocultar(id);

                Sql.DocumentosTObj.OrdenarData(("fecha", false));
                Sql.TraspasosObj.OrdenarData(("documentoT", false), ("indice", false));

                // Recarga completa: el documento eliminado pudo ser el último de su mes,
                // así que el árbol y el listado deben rehacerse (igual que nuevo/editar).
                CargarMeses();
                CargarTraspasos();

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

            Sql.DocumentosTObj.Actualizar();
            Sql.TraspasosObj.Actualizar();
            CargarTraspasos();
        }

        // ─── Entregar todos ────────────────────────────────────────────────────
        // Cambia el estado de todos los documentosT cargados (sucursal activa + período)
        // que estén en "pendiente" a "entregado". Con verificadores de conexión.
        // Solo puede afectar documentos emitidos por OTRA sucursal (emitido != activa):
        // un documento emitido por la sucursal activa lo entrega la sucursal receptora,
        // no quien lo emitió.
        private void BtnEntregarTodos_Click(object sender, RoutedEventArgs e)
        {
            // Verificación de conexión en 2 capas antes de persistir el cambio de estado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            // Recolectar los documentosT cargados cuyo estado almacenado es "pendiente"
            // y que fueron emitidos por otra sucursal (no por la activa).
            var idsPendientes = new List<string>();
            int uf = Sql.DocumentosTObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosTObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                string estado  = Sql.DocumentosTObj.ObtenerItem("estado",  id)?.ToString() ?? "";
                string emitido = Sql.DocumentosTObj.ObtenerItem("emitido", id)?.ToString() ?? "";
                if (string.Equals(estado, "pendiente", StringComparison.OrdinalIgnoreCase) &&
                    emitido != AppState.SucursalActiva)
                    idsPendientes.Add(id);
            }

            if (idsPendientes.Count == 0)
            {
                MessageBox.Show("No hay documentos pendientes para entregar.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var res = MessageBox.Show(
                $"¿Marcar como 'entregado' los {idsPendientes.Count} documento(s) pendiente(s)?",
                "Consola", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                foreach (var id in idsPendientes)
                {
                    Sql.DocumentosTObj.EstablecerItem("estado",   id, "entregado");
                    Sql.DocumentosTObj.EstablecerItem("edicion",  id, DateTime.Now);
                    Sql.DocumentosTObj.EstablecerItem("usuarioE", id, AppState.UsuarioActivo);
                }

                // Persiste los cambios (ExportarItems dentro de OrdenarData).
                Sql.DocumentosTObj.OrdenarData(("fecha", false));

                CargarTraspasos();
                MessageBox.Show($"{idsPendientes.Count} documento(s) marcados como entregado.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al entregar: {ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AbrirEditar()
        {
            if (Grid1.SelectedItem is not TraspasoFila fila) return;
            string docSel = fila.DocumentoT;
            int    linea  = fila.Linea;
            AppState.EventoFormularioM = "editar";
            string origenDoc = Sql.DocumentosTObj.ObtenerItem("origen", docSel)?.ToString() ?? "";
            AppState.TipoMovimiento = origenDoc == AppState.SucursalActiva ? "salida" : "entrada";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            var dlg = new TraspasosDetalle(this, docSel, tituloTab: $"Traspaso {fila.Codigo}");
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0)
                {
                    var actualizada = ConstruirFilaTraspaso(docSel, linea);
                    lista[idx] = actualizada;
                    RenumerarYTotales();
                    Grid1.SelectedItem = actualizada; Grid1.ScrollIntoView(actualizada);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña($"Traspaso {fila.Codigo}", dlg, $"traspaso-{docSel}");
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class TraspasoFila
    {
        public int    Linea           { get; set; }
        public string DocumentoT      { get; set; } = "";
        public string Codigo          { get; set; } = "";
        public DateTime Fecha         { get; set; }
        public string FechaStr        { get; set; } = "";
        public string Movimiento      { get; set; } = "";
        public string ContraparteDesc { get; set; } = "";
        public string Referencia      { get; set; } = "";
        public string Estado          { get; set; } = "";
        public string UsuarioCreador  { get; set; } = "";
        public double Cantidad        { get; set; }
    }

    public class TraspasoDetalleFila
    {
        public int    Linea       { get; set; }
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public double Cantidad    { get; set; }
    }
}
