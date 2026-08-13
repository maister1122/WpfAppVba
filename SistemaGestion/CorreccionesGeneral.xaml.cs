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
    public partial class CorreccionesGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private string _mesActivo  = "";
        private string _modoFiltro = "filtros"; // "filtros" = Tree1 | "busquedas" = TxtBuscar

        /// <summary>
        /// Tipo de movimiento fijo para este control ("repuesta" o "retirado"). Lo
        /// fija la sección del panel lateral (Repuestas / Retirados): el listado
        /// queda filtrado y el combo "Movimiento" se oculta. Vacío = la pantalla
        /// lista repuestas y retirados juntos.
        /// </summary>
        public string TipoMovimiento { get; set; } = "";

        private bool _iniciado = false;

        public CorreccionesGeneral() : this("") { }

        public CorreccionesGeneral(string tipoMovimiento)
        {
            InitializeComponent();
            TipoMovimiento = (tipoMovimiento ?? "").ToLower();
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; ConfigurarModo(); CargarMeses(); CargarCorrecciones(); };
        }

        // BtnEliminar queda visible para todos: además del administrador, el
        // usuario que creó el documento también puede eliminarlo/ocultarlo — el
        // permiso se valida por documento en BtnEliminar_Click.
        private void ConfigurarModo()
        {
            if (string.IsNullOrEmpty(TipoMovimiento)) return;

            // Movimiento fijado por la sección: el combo queda en el valor
            // correspondiente y se oculta (acá no es una opción del usuario).
            CboTipoMovimiento.SelectedIndex = TipoMovimiento == "retirado" ? 2 : 1;
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
            int uf = Sql.DocumentosCObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosCObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // MISMO filtro de movimiento que CargarCorrecciones: sin esto, la
                // sección Retirados arma el árbol con los meses de las repuestas (y
                // al revés), quedando el árbol lleno con el grid vacío.
                string movArbol = NormalizarMovimiento(Sql.DocumentosCObj.ObtenerItem("movimiento", id)?.ToString());
                if (!string.IsNullOrEmpty(ObtenerFiltroTipo()) &&
                    !string.Equals(movArbol, ObtenerFiltroTipo(), StringComparison.OrdinalIgnoreCase)) continue;

                string suc = Sql.DocumentosCObj.ObtenerItem("sucursal", id)?.ToString() ?? "";
                if (suc != AppState.SucursalActiva) continue;
                var fechaObj = Sql.DocumentosCObj.ObtenerItem("fecha", id);
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

        // ─── Carga la lista de correcciones ───────────────────────────────────
        public void CargarCorrecciones()
        {
            if (TxtBuscar == null || Grid1 == null) return;

            var lista = new List<CorreccionFila>();
            int linea = 1;
            double totalCant = 0;
            // Fila más reciente del listado: es la que queda seleccionada al terminar
            // de cargar (ver SeleccionarFilaMasReciente).
            DateTime fechaMax = DateTime.MinValue;
            CorreccionFila? filaMasReciente = null;
            string busqueda  = _modoFiltro == "busquedas" ? TxtBuscar.Text.Trim().ToLower() : "";
            string mesFiltro = _modoFiltro == "filtros"   ? _mesActivo : "";
            string tipoMov = ObtenerFiltroTipo();

            int uf = Sql.DocumentosCObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosCObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // Filtrar por sucursal activa
                string suc = Sql.DocumentosCObj.ObtenerItem("sucursal", id)?.ToString() ?? "";
                if (suc != AppState.SucursalActiva) continue;

                // Filtrar por tipo de movimiento (repuesta / retirado)
                string movimiento = NormalizarMovimiento(Sql.DocumentosCObj.ObtenerItem("movimiento", id)?.ToString());
                if (!string.IsNullOrEmpty(tipoMov) &&
                    !string.Equals(movimiento, tipoMov, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filtro por mes (solo en modo "filtros", independiente de TxtBuscar)
                var fechaDocObj = Sql.DocumentosCObj.ObtenerItem("fecha", id);
                DateTime fechaDoc = fechaDocObj != null ? Convert.ToDateTime(fechaDocObj) : default;
                if (!string.IsNullOrEmpty(mesFiltro))
                {
                    if (fechaDocObj == null) continue;
                    string mesDoc = ObtenerNombreMes(fechaDoc.Month);
                    if (!string.Equals(mesDoc, mesFiltro, StringComparison.OrdinalIgnoreCase)) continue;
                }

                string motivo = Sql.DocumentosCObj.ObtenerItem("motivo", id)?.ToString() ?? "";

                // Filtro por búsqueda
                if (!string.IsNullOrEmpty(busqueda))
                    if (!id.ToLower().Contains(busqueda) && !motivo.ToLower().Contains(busqueda))
                        continue;

                double cant = CalcularCantidad(id);
                lista.Add(new CorreccionFila
                {
                    Linea         = linea++,
                    Id            = id,
                    Codigo        = Sql.DocumentosCObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                    Fecha         = fechaDoc,
                    FechaStr      = fechaDoc != default ? $"{fechaDoc:d} {fechaDoc:HH:mm:ss}" : "",
                    Movimiento    = movimiento,
                    Motivo        = motivo,
                    Referencia    = Sql.DocumentosCObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                    UsuarioCreador = Sql.DocumentosCObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                    CantidadTotal = cant
                });

                if (fechaDoc >= fechaMax) { fechaMax = fechaDoc; filaMasReciente = lista[^1]; }

                totalCant += cant;
            }

            Grid1.ItemsSource       = lista;
            TxtTotalCantidad.Text   = totalCant.ToString("N0");
            TxtTotalDocumentos.Text = lista.Count.ToString("N0");
            TxtTotalIngresos.Text   = lista.Count(f => f.Movimiento == "repuesta").ToString();
            TxtTotalEgresos.Text    = lista.Count(f => f.Movimiento == "retirado").ToString();
            LblTipoMovimiento.Text = tipoMov switch
            {
                "retirado" => "Retirados de Stock (pérdida, merma, hurto, consumo interno)",
                "repuesta" => "Repuestas de Stock (error de registro, registros omitidos)",
                _          => "Correcciones de Stock (Repuestas y Retirados)"
            };
            int año = AppState.DataFechaFinal.Year > 2000
                ? AppState.DataFechaFinal.Year
                : DateTime.Now.Year;
            LblSubtitulo.Text = string.IsNullOrEmpty(_mesActivo)
                ? año.ToString()
                : $"{_mesActivo} {año}";

            OcultarDetalle();
            SeleccionarFilaMasReciente(filaMasReciente);
        }

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
        // Las selecciones explícitas de guardar/editar/eliminar corren DESPUÉS de
        // CargarCorrecciones(), así que siguen ganando sobre ésta.
        private void SeleccionarFilaMasReciente(CorreccionFila? fila)
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
                "repuestas" => "repuesta",
                "retirados" => "retirado",
                _           => ""
            };
        }

        /// <summary>
        /// Movimiento de una corrección, normalizado a "repuesta"/"retirado". Las
        /// correcciones cargadas antes del cambio de vocabulario guardaron
        /// "ingreso"/"egreso": se leen como su equivalente.
        /// </summary>
        private static string NormalizarMovimiento(string? mov) =>
            (mov ?? "").ToLower() switch
            {
                "retirado" or "descarga" or "egreso" => "retirado",
                "repuesta" or "ingreso" => "repuesta",
                _                       => ""
            };

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
        }

        // ─── Helpers de actualización incremental del Grid1 ───────────────────
        private List<CorreccionFila> FilasGrid =>
            Grid1.ItemsSource as List<CorreccionFila> ?? new List<CorreccionFila>();

        private CorreccionFila ConstruirFila(string id, int linea)
        {
            var fechaObj = Sql.DocumentosCObj.ObtenerItem("fecha", id);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;
            return new CorreccionFila
            {
                Linea         = linea,
                Id            = id,
                Codigo        = Sql.DocumentosCObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                Fecha         = fecha,
                FechaStr      = fecha != default ? $"{fecha:d} {fecha:HH:mm:ss}" : "",
                Movimiento    = NormalizarMovimiento(Sql.DocumentosCObj.ObtenerItem("movimiento", id)?.ToString()),
                Motivo        = Sql.DocumentosCObj.ObtenerItem("motivo",      id)?.ToString() ?? "",
                Referencia    = Sql.DocumentosCObj.ObtenerItem("referencia",  id)?.ToString() ?? "",
                UsuarioCreador = Sql.DocumentosCObj.ObtenerItem("usuario", id)?.ToString() ?? "",
                CantidadTotal = CalcularCantidad(id)
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
                totalCant += f.CantidadTotal;
            }
            TxtTotalCantidad.Text   = totalCant.ToString("N0");
            TxtTotalDocumentos.Text = lista.Count.ToString("N0");
            TxtTotalIngresos.Text   = lista.Count(f => f.Movimiento == "repuesta").ToString();
            TxtTotalEgresos.Text    = lista.Count(f => f.Movimiento == "retirado").ToString();
            Grid1.Items.Refresh();
        }

        // ─── Suma la cantidad total de las líneas de un documento ─────────────
        private static double CalcularCantidad(string documentoC)
        {
            double cant = 0;
            int uf = Sql.CorreccionesObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.CorreccionesObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.CorreccionesObj.ObtenerItem("documentoC", id)?.ToString() != documentoC) continue;
                cant += Convert.ToDouble(Sql.CorreccionesObj.ObtenerItem("cantidad", id) ?? 0);
            }
            return cant;
        }

        // ─── Panel de detalle artículos (Lista2) ─────────────────────────────
        private void MostrarDetalle(string documentoC)
        {
            string codigoDoc = Sql.DocumentosCObj.ObtenerItem("codigo", documentoC)?.ToString() ?? documentoC;
            LblDetalleHeader.Text = $"Artículos del documento {codigoDoc}";
            var detalles = new List<CorreccionDetalleFila>();
            int linea = 1;

            int uf = Sql.CorreccionesObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.CorreccionesObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.CorreccionesObj.ObtenerItem("documentoC", id)?.ToString() != documentoC) continue;

                string artId  = Sql.CorreccionesObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string codigo = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? artId;

                string desc     = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
                string famId    = Sql.ArticulosObj.ObtenerItem("familia", artId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo", artId)?.ToString() ?? "";
                string descFull = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                double cantidad = Convert.ToDouble(Sql.CorreccionesObj.ObtenerItem("cantidad", id) ?? 0);

                detalles.Add(new CorreccionDetalleFila
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
            if (Grid1.SelectedItem is CorreccionFila fila)
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
            CargarCorrecciones();
        }

        private void Tree1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not TreeViewItem)
                source = VisualTreeHelper.GetParent(source);

            if (source is TreeViewItem tvi && tvi.IsSelected)
            {
                _modoFiltro = "filtros";
                CargarCorrecciones();
            }
        }

        private void CboTipoMovimiento_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => CargarCorrecciones();

        // ─── Búsqueda (independiente del Tree1) ──────────────────────────────
        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _modoFiltro = "busquedas";
            CargarCorrecciones();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            _modoFiltro = "busquedas";
            CargarCorrecciones();
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
            AppState.EventoFormularioC = "nuevo";
            string filtroTipo = ObtenerFiltroTipo();
            AppState.TipoCorreccion = string.IsNullOrEmpty(filtroTipo) ? "retirado" : filtroTipo;

            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = "Nueva Corrección";
            string clave  = "nueva-correccion";
            var dlg = new CorreccionesDetalle(this, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.ItemCreadoId == null) return;
                var nueva = ConstruirFila(dlg.ItemCreadoId, 0);
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
            if (Grid1.SelectedItem is not CorreccionFila fila) return;

            // Solo el administrador o el usuario que creó el documento puede eliminarlo.
            if (!AppState.EsAdmin && fila.UsuarioCreador != AppState.UsuarioActivo)
            {
                MessageBox.Show("Solo un administrador o el usuario que creó este documento puede eliminarlo.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verificación de conexión en 2 capas antes de persistir el borrado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            var res = MessageBox.Show("¿Eliminar esta corrección y todas sus líneas?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                int idxPrevio = FilasGrid.IndexOf(fila);

                // Ocultar todas las líneas de este documentoC
                int uf = Sql.CorreccionesObj.ContarFilas;
                var idsOcultar = new List<string>();
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.CorreccionesObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    if (Sql.CorreccionesObj.ObtenerItem("documentoC", id)?.ToString() == fila.Id)
                        idsOcultar.Add(id);
                }
                foreach (string id in idsOcultar)
                    Sql.CorreccionesObj.Ocultar(id);

                // Ocultar el documento de corrección
                Sql.DocumentosCObj.EstablecerItem("edicion",  fila.Id, DateTime.Now);
                Sql.DocumentosCObj.EstablecerItem("usuarioE", fila.Id, AppState.UsuarioActivo);
                Sql.DocumentosCObj.Ocultar(fila.Id);

                Sql.CorreccionesObj.OrdenarData(("documentoC", false), ("indice", false));
                Sql.DocumentosCObj.OrdenarData(("fecha", false));

                // Recarga completa: el documento eliminado pudo ser el último de su mes,
                // así que el árbol y el listado deben rehacerse (igual que nuevo/editar).
                CargarMeses();
                CargarCorrecciones();

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

            Sql.DocumentosCObj.Actualizar();
            Sql.CorreccionesObj.Actualizar();
            CargarCorrecciones();
        }

        // ─── Helper ───────────────────────────────────────────────────────────
        private void AbrirEditar()
        {
            if (Grid1.SelectedItem is not CorreccionFila fila) return;

            string idSel = fila.Id;
            int    linea = fila.Linea;
            AppState.EventoFormularioC = "editar";
            AppState.TipoCorreccion = NormalizarMovimiento(Sql.DocumentosCObj.ObtenerItem("movimiento", idSel)?.ToString());

            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = $"Corrección {fila.Codigo}";
            var dlg = new CorreccionesDetalle(this, idSel, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0)
                {
                    var actualizada = ConstruirFila(idSel, linea);
                    lista[idx] = actualizada;
                    RenumerarYTotales();
                    Grid1.SelectedItem = actualizada; Grid1.ScrollIntoView(actualizada);
                }
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(titulo, dlg, $"correccion-{idSel}");
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class CorreccionFila
    {
        public int      Linea         { get; set; }
        public string   Id            { get; set; } = "";
        public string   Codigo        { get; set; } = "";
        public DateTime Fecha         { get; set; }
        public string   FechaStr      { get; set; } = "";
        public string   Movimiento    { get; set; } = "";
        public string   Motivo        { get; set; } = "";
        public string   Referencia    { get; set; } = "";
        public string   UsuarioCreador { get; set; } = "";
        public double   CantidadTotal { get; set; }
    }

    public class CorreccionDetalleFila
    {
        public int    Linea       { get; set; }
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public double Cantidad    { get; set; }
    }
}
