using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SistemaGestion
{
    internal static class GridFocusHelper
    {
        // ─── Destino real del último click del usuario ────────────────────────
        // Una recarga dispara una CASCADA de reasignaciones de ItemsSource:
        // RefrescarGrid pone GridItems.ItemsSource en null (para forzar el rebind),
        // eso dispara SelectionChanged con SelectedItem null, que recarga las
        // grillas auxiliares (GridStock/GridPrecios/GridCategorias) — y recién
        // después llega la asignación con los datos reales.
        //
        // Esa PRIMERA asignación destruye los contenedores de la grilla auxiliar
        // con el foco del usuario adentro, así que a partir de ahí
        // IsKeyboardFocusWithin devuelve false y la asignación FINAL (la que sí
        // trae datos) cree que la grilla nunca tuvo el foco y no lo restaura.
        // Leer el foco dentro de cada reasignación llega tarde: hay que recordar
        // dónde quiso ir el usuario ANTES de que empiece la reconstrucción.
        private static DataGrid? _gridClickeado;

        // Se llama desde el PreviewMouseLeftButtonDown del formulario (el mismo
        // que confirma la edición pendiente), o sea antes de que el click llegue
        // a destino y antes de cualquier recarga que dispare.
        internal static void RegistrarClick(DependencyObject? origenDelClick)
            => _gridClickeado = BuscarPadreVisual<DataGrid>(origenDelClick);

        // Devuelve el foco de teclado a la celda seleccionada del DataGrid.
        // Diferido a DispatcherPriority.Background para correr después de que WPF
        // restaure el foco propio al cerrar un ShowDialog(), y establece CurrentCell
        // explícitamente para que ArrowUp/ArrowDown vuelvan a navegar.
        //
        // soloSiElFocoSigueEnLaGrilla: por defecto false (comportamiento de siempre,
        // usado tras Nuevo/Insertar/Eliminar/Duplicar línea, donde el foco DEBE saltar
        // desde el botón clickeado hacia adentro de la grilla). Pasar true solo desde
        // callbacks diferidos que compiten con un click del usuario en OTRO control
        // (p. ej. CellEditEnding, que ya se dispara dentro de un Dispatcher.BeginInvoke
        // propio): si para cuando esta llamada corre el foco ya se movió fuera de la
        // grilla —el usuario clickeó Guardar, un TextBox de cabecera, etc.— no se lo
        // robamos de vuelta (si no, un click a otro control quedaba enfocado un
        // instante y de inmediato perdía el foco otra vez).
        internal static void EnfocarCeldaSeleccionada(DataGrid grid, bool soloSiElFocoSigueEnLaGrilla = false)
        {
            grid.Dispatcher.BeginInvoke(new Action(() =>
            {
                // El click del usuario mandó el foco a OTRA grilla: no se lo robamos.
                if (soloSiElFocoSigueEnLaGrilla &&
                    _gridClickeado != null && !ReferenceEquals(_gridClickeado, grid)) return;
                if (soloSiElFocoSigueEnLaGrilla && !grid.IsKeyboardFocusWithin) return;

                EnfocarAhora(grid);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Núcleo sincrónico del enfoque (sin diferir): lo comparten
        // EnfocarCeldaSeleccionada y la restauración de ReasignarItemsSource.
        private static void EnfocarAhora(DataGrid grid)
        {
            var item = grid.SelectedItem;
            if (item == null) { grid.Focus(); return; }

            grid.ScrollIntoView(item);
            grid.UpdateLayout();

            var row = grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
            if (row == null) { grid.Focus(); return; }

            // Buscar la primera celda VISIBLE (algunos grids ocultan la columna 0,
            // p. ej. el checkbox de ArticulosGeneral en modo pestaña). No se puede
            // enfocar la celda de una columna oculta.
            var cell = ObtenerPrimeraCeldaVisible(row);
            if (cell != null && cell.Column != null)
            {
                grid.CurrentCell = new DataGridCellInfo(item, cell.Column);
                cell.Focus();
                Keyboard.Focus(cell);
            }
            else
            {
                row.Focus();
            }
        }

        // Reasigna el ItemsSource de una grilla auxiliar (categorías, stock, precios)
        // preservando la fila seleccionada y el foco de teclado.
        //
        // Reasignar ItemsSource reconstruye TODAS las celdas del DataGrid: si el
        // usuario tenía el foco puesto ahí, se destruye y no vuelve solo. Estas
        // grillas se recargan como efecto colateral de editar una línea en la grilla
        // principal (RefrescarGrid → CargarTotales/CargarStock/CargarPrecios), así
        // que sin esto el click del usuario sobre ellas se perdía apenas terminaba
        // de confirmarse la edición de una celda.
        //
        // La restauración es por índice: estas listas se reconstruyen siempre en el
        // mismo orden determinístico, así que el índice identifica la misma fila.
        //
        // El foco se decide por _gridClickeado (dónde cayó el click del usuario) y NO
        // solo por IsKeyboardFocusWithin: dentro de una misma recarga esta grilla se
        // reasigna varias veces (primero null, después los datos) y la primera pasada
        // ya destruyó el contenedor enfocado, así que a partir de ahí leer el foco
        // devuelve false y la pasada con datos no restauraría nada.
        internal static void ReasignarItemsSource(DataGrid grid, System.Collections.IEnumerable? origen)
        {
            bool debeRecuperarFoco = grid.IsKeyboardFocusWithin
                                     || ReferenceEquals(_gridClickeado, grid);
            int idxPrevio = grid.SelectedIndex;

            grid.ItemsSource = origen;

            if (idxPrevio >= 0 && idxPrevio < grid.Items.Count)
                grid.SelectedIndex = idxPrevio;

            if (!debeRecuperarFoco) return;

            // Diferido a Background: recién ahí terminó toda la cascada de recargas
            // y la grilla ya tiene sus filas definitivas. Si quedó sin selección
            // (venía de una pasada con null) se toma la primera fila, así el foco
            // aterriza en una celda y no en el DataGrid vacío.
            grid.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (grid.SelectedIndex < 0 && grid.Items.Count > 0) grid.SelectedIndex = 0;
                EnfocarAhora(grid);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Selecciona todo el texto de la celda que acaba de entrar en edición.
        // Acepta el EditingElement del DataGrid: si es un TextBox (DataGridTextColumn)
        // lo usa directo; si es un contenedor (DataGridTemplateColumn) busca el TextBox
        // dentro de su árbol visual.
        // El SelectAll inmediato funciona al editar con teclado (F2/escribir), pero al
        // entrar con CLIC el clic reposiciona el cursor DESPUÉS y deshace la selección;
        // por eso también se re-despacha en prioridad Input (corre tras el clic y tras
        // generarse el template), garantizando que quede todo seleccionado.
        internal static void SeleccionarTodoEnEdicion(FrameworkElement? editingElement)
        {
            if (editingElement == null) return;
            AplicarSelectAll(editingElement);
            editingElement.Dispatcher.BeginInvoke(new Action(() => AplicarSelectAll(editingElement)),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private static void AplicarSelectAll(FrameworkElement editingElement)
        {
            var tb = editingElement as TextBox ?? BuscarHijoVisual<TextBox>(editingElement);
            if (tb == null) return;
            tb.Focus();
            tb.SelectAll();
        }

        private static DataGridCell? ObtenerPrimeraCeldaVisible(DataGridRow row)
        {
            var presenter = BuscarHijoVisual<DataGridCellsPresenter>(row);
            if (presenter == null) return null;

            int count = presenter.Items.Count;
            for (int i = 0; i < count; i++)
            {
                if (presenter.ItemContainerGenerator.ContainerFromIndex(i) is DataGridCell cell
                    && cell.Column != null
                    && cell.Column.Visibility == Visibility.Visible)
                {
                    return cell;
                }
            }
            return null;
        }

        // Sube por el árbol desde el origen de un evento hasta encontrar un T.
        // Alterna visual/lógico porque e.OriginalSource puede ser un run de texto o
        // un ContentElement, que no viven en el árbol visual.
        private static T? BuscarPadreVisual<T>(DependencyObject? origen) where T : DependencyObject
        {
            while (origen != null)
            {
                if (origen is T encontrado) return encontrado;
                origen = origen is Visual
                    ? VisualTreeHelper.GetParent(origen)
                    : LogicalTreeHelper.GetParent(origen);
            }
            return null;
        }

        private static T? BuscarHijoVisual<T>(DependencyObject parent) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                var hijo = VisualTreeHelper.GetChild(parent, i);
                if (hijo is T encontrado) return encontrado;
                var resultado = BuscarHijoVisual<T>(hijo);
                if (resultado != null) return resultado;
            }
            return null;
        }
    }
}
