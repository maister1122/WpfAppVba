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
                if (soloSiElFocoSigueEnLaGrilla && !grid.IsKeyboardFocusWithin) return;

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
            }), System.Windows.Threading.DispatcherPriority.Background);
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
        internal static void ReasignarItemsSource(DataGrid grid, System.Collections.IEnumerable? origen)
        {
            bool teniaFoco = grid.IsKeyboardFocusWithin;
            int  idxPrevio = grid.SelectedIndex;

            grid.ItemsSource = origen;

            if (idxPrevio >= 0 && idxPrevio < grid.Items.Count)
                grid.SelectedIndex = idxPrevio;

            if (teniaFoco) EnfocarCeldaSeleccionada(grid);
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
