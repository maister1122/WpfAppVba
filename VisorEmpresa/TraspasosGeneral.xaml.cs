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
    /// Duplicado de SistemaGestion.TraspasosGeneral para el visor: misma UI/lógica de
    /// listado (Tree1 de meses, filtros, grilla, detalle), pero de SOLO LECTURA
    /// (sin Entregar todos/Nuevo/Editar/Eliminar) y con dos combos independientes
    /// —Sucursal origen y Sucursal destino— (cada uno "Todas las sucursales" o
    /// una puntual, combinables) en vez de depender de AppState.SucursalActiva.
    /// La caché (Sql.DocumentosTObj/TraspasosObj) se puebla vía
    /// ConsultasEmpresa.ConectarCacheTraspasos para TODA la empresa (precalentada
    /// al loguear); los combos de Origen/Destino filtran en memoria, sin volver a SQL.
    ///
    /// Diferencias de fondo respecto al original, decididas explícitamente para
    /// el visor (no aplican a la app principal):
    ///  - Estado: el valor crudo es "pendiente"/"entregado" (sin la reinterpretación
    ///    "pendiente revisar" relativa a AppState.SucursalActiva, que solo tenía
    ///    sentido con una única sucursal activa fija), pero "pendiente" se
    ///    MUESTRA como "pendiente al revisar" (ver TraspasoFila.EstadoTexto) —
    ///    solo cambia el texto, no el valor ni el filtro.
    ///  - Sin columna ni filtro de "Movimiento" (salida/entrada): Origen+Destino
    ///    ya identifican la dirección sin ambigüedad, a pedido explícito del
    ///    usuario (el concepto de movimiento no aplica bien sin una única
    ///    sucursal de referencia fija como AppState.SucursalActiva).
    /// </summary>
    public partial class TraspasosGeneral : UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private string _mesActivo      = "";
        private string _modoFiltro     = "filtros"; // "filtros" = Tree1 | "busquedas" = TxtBuscar
        private string _origenFiltro   = "";         // "" = Todas las sucursales
        private string _destinoFiltro  = "";         // "" = Todas las sucursales

        private bool _iniciado;
        private bool _cargandoFiltros;

        private class Opcion
        {
            public string Id    { get; }
            public string Texto { get; }
            public Opcion(string id, string texto) { Id = id; Texto = texto; }
            public override string ToString() => Texto;
        }

        public TraspasosGeneral()
        {
            InitializeComponent();
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
                CmbSucursalOrigen.ItemsSource    = opciones;
                CmbSucursalOrigen.SelectedIndex  = 0;
                CmbSucursalDestino.ItemsSource   = new List<Opcion>(opciones);
                CmbSucursalDestino.SelectedIndex = 0;
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

        // Siempre carga TODA la empresa (sin filtro de origen/destino): al loguear
        // ya se precalienta con esta misma clave (empresa, año) — ver
        // LoginVisorWindow — así que, salvo la primerísima vez, esto es un no-op
        // (memoización en ConsultasEmpresa) y solo faltan CargarMeses/CargarTraspasos.
        // Los combos de Origen/Destino ya NO disparan esta recarga (ver
        // Filtro_SelectionChanged): filtran en memoria sobre los datos de toda la
        // empresa ya cargados.
        private async Task RecargarCacheYVistaAsync()
        {
            string emp  = AppState.EmpresaActiva;
            int    anio = VisorState.AnioActivo;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => ConsultasEmpresa.ConectarCacheTraspasos(emp, anio));
                CargarMeses();
                CargarTraspasos();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron cargar los traspasos:\n{ex.Message}",
                                "Visor Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Cambiar los combos de Origen/Destino ya NO vuelve a consultar SQL:
        // Sql.DocumentosTObj ya tiene TODA la empresa cargada, así que solo hace
        // falta refiltrar en memoria (CargarMeses/CargarTraspasos ya consideran
        // _origenFiltro/_destinoFiltro).
        private void Filtro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros || !_iniciado) return;
            _origenFiltro  = (CmbSucursalOrigen.SelectedItem as Opcion)?.Id ?? "";
            _destinoFiltro = (CmbSucursalDestino.SelectedItem as Opcion)?.Id ?? "";
            CargarMeses();
            CargarTraspasos();
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
            int uf = Sql.DocumentosTObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosTObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (!CumpleFiltroOrigenDestino(id)) continue;

                var fechaObj = Sql.DocumentosTObj.ObtenerItem("fecha", id);
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
            string busqueda  = _modoFiltro == "busquedas" ? TxtBuscar.Text.Trim().ToLower() : "";
            string mesFiltro = _modoFiltro == "filtros"   ? _mesActivo : "";

            int uf = Sql.DocumentosTObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosTObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                // Filtro por origen/destino (Sql.DocumentosTObj trae TODA la empresa:
                // ver ConsultasEmpresa.ConectarCacheTraspasos y RecargarCacheYVistaAsync).
                if (!CumpleFiltroOrigenDestino(id)) continue;

                string origen  = Sql.DocumentosTObj.ObtenerItem("origen",  id)?.ToString() ?? "";
                string destino = Sql.DocumentosTObj.ObtenerItem("destino", id)?.ToString() ?? "";

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

                string origenDesc  = Sql.SucursalesObj.ObtenerItem("descripcion", origen)?.ToString()  ?? origen;
                string destinoDesc = Sql.SucursalesObj.ObtenerItem("descripcion", destino)?.ToString() ?? destino;

                // Estado: SIEMPRE el valor crudo, sin reinterpretación "pendiente revisar".
                string estado = Sql.DocumentosTObj.ObtenerItem("estado", id)?.ToString() ?? "";

                // Filtro por estado
                if (!string.IsNullOrEmpty(filtroEstado) &&
                    !string.Equals(estado, filtroEstado, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filtro por búsqueda (origen o destino)
                if (!string.IsNullOrEmpty(busqueda))
                    if (!id.Contains(busqueda) &&
                        !origenDesc.ToLower().Contains(busqueda) &&
                        !destinoDesc.ToLower().Contains(busqueda))
                        continue;

                double cant = CalcularCantidad(id);
                lista.Add(new TraspasoFila
                {
                    Linea        = linea++,
                    DocumentoT   = id,
                    Codigo       = Sql.DocumentosTObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                    FechaStr     = $"{fechaDoc:d} {fechaDoc:HH:mm:ss}",
                    OrigenDesc   = origenDesc,
                    DestinoDesc  = destinoDesc,
                    Estado       = estado,
                    Cantidad     = cant
                });

                if (fechaDoc >= fechaMax) { fechaMax = fechaDoc; filaMasReciente = lista[^1]; }

                totalCant += cant;
            }

            Grid1.ItemsSource        = lista;
            TxtTotalCantidad.Text    = totalCant.ToString("N0");
            TxtTotalDocumentos.Text  = lista.Count.ToString("N0");
            TxtTotalPendientes.Text  = lista.Count(f => f.Estado == "pendiente").ToString();
            TxtTotalEntregados.Text  = lista.Count(f => f.Estado == "entregado").ToString();
            LblTipoMovimiento.Text = "Traspasos";
            int año = VisorState.AnioActivo;
            LblSubtitulo.Text = string.IsNullOrEmpty(_mesActivo)
                ? año.ToString()
                : $"{_mesActivo} {año}";

            // Limpiar el panel de detalle al recargar
            OcultarDetalle();
            SeleccionarFilaMasReciente(filaMasReciente);
        }

        // ─── Filtro de estado: solo pendiente/entregado ───────────────────────
        private string ObtenerFiltroEstado()
        {
            if (BtnFiltroPendiente?.IsChecked == true) return "pendiente";
            if (BtnFiltroEntregado?.IsChecked == true) return "entregado";
            return ""; // todos
        }

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
        }

        // ─── Filtro de origen/destino en memoria (mismo criterio que el condLado
        //     que armaba ConsultasEmpresa.ConectarCacheTraspasos por SQL) ───────
        //     Ambos combos fijados  → el documento debe cumplir los dos.
        //     Solo uno fijado       → el documento debe cumplir ese lado.
        //     Ninguno fijado        → toda la empresa, sin filtro.
        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
        private void SeleccionarFilaMasReciente(TraspasoFila? fila)
        {
            if (fila == null) return;
            Grid1.SelectedItem = fila;
            Grid1.ScrollIntoView(fila);
        }

        private bool CumpleFiltroOrigenDestino(string documentoT)
        {
            bool porOrigen  = !string.IsNullOrEmpty(_origenFiltro);
            bool porDestino = !string.IsNullOrEmpty(_destinoFiltro);
            if (!porOrigen && !porDestino) return true;

            string origen  = Sql.DocumentosTObj.ObtenerItem("origen",  documentoT)?.ToString() ?? "";
            string destino = Sql.DocumentosTObj.ObtenerItem("destino", documentoT)?.ToString() ?? "";

            if (porOrigen && origen != _origenFiltro) return false;
            if (porDestino && destino != _destinoFiltro) return false;
            return true;
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

        // Abre TraspasosDetalle (formulario real de la app principal, vinculado) en
        // modo solo lectura, sin Guardar ni edición. TraspasosDetalle muestra un
        // único campo "Sucursal" (la opuesta a AppState.TipoMovimiento): con un
        // filtro de Origen puntual, esa sucursal es la referencia (el filtro ya
        // garantiza que coincide con el origen del documento) → "salida"; con un
        // filtro de Destino puntual, la referencia es el destino → "entrada". Si
        // ninguno está fijado no hay una única referencia — se asume "salida" (el
        // campo Sucursal del detalle mostrará el destino), ya que la grilla ya
        // muestra Origen+Destino sin ambigüedad antes de abrir el detalle.
        private void AbrirVerDetalle()
        {
            if (Grid1.SelectedItem is not TraspasoFila fila) return;
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;

            AppState.TipoMovimiento = !string.IsNullOrEmpty(_origenFiltro)  ? "salida"
                                    : !string.IsNullOrEmpty(_destinoFiltro) ? "entrada"
                                    : "salida";
            AppState.EventoFormularioM = "editar";

            string titulo = $"Traspaso {fila.Codigo}";
            var dlg = new TraspasosDetalle(null, fila.DocumentoT, tituloTab: titulo);
            dlg.Cerrando += () => consola.CerrarPestaña(dlg);
            consola.AbrirPestaña(titulo, dlg, $"traspaso-{fila.DocumentoT}");
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

        // ─── Actualizar (única acción disponible: solo lectura) ───────────────
        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.DocumentosTObj.Actualizar();
            Sql.TraspasosObj.Actualizar();
            CargarTraspasos();
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class TraspasoFila
    {
        public int    Linea       { get; set; }
        public string DocumentoT  { get; set; } = "";
        public string Codigo      { get; set; } = "";
        public string FechaStr    { get; set; } = "";
        public string OrigenDesc  { get; set; } = "";
        public string DestinoDesc { get; set; } = "";
        public string Estado      { get; set; } = "";
        public double Cantidad    { get; set; }

        /// <summary>Texto mostrado en la columna Estado: "pendiente" se ve como
        /// "pendiente al revisar" (el valor crudo de Estado no cambia, solo el
        /// texto — ver nota de la clase).</summary>
        public string EstadoTexto => Estado == "pendiente" ? "pendiente al revisar" : Estado;
    }

    public class TraspasoDetalleFila
    {
        public int    Linea       { get; set; }
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public double Cantidad    { get; set; }
    }
}
