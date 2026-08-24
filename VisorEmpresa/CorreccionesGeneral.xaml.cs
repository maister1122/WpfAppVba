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
    /// Duplicado de SistemaGestion.CorreccionesGeneral para el visor: misma UI/lógica
    /// de listado, pero de SOLO LECTURA (sin Nueva/Editar/Eliminar) y con un
    /// combo de Sucursal (Todas las sucursales o una puntual) en vez de depender
    /// de AppState.SucursalActiva. La caché (Sql.DocumentosCObj/CorreccionesObj)
    /// se puebla vía ConsultasEmpresa.ConectarCacheCorrecciones para TODA la
    /// empresa (precalentada al loguear); el combo de Sucursal filtra en
    /// memoria, sin volver a SQL.
    /// </summary>
    public partial class CorreccionesGeneral : UserControl
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
        /// Tipo de movimiento fijo para este control ("repuesta" o "retirado"). Lo fija la
        /// sección del panel lateral (Repuestas / Retirados), igual que en SistemaGestión.
        /// Vacío = la pantalla lista los dos juntos.
        /// </summary>
        public string TipoMovimiento { get; set; } = "";

        public CorreccionesGeneral() : this("") { }

        public CorreccionesGeneral(string tipoMovimiento)
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
        // ConsultasEmpresa) y solo faltan CargarMeses/CargarCorrecciones. El combo
        // de Sucursal ya NO dispara esta recarga (ver Filtro_SelectionChanged):
        // filtra en memoria sobre los datos de toda la empresa ya cargados.
        private async Task RecargarCacheYVistaAsync()
        {
            string emp  = AppState.EmpresaActiva;
            int    anio = VisorState.AnioActivo;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => ConsultasEmpresa.ConectarCacheCorrecciones(emp, anio));
                CargarMeses();
                CargarCorrecciones();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron cargar las correcciones:\n{ex.Message}",
                                "Visor Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Cambiar el combo de Sucursal ya NO vuelve a consultar SQL:
        // Sql.DocumentosCObj ya tiene TODA la empresa cargada, así que solo hace
        // falta refiltrar en memoria (CargarMeses/CargarCorrecciones ya consideran
        // _sucursalFiltro).
        private void Filtro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros || !_iniciado) return;
            _sucursalFiltro = (CmbSucursal.SelectedItem as Opcion)?.Id ?? "";
            CargarMeses();
            CargarCorrecciones();
        }

        // ─── Carga el árbol de meses ──────────────────────────────────────────
        private void CargarMeses()
        {
            object? tagPrevio = (Tree1.SelectedItem as TreeViewItem)?.Tag;

            Tree1.Items.Clear();
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };

            int año = VisorState.AnioActivo;

            var mesesConDatos = new SortedSet<int>();
            int uf = Sql.DocumentosCObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosCObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (!string.IsNullOrEmpty(_sucursalFiltro) &&
                    Sql.DocumentosCObj.ObtenerItem("sucursal", id)?.ToString() != _sucursalFiltro)
                    continue;

                // MISMO filtro de movimiento que CargarCorrecciones: sin esto, la
                // sección Retirados arma el árbol con los meses de las repuestas (y
                // al revés), quedando el árbol lleno con el grid vacío.
                string movArbol = NormalizarMovimiento(Sql.DocumentosCObj.ObtenerItem("movimiento", id)?.ToString());
                if (!string.IsNullOrEmpty(ObtenerFiltroTipo()) &&
                    !string.Equals(movArbol, ObtenerFiltroTipo(), StringComparison.OrdinalIgnoreCase)) continue;

                var fechaObj = Sql.DocumentosCObj.ObtenerItem("fecha", id);
                if (fechaObj == null) continue;
                mesesConDatos.Add(Convert.ToDateTime(fechaObj).Month);
            }

            if (mesesConDatos.Count == 0) { _mesActivo = ""; return; }

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
                nodoGeneral.IsSelected = true;
                _mesActivo = "";
            }
            else if (tagPrevio is string mesPrevio && SeleccionarMes(mesPrevio))
            {
            }
            else if (mesActualNombre != null)
            {
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

                string suc = Sql.DocumentosCObj.ObtenerItem("sucursal", id)?.ToString() ?? "";

                // Filtro por sucursal (Sql.DocumentosCObj trae TODA la empresa: ver
                // ConsultasEmpresa.ConectarCacheCorrecciones y RecargarCacheYVistaAsync).
                if (!string.IsNullOrEmpty(_sucursalFiltro) && suc != _sucursalFiltro) continue;

                string sucDesc = Sql.SucursalesObj.ObtenerItem("descripcion", suc)?.ToString() ?? suc;

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
                    SucursalDesc  = sucDesc,
                    Movimiento    = movimiento,
                    Motivo        = motivo,
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
                "retirado" => "Egresos de Stock (pérdida, merma, hurto, consumo interno)",
                "repuesta" => "Ingresos de Stock (error de registro, registros omitidos)",
                _          => "Correcciones de Stock (Ingresos y Egresos)"
            };
            int año = VisorState.AnioActivo;
            LblSubtitulo.Text = string.IsNullOrEmpty(_mesActivo)
                ? año.ToString()
                : $"{_mesActivo} {año}";

            OcultarDetalle();
            SeleccionarFilaMasReciente(filaMasReciente);
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

        // ─── Nomenclatura visible según el movimiento ─────────────────────────
        // Se MUESTRAN como Ingreso/Egreso (igual que en la app principal); el valor
        // almacenado sigue siendo "repuesta"/"retirado", que es lo que leen las
        // consultas de stock y NormalizarMovimiento.
        private static string NombreDocDe(string movimiento) => NormalizarMovimiento(movimiento) switch
        {
            "repuesta" => "Ingreso",
            "retirado" => "Egreso",
            _          => "Corrección"
        };

        // El movimiento lo fija la sección del panel lateral; vacío = los dos juntos.
        private string ObtenerFiltroTipo() => TipoMovimiento;

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
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

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
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

        // Abre CorreccionesDetalle (formulario real de la app principal, vinculado)
        // en modo solo lectura: mismos campos/badges/stock que la app principal,
        // sin Guardar ni edición de líneas.
        private void AbrirVerDetalle()
        {
            if (Grid1.SelectedItem is not CorreccionFila fila) return;
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;

            AppState.EventoFormularioC = "editar";
            // El título sigue al movimiento REAL del documento, no al filtro: el
            // listado combinado mezcla los dos.
            string titulo = $"{NombreDocDe(fila.Movimiento)} {fila.Codigo}";
            var dlg = new CorreccionesDetalle(null, fila.Id, tituloTab: titulo);
            dlg.Cerrando += () => consola.CerrarPestaña(dlg);
            consola.AbrirPestaña(titulo, dlg, $"correccion-{fila.Id}");
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

        // ─── Actualizar (única acción disponible: solo lectura) ───────────────
        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.DocumentosCObj.Actualizar();
            Sql.CorreccionesObj.Actualizar();
            CargarCorrecciones();
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
        public string   SucursalDesc  { get; set; } = "";
        public string   Movimiento    { get; set; } = "";
        public string   Motivo        { get; set; } = "";
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
