using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VisorEmpresa;
using VisorEmpresa.Data;   // AppState, SqlData, CodigoRegenerator, PedidosPrecioActualizador, AppsheetsSync

namespace SistemaGestion
{
    /// <summary>
    /// Consola del VISOR (ConsolaVisor.xaml). Declara la clase
    /// SistemaGestion.ConsolaMovimientos a propósito — ver la nota en el XAML: los
    /// formularios vinculados de la app principal buscan esa clase para abrir sus
    /// pestañas (AbrirPestaña / CerrarPestaña / SeleccionarPestaña /
    /// CerrarPestañaPorClave / ConfirmarCierrePestañasRelacionadas), y al
    /// compilarla aquí (la consola original NO se vincula) funcionan sin cambios.
    ///
    /// Secciones: Dashboard + documentos en solo-visualización (Pedidos,
    /// Traspasos, Correcciones, Facturas — duplicados fieles de los controles
    /// de la app principal, con un combo de Sucursal propio en vez de
    /// AppState.SucursalActiva) + catálogos con edición completa (Precios,
    /// Empresas, Sucursales, Usuarios — formularios vinculados de la app
    /// principal).
    /// </summary>
    public partial class ConsolaMovimientos : Window
    {
        private Button? _btnActivo;

        // Auto-actualización (Velopack). Flujo manual: aviso → descarga → reiniciar.
        private readonly ActualizadorApp _actualizador = new();

        // Filtros globales de la top bar (empresa / año).
        private bool _iniciadoFiltros;
        private bool _cargandoFiltros;

        // Item de los combos de la top bar (Id oculto + texto visible).
        private class Opcion
        {
            public string Id    { get; }
            public string Texto { get; }
            public Opcion(string id, string texto) { Id = id; Texto = texto; }
            public override string ToString() => Texto;
        }

        // Paneles fijos por sección: mutables para poder recrearlos al cambiar de
        // empresa (mismo criterio que RecargarContexto en la app principal).
        private DashboardVisor    _panelDashboard    = new();
        private ArticulosGeneral  _panelArticulos    = new();
        private FamiliasGeneral   _panelFamilias     = new();
        private ProductosGeneral  _panelProductos    = new();
        private IndustriasGeneral _panelIndustrias   = new();
        private CategoriasGeneral _panelCategorias   = new();
        private PedidosGeneral    _panelVentas       = new("venta");
        private PedidosGeneral    _panelCompras      = new("compra");
        private TraspasosGeneral  _panelTraspasos    = new();
        private CorreccionesGeneral _panelRepuestas  = new("repuesta");
        private CorreccionesGeneral _panelRetirados  = new("retirado");
        private FacturasGeneral   _panelIngresos     = new("ingreso");
        private FacturasGeneral   _panelEgresos      = new("egreso");
        private PreciosGeneral    _panelPrecios      = new();
        private EmpresasGeneral   _panelEmpresas     = new();
        // Calificado explícitamente: SistemaGestion también tiene su propia clase
        // SucursalesGeneral (reducida a selector para Traspasos) — sin calificar,
        // "mismo namespace gana sobre using" resolvería a esa por error.
        private VisorEmpresa.SucursalesGeneral _panelSucursales = new();
        private RegionesGeneral   _panelRegiones     = new();
        private UsuariosGeneral   _panelUsuarios     = new();

        // Cada sección del menú lateral conserva su propio juego de pestañas dinámicas.
        private string _seccionActiva = "dashboard";
        private readonly Dictionary<string, List<TabItem>> _pestañasPorSeccion = new()
        {
            ["dashboard"]    = new List<TabItem>(),
            ["articulos"]    = new List<TabItem>(),
            ["familias"]     = new List<TabItem>(),
            ["productos"]    = new List<TabItem>(),
            ["industrias"]   = new List<TabItem>(),
            ["categorias"]   = new List<TabItem>(),
            ["ventas"]       = new List<TabItem>(),
            ["compras"]      = new List<TabItem>(),
            ["traspasos"]    = new List<TabItem>(),
            ["repuestas"]    = new List<TabItem>(),
            ["retirados"]    = new List<TabItem>(),
            ["ingresos"]     = new List<TabItem>(),
            ["egresos"]      = new List<TabItem>(),
            ["precios"]      = new List<TabItem>(),
            ["empresas"]     = new List<TabItem>(),
            ["sucursales"]   = new List<TabItem>(),
            ["regiones"]     = new List<TabItem>(),
            ["usuarios"]     = new List<TabItem>(),
        };
        private readonly Dictionary<string, TabItem?> _pestañaSeleccionadaPorSeccion = new()
        {
            ["dashboard"]    = null,
            ["articulos"]    = null,
            ["familias"]     = null,
            ["productos"]    = null,
            ["industrias"]   = null,
            ["categorias"]   = null,
            ["ventas"]       = null,
            ["compras"]      = null,
            ["traspasos"]    = null,
            ["repuestas"]    = null,
            ["retirados"]    = null,
            ["ingresos"]     = null,
            ["egresos"]      = null,
            ["precios"]      = null,
            ["empresas"]     = null,
            ["sucursales"]   = null,
            ["regiones"]     = null,
            ["usuarios"]     = null,
        };

        public ConsolaMovimientos()
        {
            InitializeComponent();
            TabFijoContenido.Content = _panelDashboard;
            MostrarVersion();
            ActualizarInfoUsuario();
            ActualizarIconoTema();
            MarcarActivo(BtnNav_Dashboard);

            // Estado de conexión: pintar el estado actual y escuchar cambios.
            ActualizarLabelConexion(ConexionEstado.EnLinea);
            ConexionEstado.Cambio += OnConexionCambio;
            ConexionEstado.Iniciar(Dispatcher);

            // Buscar actualizaciones en segundo plano (no bloquea el arranque).
            // Si hay una nueva versión, aparece el botón "🔄 Actualizar" en la top bar.
            _ = BuscarActualizacionesAsync();

            // Filtros globales (empresa/año) de la top bar. Los paneles cargan solos
            // con los valores por defecto (empresa del login + año actual), así que
            // esta población inicial no dispara recargas (_cargandoFiltros).
            Loaded += async (_, _) =>
            {
                if (_iniciadoFiltros) return;
                _iniciadoFiltros = true;
                await CargarFiltrosTopAsync();
            };
        }

        // ─── Filtros globales de la top bar (empresa / año) ───────────────────
        private async Task CargarFiltrosTopAsync()
        {
            _cargandoFiltros = true;
            try
            {
                var empresas = await Task.Run(ConsultasEmpresa.CargarEmpresas);
                var opciones = empresas.Select(e => new Opcion(e.Id, e.Descripcion)).ToList();
                CmbEmpresaTop.ItemsSource = opciones;

                int idx = opciones.FindIndex(o => o.Id == AppState.EmpresaActiva);
                bool sinEmpresa = idx < 0 && opciones.Count > 0;
                if (idx < 0) idx = opciones.Count > 0 ? 0 : -1;
                CmbEmpresaTop.SelectedIndex = idx;

                // Admin sin empresa asignada: fijar la primera del combo y recargar
                // las cachés de los módulos de edición para esa empresa.
                if (sinEmpresa)
                {
                    AppState.EmpresaActiva   = opciones[0].Id;
                    VisorState.EmpresaActiva = opciones[0].Id;
                    Mouse.OverrideCursor = Cursors.Wait;
                    await Task.Run(() => AppLoader.ConectarProductos());
                    // Precalienta stock + pedidos/traspasos/correcciones del Dashboard
                    // para esta empresa (mismo motivo que en LoginVisorWindow).
                    await Task.Run(() => ConsultasEmpresa.ObtenerStockEmpresa(opciones[0].Id));
                    // Ídem Pedidos/Traspasos/Correcciones/FacturasGeneral (toda la
                    // empresa, año activo — ver el mismo comentario en LoginVisorWindow).
                    int añoPrevio = VisorState.AnioActivo;
                    await Task.Run(() =>
                    {
                        ConsultasEmpresa.ConectarCachePedidos(opciones[0].Id, añoPrevio);
                        ConsultasEmpresa.ConectarCacheTraspasos(opciones[0].Id, añoPrevio);
                        ConsultasEmpresa.ConectarCacheCorrecciones(opciones[0].Id, añoPrevio);
                        ConsultasEmpresa.ConectarCacheFacturas(opciones[0].Id, añoPrevio);
                    });
                    ActualizarInfoUsuario();
                    RefrescarPanelesDatos();
                }

                await RepoblarAniosTopAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron cargar los filtros de empresa/año:\n{ex.Message}",
                                "Visor Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _cargandoFiltros = false;
            }
        }

        private async Task RepoblarAniosTopAsync()
        {
            var anios = await Task.Run(() => ConsultasEmpresa.CargarAnios(AppState.EmpresaActiva));
            CmbAnioTop.ItemsSource = anios;

            int idx = anios.IndexOf(VisorState.AnioActivo);
            if (idx < 0) idx = anios.IndexOf(DateTime.Now.Year);
            if (idx < 0) idx = 0;
            CmbAnioTop.SelectedIndex = anios.Count > 0 ? idx : -1;

            if (CmbAnioTop.SelectedItem is int anio)
                VisorState.AnioActivo = anio;
        }

        private async void CmbEmpresaTop_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros) return;
            string nueva = (CmbEmpresaTop.SelectedItem as Opcion)?.Id ?? "";
            if (string.IsNullOrEmpty(nueva) || nueva == AppState.EmpresaActiva) return;

            // Cambiar de empresa cierra todas las pestañas y recrea los paneles
            // (mismo criterio que el cambio de contexto de la app principal):
            // confirmar primero si hay cambios sin guardar.
            if (!ConfirmarPerderCambios())
            {
                _cargandoFiltros = true;
                var lista = CmbEmpresaTop.ItemsSource as List<Opcion>;
                CmbEmpresaTop.SelectedIndex = lista?.FindIndex(o => o.Id == AppState.EmpresaActiva) ?? -1;
                _cargandoFiltros = false;
                return;
            }

            _cargandoFiltros = true;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                AppState.EmpresaActiva   = nueva;
                VisorState.EmpresaActiva = nueva;
                AppState.RegionActiva    = "";

                // Recargar las cachés empresa-scoped de los módulos de edición.
                await Task.Run(() => AppLoader.ConectarProductos());

                // Precalienta stock + pedidos/traspasos/correcciones del Dashboard
                // para la nueva empresa (mismo motivo que en LoginVisorWindow): sin
                // esto, la primera consulta del Dashboard tras el cambio de empresa
                // dispara la recarga completa de la caché en ese momento.
                await Task.Run(() => ConsultasEmpresa.ObtenerStockEmpresa(nueva));

                await RepoblarAniosTopAsync();

                // Precalienta Pedidos/Traspasos/Correcciones/FacturasGeneral (toda la
                // empresa, ya con el año correcto para la nueva empresa que acaba de
                // fijar RepoblarAniosTopAsync). Las 4 pantallas quedan con la misma
                // clave (empresa, año) al recrearse en RecargarPaneles(), así que su
                // propia carga inicial es un no-op (ver memoización en ConsultasEmpresa).
                int añoNuevo = VisorState.AnioActivo;
                await Task.Run(() =>
                {
                    ConsultasEmpresa.ConectarCachePedidos(nueva, añoNuevo);
                    ConsultasEmpresa.ConectarCacheTraspasos(nueva, añoNuevo);
                    ConsultasEmpresa.ConectarCacheCorrecciones(nueva, añoNuevo);
                    ConsultasEmpresa.ConectarCacheFacturas(nueva, añoNuevo);
                });

                RecargarPaneles();
                ActualizarInfoUsuario();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo cambiar de empresa:\n{ex.Message}",
                                "Visor Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _cargandoFiltros = false;
            }
        }

        private void CmbAnioTop_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros) return;
            if (CmbAnioTop.SelectedItem is not int anio || anio == VisorState.AnioActivo) return;

            VisorState.AnioActivo = anio;
            RefrescarPanelesDatos();
        }

        // Recarga los paneles con datos (dashboard + vistas de documentos) que ya
        // se hayan inicializado; los no abiertos cargarán solos al abrirse.
        private void RefrescarPanelesDatos()
        {
            _panelDashboard.RefrescarDatos();
            _panelVentas.RefrescarDatos();
            _panelCompras.RefrescarDatos();
            _panelTraspasos.RefrescarDatos();
            _panelRepuestas.RefrescarDatos();
            _panelRetirados.RefrescarDatos();
            _panelIngresos.RefrescarDatos();
            _panelEgresos.RefrescarDatos();
        }

        // Cierra todas las pestañas dinámicas y recrea TODOS los paneles para que
        // relean las cachés/filtros recién cargados (equivalente a RecargarContexto
        // de la app principal, manteniendo la sección enfocada).
        private void RecargarPaneles()
        {
            for (int i = TabContenido.Items.Count - 1; i >= 0; i--)
                if (TabContenido.Items[i] is TabItem t && t != TabFijo)
                    TabContenido.Items.RemoveAt(i);
            foreach (var clave in _pestañasPorSeccion.Keys.ToList())
                _pestañasPorSeccion[clave].Clear();
            foreach (var clave in _pestañaSeleccionadaPorSeccion.Keys.ToList())
                _pestañaSeleccionadaPorSeccion[clave] = null;

            _panelDashboard    = new();
            _panelArticulos    = new();
            _panelFamilias     = new();
            _panelProductos    = new();
            _panelIndustrias   = new();
            _panelCategorias   = new();
            _panelVentas       = new("venta");
            _panelCompras      = new("compra");
            _panelTraspasos    = new();
            _panelRepuestas  = new("repuesta");
            _panelRetirados  = new("retirado");
            _panelIngresos     = new("ingreso");
            _panelEgresos      = new("egreso");
            _panelPrecios      = new();
            _panelEmpresas     = new();
            _panelSucursales   = new();
            _panelRegiones     = new();
            _panelUsuarios     = new();

            AsignarPanelFijo(_seccionActiva);
            TabContenido.SelectedItem = TabFijo;
        }

        // ─── Versión de la app (barra de título de la ventana) ────────────────
        private void MostrarVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            Title = v == null
                ? "Visor Empresa"
                : $"Visor Empresa  v{v.Major}.{v.Minor}.{v.Build}";
        }

        // ─── Auto-actualización (Velopack) ────────────────────────────────────
        private async Task BuscarActualizacionesAsync()
        {
            try
            {
                if (await _actualizador.HayActualizacionAsync())
                {
                    BloqueActualizar.Visibility = Visibility.Visible;
                    BtnActualizar.Visibility    = Visibility.Visible;
                    BtnActualizar.ToolTip       = $"Nueva versión disponible: {_actualizador.VersionNueva}";
                }
            }
            catch
            {
                // Sin red o sin feed accesible: silencioso. Se reintenta al próximo arranque.
            }
        }

        // Estado A → B: el usuario pulsa "Actualizar". Descarga en segundo plano con barra.
        private async void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            BtnActualizar.Visibility = Visibility.Collapsed;
            PanelDescarga.Visibility = Visibility.Visible;
            LblDescarga.Text         = "Descargando…";
            BarraDescarga.Value      = 0;

            double totalMB = _actualizador.TamañoDescargaMB;

            var progreso = new Progress<int>(p =>
            {
                BarraDescarga.Value = p;
                double bajadoMB = totalMB * p / 100.0;
                LblDescarga.Text = totalMB > 0
                    ? $"Descargando… {bajadoMB:0.0} / {totalMB:0.0} MB ({p}%)"
                    : $"Descargando… {p}%";
            });

            try
            {
                await _actualizador.DescargarAsync(progreso);
                // Estado B → C: lista. El usuario decide cuándo reiniciar.
                PanelDescarga.Visibility = Visibility.Collapsed;
                BtnReiniciar.Visibility  = Visibility.Visible;
            }
            catch
            {
                // Falló la descarga: volver al estado A para poder reintentar.
                PanelDescarga.Visibility = Visibility.Collapsed;
                BtnActualizar.Visibility = Visibility.Visible;
                MessageBox.Show(
                    "No se pudo descargar la actualización. Revisa tu conexión e inténtalo de nuevo.",
                    "Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Estado C: aplica lo descargado y reinicia la app ya actualizada.
        private void BtnReiniciar_Click(object sender, RoutedEventArgs e)
        {
            _actualizador.AplicarYReiniciar();
        }

        // ─── Estado de conexión (top bar) ─────────────────────────────────────
        private void OnConexionCambio(bool enLinea) => ActualizarLabelConexion(enLinea);

        private void ActualizarLabelConexion(bool enLinea)
        {
            if (enLinea)
            {
                LblConexion.Text = "●  En línea";
                PillConexion.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // verde
            }
            else
            {
                LblConexion.Text = "●  Sin conexión";
                PillConexion.Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)); // rojo
            }
        }

        // ─── Info usuario ─────────────────────────────────────────────────────
        // La empresa ya se ve (y se elige) en CmbEmpresaTop: no hace falta repetirla
        // en un TextBlock aparte.
        public void ActualizarInfoUsuario()
        {
            var sql = SqlData.Instance;
            string nombres = sql.UsuariosObj.ObtenerItem("nombres", AppState.UsuarioActivo)?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(nombres)) nombres = AppState.UsuarioActivo;

            LblUsuario.Text = $"Usuario: {nombres}";
        }

        // ─── Tema claro / oscuro ──────────────────────────────────────────────
        // El tema del visor es INDEPENDIENTE del de la app principal: se persiste
        // solo en %LOCALAPPDATA%\VisorEmpresa\theme.txt (TemaVisor). La app principal
        // también es 100% local (ThemeManager, theme.txt propio) — usuarios.temaC ya
        // no lo usa ninguna de las dos (columna en desuso, ver sesión 2026-07-27).
        private void BtnTema_Click(object sender, RoutedEventArgs e)
        {
            string nuevo = TemaVisor.EsOscuroActivo ? TemaVisor.TemaClaro : TemaVisor.TemaOscuro;
            TemaVisor.AplicarTema(nuevo);
            AppState.TemaActivo = nuevo;   // por si algún formulario vinculado lo consulta
            ActualizarIconoTema();
            // Los gráficos del dashboard resuelven sus brushes al dibujar: re-render.
            _panelDashboard.RefrescarTema();
        }

        private void ActualizarIconoTema()
        {
            BtnTema.Content = TemaVisor.EsOscuroActivo ? "☀" : "🌙";
        }

        // ─── Navegación por pestañas (mismo mecanismo que la app principal) ───
        private void MostrarPanel(string nombre)
        {
            if (nombre == _seccionActiva)
            {
                TabContenido.SelectedItem = TabFijo;
                return;
            }

            // 1. Guardar la pestaña activa y las pestañas dinámicas de la sección actual
            _pestañaSeleccionadaPorSeccion[_seccionActiva] = TabContenido.SelectedItem as TabItem;
            var guardadas = _pestañasPorSeccion[_seccionActiva];
            guardadas.Clear();
            for (int i = TabContenido.Items.Count - 1; i >= 0; i--)
            {
                if (TabContenido.Items[i] is TabItem t && t != TabFijo)
                {
                    guardadas.Insert(0, t);
                    TabContenido.Items.RemoveAt(i);
                }
            }

            // 2. Cambiar el contenido y título de la pestaña fija
            AsignarPanelFijo(nombre);

            // 3. Restaurar las pestañas propias de la nueva sección
            _seccionActiva = nombre;
            var restaurar = _pestañasPorSeccion[nombre];
            foreach (var t in restaurar)
                TabContenido.Items.Add(t);
            restaurar.Clear();

            // 4. Restaurar la pestaña que estaba activa al salir de esta sección
            var selAnterior = _pestañaSeleccionadaPorSeccion[nombre];
            TabContenido.SelectedItem = (selAnterior != null && TabContenido.Items.Contains(selAnterior))
                ? selAnterior
                : TabFijo;
        }

        // Contenido y título de la pestaña fija según la sección.
        private void AsignarPanelFijo(string nombre)
        {
            switch (nombre)
            {
                case "dashboard":    TabFijoContenido.Content = _panelDashboard;    TabFijoTitulo.Text = "Dashboard";    break;
                case "articulos":    TabFijoContenido.Content = _panelArticulos;    TabFijoTitulo.Text = "Artículos";    break;
                case "familias":     TabFijoContenido.Content = _panelFamilias;     TabFijoTitulo.Text = "Familias";     break;
                case "productos":    TabFijoContenido.Content = _panelProductos;    TabFijoTitulo.Text = "Productos";    break;
                case "industrias":   TabFijoContenido.Content = _panelIndustrias;   TabFijoTitulo.Text = "Industrias";   break;
                case "categorias":   TabFijoContenido.Content = _panelCategorias;   TabFijoTitulo.Text = "Categorías";   break;
                case "ventas":       TabFijoContenido.Content = _panelVentas;       TabFijoTitulo.Text = "Ventas";       break;
                case "compras":      TabFijoContenido.Content = _panelCompras;      TabFijoTitulo.Text = "Compras";      break;
                case "traspasos":    TabFijoContenido.Content = _panelTraspasos;    TabFijoTitulo.Text = "Traspasos";    break;
                case "repuestas":    TabFijoContenido.Content = _panelRepuestas;    TabFijoTitulo.Text = "Ingresos";     break;
                case "retirados":    TabFijoContenido.Content = _panelRetirados;    TabFijoTitulo.Text = "Egresos";      break;
                case "ingresos":     TabFijoContenido.Content = _panelIngresos;     TabFijoTitulo.Text = "Créditos";     break;
                case "egresos":      TabFijoContenido.Content = _panelEgresos;      TabFijoTitulo.Text = "Débitos";      break;
                case "precios":      TabFijoContenido.Content = _panelPrecios;      TabFijoTitulo.Text = "Precios";      break;
                case "empresas":     TabFijoContenido.Content = _panelEmpresas;     TabFijoTitulo.Text = "Empresas";     break;
                case "sucursales":   TabFijoContenido.Content = _panelSucursales;   TabFijoTitulo.Text = "Sucursales";   break;
                case "regiones":     TabFijoContenido.Content = _panelRegiones;     TabFijoTitulo.Text = "Regiones";     break;
                case "usuarios":     TabFijoContenido.Content = _panelUsuarios;     TabFijoTitulo.Text = "Usuarios";     break;
            }
        }

        public void AbrirPestaña(string titulo, UIElement contenido, string? clave = null)
        {
            foreach (TabItem t in TabContenido.Items)
            {
                if (clave != null && t.Tag as string == clave) { TabContenido.SelectedItem = t; return; }
                if (clave == null && t.Content == contenido)   { TabContenido.SelectedItem = t; return; }
            }

            var lblTitulo = new TextBlock
            {
                Text = titulo,
                VerticalAlignment = VerticalAlignment.Center
            };
            var btnCerrar = new Button
            {
                Content           = "✕",
                Background        = Brushes.Transparent,
                BorderThickness   = new Thickness(0),
                Padding           = new Thickness(6, 0, 0, 0),
                FontSize          = 10,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB8)),
                VerticalAlignment = VerticalAlignment.Center,
                FocusVisualStyle  = null,
                Cursor            = Cursors.Hand
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(lblTitulo);
            header.Children.Add(btnCerrar);

            var tab = new TabItem { Header = header, Content = contenido, Tag = clave };
            btnCerrar.Click += (s, e) =>
            {
                e.Handled = true;
                var intentar = contenido.GetType().GetMethod("IntentarCerrar", Type.EmptyTypes);
                if (intentar != null) intentar.Invoke(contenido, null);
                else CerrarPestaña(contenido);
            };

            TabContenido.Items.Add(tab);
            TabContenido.SelectedItem = tab;
            MostrarPestañaEnBarra(tab);
        }

        // Trae la pestaña al área visible de la barra. Con muchas abiertas, las más
        // antiguas quedan fuera de vista (a la izquierda) y solo se llega a ellas con
        // el scroll horizontal: sin esto, abrir o elegir una pestaña la dejaría
        // seleccionada pero fuera de la vista.
        private void MostrarPestañaEnBarra(TabItem tab)
            => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(tab.BringIntoView));

        public void CerrarPestaña(UIElement contenido)
        {
            TabItem? target = null;
            foreach (TabItem t in TabContenido.Items)
                if (t.Content == contenido) { target = t; break; }
            if (target == null) return;
            int idx = TabContenido.Items.IndexOf(target);
            TabContenido.Items.Remove(target);
            if (TabContenido.Items.Count > 0)
                TabContenido.SelectedIndex = Math.Max(0, idx - 1);
        }

        public void SeleccionarPestaña(UIElement? contenido)
        {
            if (contenido == null) return;
            foreach (TabItem t in TabContenido.Items)
                if (t.Content == contenido) { TabContenido.SelectedItem = t; return; }
        }

        public void CerrarPestañaPorClave(string clave)
        {
            TabItem? target = null;
            foreach (TabItem t in TabContenido.Items)
                if (t.Tag as string == clave) { target = t; break; }
            if (target == null) return;
            int idx = TabContenido.Items.IndexOf(target);
            TabContenido.Items.Remove(target);
            if (TabContenido.Items.Count > 0)
                TabContenido.SelectedIndex = Math.Max(0, idx - 1);
        }

        // ─── Lista completa de pestañas (botón "▾" de la barra) ───────────────
        // La barra es una sola fila con scroll horizontal: si no entran todas, las
        // más antiguas quedan fuera de vista. Este menú las lista TODAS y salta a la
        // elegida trayéndola a la vista, igual que el botón de pestañas de Chrome.
        private void BtnPestanasTodas_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var menu = new ContextMenu
            {
                PlacementTarget = btn,
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Style           = (Style)FindResource("TabListaMenu")
            };

            var estiloItem = (Style)FindResource("TabListaItem");
            foreach (TabItem t in TabContenido.Items)
            {
                bool actual = ReferenceEquals(t, TabContenido.SelectedItem);
                var item = new MenuItem
                {
                    Header = FilaListaPestaña(TituloPestaña(t), actual),
                    Style  = estiloItem
                };
                var destino = t;
                item.Click += (_, _) =>
                {
                    TabContenido.SelectedItem = destino;
                    MostrarPestañaEnBarra(destino);
                };
                menu.Items.Add(item);
            }

            btn.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // La barra de pestañas no tiene scroll vertical, así que la rueda del mouse
        // sobre ella se usa para moverla a lo ancho.
        private void BarraPestanas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer barra) return;
            barra.ScrollToHorizontalOffset(barra.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        // Fila de la lista: un punto azul (el acento de la app) delante de la pestaña
        // que se está viendo, y el título al lado. El hueco del punto se reserva
        // siempre para que todos los títulos queden alineados.
        private static StackPanel FilaListaPestaña(string titulo, bool actual)
        {
            var fila = new StackPanel { Orientation = Orientation.Horizontal };
            fila.Children.Add(new TextBlock
            {
                Text              = actual ? "●" : "",
                Width             = 14,
                FontSize          = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xE3))
            });
            fila.Children.Add(new TextBlock
            {
                Text              = titulo,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight        = actual ? FontWeights.Bold : FontWeights.Normal
            });
            return fila;
        }

        // ─── Verificar y cerrar pestañas vinculadas antes de guardar/cerrar ─────
        // Devuelve true si se puede continuar (no hay pestañas o el usuario aceptó cerrarlas).
        public bool ConfirmarCierrePestañasRelacionadas(string contexto)
        {
            if (string.IsNullOrEmpty(contexto)) return true;
            var relacionadas = new List<TabItem>();
            foreach (TabItem t in TabContenido.Items)
                if (t.Tag is string clave && clave.EndsWith($"|{contexto}"))
                    relacionadas.Add(t);
            if (relacionadas.Count == 0) return true;

            string lista = string.Join("\n• ", relacionadas.Select(t =>
                t.Header is StackPanel sp
                    ? sp.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "pestaña"
                    : t.Tag?.ToString() ?? "pestaña"));

            var res = MessageBox.Show(
                $"Tiene pestaña(s) vinculada(s) aún abierta(s):\n• {lista}\n\nAceptar: cerrarlas y continuar.\nCancelar: volver sin cerrar nada.",
                "Pestañas relacionadas abiertas",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.OK) return false;
            foreach (var t in relacionadas)
                TabContenido.Items.Remove(t);
            return true;
        }

        // ─── Resaltar ítem activo en la barra lateral ─────────────────────────
        private void MarcarActivo(Button btn)
        {
            if (_btnActivo != null)
            {
                _btnActivo.Background  = Brushes.Transparent;
                _btnActivo.BorderBrush = Brushes.Transparent;
                _btnActivo.SetResourceReference(Control.ForegroundProperty, "ThemeTextoSec");
            }
            _btnActivo = btn;
            // SetResourceReference (en vez de asignar un Brush fijo) para que el
            // resaltado del ítem activo siga el tema actual incluso si el usuario
            // cambia de tema sin volver a hacer clic en el ítem.
            btn.SetResourceReference(Control.BackgroundProperty, "ThemeNavActivoBg");
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xE3));
            btn.SetResourceReference(Control.ForegroundProperty, "ThemeNavActivoFg");
        }

        // ─── Navegación lateral ───────────────────────────────────────────────

        private void BtnNav_Dashboard_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("dashboard");
            MarcarActivo(BtnNav_Dashboard);
        }

        private void BtnNav_Articulos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("articulos");
            MarcarActivo(BtnNav_Articulos);
        }

        private void BtnNav_Familias_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("familias");
            MarcarActivo(BtnNav_Familias);
        }

        private void BtnNav_Productos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("productos");
            MarcarActivo(BtnNav_Productos);
        }

        private void BtnNav_Industrias_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("industrias");
            MarcarActivo(BtnNav_Industrias);
        }

        private void BtnNav_Categorias_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("categorias");
            MarcarActivo(BtnNav_Categorias);
        }

        private void BtnNav_Ventas_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("ventas");
            MarcarActivo(BtnNav_Ventas);
        }

        private void BtnNav_Compras_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("compras");
            MarcarActivo(BtnNav_Compras);
        }

        private void BtnNav_Traspasos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("traspasos");
            MarcarActivo(BtnNav_Traspasos);
        }

        private void BtnNav_Repuestas_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("repuestas");
            MarcarActivo(BtnNav_Repuestas);
        }

        private void BtnNav_Retirados_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("retirados");
            MarcarActivo(BtnNav_Retirados);
        }

        private void BtnNav_Ingresos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("ingresos");
            MarcarActivo(BtnNav_Ingresos);
        }

        private void BtnNav_Egresos_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("egresos");
            MarcarActivo(BtnNav_Egresos);
        }

        private void BtnNav_Precios_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("precios");
            MarcarActivo(BtnNav_Precios);
        }

        private void BtnNav_Empresas_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("empresas");
            MarcarActivo(BtnNav_Empresas);
        }

        private void BtnNav_Sucursales_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("sucursales");
            MarcarActivo(BtnNav_Sucursales);
        }

        private void BtnNav_Regiones_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("regiones");
            MarcarActivo(BtnNav_Regiones);
        }

        private void BtnNav_Usuarios_Click(object sender, RoutedEventArgs e)
        {
            MostrarPanel("usuarios");
            MarcarActivo(BtnNav_Usuarios);
        }

        // ─── Herramientas de administración (mismas de Configuracion en la app
        //     principal, duplicadas aquí para acceso directo desde el visor —
        //     Configuracion.xaml de la app principal NO se tocó). Sin gating por
        //     AppState.EsAdmin: toda cuenta que logra entrar al visor ya es admin
        //     (el login solo deja pasar tipo "admin"). Mismas confirmaciones y
        //     mismos textos que la app principal: son operaciones que SOBRESCRIBEN
        //     datos de TODA la empresa activa (todas sus sucursales) y no son
        //     reversibles — ver CodigoRegenerator para el detalle por tabla. ─
        private async void BtnRegenerarCodigos_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Se regenerarán los códigos (desde 1) de las tablas maestras (globales, toda la base) " +
                "y de documentosT/I/P/C/F/L de la EMPRESA ACTIVA — no toca documentos de otras empresas. " +
                "Al finalizar se cerrará la sesión.\n\nEsta acción SOBRESCRIBE los códigos existentes " +
                "en el servidor activo. ¿Continuar?",
                "Regenerar códigos", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                string resumen = await Task.Run(CodigoRegenerator.RegenerarTodos);

                MessageBox.Show($"Códigos regenerados (filas actualizadas):\n\n{resumen}" +
                                "\n\nSe cerrará la sesión.",
                                "Regenerar códigos", MessageBoxButton.OK, MessageBoxImage.Information);

                CerrarSesionForzada();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al regenerar códigos: {ex.Message}",
                                "Regenerar códigos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void BtnRecalcularPrecios_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Se recalculará el importe de TODOS los pedidos de tipo automático (toda la tabla, " +
                "todas las sucursales), según la lista de precios vigente a la fecha de cada documento. " +
                "Los pedidos de tipo manual no se modifican. Si un pedido no tiene ninguna lista de " +
                "precios aplicable, su importe quedará en 0.\n\n" +
                "Esta acción SOBRESCRIBE los importes existentes en el servidor activo. ¿Continuar?",
                "Recalcular precios", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                string resumen = await Task.Run(PedidosPrecioActualizador.ActualizarImportesAutomaticos);

                MessageBox.Show($"Recálculo finalizado:\n\n{resumen}",
                                "Recalcular precios", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al recalcular precios: {ex.Message}",
                                "Recalcular precios", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void BtnSincronizarAppsheets_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                string resumen = AppsheetsSync.SincronizarTodasLasSucursales();

                MessageBox.Show(resumen, "Sincronizar AppSheets",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al sincronizar AppSheets: {ex.Message}",
                                "Sincronizar AppSheets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        // Borra FÍSICAMENTE (DELETE real) las filas ocultas/eliminadas de la
        // EMPRESA ACTIVA en las 26 tablas y renumera 'indice' sin huecos. Es la
        // única acción de la app que no es reversible ni queda como estadof —
        // ver RegistrosOcultosPurgador para el detalle por tabla.
        private async void BtnPurgarOcultos_Click(object sender, RoutedEventArgs e)
        {
            var r1 = MessageBox.Show(
                "Se BORRARÁN DEFINITIVAMENTE (sin posibilidad de recuperación) todas las filas " +
                "con estado oculto/eliminado de la EMPRESA ACTIVA, en todas las tablas — no toca " +
                "otras empresas. Luego se renumerará sin huecos la columna 'indice' de las líneas " +
                "de documentos y de artículos. Al finalizar se cerrará la sesión.\n\n" +
                "Esta acción es IRREVERSIBLE. ¿Continuar?",
                "Eliminar registros ocultos", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r1 != MessageBoxResult.Yes) return;

            var r2 = MessageBox.Show(
                "Última confirmación: los datos borrados NO se pueden recuperar. ¿Seguro que querés continuar?",
                "Eliminar registros ocultos", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r2 != MessageBoxResult.Yes) return;

            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                string resumen = await Task.Run(RegistrosOcultosPurgador.PurgarTodos);

                MessageBox.Show($"Registros ocultos eliminados (filas afectadas):\n\n{resumen}" +
                                "\n\nSe cerrará la sesión.",
                                "Eliminar registros ocultos", MessageBoxButton.OK, MessageBoxImage.Information);

                CerrarSesionForzada();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar registros ocultos: {ex.Message}",
                                "Eliminar registros ocultos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        // ─── Cambios sin guardar (mismo mecanismo que la app principal) ───────

        // Marcado cuando el cierre ya fue confirmado (p. ej. desde Cerrar sesión) para
        // que el evento Closing no vuelva a preguntar.
        private bool _cierreConfirmado = false;

        // Devuelve los títulos de las pestañas (nuevo/editar) con cambios sin guardar,
        // tanto en la sección actual como en las demás secciones.
        private List<string> PestañasConCambios()
        {
            var res = new List<string>();
            foreach (TabItem t in TabContenido.Items)
                if (t != TabFijo && TieneCambiosSinGuardar(t.Content)) res.Add(TituloPestaña(t));
            foreach (var lista in _pestañasPorSeccion.Values)
                foreach (var t in lista)
                    if (TieneCambiosSinGuardar(t.Content)) res.Add(TituloPestaña(t));
            return res;
        }

        // Extrae el texto del título de una pestaña: en las dinámicas el Header es un
        // StackPanel con un TextBlock de título seguido del botón de cierre; en la
        // pestaña fija es un TextBlock suelto (TabFijoTitulo).
        private static string TituloPestaña(TabItem t)
        {
            if (t.Header is StackPanel sp)
                foreach (var hijo in sp.Children)
                    if (hijo is TextBlock tb) return tb.Text;
            if (t.Header is TextBlock titulo) return titulo.Text;
            return t.Header?.ToString() ?? "(pestaña)";
        }

        // Lee por reflexión el estado de cambios del detalle: la propiedad "HayCambios"
        // o el campo "_hayCambios". Los paneles que no tengan ninguno cuentan como
        // "sin cambios".
        private static bool TieneCambiosSinGuardar(object? content)
        {
            if (content == null) return false;
            var tipo = content.GetType();

            var prop = tipo.GetProperty("HayCambios",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.PropertyType == typeof(bool))
                return prop.GetValue(content) is bool pb && pb;

            var field = tipo.GetField("_hayCambios",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
                return field.GetValue(content) is bool fb && fb;

            return false;
        }

        // Pide confirmación si hay cambios sin guardar, indicando EXACTAMENTE en qué
        // pestañas. Devuelve true si se puede cerrar.
        public bool ConfirmarPerderCambios()
        {
            var conCambios = PestañasConCambios();
            if (conCambios.Count == 0) return true;

            string detalle = string.Join("\n", conCambios.ConvertAll(t => "   •  " + t));
            var res = MessageBox.Show(
                "Hay cambios sin guardar en:\n\n" + detalle +
                "\n\nSi cierras se perderán esos cambios.\n¿Seguro que deseas cerrar?",
                "Cerrar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return res == MessageBoxResult.Yes;
        }

        private void ConsolaVisor_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_cierreConfirmado && !ConfirmarPerderCambios())
            {
                e.Cancel = true;   // el usuario decidió no cerrar
                return;
            }
            ConexionEstado.Cambio -= OnConexionCambio;
        }

        private void BtnCerrarSesion_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmarPerderCambios()) return;
            CerrarSesionInterno();
        }

        // Cierra sesión sin volver a pedir confirmación de cambios sin guardar: la
        // usa un llamador que ya avisó al usuario de antemano (Regenerar códigos).
        public void CerrarSesionForzada() => CerrarSesionInterno();

        private void CerrarSesionInterno()
        {
            _cierreConfirmado = true;   // ya confirmado: Closing no vuelve a preguntar

            AppState.SesionActiva  = false;
            AppState.UsuarioActivo = "";
            AppState.EmpresaActiva = "";
            VisorState.UsuarioActivo = "";
            VisorState.EmpresaActiva = "";

            // Las cachés de ConsultasEmpresa son static: sin limpiarlas acá,
            // volver a entrar con la misma empresa+año reusa lo que quedó en
            // memoria (las claves memoizadas coinciden y ConectarCache* sale por
            // el early-return) en vez de releer SQL. Por eso los códigos recién
            // regenerados no se veían al reingresar.
            ConsultasEmpresa.LimpiarCaches();

            DatabaseConnection.CerrarConexion();

            var login = new LoginVisorWindow();
            login.Show();
            Close();
        }
    }
}
