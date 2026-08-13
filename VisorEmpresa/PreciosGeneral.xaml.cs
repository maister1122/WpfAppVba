using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SistemaGestion;
using VisorEmpresa.Data;

namespace VisorEmpresa
{
    public partial class PreciosGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;
        private int    _añoActivo  = 0;
        private int    _mesActivo  = 0;
        private string _modoFiltro = "filtros"; // "filtros" = Tree1 | "busquedas" = TxtBuscar

        private bool _iniciado = false;

        public event Action? Cerrando;
        public void IntentarCerrar() => Cerrando?.Invoke();

        public PreciosGeneral()
        {
            InitializeComponent();
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; ConfigurarModo(); CargarRegiones(); SincronizarArbolFechas(); CargarListas(); };
        }

        private void ConfigurarModo()
        {
            if (!AppState.EsAdmin)
            {
                BtnNuevo.Visibility    = Visibility.Collapsed;
                BtnCopiar.Visibility   = Visibility.Collapsed;
                BtnEditar.Visibility   = Visibility.Collapsed;
                BtnEliminar.Visibility = Visibility.Collapsed;
            }
        }

        // ─── Combo de regiones (filtro) ───────────────────────────────────────
        private void CargarRegiones()
        {
            var lista = new List<RegionItem> { new RegionItem { Id = "", Descripcion = "Todas las regiones" } };

            int uf = Sql.RegionesObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.RegionesObj.Mover(i);
                if (idObj == null) continue;
                string id      = idObj.ToString()!;
                string codigo  = Sql.RegionesObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
                string desc    = Sql.RegionesObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                lista.Add(new RegionItem { Id = id, Descripcion = $"{codigo} - {desc}" });
            }

            CboRegion.DisplayMemberPath = "Descripcion";
            CboRegion.SelectedValuePath = "Id";
            CboRegion.ItemsSource       = lista;
            CboRegion.SelectedIndex     = 0;
        }

        // ─── Sincroniza el árbol de años/meses con los documentos registrados ─
        // documentosL/precios se cargan sin límite de fecha (toda la empresa, ver
        // AppLoader.ConectarProductos): a diferencia de Traspasos/Pedidos no hay un
        // único período activo, así que el árbol muestra solo los años y meses que
        // realmente tienen documentos (sin nodo "General"); sin ninguno registrado
        // queda vacío y CargarListas() muestra todas las listas.
        // Sincronización incremental: agrega/quita nodos puntuales sin Clear(), para
        // conservar el estado expandido/seleccionado de los nodos no afectados. Se
        // llama al iniciar y luego de Nuevo/Editar/Eliminar/Actualizar.
        private void SincronizarArbolFechas()
        {
            var mesesPorAño = new Dictionary<int, SortedSet<int>>();
            int uf = Sql.DocumentosLObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosLObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                var fechaObj = Sql.DocumentosLObj.ObtenerItem("fecha", id);
                if (fechaObj == null) continue;
                DateTime fecha = Convert.ToDateTime(fechaObj);

                if (!mesesPorAño.TryGetValue(fecha.Year, out var mesesAño))
                    mesesPorAño[fecha.Year] = mesesAño = new SortedSet<int>();
                mesesAño.Add(fecha.Month);
            }

            bool seleccionEliminada = false;

            // Quitar años (y sus meses) que ya no tienen documentos; dentro de los
            // años que quedan, quitar los meses puntuales sin documentos.
            for (int i = Tree1.Items.Count - 1; i >= 0; i--)
            {
                if (Tree1.Items[i] is not TreeViewItem nodoAño || nodoAño.Tag is not (int anioNodo, _))
                    continue;

                if (!mesesPorAño.TryGetValue(anioNodo, out var mesesAño))
                {
                    if (NodoOHijoSeleccionado(nodoAño)) seleccionEliminada = true;
                    Tree1.Items.RemoveAt(i);
                    continue;
                }

                for (int j = nodoAño.Items.Count - 1; j >= 0; j--)
                {
                    if (nodoAño.Items[j] is not TreeViewItem nodoMes || nodoMes.Tag is not (_, int mesNodo))
                        continue;
                    if (mesesAño.Contains(mesNodo)) continue;
                    if (nodoMes.IsSelected) seleccionEliminada = true;
                    nodoAño.Items.RemoveAt(j);
                }
            }

            // Agregar años/meses con documentos que todavía no están en el árbol.
            foreach (int año in mesesPorAño.Keys.OrderByDescending(a => a))
            {
                var nodoAño = BuscarNodoAño(año);
                if (nodoAño == null)
                {
                    nodoAño = new TreeViewItem { Header = año.ToString(), Tag = (Anio: año, Mes: 0), IsExpanded = true };
                    InsertarNodoAñoOrdenado(nodoAño, año);
                }

                foreach (int mes in mesesPorAño[año])
                {
                    if (BuscarNodoMes(nodoAño, mes) != null) continue;
                    var nodoMes = new TreeViewItem { Header = ObtenerNombreMes(mes), Tag = (Anio: año, Mes: mes) };
                    InsertarNodoMesOrdenado(nodoAño, nodoMes, mes);
                }
            }

            if (seleccionEliminada)
            {
                _añoActivo = 0;
                _mesActivo = 0;
            }
        }

        private TreeViewItem? BuscarNodoAño(int año)
        {
            foreach (var item in Tree1.Items)
                if (item is TreeViewItem nodo && nodo.Tag is (int anio, _) && anio == año) return nodo;
            return null;
        }

        private static TreeViewItem? BuscarNodoMes(TreeViewItem nodoAño, int mes)
        {
            foreach (var item in nodoAño.Items)
                if (item is TreeViewItem nodo && nodo.Tag is (_, int m) && m == mes) return nodo;
            return null;
        }

        // Inserta manteniendo el orden descendente de años.
        private void InsertarNodoAñoOrdenado(TreeViewItem nodoNuevo, int año)
        {
            int idx = 0;
            while (idx < Tree1.Items.Count
                   && Tree1.Items[idx] is TreeViewItem existente
                   && existente.Tag is (int anioExistente, _)
                   && anioExistente > año)
                idx++;
            Tree1.Items.Insert(idx, nodoNuevo);
        }

        // Inserta manteniendo el orden ascendente de meses dentro de un año.
        private static void InsertarNodoMesOrdenado(TreeViewItem nodoAño, TreeViewItem nodoNuevo, int mes)
        {
            int idx = 0;
            while (idx < nodoAño.Items.Count
                   && nodoAño.Items[idx] is TreeViewItem existente
                   && existente.Tag is (_, int mesExistente)
                   && mesExistente < mes)
                idx++;
            nodoAño.Items.Insert(idx, nodoNuevo);
        }

        private static bool NodoOHijoSeleccionado(TreeViewItem nodo)
        {
            if (nodo.IsSelected) return true;
            foreach (var item in nodo.Items)
                if (item is TreeViewItem hijo && hijo.IsSelected) return true;
            return false;
        }

        // ─── Carga la lista de documentos de precios (documentosL) ───────────
        public void CargarListas()
        {
            if (TxtBuscar == null || Grid1 == null) return;

            var lista = new List<PrecioListaFila>();
            int linea = 1;
            string busqueda     = _modoFiltro == "busquedas" ? TxtBuscar.Text.Trim().ToLower() : "";
            int    mesFiltro    = _modoFiltro == "filtros"   ? _mesActivo : 0;
            int    añoFiltro    = _modoFiltro == "filtros"   ? _añoActivo : 0;
            string regionId     = CboRegion?.SelectedValue?.ToString() ?? "";
            string filtroEstado = ObtenerFiltroEstado();

            int uf = Sql.DocumentosLObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.DocumentosLObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string region = Sql.DocumentosLObj.ObtenerItem("region", id)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(regionId) && region != regionId) continue;

                var fechaDocObj = Sql.DocumentosLObj.ObtenerItem("fecha", id);
                DateTime fechaDoc = fechaDocObj != null ? Convert.ToDateTime(fechaDocObj) : default;

                if (añoFiltro > 0 && (fechaDocObj == null || fechaDoc.Year != añoFiltro)) continue;

                if (mesFiltro > 0 && (fechaDocObj == null || fechaDoc.Month != mesFiltro)) continue;

                if (!string.IsNullOrEmpty(filtroEstado))
                {
                    string estadoDoc = (Sql.DocumentosLObj.ObtenerItem("estado", id)?.ToString() ?? "").ToLower();
                    bool esPendienteDoc = estadoDoc == "pendiente";
                    string estadoNormalizado = esPendienteDoc ? "pendiente" : "valido";
                    if (estadoNormalizado != filtroEstado) continue;
                }

                string codigo      = Sql.DocumentosLObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
                string observacion = Sql.DocumentosLObj.ObtenerItem("observacion", id)?.ToString() ?? "";

                if (!string.IsNullOrEmpty(busqueda))
                    if (!codigo.ToLower().Contains(busqueda) && !observacion.ToLower().Contains(busqueda))
                        continue;

                lista.Add(ConstruirFila(id, linea++));
            }

            Grid1.ItemsSource       = lista;
            TxtTotalDocumentos.Text = lista.Count.ToString("N0");
            TxtTotalPendientes.Text = lista.Count(f => f.Estado == "pendiente").ToString("N0");

            LblSubtitulo.Text = _añoActivo == 0
                ? "Todas las listas"
                : _mesActivo == 0 ? _añoActivo.ToString() : $"{ObtenerNombreMes(_mesActivo)} {_añoActivo}";

            OcultarDetalle();
            SeleccionarFilaMasReciente(lista);
        }

        // ─── Selección inicial del grid ───────────────────────────────────────
        // Al asignar ItemsSource, WPF deja seleccionada la PRIMERA fila (el
        // CurrentItem del CollectionView). Se prefiere la MÁS RECIENTE por fecha.
        private void SeleccionarFilaMasReciente(List<PrecioListaFila> lista)
        {
            if (lista.Count == 0) return;
            var fila = lista.Aggregate((a, b) => b.Fecha >= a.Fecha ? b : a);
            Grid1.SelectedItem = fila;
            Grid1.ScrollIntoView(fila);
        }

        // ─── Filtros ──────────────────────────────────────────────────────────
        private string ObtenerFiltroEstado()
        {
            if (BtnFiltroPendiente?.IsChecked == true) return "pendiente";
            if (BtnFiltroValido?.IsChecked    == true) return "valido";
            return "";
        }

        // ─── Nombre de mes ────────────────────────────────────────────────────
        private static string ObtenerNombreMes(int mes)
        {
            string[] meses = { "Enero","Febrero","Marzo","Abril","Mayo","Junio",
                                "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
            return mes >= 1 && mes <= 12 ? meses[mes - 1] : "";
        }

        // ─── Helpers de actualización incremental del Grid1 ───────────────────
        private List<PrecioListaFila> FilasGrid =>
            Grid1.ItemsSource as List<PrecioListaFila> ?? new List<PrecioListaFila>();

        private PrecioListaFila ConstruirFila(string id, int linea)
        {
            var fechaObj = Sql.DocumentosLObj.ObtenerItem("fecha", id);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : default;
            string region  = Sql.DocumentosLObj.ObtenerItem("region", id)?.ToString() ?? "";
            string estado  = Sql.DocumentosLObj.ObtenerItem("estado", id)?.ToString() ?? "";
            bool esPendiente = estado == "pendiente";
            int cantLineas = CalcularTotales(id);
            return new PrecioListaFila
            {
                Linea      = linea,
                Id         = id,
                Codigo     = Sql.DocumentosLObj.ObtenerItem("codigo", id)?.ToString() ?? "",
                Fecha      = fecha,
                FechaStr   = fecha != default ? $"{fecha:d} {fecha:HH:mm:ss}" : "",
                RegionDesc = Sql.RegionesObj.ObtenerItem("descripcion", region)?.ToString() ?? "",
                Referencia = Sql.DocumentosLObj.ObtenerItem("referencia", id)?.ToString() ?? "",
                Estado      = esPendiente ? "pendiente" : "valido",
                EstadoTexto = esPendiente ? "Pendiente"  : "Válido",
                Lineas     = cantLineas
            };
        }

        private void RenumerarYTotales()
        {
            var lista = FilasGrid;
            int n = 1;
            foreach (var f in lista) f.Linea = n++;
            TxtTotalDocumentos.Text = lista.Count.ToString("N0");
            TxtTotalPendientes.Text = lista.Count(f => f.Estado == "pendiente").ToString("N0");
            Grid1.Items.Refresh();
        }

        // ─── Cuenta líneas de un documentoL ───────────────────────────────────
        private static int CalcularTotales(string documentoL)
        {
            int lineas = 0;
            int uf = Sql.PreciosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PreciosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PreciosObj.ObtenerItem("documentoL", id)?.ToString() != documentoL) continue;
                lineas++;
            }
            return lineas;
        }

        // ─── Panel de detalle artículos (Lista2) ─────────────────────────────
        private void MostrarDetalle(string documentoL)
        {
            string codigoDoc = Sql.DocumentosLObj.ObtenerItem("codigo", documentoL)?.ToString() ?? documentoL;
            LblDetalleHeader.Text = $"Artículos de la lista {codigoDoc}";
            var detalles = new List<PrecioDetalleFila>();
            int linea = 1;

            int uf = Sql.PreciosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PreciosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PreciosObj.ObtenerItem("documentoL", id)?.ToString() != documentoL) continue;

                string artId  = Sql.PreciosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string codigo = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? artId;

                string desc     = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
                string famId    = Sql.ArticulosObj.ObtenerItem("familia", artId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo", artId)?.ToString() ?? "";
                string descFull = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                double precio = Convert.ToDouble(Sql.PreciosObj.ObtenerItem("precio", id) ?? 0);

                detalles.Add(new PrecioDetalleFila
                {
                    Linea       = linea++,
                    Codigo      = codigo,
                    Descripcion = descFull,
                    Precio      = precio
                });
            }

            Lista2.ItemsSource = detalles;
        }

        private void OcultarDetalle()
        {
            LblDetalleHeader.Text = "Artículos de la lista";
            Lista2.ItemsSource    = null;
        }

        // ─── Selección en Grid1 → mostrar detalle ────────────────────────────
        private void Grid1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Grid1.SelectedItem is PrecioListaFila fila)
                MostrarDetalle(fila.Id);
            else
                OcultarDetalle();
        }

        // ─── Eventos de árbol ─────────────────────────────────────────────────
        private void Tree1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (Tree1.SelectedItem is TreeViewItem ti && ti.Tag is (int anio, int mes))
            {
                _añoActivo = anio;
                _mesActivo = mes;
            }
            _modoFiltro = "filtros";
            CargarListas();
        }

        private void Tree1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not TreeViewItem)
                source = VisualTreeHelper.GetParent(source);

            if (source is TreeViewItem tvi && tvi.IsSelected)
            {
                _modoFiltro = "filtros";
                CargarListas();
            }
        }

        private void CboRegion_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => CargarListas();

        private void FiltroEstado_Checked(object sender, RoutedEventArgs e)
            => CargarListas();

        // ─── Búsqueda (independiente del Tree1) ──────────────────────────────
        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _modoFiltro = "busquedas";
            CargarListas();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            _modoFiltro = "busquedas";
            CargarListas();
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
            if (!AppState.EsAdmin) return;

            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = "Nueva Lista de Precios";
            string clave  = "nueva-lista-precios";
            var dlg = new PreciosDetalle(this, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.ItemCreadoId == null) return;
                SincronizarArbolFechas();
                var nueva = ConstruirFila(dlg.ItemCreadoId, 0);
                FilasGrid.Add(nueva);
                RenumerarYTotales();
                Grid1.SelectedItem = nueva; Grid1.ScrollIntoView(nueva);
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(titulo, dlg, clave);
        }

        // ─── Copiar el documento seleccionado como base de una lista nueva ────
        private void BtnCopiar_Click(object sender, RoutedEventArgs e)
        {
            if (!AppState.EsAdmin) return;
            if (Grid1.SelectedItem is not PrecioListaFila fila) return;

            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = $"Copia de {fila.Codigo}";
            var dlg = new PreciosDetalle(this, tituloTab: titulo, idCopiarDe: fila.Id);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.ItemCreadoId == null) return;
                SincronizarArbolFechas();
                var nueva = ConstruirFila(dlg.ItemCreadoId, 0);
                FilasGrid.Add(nueva);
                RenumerarYTotales();
                Grid1.SelectedItem = nueva; Grid1.ScrollIntoView(nueva);
                GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
            };
            consola.AbrirPestaña(titulo, dlg, $"copiar-lista-precios-{fila.Id}");
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (!AppState.EsAdmin) return;
            AbrirEditar();
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (!AppState.EsAdmin) return;
            if (Grid1.SelectedItem is not PrecioListaFila fila) return;

            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            var res = MessageBox.Show("¿Eliminar esta lista de precios y todas sus líneas?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                int uf = Sql.PreciosObj.ContarFilas;
                var idsOcultar = new List<string>();
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.PreciosObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    if (Sql.PreciosObj.ObtenerItem("documentoL", id)?.ToString() == fila.Id)
                        idsOcultar.Add(id);
                }
                foreach (string id in idsOcultar)
                    Sql.PreciosObj.Ocultar(id);

                Sql.DocumentosLObj.EstablecerItem("edicion",  fila.Id, DateTime.Now);
                Sql.DocumentosLObj.EstablecerItem("usuarioE", fila.Id, AppState.UsuarioActivo);
                Sql.DocumentosLObj.Ocultar(fila.Id);

                Sql.PreciosObj.OrdenarData(("documentoL", false));
                Sql.DocumentosLObj.OrdenarData(("fecha", false));

                SincronizarArbolFechas();

                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0) lista.RemoveAt(idx);
                RenumerarYTotales();

                if (lista.Count > 0)
                {
                    var sel = lista[idx >= 0 ? Math.Min(idx, lista.Count - 1) : 0];
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
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            Sql.DocumentosLObj.Actualizar();
            Sql.PreciosObj.Actualizar();
            CargarRegiones();
            SincronizarArbolFechas();
            CargarListas();
        }

        // articulos.estado ("mostrar"/"ocultar", cargada por el usuario en
        // ArticulosDetalle) filtra qué artículos entran en las plantillas Excel/PDF.
        // Solo se excluye si quedó explícitamente en "ocultar"; null/vacío/cualquier
        // otro valor (artículos guardados antes de que existiera esta columna) cuenta
        // como visible, para no vaciar de golpe las plantillas ya existentes.
        private static bool EsVisibleEnPlantillas(string artId)
        {
            string estadoV = Sql.ArticulosObj.ObtenerItem("estado", artId)?.ToString()?.Trim().ToLowerInvariant() ?? "";
            return estadoV != "ocultar";
        }

        // ─── Crear Plantilla: elige Excel o PDF, después guarda y abre el archivo ──
        // Excel: catálogo completo, sin agrupar, con columnas Producto y Familia
        // (N° / Código / Producto / Familia / Descripción / Precio) — el import
        // (PreciosDetalle.ImportarPreciosDesdeExcel) busca Código y Precio por
        // encabezado, no por posición, así que el orden de columnas no le afecta.
        // PDF: catálogo completo agrupado por Producto & Familia (banda de grupo en
        // vez de columnas propias) — mismo estilo que GenerarPdfListaPrecios.
        // En ambos casos Precio arranca en 0 para que el usuario la complete.
        private void BtnCrearPlantilla_Click(object sender, RoutedEventArgs e)
        {
            var dlgFormato = new SeleccionarFormatoWindow { Owner = Window.GetWindow(this) };
            if (dlgFormato.ShowDialog() != true) return;

            bool esExcel = dlgFormato.FormatoElegido == "excel";

            var dlg = new SaveFileDialog
            {
                Title            = "Guardar plantilla de precios",
                FileName         = $"{DateTime.Now:yyyyMMdd HHmmss} plantilla precios.{(esExcel ? "xlsx" : "pdf")}",
                DefaultExt       = esExcel ? ".xlsx" : ".pdf",
                Filter           = esExcel ? "Excel (*.xlsx)|*.xlsx" : "PDF (*.pdf)|*.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (esExcel) GenerarPlantillaExcel(dlg.FileName);
                else         GenerarPlantillaPdf(dlg.FileName);
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar la plantilla:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void GenerarPlantillaExcel(string filePath)
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Precios");

            ws.Cell(1, 1).Value = "N°";
            ws.Cell(1, 2).Value = "Código";
            ws.Cell(1, 3).Value = "Producto";
            ws.Cell(1, 4).Value = "Familia";
            ws.Cell(1, 5).Value = "Descripción";
            ws.Cell(1, 6).Value = "Precio";

            // Sin reordenar: Sql.ArticulosObj ya viene cargado en cascada Producto →
            // Familia → Índice (ver AppLoader.cs, ORDER BY p.descripcion, f.descripcion,
            // a.indice), el mismo orden que usan PreciosDetalle y ArticulosGeneral.
            int row = 2;
            int numero = 1;
            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string artId = idObj.ToString()!;
                if (!EsVisibleEnPlantillas(artId)) continue;

                string famId    = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto",    famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
                string codigo   = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString() ?? "";
                string desc     = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                ws.Cell(row, 1).Value = numero;
                ws.Cell(row, 2).Value = codigo;
                ws.Cell(row, 3).Value = prodDesc;
                ws.Cell(row, 4).Value = famDesc;
                ws.Cell(row, 5).Value = descCompleta;
                ws.Cell(row, 6).Value = 0; // Precio: arranca en 0 para que el usuario la complete.
                row++;
                numero++;
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        // Plantilla en PDF: catálogo completo agrupado por Producto & Familia (misma
        // banda celeste y estilo de tabla que GenerarPdfListaPrecios más abajo), con
        // Precio en 0 para completar a mano. A diferencia del Excel, acá Producto/
        // Familia NO son columnas de datos: van en la banda que encabeza cada grupo.
        private void GenerarPlantillaPdf(string filePath)
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;

            var fontTitulo = new XFont("Arial", 14, XFontStyleEx.Bold);
            var fontHeader = new XFont("Arial", 9,  XFontStyleEx.Bold);
            var fontGrupo  = new XFont("Arial", 10, XFontStyleEx.Bold);
            var fontCuerpo = new XFont("Arial", 9,  XFontStyleEx.Regular);

            var brushCeleste = new XSolidBrush(XColor.FromArgb(191, 219, 254));
            var brushHeader  = new XSolidBrush(XColor.FromArgb(241, 245, 249));
            var penLinea     = new XPen(XColors.Black, 0.6);

            const double margen      = 40;
            const double altoHeader  = 20;
            const double altoGrupo   = 18;
            const double altoFila    = 16;
            const double anchoN      = 28;
            const double anchoCodigo = 80;
            const double anchoPrecio = 75;

            var document = new PdfDocument();
            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;
            XGraphics gfx = XGraphics.FromPdfPage(page);

            double anchoTabla = page.Width - margen * 2;
            double anchoDesc  = anchoTabla - anchoN - anchoCodigo - anchoPrecio;
            double xN      = margen;
            double xCodigo = xN + anchoN;
            double xDesc   = xCodigo + anchoCodigo;
            double xPrecio = xDesc + anchoDesc;

            double y = margen;

            void DibujarFilaDatos(string n, string codigo, string desc, string precio)
            {
                gfx.DrawRectangle(penLinea, xN, y, anchoTabla, altoFila);
                gfx.DrawLine(penLinea, xCodigo, y, xCodigo, y + altoFila);
                gfx.DrawLine(penLinea, xDesc,   y, xDesc,   y + altoFila);
                gfx.DrawLine(penLinea, xPrecio, y, xPrecio, y + altoFila);

                gfx.DrawString(n,      fontCuerpo, XBrushes.Black, new XRect(xN,          y, anchoN,          altoFila), XStringFormats.Center);
                gfx.DrawString(codigo, fontCuerpo, XBrushes.Black, new XRect(xCodigo + 4, y, anchoCodigo - 8, altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(desc,   fontCuerpo, XBrushes.Black, new XRect(xDesc + 4,   y, anchoDesc - 8,   altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(precio, fontCuerpo, XBrushes.Black, new XRect(xPrecio + 4, y, anchoPrecio - 8, altoFila), XStringFormats.CenterRight);

                y += altoFila;
            }

            void DibujarEncabezadoColumnas()
            {
                gfx.DrawRectangle(penLinea, brushHeader, xN, y, anchoTabla, altoHeader);
                gfx.DrawLine(penLinea, xCodigo, y, xCodigo, y + altoHeader);
                gfx.DrawLine(penLinea, xDesc,   y, xDesc,   y + altoHeader);
                gfx.DrawLine(penLinea, xPrecio, y, xPrecio, y + altoHeader);

                gfx.DrawString("N°",          fontHeader, XBrushes.Black, new XRect(xN,          y, anchoN,          altoHeader), XStringFormats.Center);
                gfx.DrawString("Código",      fontHeader, XBrushes.Black, new XRect(xCodigo + 4, y, anchoCodigo - 8, altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Descripción", fontHeader, XBrushes.Black, new XRect(xDesc + 4,   y, anchoDesc - 8,   altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Precio",      fontHeader, XBrushes.Black, new XRect(xPrecio + 4, y, anchoPrecio - 8, altoHeader), XStringFormats.CenterRight);

                y += altoHeader;
            }

            void DibujarBandaGrupo(string texto)
            {
                gfx.DrawRectangle(penLinea, brushCeleste, xN, y, anchoTabla, altoGrupo);
                gfx.DrawString(texto, fontGrupo, XBrushes.Black, new XRect(xN + 6, y, anchoTabla - 12, altoGrupo), XStringFormats.CenterLeft);
                y += altoGrupo;
            }

            void NuevaPagina()
            {
                gfx.Dispose();
                page = document.AddPage();
                page.Size = PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = margen;
                DibujarEncabezadoColumnas();
            }

            void AsegurarEspacio(double alto)
            {
                if (y + alto > page.Height - margen) NuevaPagina();
            }

            gfx.DrawString("Plantilla de Precios", fontTitulo, XBrushes.Black, new XPoint(margen, y + 12));
            y += 28;

            DibujarEncabezadoColumnas();

            var lineas = new List<(string prodDesc, string famDesc, string codigo, string desc)>();

            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string artId = idObj.ToString()!;
                if (!EsVisibleEnPlantillas(artId)) continue;

                string famId    = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto",    famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
                string codigo   = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString() ?? "";
                string desc     = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                lineas.Add((prodDesc, famDesc, codigo, descCompleta));
            }

            var grupos = lineas
                .GroupBy(l => (l.prodDesc, l.famDesc))
                .OrderBy(g => g.Key.prodDesc, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.famDesc, StringComparer.OrdinalIgnoreCase);

            int n = 0;
            foreach (var grupo in grupos)
            {
                AsegurarEspacio(altoGrupo + altoFila);
                DibujarBandaGrupo($"{grupo.Key.prodDesc} & {grupo.Key.famDesc}");

                foreach (var l in grupo)
                {
                    AsegurarEspacio(altoFila);
                    n++;
                    DibujarFilaDatos(n.ToString(), l.codigo, l.desc, ""); // Precio en blanco para completar a mano.
                }
            }

            // El último gfx de la generación de contenido sigue abierto: hay que
            // cerrarlo antes de poder volver a abrir un XGraphics sobre esa misma
            // página en la segunda pasada de abajo.
            gfx.Dispose();

            // ── Encabezado (empresa / fecha de emisión) y pie de página (páginas), en
            // todas las páginas. Se hace en una segunda pasada porque el total de
            // páginas recién se conoce una vez generado todo el contenido.
            string empresaDesc     = Sql.EmpresasObj.ObtenerItem("descripcion", AppState.EmpresaActiva)?.ToString() ?? "";
            string sucursalDesc    = Sql.SucursalesObj.ObtenerItem("descripcion", AppState.SucursalActiva)?.ToString() ?? "";
            string encabezadoIzq   = string.IsNullOrEmpty(sucursalDesc) ? empresaDesc : $"{empresaDesc} - {sucursalDesc}";
            DateTime fechaEmision  = DateTime.Now;
            string fechaEmisionStr = $"{fechaEmision:d} {fechaEmision:HH:mm:ss}";
            int totalPaginas       = document.PageCount;

            for (int p = 0; p < totalPaginas; p++)
            {
                var paginaActual = document.Pages[p];
                using var gfxPagina = XGraphics.FromPdfPage(paginaActual);
                double anchoMedio = (paginaActual.Width - margen * 2) / 2;

                gfxPagina.DrawString(encabezadoIzq, fontCuerpo, XBrushes.Black,
                    new XRect(margen, 16, anchoMedio, 16), XStringFormats.CenterLeft);
                gfxPagina.DrawString($"Fecha de emisión: {fechaEmisionStr}", fontCuerpo, XBrushes.Black,
                    new XRect(margen + anchoMedio, 16, anchoMedio, 16), XStringFormats.CenterRight);

                gfxPagina.DrawString($"Páginas {p + 1}-{totalPaginas}", fontCuerpo, XBrushes.Black,
                    new XRect(margen, paginaActual.Height - margen + 10, paginaActual.Width - margen * 2, 16), XStringFormats.CenterRight);
            }

            document.Save(filePath);
        }

        // ─── PDF de la lista de precios (botón "Acciones" por fila) ──────────
        private void BtnPdfFila_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not PrecioListaFila fila) return;

            var dlg = new SaveFileDialog
            {
                Title            = "Guardar lista de precios",
                FileName         = $"{DateTime.Now:yyyyMMdd HHmmss} lista de precios {fila.Codigo}.pdf",
                DefaultExt       = ".pdf",
                Filter           = "PDF (*.pdf)|*.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                GenerarPdfListaPrecios(dlg.FileName, fila);

                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el PDF:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        // Agrupa las líneas del documento por familia (encabezado "Producto & Familia").
        // Se dibuja como una tabla real: columnas con líneas separadoras, encabezado de
        // columnas repetido en cada página, y los subtítulos de agrupación resaltados
        // con una banda celeste.
        private void GenerarPdfListaPrecios(string filePath, PrecioListaFila fila)
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;

            var fontTitulo = new XFont("Arial", 14, XFontStyleEx.Bold);
            var fontHeader = new XFont("Arial", 9,  XFontStyleEx.Bold);
            var fontGrupo  = new XFont("Arial", 10, XFontStyleEx.Bold);
            var fontCuerpo = new XFont("Arial", 9,  XFontStyleEx.Regular);

            var brushCeleste = new XSolidBrush(XColor.FromArgb(191, 219, 254));
            var brushHeader  = new XSolidBrush(XColor.FromArgb(241, 245, 249));
            var penLinea     = new XPen(XColors.Black, 0.6);

            const double margen      = 40;
            const double altoHeader  = 20;
            const double altoGrupo   = 18;
            const double altoFila    = 16;
            const double anchoN      = 28;
            const double anchoCodigo = 80;
            const double anchoPrecio = 75;

            var document = new PdfDocument();
            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;
            XGraphics gfx = XGraphics.FromPdfPage(page);

            double anchoTabla = page.Width - margen * 2;
            double anchoDesc  = anchoTabla - anchoN - anchoCodigo - anchoPrecio;
            double xN      = margen;
            double xCodigo = xN + anchoN;
            double xDesc   = xCodigo + anchoCodigo;
            double xPrecio = xDesc + anchoDesc;

            double y = margen;

            // Fila de datos: 4 columnas con sus líneas separadoras.
            void DibujarFilaDatos(string n, string codigo, string desc, string precio)
            {
                gfx.DrawRectangle(penLinea, xN, y, anchoTabla, altoFila);
                gfx.DrawLine(penLinea, xCodigo, y, xCodigo, y + altoFila);
                gfx.DrawLine(penLinea, xDesc,   y, xDesc,   y + altoFila);
                gfx.DrawLine(penLinea, xPrecio, y, xPrecio, y + altoFila);

                gfx.DrawString(n,      fontCuerpo, XBrushes.Black, new XRect(xN,          y, anchoN,          altoFila), XStringFormats.Center);
                gfx.DrawString(codigo, fontCuerpo, XBrushes.Black, new XRect(xCodigo + 4, y, anchoCodigo - 8, altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(desc,   fontCuerpo, XBrushes.Black, new XRect(xDesc + 4,   y, anchoDesc - 8,   altoFila), XStringFormats.CenterLeft);
                gfx.DrawString(precio, fontCuerpo, XBrushes.Black, new XRect(xPrecio + 4, y, anchoPrecio - 8, altoFila), XStringFormats.CenterRight);

                y += altoFila;
            }

            // Encabezado de columnas con fondo gris claro.
            void DibujarEncabezadoColumnas()
            {
                gfx.DrawRectangle(penLinea, brushHeader, xN, y, anchoTabla, altoHeader);
                gfx.DrawLine(penLinea, xCodigo, y, xCodigo, y + altoHeader);
                gfx.DrawLine(penLinea, xDesc,   y, xDesc,   y + altoHeader);
                gfx.DrawLine(penLinea, xPrecio, y, xPrecio, y + altoHeader);

                gfx.DrawString("N°",          fontHeader, XBrushes.Black, new XRect(xN,          y, anchoN,          altoHeader), XStringFormats.Center);
                gfx.DrawString("Código",      fontHeader, XBrushes.Black, new XRect(xCodigo + 4, y, anchoCodigo - 8, altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Descripción", fontHeader, XBrushes.Black, new XRect(xDesc + 4,   y, anchoDesc - 8,   altoHeader), XStringFormats.CenterLeft);
                gfx.DrawString("Precio",      fontHeader, XBrushes.Black, new XRect(xPrecio + 4, y, anchoPrecio - 8, altoHeader), XStringFormats.CenterRight);

                y += altoHeader;
            }

            // Banda de agrupación: una sola celda celeste de ancho completo (sin separadores
            // internos), para que el subtítulo resalte como una franja y no como otra fila de la tabla.
            void DibujarBandaGrupo(string texto)
            {
                gfx.DrawRectangle(penLinea, brushCeleste, xN, y, anchoTabla, altoGrupo);
                gfx.DrawString(texto, fontGrupo, XBrushes.Black, new XRect(xN + 6, y, anchoTabla - 12, altoGrupo), XStringFormats.CenterLeft);
                y += altoGrupo;
            }

            void NuevaPagina()
            {
                gfx.Dispose();
                page = document.AddPage();
                page.Size = PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = margen;
                DibujarEncabezadoColumnas();
            }

            void AsegurarEspacio(double alto)
            {
                if (y + alto > page.Height - margen) NuevaPagina();
            }

            string fechaHora = fila.FechaStr;
            string tituloPrincipal = string.IsNullOrEmpty(fechaHora)
                ? $"Lista de Precios — {fila.Codigo}"
                : $"Lista de Precios — {fila.Codigo}   ({fechaHora})";
            gfx.DrawString(tituloPrincipal, fontTitulo, XBrushes.Black, new XPoint(margen, y + 12));
            y += 28;

            DibujarEncabezadoColumnas();

            var lineas = new List<(string prodDesc, string famDesc, string codigo, string desc, double precio)>();

            int uf = Sql.PreciosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PreciosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PreciosObj.ObtenerItem("documentoL", id)?.ToString() != fila.Id) continue;

                string artId  = Sql.PreciosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string famId  = Sql.ArticulosObj.ObtenerItem("familia", artId)?.ToString() ?? "";
                string prodId = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";

                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString()  ?? "";
                string codigo   = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString()  ?? "";
                string descArt  = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString()  ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString()  ?? "";
                double precio   = Convert.ToDouble(Sql.PreciosObj.ObtenerItem("precio", id) ?? 0);
                if (precio <= 0) continue;

                string descCompleta = FuncionesComunes.UnirVariables(descArt, famDesc, modelo);
                lineas.Add((prodDesc, famDesc, codigo, descCompleta, precio));
            }

            var grupos = lineas
                .GroupBy(l => (l.prodDesc, l.famDesc))
                .OrderBy(g => g.Key.prodDesc, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.famDesc, StringComparer.OrdinalIgnoreCase);

            int n = 0;
            foreach (var grupo in grupos)
            {
                AsegurarEspacio(altoGrupo + altoFila);
                DibujarBandaGrupo($"{grupo.Key.prodDesc} & {grupo.Key.famDesc}");

                foreach (var l in grupo)
                {
                    AsegurarEspacio(altoFila);
                    n++;
                    DibujarFilaDatos(n.ToString(), l.codigo, l.desc, l.precio.ToString("#,##0.##"));
                }
            }

            // El último gfx de la generación de contenido sigue abierto: hay que
            // cerrarlo antes de poder volver a abrir un XGraphics sobre esa misma
            // página en la segunda pasada de abajo.
            gfx.Dispose();

            // ── Encabezado (empresa / fecha de emisión) y pie de página (páginas), en
            // todas las páginas. Se hace en una segunda pasada porque el total de
            // páginas recién se conoce una vez generado todo el contenido.
            string empresaDesc     = Sql.EmpresasObj.ObtenerItem("descripcion", AppState.EmpresaActiva)?.ToString() ?? "";
            string sucursalDesc    = Sql.SucursalesObj.ObtenerItem("descripcion", AppState.SucursalActiva)?.ToString() ?? "";
            string encabezadoIzq   = string.IsNullOrEmpty(sucursalDesc) ? empresaDesc : $"{empresaDesc} - {sucursalDesc}";
            DateTime fechaEmision  = DateTime.Now;
            string fechaEmisionStr = $"{fechaEmision:d} {fechaEmision:HH:mm:ss}";
            int totalPaginas       = document.PageCount;

            for (int p = 0; p < totalPaginas; p++)
            {
                var paginaActual = document.Pages[p];
                using var gfxPagina = XGraphics.FromPdfPage(paginaActual);
                double anchoMedio = (paginaActual.Width - margen * 2) / 2;

                gfxPagina.DrawString(encabezadoIzq, fontCuerpo, XBrushes.Black,
                    new XRect(margen, 16, anchoMedio, 16), XStringFormats.CenterLeft);
                gfxPagina.DrawString($"Fecha de emisión: {fechaEmisionStr}", fontCuerpo, XBrushes.Black,
                    new XRect(margen + anchoMedio, 16, anchoMedio, 16), XStringFormats.CenterRight);

                gfxPagina.DrawString($"Páginas {p + 1}-{totalPaginas}", fontCuerpo, XBrushes.Black,
                    new XRect(margen, paginaActual.Height - margen + 10, paginaActual.Width - margen * 2, 16), XStringFormats.CenterRight);
            }

            document.Save(filePath);
        }

        // ─── Excel de la lista de precios (botón "Excel" por fila) ───────────
        private void BtnExcelFila_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not PrecioListaFila fila) return;

            var dlg = new SaveFileDialog
            {
                Title            = "Guardar lista de precios",
                FileName         = $"{DateTime.Now:yyyyMMdd HHmmss} lista de precios {fila.Codigo}.xlsx",
                DefaultExt       = ".xlsx",
                Filter           = "Excel (*.xlsx)|*.xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            var btn = sender as Button;
            try
            {
                if (btn != null) btn.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                GenerarExcelListaPrecios(dlg.FileName, fila);

                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el Excel:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void GenerarExcelListaPrecios(string filePath, PrecioListaFila fila)
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Lista de Precios");

            ws.Cell(1, 1).Value = "N°";
            ws.Cell(1, 2).Value = "Código";
            ws.Cell(1, 3).Value = "Producto";
            ws.Cell(1, 4).Value = "Familia";
            ws.Cell(1, 5).Value = "Descripción";
            ws.Cell(1, 6).Value = "Precio";

            int uf = Sql.PreciosObj.ContarFilas;
            var lineas = new List<(string prodDesc, string famDesc, string codigo, string desc, double precio)>();

            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.PreciosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (Sql.PreciosObj.ObtenerItem("documentoL", id)?.ToString() != fila.Id) continue;

                string artId    = Sql.PreciosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                string famId    = Sql.ArticulosObj.ObtenerItem("familia", artId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? "";
                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString()  ?? "";
                string codigo   = Sql.ArticulosObj.ObtenerItem("codigo",      artId)?.ToString()  ?? "";
                string descArt  = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString()  ?? "";
                string modelo   = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString()  ?? "";
                double precio   = Convert.ToDouble(Sql.PreciosObj.ObtenerItem("precio", id) ?? 0);
                if (precio <= 0) continue;

                string descCompleta = FuncionesComunes.UnirVariables(descArt, famDesc, modelo);
                lineas.Add((prodDesc, famDesc, codigo, descCompleta, precio));
            }

            int row = 2;
            int n = 0;
            foreach (var l in lineas)
            {
                n++;
                ws.Cell(row, 1).Value = n;
                ws.Cell(row, 2).Value = l.codigo;
                ws.Cell(row, 3).Value = l.prodDesc;
                ws.Cell(row, 4).Value = l.famDesc;
                ws.Cell(row, 5).Value = l.desc;
                ws.Cell(row, 6).Value = l.precio;
                row++;
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        // ─── Helper ───────────────────────────────────────────────────────────
        private void AbrirEditar()
        {
            if (Grid1.SelectedItem is not PrecioListaFila fila) return;

            string idSel = fila.Id;
            int    linea = fila.Linea;

            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = $"Lista de Precios {fila.Codigo}";
            var dlg = new PreciosDetalle(this, idSel, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                SincronizarArbolFechas();
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
            consola.AbrirPestaña(titulo, dlg, $"lista-precios-{idSel}");
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class PrecioListaFila
    {
        public int      Linea      { get; set; }
        public string   Id         { get; set; } = "";
        public string   Codigo     { get; set; } = "";
        public DateTime Fecha      { get; set; }
        public string   FechaStr   { get; set; } = "";
        public string   RegionDesc { get; set; } = "";
        public string   Referencia { get; set; } = "";
        public string   Estado     { get; set; } = "";
        public string   EstadoTexto { get; set; } = "";
        public int      Lineas     { get; set; }
    }

    public class PrecioDetalleFila
    {
        public int    Linea       { get; set; }
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public double Precio      { get; set; }
    }

    public class RegionItem
    {
        public string Id          { get; set; } = "";
        public string Descripcion { get; set; } = "";
    }
}
