using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class InventariosDetalle : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        private readonly InventariosGeneral? _padre;
        private readonly string _idEditar;
        private bool _hayCambios   = false;
        private bool _cargando     = true;
        private bool _iniciado     = false;
        // Solo la apertura más reciente (AppState.AperturaIdActiva) se puede editar/guardar;
        // las anteriores se abren igual, pero en modo lectura (ver InventariosGeneral.AbrirEditar).
        private bool _soloLectura  = false;
        private string _tituloTab = "";
        private string _codigoDocI = "";
        private List<InventarioItemFila> _items = new();
        // Evita guardados duplicados por clicks repetidos en Guardar (ver TraspasosDetalle).
        private bool _guardando = false;

        // ─── Confirmar edición pendiente antes de que el click llegue a su destino ──
        // Bug conocido de WPF DataGrid: si una celda de Grid1 está en edición y se
        // hace click en OTRO control (Guardar, un TextBox de cabecera), el CommitEdit
        // del grid recién se dispara al procesar ESE MISMO click — lo que descarta
        // el click en curso y obliga a hacer uno segundo para que el foco realmente
        // llegue al control de destino. Confirmando acá, en el túnel (se ejecuta ANTES
        // de que el click llegue al control real), se evita esa carrera.
        private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Grid1.IsKeyboardFocusWithin)
                Grid1.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // Modo de filtro activo: "todos" (carga inicial) | "busqueda" (TxtBuscar) | "familia" (Tree1)
        private string _modoFiltro = "todos";

        public event Action? Cerrando;

        /// <summary>ID del documento de inventario recién creado.</summary>
        public string? ItemCreadoId { get; private set; }

        public InventariosDetalle(InventariosGeneral? padre = null, string idEditar = "")
        {
            InitializeComponent();
            _padre     = padre;
            _idEditar  = idEditar;
            _tituloTab = string.IsNullOrEmpty(idEditar) ? "nuevo-inventario" : $"inventario-{idEditar}";
            Loaded    += (_, _) => { if (_iniciado) return; _iniciado = true; CargarUserform(); };
        }

        // ─── Al cerrar: preguntar si hay cambios ──────────────────────────────
        public void IntentarCerrar()
        {
            // Confirma cualquier celda en edición antes de chequear cambios pendientes
            Grid1.CommitEdit(DataGridEditingUnit.Row, true);

            if (!_hayCambios) { Cerrando?.Invoke(); return; }

            var res = MessageBox.Show("¿Guardar Cambios?", "Consola",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes && Guardar()) Cerrando?.Invoke();
            else if (res == MessageBoxResult.No)          Cerrando?.Invoke();
        }

        // ─── Carga inicial ────────────────────────────────────────────────────
        private void CargarUserform()
        {
            _cargando = true;

            if (AppState.EventoFormularioI == "editar")
            {
                LblTitulo.Text = "Editar Inventario";
                CargarParaEditar();
            }
            else
            {
                LblTitulo.Text = "Nuevo Inventario";
                CargarParaNuevo();
            }

            LblDocNum.Text = Box_DocumentoI.Text;
            _cargando   = false;
            _hayCambios = false;
        }

        private void CargarParaEditar()
        {
            Box_DocumentoI.IsEnabled = false;
            string codigoDocEdit = Sql.DocumentosIObj.ObtenerItem("codigo", _idEditar)?.ToString() ?? "";
            Box_DocumentoI.Text = codigoDocEdit;

            var fechaObj = Sql.DocumentosIObj.ObtenerItem("fecha", _idEditar);
            DateTime fecha = fechaObj != null ? Convert.ToDateTime(fechaObj) : DateTime.Now;
            Box_Fecha.SelectedDate = fecha;
            Box_Hora.Text          = fecha.ToString("HH:mm:ss");
            Box_Observacion.Text   = Sql.DocumentosIObj.ObtenerItem("observacion", _idEditar)?.ToString() ?? "";
            Box_Referencia.Text    = Sql.DocumentosIObj.ObtenerItem("referencia",  _idEditar)?.ToString() ?? "";

            // Catálogo completo de artículos, con la cantidad existente (si la hay) de este inventario.
            CargarItems(_idEditar);
            CargarArbol();
            RefrescarGrid();

            _soloLectura = _idEditar != AppState.AperturaIdActiva;
            if (_soloLectura) AplicarModoSoloLectura();
        }

        // ─── Modo lectura: aperturas anteriores a la activa se pueden ver, no editar ──
        private void AplicarModoSoloLectura()
        {
            LblTitulo.Text            = "Ver Inventario (solo lectura)";
            Box_Fecha.IsEnabled       = false;
            Box_Hora.IsEnabled        = false;
            Box_Referencia.IsEnabled  = false;
            Box_Observacion.IsEnabled = false;
            Grid1.IsReadOnly          = true;
            BtnGuardar.IsEnabled      = false;
        }

        private void CargarParaNuevo()
        {
            Box_DocumentoI.IsEnabled = false;
            _codigoDocI = CodigoDocumento.SiguienteInventario(AppState.EmpresaActiva);
            Box_DocumentoI.Text  = _codigoDocI;
            Box_Fecha.SelectedDate = DateTime.Today;
            Box_Hora.Text          = DateTime.Now.ToString("HH:mm:ss");

            CargarItems(null);
            CargarArbol();
            RefrescarGrid();
        }

        // ─── Carga unificada del catálogo de artículos + cantidades existentes ─
        private void CargarItems(string? docIdOrigen)
        {
            var existentes = new Dictionary<string, (string InventarioId, double Cantidad)>();
            if (!string.IsNullOrEmpty(docIdOrigen))
            {
                int uf = Sql.InventariosObj.ContarFilas;
                for (int i = 1; i <= uf; i++)
                {
                    var idObj = Sql.InventariosObj.Mover(i);
                    if (idObj == null) continue;
                    string id = idObj.ToString()!;
                    if (Sql.InventariosObj.ObtenerItem("documentoI", id)?.ToString() != docIdOrigen) continue;

                    string artId    = Sql.InventariosObj.ObtenerItem("articulo", id)?.ToString() ?? "";
                    double cantidad = Convert.ToDouble(Sql.InventariosObj.ObtenerItem("cantidad", id) ?? 0);
                    existentes[artId] = (id, cantidad);
                }
            }

            _items = new List<InventarioItemFila>();
            int ufA = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= ufA; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string artId = idObj.ToString()!;

                existentes.TryGetValue(artId, out var ex);
                _items.Add(new InventarioItemFila
                {
                    InventarioId = ex.InventarioId ?? "",
                    ArticuloId   = artId,
                    Codigo       = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? "",
                    Categoria    = ObtenerCategoriaArticulo(artId),
                    Descripcion  = ObtenerDescripcionArticulo(artId),
                    Cantidad     = ex.Cantidad
                });
            }

            AgregarEliminadosRegistrados(existentes);
        }

        /// <summary>
        /// Suma al listado las líneas del documento cuyo artículo YA NO está en el
        /// catálogo (se ocultó o eliminó de `articulos`, así que ArticulosObj —que
        /// solo trae los 'normal'— no lo tiene). Esas líneas siguen registradas en
        /// `inventarios` con su cantidad: antes desaparecían del formulario y en el
        /// informe salían como una fila con cantidad pero sin código ni descripción.
        /// Acá se recuperan sus datos de SQL y se marcan como eliminadas, para verlas
        /// bajo el nodo "Eliminados" (ver <see cref="CargarArbol"/>).
        /// </summary>
        private void AgregarEliminadosRegistrados(
            Dictionary<string, (string InventarioId, double Cantidad)> existentes)
        {
            var enCatalogo = new HashSet<string>(_items.Select(x => x.ArticuloId),
                                                 StringComparer.OrdinalIgnoreCase);
            var faltantes = existentes.Keys
                .Where(id => !string.IsNullOrEmpty(id) && !enCatalogo.Contains(id))
                .ToList();
            if (faltantes.Count == 0) return;

            var datos = LeerArticulosFueraDeCatalogo(faltantes);
            foreach (string artId in faltantes)
            {
                datos.TryGetValue(artId, out var d);
                _items.Add(new InventarioItemFila
                {
                    InventarioId = existentes[artId].InventarioId,
                    ArticuloId   = artId,
                    Codigo       = d.Codigo ?? "",
                    Categoria    = d.Categoria ?? "",
                    Descripcion  = string.IsNullOrWhiteSpace(d.Descripcion)
                                   ? "(artículo eliminado)"
                                   : d.Descripcion,
                    Cantidad     = existentes[artId].Cantidad,
                    Eliminado    = true
                });
            }
        }

        /// <summary>
        /// Código, descripción y categoría de artículos que ya no están en el
        /// catálogo en memoria: se leen directo de SQL, sin filtrar por `estadof`.
        /// Si la fila tampoco está en la base (se corrió "Eliminar ocultos") el
        /// artículo no vuelve en el resultado y la línea queda solo con su cantidad.
        /// </summary>
        private static Dictionary<string, (string Codigo, string Descripcion, string Categoria)>
            LeerArticulosFueraDeCatalogo(List<string> ids)
        {
            var res = new Dictionary<string, (string Codigo, string Descripcion, string Categoria)>(
                StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0) return res;

            try
            {
                SqlRetry.Ejecutar(() =>
                {
                    var conn = DatabaseConnection.ObtenerConexion();
                    string parametros = string.Join(", ", ids.Select((_, i) => "@id" + i));
                    using var cmd = new SqlCommand(
                        "SELECT a.id, a.codigo, a.descripcion, a.modelo, " +
                        "       ISNULL(f.descripcion, '') AS familia, " +
                        "       ISNULL(c.descripcion, '') AS categoria " +
                        "FROM articulos AS a " +
                        "LEFT JOIN familias   AS f ON f.id = a.familia " +
                        "LEFT JOIN categorias AS c ON c.id = a.categoria " +
                        $"WHERE a.id IN ({parametros})", conn);
                    for (int i = 0; i < ids.Count; i++)
                        cmd.Parameters.AddWithValue("@id" + i, ids[i]);

                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        string id = rd["id"]?.ToString() ?? "";
                        if (id == "") continue;
                        res[id] = (
                            rd["codigo"]?.ToString() ?? "",
                            FuncionesComunes.UnirVariables(
                                rd["descripcion"]?.ToString() ?? "",
                                rd["familia"]?.ToString()     ?? "",
                                rd["modelo"]?.ToString()      ?? ""),
                            rd["categoria"]?.ToString() ?? "");
                    }
                });
            }
            catch
            {
                // Sin conexión: las líneas se muestran igual, sin código ni descripción.
            }
            return res;
        }

        // ─── Descripción de artículo ──────────────────────────────────────────
        private static string ObtenerDescripcionArticulo(string artId)
        {
            if (string.IsNullOrEmpty(artId)) return "";
            string desc    = Sql.ArticulosObj.ObtenerItem("descripcion", artId)?.ToString() ?? "";
            string famId   = Sql.ArticulosObj.ObtenerItem("familia",     artId)?.ToString() ?? "";
            string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
            string modelo  = Sql.ArticulosObj.ObtenerItem("modelo",      artId)?.ToString() ?? "";
            return FuncionesComunes.UnirVariables(desc, famDesc, modelo);
        }

        // ─── Categoría de artículo ─────────────────────────────────────────────
        private static string ObtenerCategoriaArticulo(string artId)
        {
            if (string.IsNullOrEmpty(artId)) return "";
            string catId = Sql.ArticulosObj.ObtenerItem("categoria", artId)?.ToString() ?? "";
            if (string.IsNullOrEmpty(catId)) return "";
            return Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "";
        }

        // Tag del nodo final del árbol: los artículos que ya no están en el catálogo
        // pero siguen registrados en el documento.
        private const string TagEliminados = "eliminados";

        // ─── Árbol de productos/familias (mismo patrón que ArticulosGeneral) ──
        private void CargarArbol()
        {
            Tree1.Items.Clear();
            var nodoTodos = new TreeViewItem { Header = "Todos", Tag = "todos" };

            int ufProd = Sql.ProductosObj.ContarFilas;
            for (int i = 1; i <= ufProd; i++)
            {
                var idObj = Sql.ProductosObj.Mover(i);
                if (idObj == null) continue;
                string prodId   = idObj.ToString()!;
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? prodId;

                var nodoProd = new TreeViewItem { Header = prodDesc, Tag = $"producto:{prodId}" };

                int ufFam = Sql.FamiliasObj.ContarFilas;
                for (int j = 1; j <= ufFam; j++)
                {
                    var famIdObj = Sql.FamiliasObj.Mover(j);
                    if (famIdObj == null) continue;
                    string famId = famIdObj.ToString()!;
                    if ((Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "") != prodId) continue;

                    string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? famId;
                    nodoProd.Items.Add(new TreeViewItem { Header = famDesc, Tag = $"familia:{famId}" });
                }

                if (nodoProd.Items.Count > 0) nodoProd.IsExpanded = true;
                nodoTodos.Items.Add(nodoProd);
            }

            // Último nodo, para lo que no cuelga del catálogo: los artículos que se
            // eliminaron/ocultaron de `articulos` pero siguen registrados en este
            // inventario con su cantidad (ver AgregarEliminadosRegistrados).
            nodoTodos.Items.Add(new TreeViewItem { Header = "Eliminados", Tag = TagEliminados });

            Tree1.Items.Add(nodoTodos);
            nodoTodos.IsExpanded = true;
            nodoTodos.IsSelected = true;
        }

        private string ObtenerTagFiltro()
        {
            if (Tree1.SelectedItem is TreeViewItem item)
                return item.Tag?.ToString() ?? "todos";
            return "todos";
        }

        // ─── Refrescar grid (aplica filtro de árbol/búsqueda) ─────────────────
        private void RefrescarGrid()
        {
            // Confirma cualquier celda en edición antes de reemplazar el ItemsSource: hacerlo
            // a mitad de una transacción AddNew/EditItem dispara InvalidOperationException
            // ("'Refresh' no permitido...") al recrear la vista.
            Grid1.CommitEdit(DataGridEditingUnit.Row, true);

            string busqueda  = _modoFiltro == "busqueda" ? TxtBuscar.Text.Trim().ToLower() : "";
            string tagFiltro = _modoFiltro == "familia"  ? ObtenerTagFiltro()              : "";

            // "Eliminados": solo los artículos que se borraron del
            // catálogo pero siguen registrados en este inventario.
            bool soloEliminados = tagFiltro == TagEliminados;

            var visibles = new List<InventarioItemFila>();
            foreach (var item in _items)
            {
                string famId = Sql.ArticulosObj.ObtenerItem("familia", item.ArticuloId)?.ToString() ?? "";

                if (soloEliminados)
                {
                    if (!item.Eliminado) continue;
                }
                else if (!string.IsNullOrEmpty(tagFiltro))
                {
                    if (tagFiltro.StartsWith("familia:"))
                    {
                        if (famId != tagFiltro.Substring("familia:".Length)) continue;
                    }
                    else if (tagFiltro.StartsWith("producto:"))
                    {
                        string prodFiltro = tagFiltro.Substring("producto:".Length);
                        string famProd    = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";
                        if (famProd != prodFiltro) continue;
                    }
                    // "todos" o vacío → sin filtro
                }

                if (!string.IsNullOrEmpty(busqueda) &&
                    !item.Codigo.ToLower().Contains(busqueda) &&
                    !item.Descripcion.ToLower().Contains(busqueda))
                    continue;

                visibles.Add(item);
            }

            var seleccionado = Grid1.SelectedItem as InventarioItemFila;

            Grid1.ItemsSource = visibles;

            if (seleccionado != null && visibles.Contains(seleccionado))
                Grid1.SelectedItem = seleccionado;

            ActualizarTotales();
            CargarTotalesCategoria();
        }

        // Recalcula los totales sobre TODO el catálogo (no solo lo visible tras el
        // filtro): representan el contenido real del inventario, no la vista.
        private void ActualizarTotales()
        {
            TxtTotalUnidades.Text      = _items.Sum(x => x.Cantidad).ToString("N2");
            TxtUnidadesDiferentes.Text = _items.Count(x => x.Cantidad > 0).ToString();
        }

        // ─── Totales por categoría ────────────────────────────────────────────
        private void CargarTotalesCategoria()
        {
            int ufCat = Sql.CategoriasObj.ContarFilas;
            var categoriaIds     = new List<string>();
            var categoriaDescs   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cantPorCategoria = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i <= ufCat; i++)
            {
                var idObj = Sql.CategoriasObj.Mover(i);
                if (idObj == null) continue;
                string catId   = idObj.ToString()!;
                string catDesc = Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? catId;
                categoriaIds.Add(catId);
                categoriaDescs[catId]   = catDesc;
                cantPorCategoria[catId] = 0;
            }

            double cantOtros = 0;
            foreach (var item in _items)
            {
                if (item.Cantidad <= 0) continue;

                string catId = Sql.ArticulosObj.ObtenerItem("categoria", item.ArticuloId)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(catId) && cantPorCategoria.ContainsKey(catId))
                    cantPorCategoria[catId] += item.Cantidad;
                else
                    cantOtros += item.Cantidad;
            }

            var filas = categoriaIds
                .Select(id => new CategoriaCantFila
                {
                    Categoria = categoriaDescs[id],
                    Cantidad  = cantPorCategoria[id].ToString("N0")
                })
                .ToList();

            filas.Add(new CategoriaCantFila { Categoria = "Otros", Cantidad = cantOtros.ToString("N0") });
            GridCategorias.ItemsSource = filas;
        }

        // ─── Eventos árbol y búsqueda (mismo patrón que ArticulosGeneral) ─────
        private void Tree1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _modoFiltro = "familia";
            RefrescarGrid();
        }

        private void Tree1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not TreeViewItem)
                source = VisualTreeHelper.GetParent(source);

            if (source is TreeViewItem tvi && tvi.IsSelected)
            {
                _modoFiltro = "familia";
                RefrescarGrid();
            }
        }

        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _modoFiltro = "busqueda";
            RefrescarGrid();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            _modoFiltro = "busqueda";
            RefrescarGrid();
        }

        // ─── Detectar cambios ─────────────────────────────────────────────────
        private void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
            if (sender == Box_DocumentoI) LblDocNum.Text = Box_DocumentoI.Text;
        }

        private void Campo_DateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_cargando) _hayCambios = true;
        }

        // ─── Validación de entrada ────────────────────────────────────────────
        private void Box_Numeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => FuncionesComunes.ValidarSoloNumeros(sender, e, permitirDecimales: false);

        // ─── Celda Cantidad editada ────────────────────────────────────────────
        private void Grid1_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            _hayCambios = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ActualizarTotales();
                CargarTotalesCategoria();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ─── Seleccionar todo al entrar a la celda Cantidad ───────────────────
        private void Grid1_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Column.Header?.ToString() != "Cantidad") return;
            GridFocusHelper.SeleccionarTodoEnEdicion(e.EditingElement);
            if (e.EditingElement is TextBox tb) FuncionesComunes.RestringirACantidad(tb);
        }

        // ─── Actualizar (recarga catálogo desde SQL conservando cantidades editadas) ─
        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            string artSelId = (Grid1.SelectedItem as InventarioItemFila)?.ArticuloId ?? "";

            AppState.ActualizarProductos();

            var porArticulo = _items.ToDictionary(x => x.ArticuloId, x => x);

            var actualizados = new List<InventarioItemFila>();
            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string artId = idObj.ToString()!;

                if (porArticulo.TryGetValue(artId, out var existente))
                {
                    existente.Codigo      = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? "";
                    existente.Categoria   = ObtenerCategoriaArticulo(artId);
                    existente.Descripcion = ObtenerDescripcionArticulo(artId);
                    // Si el artículo volvió al catálogo, deja de estar eliminado.
                    existente.Eliminado   = false;
                    actualizados.Add(existente);
                }
                else
                {
                    actualizados.Add(new InventarioItemFila
                    {
                        InventarioId = "",
                        ArticuloId   = artId,
                        Codigo       = Sql.ArticulosObj.ObtenerItem("codigo", artId)?.ToString() ?? "",
                        Categoria    = ObtenerCategoriaArticulo(artId),
                        Descripcion  = ObtenerDescripcionArticulo(artId),
                        Cantidad     = 0
                    });
                }
            }

            // Los artículos eliminados que siguen registrados en el inventario no
            // están en el catálogo: si no se re-agregan acá, desaparecerían al
            // actualizar y volvería el problema de la línea sin datos en el informe.
            var yaEstan = new HashSet<string>(actualizados.Select(x => x.ArticuloId),
                                              StringComparer.OrdinalIgnoreCase);
            actualizados.AddRange(_items.Where(x => x.Eliminado && !yaEstan.Contains(x.ArticuloId)));

            _items = actualizados;

            CargarArbol();
            RefrescarGrid();

            if (!string.IsNullOrEmpty(artSelId))
            {
                var fila = (Grid1.ItemsSource as List<InventarioItemFila>)?.Find(x => x.ArticuloId == artSelId);
                if (fila != null) { Grid1.SelectedItem = fila; Grid1.ScrollIntoView(fila); }
            }
        }

        // ─── Importar Excel: carga cantidades masivamente (misma plantilla que
        // "Crear Plantilla" de InventariosGeneral) ─────────────────────────────
        private void BtnImportarExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Importar cantidades desde Excel",
                Filter = "Excel (*.xlsx)|*.xlsx"
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                Grid1.CommitEdit(DataGridEditingUnit.Row, true);
                Mouse.OverrideCursor = Cursors.Wait;

                var (importados, noEncontrados) = ImportarCantidadesDesdeExcel(dlg.FileName);

                RefrescarGrid();
                _hayCambios = true;

                string mensaje = $"Se importaron {importados} cantidad(es).";
                int reseteados = _items.Count - importados;
                if (reseteados > 0)
                    mensaje += $"\n{reseteados} artículo(s) del catálogo no estaban en el Excel y quedaron en 0.";
                if (noEncontrados.Count > 0)
                    mensaje += $"\n\nNo se encontraron {noEncontrados.Count} código(s) del Excel en el catálogo:\n"
                             + string.Join(", ", noEncontrados.Take(20))
                             + (noEncontrados.Count > 20 ? "…" : "");

                MessageBox.Show(mensaje, "Importar Excel", MessageBoxButton.OK,
                    noEncontrados.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (IOException)
            {
                MessageBox.Show(
                    "No se pudo abrir el archivo: está abierto en Excel u otro programa.\n" +
                    "Cerralo y volvé a intentar.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al importar el Excel:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Busca las columnas "Código" y "Cantidad" por su encabezado (fila 1), sin
        // importar en qué posición estén ni cuántas otras columnas de referencia
        // (Producto/Familia/Descripción, etc.) haya entre medio. Reemplaza la cantidad
        // de TODO el catálogo (_items), no solo de las filas presentes en el Excel: el
        // artículo cuyo código aparece en el Excel con un valor numérico toma ese valor
        // (incluido 0 explícito); cualquier otro artículo del catálogo — código ausente
        // del Excel, o presente pero con la celda de cantidad en blanco — queda en 0.
        private (int Importados, List<string> NoEncontrados) ImportarCantidadesDesdeExcel(string filePath)
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(filePath);
            var ws = wb.Worksheet(1);

            var (colCodigo, colCantidad) = DetectarColumnas(ws);

            // Solo el catálogo vivo: los artículos eliminados no están en la plantilla,
            // y su código puede estar en blanco (fila purgada) o haber sido reasignado
            // a un artículo nuevo — con ToDictionary directo eso reventaba por clave
            // duplicada.
            var porCodigoCatalogo = _items
                .Where(x => !x.Eliminado && !string.IsNullOrEmpty(x.Codigo))
                .GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var cantidadesDelExcel = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var noEncontrados      = new List<string>();

            int ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int fila = 2; fila <= ultimaFila; fila++) // fila 1 = encabezados
            {
                string codigo       = ws.Cell(fila, colCodigo).GetString().Trim();
                bool   tieneCantidad = ws.Cell(fila, colCantidad).TryGetValue(out double cantidad);

                if (string.IsNullOrEmpty(codigo) || !tieneCantidad) continue;

                cantidadesDelExcel[codigo] = cantidad;
                if (!porCodigoCatalogo.ContainsKey(codigo))
                    noEncontrados.Add(codigo);
            }

            int importados = 0;
            foreach (var item in _items)
            {
                // Los eliminados nunca están en la plantilla: quedan en 0, igual que
                // cualquier artículo del catálogo ausente del Excel.
                if (!item.Eliminado && cantidadesDelExcel.TryGetValue(item.Codigo, out double cantidad))
                {
                    item.Cantidad = cantidad;
                    importados++;
                }
                else
                {
                    item.Cantidad = 0;
                }
            }

            return (importados, noEncontrados);
        }

        // Recorre los encabezados de la fila 1 buscando la columna de "Código" y la
        // de "Cantidad", comparando sin tildes/mayúsculas y por coincidencia parcial
        // (p. ej. "Cód. Artículo" o "Cantidad Física" también matchean). No asume
        // ningún orden ni cantidad fija de columnas.
        private static (int Codigo, int Cantidad) DetectarColumnas(ClosedXML.Excel.IXLWorksheet ws)
        {
            int ultimaCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            int colCodigo = 0, colCantidad = 0;

            for (int col = 1; col <= ultimaCol; col++)
            {
                string encabezado = NormalizarEncabezado(ws.Cell(1, col).GetString());

                if (colCodigo == 0 && encabezado.Contains("codigo"))
                    colCodigo = col;
                else if (colCantidad == 0 && encabezado.Contains("cantidad"))
                    colCantidad = col;
            }

            if (colCodigo == 0 || colCantidad == 0)
            {
                var faltantes = new List<string>();
                if (colCodigo == 0) faltantes.Add("\"Código\"");
                if (colCantidad == 0) faltantes.Add("\"Cantidad\"");
                throw new InvalidOperationException(
                    $"No se encontró la columna {string.Join(" ni ", faltantes)} en la fila de encabezados del Excel.");
            }

            return (colCodigo, colCantidad);
        }

        // Quita tildes y pasa a minúsculas/recortado, para que "Código", "CODIGO" o
        // "código " (con espacios) comparen todos igual contra "codigo".
        private static string NormalizarEncabezado(string texto)
        {
            string sinTildes = string.Concat(
                texto.Normalize(NormalizationForm.FormD)
                     .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
            return sinTildes.Trim().ToLowerInvariant();
        }

        // ─── Botones Guardar / Cancelar ───────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            Grid1.CommitEdit(DataGridEditingUnit.Row, true);
            bool ok = Guardar();
            if (ok) { _hayCambios = false; Cerrando?.Invoke(); }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        { _hayCambios = false; Cerrando?.Invoke(); }

        // ─── Guardar ─────────────────────────────────────────────────────────
        private bool Guardar()
        {
            if (_guardando) return false;
            _guardando = true;
            BtnGuardar.IsEnabled = false;
            try
            {
                if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return false;

                return AppState.EventoFormularioI == "editar"
                    ? GuardarEditar()
                    : GuardarNuevo();
            }
            finally
            {
                _guardando = false;
                BtnGuardar.IsEnabled = true;
            }
        }

        private bool GuardarEditar()
        {
            string docId = _idEditar;
            try
            {
                // Combinar fecha y hora
                DateTime fecha = CombinarFechaHora();

                // Actualizar documento
                Sql.DocumentosIObj.EstablecerItem("fecha",       docId, fecha);
                Sql.DocumentosIObj.EstablecerItem("observacion", docId, Box_Observacion.Text);
                Sql.DocumentosIObj.EstablecerItem("referencia",  docId, Box_Referencia.Text.Trim());
                Sql.DocumentosIObj.EstablecerItem("edicion",     docId, DateTime.Now);
                Sql.DocumentosIObj.EstablecerItem("usuarioE",    docId, AppState.UsuarioActivo);

                GuardarLineasInventario(docId);

                Sql.InventariosObj.OrdenarData(("documentoI", false));
                Sql.DocumentosIObj.OrdenarData(("fecha", false));

                int periodo = string.IsNullOrEmpty(AppState.PeriodoActivo)
                    ? DateTime.Now.Year
                    : int.Parse(AppState.PeriodoActivo);
                AppState.ActualizarBase(periodo);
                AppLoader.ConectarDocumentos(AppState.DataFechaInicio, AppState.DataFechaFinal);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Revisión del código al guardar ───────────────────────────────────
        // El código se calculó al abrir el formulario: entre ese momento y el
        // guardado, otro usuario (u otra sucursal de la misma empresa) pudo haberse
        // quedado con el número. Si ya está tomado se avisa y se usa el siguiente
        // libre, en vez de guardar un código duplicado.
        private void VerificarCodigo()
        {
            string nuevo = CodigoDocumento.VerificarInventario(_codigoDocI, AppState.EmpresaActiva);
            if (nuevo != _codigoDocI)
            {
                MessageBox.Show(
                    $"El código {_codigoDocI} ya estaba en uso. El documento se guardará " +
                    $"con el código {nuevo}.",
                    "Consola", MessageBoxButton.OK, MessageBoxImage.Information);
                _codigoDocI = nuevo;
                Box_DocumentoI.Text = _codigoDocI;
            }
        }

        private bool GuardarNuevo()
        {
            try
            {
                string docId = Guid.NewGuid().ToString();
                DateTime fecha = CombinarFechaHora();

                VerificarCodigo();

                Sql.DocumentosIObj.Nuevo(docId);
                Sql.DocumentosIObj.EstablecerItem("codigo",      docId, _codigoDocI);
                Sql.DocumentosIObj.EstablecerItem("fecha",       docId, fecha);
                Sql.DocumentosIObj.EstablecerItem("observacion", docId, Box_Observacion.Text);
                Sql.DocumentosIObj.EstablecerItem("referencia",  docId, Box_Referencia.Text.Trim());
                Sql.DocumentosIObj.EstablecerItem("sucursal",    docId, AppState.SucursalActiva);
                Sql.DocumentosIObj.EstablecerItem("emision",     docId, DateTime.Now);
                Sql.DocumentosIObj.EstablecerItem("edicion",     docId, DateTime.Now);
                Sql.DocumentosIObj.EstablecerItem("usuario",     docId, AppState.UsuarioActivo);
                Sql.DocumentosIObj.EstablecerItem("usuarioE",    docId, AppState.UsuarioActivo);

                GuardarLineasInventario(docId);

                Sql.InventariosObj.OrdenarData(("documentoI", false));
                Sql.DocumentosIObj.OrdenarData(("fecha", false));

                int periodo = string.IsNullOrEmpty(AppState.PeriodoActivo)
                    ? DateTime.Now.Year
                    : int.Parse(AppState.PeriodoActivo);
                AppState.ActualizarBase(periodo);
                AppLoader.ConectarDocumentos(AppState.DataFechaInicio, AppState.DataFechaFinal);

                ItemCreadoId = docId;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Consola", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ─── Persistencia de líneas ────────────────────────────────────────────
        // Solo se registra en SQL un artículo SIN registro previo si su cantidad editada
        // es mayor a cero (nuevo inventario o ítem nuevo al editar). Un artículo CON
        // registro previo se actualiza siempre, incluso si su cantidad se editó a cero
        // (permanece "normal"). Nunca se elimina/oculta un registro aquí.
        private void GuardarLineasInventario(string docId)
        {
            foreach (var item in _items)
            {
                bool tieneRegistro = !string.IsNullOrEmpty(item.InventarioId);
                if (!tieneRegistro && item.Cantidad <= 0) continue;

                string id;
                if (!tieneRegistro)
                {
                    id = Guid.NewGuid().ToString();
                    Sql.InventariosObj.Nuevo(id);
                    Sql.InventariosObj.EstablecerItem("documentoI", id, docId);
                    item.InventarioId = id;
                }
                else id = item.InventarioId;

                Sql.InventariosObj.EstablecerItem("articulo", id, item.ArticuloId);
                Sql.InventariosObj.EstablecerItem("cantidad", id, item.Cantidad);
            }
        }

        // ─── Helper: combinar fecha del DatePicker y hora del TextBox ─────────
        private DateTime CombinarFechaHora()
        {
            DateTime fecha = Box_Fecha.SelectedDate ?? DateTime.Today;

            if (TimeSpan.TryParse(Box_Hora.Text, out TimeSpan hora))
                return fecha.Date + hora;

            return fecha.Date + DateTime.Now.TimeOfDay;
        }
    }

    // ─── Modelo de ítem ───────────────────────────────────────────────────────
    public class InventarioItemFila
    {
        public string InventarioId { get; set; } = ""; // vacío = sin registro en SQL
        public string ArticuloId   { get; set; } = "";
        public string Codigo       { get; set; } = "";
        public string Categoria    { get; set; } = "";
        public string Descripcion  { get; set; } = "";
        public double Cantidad     { get; set; }
        // true = el artículo ya no está en el catálogo (se ocultó o eliminó) pero la
        // línea sigue registrada en `inventarios`. Se listan bajo el nodo
        // "Eliminados" y la grilla los pinta en rojo.
        public bool   Eliminado    { get; set; }
    }
}
