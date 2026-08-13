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
    /// Duplicado de SistemaGestion.FacturasGeneral para el visor: misma UI/lógica de
    /// listado, pero de SOLO LECTURA (sin Nueva/Editar/Eliminar) y con un combo
    /// de Sucursal (Todas las sucursales o una puntual) en vez de depender de
    /// AppState.SucursalActiva. La caché (Sql.DocumentosFObj/FacturasObj) se
    /// puebla vía ConsultasEmpresa.ConectarCacheFacturas para TODA la empresa
    /// (precalentada al loguear); el combo de Sucursal filtra en memoria, sin
    /// volver a SQL.
    /// </summary>
    public partial class FacturasGeneral : UserControl
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
        /// Tipo de movimiento fijo para este control ("ingreso" o "egreso"). Lo fija la
        /// sección del panel lateral (Ingresos / Egresos), igual que en SistemaGestión.
        /// Vacío = la pantalla lista los dos juntos.
        /// </summary>
        public string TipoMovimiento { get; set; } = "";

        public FacturasGeneral() : this("") { }

        public FacturasGeneral(string tipoMovimiento)
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
        // ConsultasEmpresa) y solo faltan CargarMeses/CargarFacturas. El combo de
        // Sucursal ya NO dispara esta recarga (ver Filtro_SelectionChanged):
        // filtra en memoria sobre los datos de toda la empresa ya cargados.
        private async Task RecargarCacheYVistaAsync()
        {
            string emp  = AppState.EmpresaActiva;
            int    anio = VisorState.AnioActivo;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => ConsultasEmpresa.ConectarCacheFacturas(emp, anio));
                CargarMeses();
                CargarFacturas();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron cargar las facturas:\n{ex.Message}",
                                "Visor Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Cambiar el combo de Sucursal ya NO vuelve a consultar SQL:
        // Sql.DocumentosFObj ya tiene TODA la empresa cargada, así que solo hace
        // falta refiltrar en memoria (CargarMeses/CargarFacturas ya consideran
        // _sucursalFiltro).
        private void Filtro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros || !_iniciado) return;
            _sucursalFiltro = (CmbSucursal.SelectedItem as Opcion)?.Id ?? "";
            CargarMeses();
            CargarFacturas();
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
            int uf = Sql.DocumentosFObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosFObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                if (!string.IsNullOrEmpty(_sucursalFiltro) &&
                    Sql.DocumentosFObj.ObtenerItem("sucursal", id)?.ToString() != _sucursalFiltro)
                    continue;

                // MISMO filtro de movimiento que CargarFacturas: sin esto, la sección
                // Egresos arma el árbol con los meses de los ingresos (y al revés),
                // quedando el árbol lleno con el grid vacío.
                string movArbol = NormalizarMovimiento(Sql.DocumentosFObj.ObtenerItem("movimiento", id)?.ToString());
                if (!string.IsNullOrEmpty(ObtenerFiltroTipo()) &&
                    !string.Equals(movArbol, ObtenerFiltroTipo(), StringComparison.OrdinalIgnoreCase)) continue;

                var fechaObj = Sql.DocumentosFObj.ObtenerItem("fecha", id);
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

                // Filtrar por tipo de movimiento
                string movDoc = NormalizarMovimiento(Sql.DocumentosFObj.ObtenerItem("movimiento", id)?.ToString());
                if (!string.IsNullOrEmpty(filtroTipo) &&
                    !string.Equals(movDoc, filtroTipo, StringComparison.OrdinalIgnoreCase)) continue;

                string suc = Sql.DocumentosFObj.ObtenerItem("sucursal", id)?.ToString() ?? "";

                // Filtro por sucursal (Sql.DocumentosFObj trae TODA la empresa: ver
                // ConsultasEmpresa.ConectarCacheFacturas y RecargarCacheYVistaAsync).
                if (!string.IsNullOrEmpty(_sucursalFiltro) && suc != _sucursalFiltro) continue;

                string sucDesc = Sql.SucursalesObj.ObtenerItem("descripcion", suc)?.ToString() ?? suc;

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
                    SucursalDesc = sucDesc,
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
            int año = VisorState.AnioActivo;
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

        // El movimiento lo fija la sección del panel lateral; vacío = los dos juntos.
        private string ObtenerFiltroTipo() => TipoMovimiento;

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
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
        /// facturas creadas antes del cambio de vocabulario guardaron
        /// "venta"/"compra": se leen como su equivalente.
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

        // ─── Suma el importe total de las líneas de un documento ──────────────
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

        // Abre FacturasDetalle (formulario real de la app principal, vinculado) en
        // modo solo lectura: mismos campos/badges/cobros que la app principal, sin
        // Guardar ni edición de líneas.
        private void AbrirVerDetalle()
        {
            if (Grid1.SelectedItem is not FacturaFila fila) return;
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;

            string titulo = $"Factura {fila.Codigo}";
            var dlg = new FacturasDetalle(null, fila.Id, tituloTab: titulo);
            dlg.Cerrando += () => consola.CerrarPestaña(dlg);
            consola.AbrirPestaña(titulo, dlg, $"factura-{fila.Id}");
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

        // ─── Actualizar (única acción disponible: solo lectura) ───────────────
        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.DocumentosFObj.Actualizar();
            Sql.FacturasObj.Actualizar();
            CargarFacturas();
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
        public string   SucursalDesc { get; set; } = "";
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
