using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class ArticulosGeneral : System.Windows.Controls.UserControl
    {
        private static SqlData Sql => SqlData.Instance;

        public event Action? Cerrando;
        public void IntentarCerrar() => Cerrando?.Invoke();

        // Modo exportar: cuando se abre desde Traspasos/Inventarios/Pedidos (multi).
        // Devuelve true para cerrar el diálogo (aceptado) o false para mantenerlo
        // abierto (ej. el llamador detectó duplicados y bloqueó la exportación).
        private readonly Func<List<ArticuloExportado>, bool>? _callbackExportar;
        // Modo single: buscar un solo artículo con doble clic (sin checkbox ni #)
        private readonly Func<ArticuloExportado, bool>? _callbackSingle;
        // List en lugar de HashSet para conservar el orden de selección
        private readonly List<string> _seleccionados = new();

        // Modo de filtro activo: "todos" (carga inicial) | "busqueda" (TxtBuscar) | "familia" (Tree1)
        private string _modoFiltro = "todos";

        public bool ModoExportar => _callbackExportar != null;
        public bool ModoSingle   => _callbackSingle   != null;

        private bool _iniciado = false;

        // Cuando es true, los cambios de selección del árbol NO recargan la grilla
        // (se usa durante el refresco incremental de "Actualizar" para no recargar todo).
        private bool _suspenderEventosArbol = false;

        /// <summary>Constructor sin parámetros requerido por el compilador XAML.</summary>
        public ArticulosGeneral() : this(null, null) { }

        public ArticulosGeneral(Func<List<ArticuloExportado>, bool>? callbackExportar = null,
                                 Func<ArticuloExportado, bool>?      callbackSingle   = null)
        {
            InitializeComponent();
            _callbackExportar = callbackExportar;
            _callbackSingle   = callbackSingle;
            Loaded += (_, _) => { if (_iniciado) return; _iniciado = true; CargarArbol(); CargarArticulos(); ConfigurarModo(); };
        }

        /// <summary>Abre ArticulosGeneral como pestaña dentro de ConsolaMovimientos.</summary>
        public static void OpenAsTab(Window owner,
                                     Func<List<ArticuloExportado>, bool>? callbackExportar = null,
                                     Func<ArticuloExportado, bool>?    callbackSingle   = null,
                                     string                            contexto         = "",
                                     UIElement?                        llamador         = null)
        {
            var consola = owner as ConsolaMovimientos;
            if (consola == null) return;

            bool esMulti = callbackExportar != null;
            string titulo = esMulti
                ? (string.IsNullOrEmpty(contexto) ? "Importar Artículos" : $"Importar Artículos ({contexto})")
                : (string.IsNullOrEmpty(contexto) ? "Buscar Artículo"    : $"Buscar Artículo ({contexto})");
            string clave = esMulti
                ? $"importar-articulos|{contexto}"
                : $"buscar-articulo|{contexto}";
            // La otra variante (Importar ↔ Buscar) para el mismo contexto: se cierra
            // también, para que como mucho quede una sola de las dos pestañas abierta.
            string claveOtra = esMulti
                ? $"buscar-articulo|{contexto}"
                : $"importar-articulos|{contexto}";

            var ctrl = new ArticulosGeneral(callbackExportar, callbackSingle);
            ctrl.Cerrando += () => { consola.CerrarPestaña(ctrl); consola.SeleccionarPestaña(llamador); };

            consola.CerrarPestañaPorClave(clave);
            consola.CerrarPestañaPorClave(claveOtra);
            consola.AbrirPestaña(titulo, ctrl, clave);
        }

        /// <summary>Abre ArticulosGeneral como diálogo modal dentro de una ventana temporal.
        /// Usado por formularios que aún se muestran como ventana (Traspasos, Correcciones, Inventarios).</summary>
        public static void OpenAsDialog(Window owner,
                                        Func<List<ArticuloExportado>, bool>? callbackExportar = null,
                                        Func<ArticuloExportado, bool>?    callbackSingle   = null)
        {
            var ctrl = new ArticulosGeneral(callbackExportar, callbackSingle);
            var win  = new Window
            {
                Content               = ctrl,
                Title                 = "Artículos",
                Width                 = 900,
                Height                = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner                 = owner,
                ShowInTaskbar         = false,
                Background            = System.Windows.Media.Brushes.WhiteSmoke,
                ResizeMode            = ResizeMode.CanResize
            };
            WindowHelper.AjustarAlEcran(win);
            win.ShowDialog();
        }

        // ─── Configurar modo exportar ─────────────────────────────────────────
        private void ConfigurarModo()
        {
            bool esDialog = ModoExportar || ModoSingle;

            PanelExportar.Visibility   = ModoExportar ? Visibility.Visible   : Visibility.Collapsed;
            BtnInformeExcel.Visibility = esDialog     ? Visibility.Collapsed : Visibility.Visible;
            BtnInformePdf.Visibility   = esDialog     ? Visibility.Collapsed : Visibility.Visible;
            BtnArqueoExcel.Visibility  = esDialog     ? Visibility.Collapsed : Visibility.Visible;
            BtnNuevo.Visibility        = esDialog     ? Visibility.Collapsed : Visibility.Visible;
            BtnInsertar.Visibility     = esDialog     ? Visibility.Collapsed : Visibility.Visible;
            BtnEditar.Visibility       = esDialog     ? Visibility.Collapsed : Visibility.Visible;
            BtnEliminar.Visibility     = (esDialog || !AppState.EsAdmin) ? Visibility.Collapsed : Visibility.Visible;

            // Columna checkbox (✓) y columna "#" solo visibles en modo importar
            var visibilidad = ModoExportar ? Visibility.Visible : Visibility.Collapsed;
            Grid1.Columns[0].Visibility = visibilidad;   // ✓ checkbox
            Grid1.Columns[1].Visibility = visibilidad;   // #  orden selección
        }

        // ─── Árbol de productos/familias ──────────────────────────────────────
        private void CargarArbol()
        {
            Tree1.Items.Clear();
            foreach (var d in ConstruirArbolDeseado())
                Tree1.Items.Add(CrearNodo(d, expandidoPorDefecto: true));

            // Seleccionar primer nodo (raíz "Todos")
            if (Tree1.Items.Count > 0 && Tree1.Items[0] is TreeViewItem raiz)
                raiz.IsSelected = true;
        }

        // Estructura "deseada" del árbol según la caché actual (fuente única para la
        // carga inicial y el refresco incremental).
        private List<NodoDeseado> ConstruirArbolDeseado()
        {
            var nodoTodos = new NodoDeseado { Tag = "todos", Header = "Todos" };
            // Ids de productos vivos: una familia puede apuntar a un producto ya
            // borrado, y en ese caso también cuenta como "sin producto".
            var productosVivos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int ufProd = Sql.ProductosObj.ContarFilas;
            for (int i = 1; i <= ufProd; i++)
            {
                var idObj = Sql.ProductosObj.Mover(i);
                if (idObj == null) continue;
                string prodId   = idObj.ToString()!;
                productosVivos.Add(prodId);
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion", prodId)?.ToString() ?? prodId;

                var nodoProd = new NodoDeseado { Tag = $"producto:{prodId}", Header = prodDesc };

                int ufFam = Sql.FamiliasObj.ContarFilas;
                for (int j = 1; j <= ufFam; j++)
                {
                    var famIdObj = Sql.FamiliasObj.Mover(j);
                    if (famIdObj == null) continue;
                    string famId = famIdObj.ToString()!;
                    if ((Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "") != prodId) continue;

                    string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? famId;
                    nodoProd.Hijos.Add(new NodoDeseado { Tag = $"familia:{famId}", Header = famDesc });
                }

                nodoTodos.Hijos.Add(nodoProd);
            }

            // ── Nodo de lo no clasificado ────────────────────────────────────
            // "Sin Producto" agrupa las familias que no tienen producto (o cuyo
            // producto ya no existe), que de otro modo no aparecerían bajo ningún
            // nodo del árbol. Adentro cuelga "Sin Familia" con los artículos que no
            // tienen familia asignada. Se muestra siempre, igual que los productos
            // sin familias, para que el punto de entrada exista aunque hoy esté vacío.
            var nodoSinProducto = new NodoDeseado { Tag = "sin-producto", Header = "Sin Producto" };
            nodoSinProducto.Hijos.Add(new NodoDeseado { Tag = "sin-familia", Header = "Sin Familia" });

            int ufFamSin = Sql.FamiliasObj.ContarFilas;
            for (int j = 1; j <= ufFamSin; j++)
            {
                var famIdObj = Sql.FamiliasObj.Mover(j);
                if (famIdObj == null) continue;
                string famId = famIdObj.ToString()!;

                string prodDeFam = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";
                if (!string.IsNullOrEmpty(prodDeFam) && productosVivos.Contains(prodDeFam)) continue;

                string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? famId;
                nodoSinProducto.Hijos.Add(new NodoDeseado { Tag = $"familia:{famId}", Header = famDesc });
            }

            nodoTodos.Hijos.Add(nodoSinProducto);

            return new List<NodoDeseado> { nodoTodos };
        }

        private static TreeViewItem CrearNodo(NodoDeseado d, bool expandidoPorDefecto)
        {
            var tvi = new TreeViewItem { Header = d.Header, Tag = d.Tag };
            foreach (var h in d.Hijos) tvi.Items.Add(CrearNodo(h, expandidoPorDefecto));
            if (d.Hijos.Count > 0) tvi.IsExpanded = expandidoPorDefecto;
            return tvi;
        }

        // Reconcilia el árbol existente con la estructura deseada: actualiza encabezados,
        // agrega nodos nuevos y quita los eliminados, preservando la expansión y selección
        // de los nodos que se conservan (no reconstruye todo).
        private void RefrescarArbolIncremental() => ReconciliarNodos(Tree1.Items, ConstruirArbolDeseado());

        private void ReconciliarNodos(ItemCollection existentes, List<NodoDeseado> deseados)
        {
            var porTag = new Dictionary<string, TreeViewItem>();
            foreach (var obj in existentes)
                if (obj is TreeViewItem t && t.Tag is string tag) porTag[tag] = t;

            var nuevos = new List<TreeViewItem>(deseados.Count);
            foreach (var d in deseados)
            {
                if (porTag.TryGetValue(d.Tag, out var ex))
                {
                    if (!Equals(ex.Header, d.Header)) ex.Header = d.Header;
                    ReconciliarNodos(ex.Items, d.Hijos);   // recurse (preserva IsExpanded)
                    nuevos.Add(ex);
                }
                else nuevos.Add(CrearNodo(d, expandidoPorDefecto: true));
            }

            // Solo tocar la colección si cambió la membresía o el orden (evita perder
            // la selección/expansión cuando no hubo cambios reales).
            bool igual = existentes.Count == nuevos.Count;
            if (igual)
                for (int i = 0; i < nuevos.Count; i++)
                    if (!ReferenceEquals(existentes[i], nuevos[i])) { igual = false; break; }

            if (!igual)
            {
                existentes.Clear();
                foreach (var n in nuevos) existentes.Add(n);
            }
        }

        private void RestaurarSeleccionArbol(string tag)
        {
            var nodo = BuscarNodoPorTag(Tree1.Items, tag) ?? BuscarNodoPorTag(Tree1.Items, "todos");
            if (nodo != null && !nodo.IsSelected) nodo.IsSelected = true;
        }

        private static TreeViewItem? BuscarNodoPorTag(ItemCollection items, string tag)
        {
            foreach (var obj in items)
            {
                if (obj is not TreeViewItem t) continue;
                if (t.Tag is string tg && tg == tag) return t;
                var hijo = BuscarNodoPorTag(t.Items, tag);
                if (hijo != null) return hijo;
            }
            return null;
        }

        // ─── Carga la lista de artículos ──────────────────────────────────────
        public void CargarArticulos()
        {
            Grid1.ItemsSource = ConstruirListaArticulos();
            RenumerarYActualizarTotales();
        }

        // Construye la lista de artículos según el filtro activo (sin tocar la UI).
        private List<ArticuloFila> ConstruirListaArticulos()
        {
            var lista = new List<ArticuloFila>();
            int linea = 1;

            // Filtros excluyentes: solo se aplica el del modo activo
            string busqueda  = _modoFiltro == "busqueda" ? TxtBuscar.Text.Trim().ToLower() : "";
            string tagFiltro = _modoFiltro == "familia"  ? ObtenerTagFiltro()              : "";

            // Solo los filtros de "no clasificado" necesitan saber qué familias y
            // productos siguen existiendo (un artículo puede apuntar a una familia
            // borrada, y una familia a un producto borrado). Se resuelve una vez acá
            // en vez de por artículo, y solo cuando hace falta.
            HashSet<string>? familiasVivas = null, productosVivos = null;
            if (tagFiltro is "sin-producto" or "sin-familia")
            {
                familiasVivas  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                productosVivos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                int ufFamV = Sql.FamiliasObj.ContarFilas;
                for (int j = 1; j <= ufFamV; j++)
                    if (Sql.FamiliasObj.Mover(j)?.ToString() is string fid) familiasVivas.Add(fid);

                int ufProdV = Sql.ProductosObj.ContarFilas;
                for (int j = 1; j <= ufProdV; j++)
                    if (Sql.ProductosObj.Mover(j)?.ToString() is string pid) productosVivos.Add(pid);
            }

            int uf = Sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string famId = Sql.ArticulosObj.ObtenerItem("familia", id)?.ToString() ?? "";

                // Filtro de árbol
                if (!string.IsNullOrEmpty(tagFiltro))
                {
                    if (tagFiltro.StartsWith("familia:"))
                    {
                        string famFiltro = tagFiltro.Substring("familia:".Length);
                        if (famId != famFiltro) continue;
                    }
                    else if (tagFiltro.StartsWith("producto:"))
                    {
                        string prodFiltro = tagFiltro.Substring("producto:".Length);
                        string famProd    = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";
                        if (famProd != prodFiltro) continue;
                    }
                    else if (tagFiltro == "sin-familia")
                    {
                        // Sin familia asignada, o apuntando a una familia borrada.
                        if (!string.IsNullOrEmpty(famId) && familiasVivas!.Contains(famId)) continue;
                    }
                    else if (tagFiltro == "sin-producto")
                    {
                        // Todo lo que cuelga del nodo: los artículos sin familia (el
                        // nodo hijo "Sin Familia") más los de familias que no tienen
                        // un producto vivo.
                        bool sinFamilia = string.IsNullOrEmpty(famId) || !familiasVivas!.Contains(famId);
                        if (!sinFamilia)
                        {
                            string famProd = Sql.FamiliasObj.ObtenerItem("producto", famId)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(famProd) && productosVivos!.Contains(famProd)) continue;
                        }
                    }
                    // "todos" o vacío → sin filtro
                }

                string codigo    = Sql.ArticulosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
                string desc      = Sql.ArticulosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                string modelo    = Sql.ArticulosObj.ObtenerItem("modelo",      id)?.ToString() ?? "";
                string famDesc   = Sql.FamiliasObj.ObtenerItem("descripcion",  famId)?.ToString() ?? "";
                string catDesc   = ObtenerDescripcionCategoria(id);
                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

                // Filtro de búsqueda (código, descripción completa o categoría)
                if (!string.IsNullOrEmpty(busqueda))
                {
                    if (!codigo.ToLower().Contains(busqueda) &&
                        !descCompleta.ToLower().Contains(busqueda) &&
                        !catDesc.ToLower().Contains(busqueda))
                        continue;
                }

                double stock  = StockCalculator.ContarStock(id,  DateTime.Now);
                double stock2 = StockCalculator.ContarStock2(id, DateTime.Now);

                int ordenIdx = _seleccionados.IndexOf(id);   // -1 si no está

                lista.Add(new ArticuloFila
                {
                    Linea          = linea++,
                    Id             = id,
                    Codigo         = codigo,
                    Categoria      = catDesc,
                    Descripcion    = descCompleta,
                    Disponible     = stock2,
                    Stock          = stock,
                    Seleccionado   = ordenIdx >= 0,
                    OrdenSeleccion = ordenIdx >= 0 ? ordenIdx + 1 : 0
                });
            }

            return lista;
        }

        // ─── Obtener filtro del árbol ─────────────────────────────────────────
        private string ObtenerTagFiltro()
        {
            if (Tree1.SelectedItem is TreeViewItem item)
                return item.Tag?.ToString() ?? "todos";
            return "todos";
        }

        // ─── Helpers de actualización incremental del Grid1 ───────────────────
        private List<ArticuloFila> FilasGrid =>
            Grid1.ItemsSource as List<ArticuloFila> ?? new List<ArticuloFila>();

        // Refresca la grilla con la caché actual sin recrear las filas: actualiza en
        // sitio las que ya existen (por Id), agrega las nuevas, quita las eliminadas y
        // reordena según el filtro activo. Conserva las instancias (y por tanto la
        // selección por referencia) de las filas que se mantienen.
        private void RefrescarGridIncremental()
        {
            var deseadas = ConstruirListaArticulos();

            var actuales = FilasGrid;
            var porId = new Dictionary<string, ArticuloFila>();
            foreach (var f in actuales) porId[f.Id] = f;

            var resultado = new List<ArticuloFila>(deseadas.Count);
            foreach (var d in deseadas)
            {
                if (porId.TryGetValue(d.Id, out var ex))
                {
                    ex.Codigo         = d.Codigo;
                    ex.Categoria      = d.Categoria;
                    ex.Descripcion    = d.Descripcion;
                    ex.Disponible     = d.Disponible;
                    ex.Stock          = d.Stock;
                    ex.Seleccionado   = d.Seleccionado;
                    ex.OrdenSeleccion = d.OrdenSeleccion;
                    resultado.Add(ex);
                }
                else resultado.Add(d);
            }

            actuales.Clear();
            actuales.AddRange(resultado);
            RenumerarYActualizarTotales();   // renumera Línea + totales + Items.Refresh()
        }

        // Actualiza solo las banderas Seleccionado/OrdenSeleccion de las filas ya
        // existentes en memoria, sin recalcular stock ni volver a consultar SQL.
        // Marcar/desmarcar no cambia el stock de ningún artículo, así que no hace
        // falta pagar el costo de ConstruirListaArticulos() (que recalcula
        // StockCalculator.ContarStock/ContarStock2 de TODOS los artículos filtrados)
        // en cada clic — antes eso hacía notoriamente lento tildar artículos uno
        // por uno al importar a un pedido.
        private void ActualizarSeleccionEnGrid()
        {
            foreach (var f in FilasGrid)
            {
                int ordenIdx = _seleccionados.IndexOf(f.Id);
                f.Seleccionado   = ordenIdx >= 0;
                f.OrdenSeleccion = ordenIdx >= 0 ? ordenIdx + 1 : 0;
            }
            RenumerarYActualizarTotales();
        }

        private void RestaurarSeleccionGrid(string artId)
        {
            if (string.IsNullOrEmpty(artId)) return;
            var item = FilasGrid.Find(x => x.Id == artId);
            if (item != null) EnfocarFila(item);
        }

        // Construye una sola fila para un artículo (incluye cálculo de stock).
        private ArticuloFila ConstruirFila(string id, int linea)
        {
            string famId  = Sql.ArticulosObj.ObtenerItem("familia",     id)?.ToString() ?? "";
            string codigo = Sql.ArticulosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
            string desc   = Sql.ArticulosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
            string modelo = Sql.ArticulosObj.ObtenerItem("modelo",      id)?.ToString() ?? "";
            string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? "";
            string catDesc = ObtenerDescripcionCategoria(id);
            string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);

            double stock  = StockCalculator.ContarStock(id,  DateTime.Now);
            double stock2 = StockCalculator.ContarStock2(id, DateTime.Now);
            int ordenIdx  = _seleccionados.IndexOf(id);

            return new ArticuloFila
            {
                Linea          = linea,
                Id             = id,
                Codigo         = codigo,
                Categoria      = catDesc,
                Descripcion    = descCompleta,
                Disponible     = stock2,
                Stock          = stock,
                Seleccionado   = ordenIdx >= 0,
                OrdenSeleccion = ordenIdx >= 0 ? ordenIdx + 1 : 0
            };
        }

        // Descripción de la categoría de un artículo (columna 'Categoria' del artículo).
        private string ObtenerDescripcionCategoria(string id)
        {
            string catId = Sql.ArticulosObj.ObtenerItem("Categoria", id)?.ToString() ?? "";
            if (string.IsNullOrEmpty(catId)) return "";
            return Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "";
        }

        // Renumera la columna Línea, recalcula totales desde el grid y refresca.
        private void RenumerarYActualizarTotales()
        {
            var lista = FilasGrid;
            int n = 1;
            double totalDisp = 0, totalStock = 0;
            foreach (var f in lista)
            {
                f.Linea     = n++;
                totalDisp  += f.Disponible;
                totalStock += f.Stock;
            }
            TxtTotalArticulos.Text  = lista.Count.ToString("N0");
            TxtTotalDisponible.Text = totalDisp.ToString("N0");
            TxtTotalStock.Text      = totalStock.ToString("N0");
            LblSubtitulo.Text       = $"{lista.Count:N0} artículos";
            if (ModoExportar)
                LblSeleccionados.Text = $"Seleccionados: {_seleccionados.Count}";
            Grid1.Items.Refresh();
        }

        private void EnfocarFila(ArticuloFila item)
        {
            Grid1.SelectedItem = item;
            Grid1.ScrollIntoView(item);
            GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
        }

        // ─── Eventos árbol y búsqueda ─────────────────────────────────────────
        private void Tree1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suspenderEventosArbol) return;   // refresco incremental en curso
            _modoFiltro = "familia";
            CargarArticulos();
        }

        private void Tree1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Si el usuario hace clic en el item ya seleccionado, SelectedItemChanged no se dispara.
            // Detectamos ese caso y recargamos manualmente.
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not TreeViewItem)
                source = VisualTreeHelper.GetParent(source);

            if (source is TreeViewItem tvi && tvi.IsSelected)
            {
                _modoFiltro = "familia";
                CargarArticulos();
            }
        }

        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _modoFiltro = "busqueda";
            CargarArticulos();
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            _modoFiltro = "busqueda";
            CargarArticulos();
        }

        // ─── Doble clic ───────────────────────────────────────────────────────
        private void Grid1_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;   // evita que eventos de ratón pendientes quiten el foco al cerrar el diálogo
            EjecutarAccionFila();
        }

        // ─── Tecla Enter ──────────────────────────────────────────────────────
        private void Grid1_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            EjecutarAccionFila();
        }

        private void EjecutarAccionFila()
        {
            if (ModoSingle)
            {
                if (Grid1.SelectedItem is not ArticuloFila fila) return;
                bool aceptado = _callbackSingle!(new ArticuloExportado
                {
                    Id          = fila.Id,
                    Codigo      = fila.Codigo,
                    Descripcion = fila.Descripcion
                });
                if (aceptado) CerrarDialogo();
            }
            else if (ModoExportar)
                ToggleSeleccion();
            else
                AbrirEditar();
        }

        // Cierra la pestaña o la ventana temporal según el contexto de apertura
        private void CerrarDialogo()
        {
            var w = Window.GetWindow(this);
            if (w is ConsolaMovimientos)
                Cerrando?.Invoke();
            else
                w?.Close();
        }

        // ─── Botones ──────────────────────────────────────────────────────────
        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            AppState.EventoFormularioA = "nuevo";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = "Nuevo Artículo";
            var dlg = new ArticulosDetalle(this, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.ItemCreadoId == null) return;   // cancelado
                var lista = FilasGrid;
                var nueva = ConstruirFila(dlg.ItemCreadoId, 0);
                // Al FINAL de la lista: la posición del artículo nuevo depende de la
                // familia/índice que cargó el usuario, no de qué fila estaba
                // seleccionada al abrir el formulario, así que insertarlo encima de
                // esa fila era arbitrario. Queda seleccionado y enfocado.
                lista.Add(nueva);
                RenumerarYActualizarTotales();
                EnfocarFila(nueva);
            };
            consola.AbrirPestaña(titulo, dlg, "nuevo-articulo");
        }

        private void BtnInsertar_Click(object sender, RoutedEventArgs e)
        {
            if (Grid1.SelectedItem is not ArticuloFila fila) return;
            AppState.EventoFormularioA = "insertar";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = "Insertar Artículo";
            var dlg = new ArticulosDetalle(this, fila.Id, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                if (dlg.ItemCreadoId == null) return;   // cancelado
                var lista = FilasGrid;
                var nueva = ConstruirFila(dlg.ItemCreadoId, 0);
                // Al FINAL de la lista (a pedido), igual que "Nuevo": el artículo
                // recién creado queda siempre a la vista, seleccionado y enfocado.
                // Nota: en SQL este artículo sí ocupa el índice del de referencia
                // (GuardarInsertar corre el resto de la familia y la renumera), así
                // que hasta el próximo "Actualizar" la fila se ve al final en vez de
                // en su posición por familia+índice.
                lista.Add(nueva);
                RenumerarYActualizarTotales();
                EnfocarFila(nueva);
            };
            consola.AbrirPestaña(titulo, dlg, "insertar-articulo");
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
            => AbrirEditar();

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (Grid1.SelectedItem is not ArticuloFila fila) return;

            // Verificación de conexión en 2 capas antes de persistir el borrado.
            if (!FuncionesComunes.VerificarConexionParaGuardar(Window.GetWindow(this))) return;

            var res = MessageBox.Show("¿Eliminar este artículo?", "Consola",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            // Ocultar el artículo: su índice queda RESERVADO y NO se reutiliza, por lo
            // que NO se corren los índices de los demás artículos de la familia.
            Sql.ArticulosObj.EstablecerItem("edicion",  fila.Id, DateTime.Now);
            Sql.ArticulosObj.EstablecerItem("usuarioE", fila.Id, AppState.UsuarioActivo);
            Sql.ArticulosObj.Ocultar(fila.Id);

            Sql.ArticulosObj.OrdenarData(("familia", false), ("indice", false));

            // Quitar solo la fila eliminada del grid (sin recargar todo)
            _seleccionados.Remove(fila.Id);
            var lista = FilasGrid;
            int idx   = lista.IndexOf(fila);
            if (idx >= 0) lista.RemoveAt(idx);
            RenumerarYActualizarTotales();

            if (lista.Count > 0)
                EnfocarFila(lista[Math.Min(idx, lista.Count - 1)]);

            // Artículo eliminado → sincronizar AppSheets (todas las sucursales).
            SincronizarAppsheetsTrasCambio();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            // Sin conexión no se puede refrescar desde SQL: avisar y no congelar.
            if (!FuncionesComunes.VerificarConexionParaActualizar(Window.GetWindow(this))) return;

            // Recordar el enfoque actual para restaurarlo tras el refresco.
            string tagArbolSel = (Tree1.SelectedItem as TreeViewItem)?.Tag?.ToString() ?? "todos";
            string artSelId    = (Grid1.SelectedItem as ArticuloFila)?.Id ?? "";

            // Recargar la caché desde SQL (única fuente de datos).
            AppState.ActualizarProductos();

            // Árbol: refresco incremental (alta/baja/edición de nodos) preservando
            // expansión y selección; sin disparar la recarga de la grilla por el evento.
            _suspenderEventosArbol = true;
            RefrescarArbolIncremental();
            RestaurarSeleccionArbol(tagArbolSel);
            _suspenderEventosArbol = false;

            // Grilla: refresco incremental preservando selección y foco.
            RefrescarGridIncremental();
            RestaurarSeleccionGrid(artSelId);
        }

        /// <summary>
        /// Renumera los artículos ACTIVOS de una familia por su orden de índice actual,
        /// saltando los índices ya ocupados por artículos eliminados/ocultos de esa familia
        /// (que NO se reutilizan). No persiste: el llamador hace OrdenarData/ExportarItems.
        /// </summary>
        public static void RenumerarFamilia(string famId)
        {
            if (string.IsNullOrEmpty(famId)) return;
            var sql = SqlData.Instance;
            var reservados = sql.ArticulosObj.IndicesNoNormales("familia", famId);

            var activos = new List<(string id, int ind)>();
            int uf = sql.ArticulosObj.ContarFilas;
            for (int i = 1; i <= uf; i++)
            {
                var idObj = sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                if (sql.ArticulosObj.ObtenerItem("familia", id)?.ToString() != famId) continue;
                int ind = Convert.ToInt32(sql.ArticulosObj.ObtenerItem("indice", id) ?? 0);
                activos.Add((id, ind));
            }
            activos.Sort((a, b) => a.ind.CompareTo(b.ind));

            int next = 1;
            foreach (var (id, _) in activos)
            {
                while (reservados.Contains(next)) next++;
                sql.ArticulosObj.EstablecerItem("indice", id, next);
                next++;
            }
        }

        /// <summary>
        /// Re-sincroniza la tabla appsheets (todas las sucursales de la empresa activa)
        /// tras agregar o eliminar un artículo. Un fallo aquí NO revierte el cambio del
        /// artículo (ya persistido): solo se informa con una advertencia.
        /// </summary>
        public static void SincronizarAppsheetsTrasCambio()
        {
            try
            {
                AppsheetsSync.SincronizarTodasLasSucursales();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"El artículo se guardó, pero falló la sincronización de AppSheets:\n{ex.Message}",
                    "AppSheets", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnInformeExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InformeExcelArticulos
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
        }

        private void BtnInformePdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InformePdfArticulos
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
        }

        // ─── Arqueo Excel: catálogo completo listo para conteo físico ─────────
        private void BtnArqueoExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ArqueoExcelArticulos
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (_callbackExportar == null) return;

            var exportados = new List<ArticuloExportado>();
            foreach (string id in _seleccionados)
            {
                string codigo = Sql.ArticulosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
                string desc   = Sql.ArticulosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                string modelo = Sql.ArticulosObj.ObtenerItem("modelo",      id)?.ToString() ?? "";
                string famId  = Sql.ArticulosObj.ObtenerItem("familia",     id)?.ToString() ?? "";
                string famDesc = Sql.FamiliasObj.ObtenerItem("descripcion", famId)?.ToString() ?? "";
                exportados.Add(new ArticuloExportado
                {
                    Id          = id,
                    Codigo      = codigo,
                    Descripcion = FuncionesComunes.UnirVariables(desc, famDesc, modelo)
                });
            }

            bool aceptado = _callbackExportar(exportados);
            if (aceptado) CerrarDialogo();
        }

        // ─── Desmarcar todos los artículos seleccionados ──────────────────────
        private void BtnDeseleccionarTodos_Click(object sender, RoutedEventArgs e)
        {
            if (_seleccionados.Count == 0) return;
            _seleccionados.Clear();
            ActualizarSeleccionEnGrid();
            GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────
        private void AbrirEditar()
        {
            if (Grid1.SelectedItem is not ArticuloFila fila) return;
            string idSel = fila.Id;
            int    linea = fila.Linea;
            AppState.EventoFormularioA = "modificar";
            var consola = Window.GetWindow(this) as ConsolaMovimientos;
            if (consola == null) return;
            string titulo = $"Artículo {fila.Codigo}";
            var dlg = new ArticulosDetalle(this, idSel, tituloTab: titulo);
            dlg.Cerrando += () =>
            {
                consola.CerrarPestaña(dlg);
                // Reconstruir solo esta fila en su lugar (el Id interno no cambia)
                var lista = FilasGrid;
                int idx   = lista.IndexOf(fila);
                if (idx >= 0)
                {
                    var actualizada = ConstruirFila(idSel, linea);
                    lista[idx] = actualizada;
                    RenumerarYActualizarTotales();
                    EnfocarFila(actualizada);
                }
            };
            consola.AbrirPestaña(titulo, dlg, $"articulo-{idSel}");
        }

        // Un solo click en el checkbox marca/desmarca la fila (antes hacía
        // falta doble clic o Enter, vía EjecutarAccionFila).
        private void ChkSeleccionado_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is CheckBox chk && chk.DataContext is ArticuloFila fila)
                Grid1.SelectedItem = fila;
            ToggleSeleccion();
        }

        private void ToggleSeleccion()
        {
            if (Grid1.SelectedItem is not ArticuloFila fila) return;

            string idActual = fila.Id;   // guardar antes de recargar

            if (_seleccionados.Contains(idActual))
                _seleccionados.Remove(idActual);
            else
                _seleccionados.Add(idActual);

            // Solo actualiza las banderas de selección en memoria (sin recalcular
            // stock ni volver a SQL) — conserva las instancias de fila (y sus
            // contenedores visuales reciclados) en vez de reemplazar todo el
            // ItemsSource. Reemplazar la lista entera (CargarArticulos) hacía que la
            // virtualización reciclara contenedores entre instancias distintas y se
            // viera un "parpadeo" de colores de fondo al seleccionar/marcar, sobre
            // todo en modo oscuro.
            ActualizarSeleccionEnGrid();

            // Restaurar selección y foco al mismo ítem
            var item = FilasGrid.Find(x => x.Id == idActual);
            if (item != null) { Grid1.SelectedItem = item; Grid1.ScrollIntoView(item); }
            GridFocusHelper.EnfocarCeldaSeleccionada(Grid1);
        }

        // Nodo "deseado" del árbol (estructura ligera para la reconciliación incremental).
        private sealed class NodoDeseado
        {
            public string Tag    = "";
            public string Header = "";
            public List<NodoDeseado> Hijos = new();
        }
    }

    // ─── Modelos ──────────────────────────────────────────────────────────────
    public class ArticuloFila
    {
        public int    Linea          { get; set; }
        // Zebra de Grid1 calculado desde el dato (no desde AlternationIndex, que es
        // solo lectura y se desfasa al reciclar contenedores virtualizados).
        public bool   FilaPar        => Linea % 2 == 0;
        public string Id             { get; set; } = "";
        public string Codigo         { get; set; } = "";
        public string Categoria      { get; set; } = "";
        public string Descripcion    { get; set; } = "";
        public double Disponible     { get; set; }
        public double Stock          { get; set; }
        public bool   Seleccionado   { get; set; }
        public int    OrdenSeleccion { get; set; }         // 0 = no seleccionado
        public string OrdenStr       => OrdenSeleccion > 0 ? OrdenSeleccion.ToString() : "";
    }

    public class ArticuloExportado
    {
        public string Id          { get; set; } = "";
        public string Codigo      { get; set; } = "";
        public string Descripcion { get; set; } = "";
    }
}
