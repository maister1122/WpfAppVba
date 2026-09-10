using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SistemaGestion.Data;

namespace SistemaGestion
{
    /// <summary>
    /// Equivalente a Funciones_Comunes.bas + test_coneccion.bas
    /// Funciones utilitarias reutilizables en todos los formularios.
    /// </summary>
    public static class FuncionesComunes
    {
        // ─── Equivalente a formActivo(formNombre) ─────────────────────────────
        /// <summary>Devuelve true si hay una ventana del tipo T abierta.</summary>
        public static bool FormActivo<T>() where T : Window
            => Application.Current.Windows.OfType<T>().Any();

        // ─── Equivalente a TieneInternetHTTP() ───────────────────────────────
        /// <summary>
        /// En VBA verificaba internet; en WPF verifica la conexión a la BD.
        /// Reemplaza todos los "If TieneInternetHTTP Then" del código original.
        /// </summary>
        public static bool TieneConexion()
            => DatabaseConnection.ConexionEstaActiva();

        // ─── Guard de conexión antes de guardar ───────────────────────────────
        /// <summary>
        /// Verifica la conexión en dos capas y, si no hay, muestra <paramref name="mensaje"/>:
        ///   1) Estado del label (ConexionEstado.EnLinea): lectura instantánea, no congela.
        ///   2) Si el label dice "en línea": verificación REAL (TieneConexion / SELECT 1).
        /// Devuelve false si no hay conexión.
        /// </summary>
        public static bool HayConexionOAvisa(Window? owner, string mensaje)
        {
            // Capa 1: estado del label (instantáneo). Capa 2: verificación real
            // (solo si la capa 1 pasó, por el cortocircuito &&).
            bool hay = ConexionEstado.EnLinea && TieneConexion();
            if (!hay)
                MessageBox.Show(owner, mensaje, "Conexión",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            return hay;
        }

        // ─── Guard de actualización pendiente (Velopack) ──────────────────────
        /// <summary>
        /// Si hay una actualización pendiente (AppState.VersionPendiente), avisa y
        /// bloquea la acción: nada debe escribir ni recargar datos con una versión
        /// desactualizada hasta que el usuario actualice.
        /// </summary>
        private static bool HayActualizacionPendienteOAvisa(Window? owner)
        {
            if (string.IsNullOrEmpty(AppState.VersionPendiente)) return false;
            MessageBox.Show(owner,
                $"Hay una actualización pendiente (versión {AppState.VersionPendiente}). " +
                "Debes actualizar para continuar.",
                "Actualización requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }

        /// <summary>Guard de conexión para acciones de guardado/borrado.</summary>
        public static bool VerificarConexionParaGuardar(Window? owner = null)
        {
            if (HayActualizacionPendienteOAvisa(owner)) return false;
            return HayConexionOAvisa(owner, "Sin conexión. No se pueden guardar los cambios.");
        }

        /// <summary>Guard de conexión para acciones de actualizar/refrescar datos.</summary>
        public static bool VerificarConexionParaActualizar(Window? owner = null)
        {
            if (HayActualizacionPendienteOAvisa(owner)) return false;
            return HayConexionOAvisa(owner, "Sin conexión. No se pueden actualizar los datos.");
        }

        // ─── Equivalente a ValidarSoloNumeros ────────────────────────────────
        /// <summary>
        /// Usar en PreviewTextInput de un TextBox para aceptar solo dígitos.
        /// permitirDecimales = true → acepta un único punto decimal.
        /// </summary>
        public static void ValidarSoloNumeros(
            object sender,
            TextCompositionEventArgs e,
            bool permitirDecimales = false)
        {
            if (permitirDecimales && e.Text == ".")
            {
                // Permitir punto solo si no hay otro ya
                e.Handled = (sender is TextBox tb && tb.Text.Contains('.'));
            }
            else
            {
                e.Handled = !e.Text.All(char.IsDigit);
            }
        }

        // ─── Equivalente a ValidarSoloLetras ─────────────────────────────────
        /// <summary>
        /// Usar en PreviewTextInput de un TextBox para aceptar solo letras.
        /// permitirEspacios = true → también acepta el espacio.
        /// </summary>
        public static void ValidarSoloLetras(
            object sender,
            TextCompositionEventArgs e,
            bool permitirEspacios = true)
        {
            e.Handled = !e.Text.All(c => char.IsLetter(c) || (permitirEspacios && c == ' '));
        }

        // ─── Restricción de entrada para celdas de Cantidad ──────────────────
        /// <summary>
        /// Restringe un TextBox a una cantidad numérica: solo dígitos y un único
        /// separador decimal, el que corresponda al formato regional de la PC (coma
        /// o punto). Bloquea letras, el otro separador y cualquier otro carácter,
        /// tanto al escribir como al pegar.
        /// </summary>
        public static void RestringirACantidad(TextBox tb)
        {
            tb.PreviewTextInput -= CantidadPreviewTextInput;
            tb.PreviewTextInput += CantidadPreviewTextInput;
            DataObject.RemovePastingHandler(tb, CantidadPasting);
            DataObject.AddPastingHandler(tb, CantidadPasting);
        }

        private static void CantidadPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb) return;
            // Texto resultante tras aplicar la entrada sobre la selección actual.
            string prospecto = tb.Text.Substring(0, tb.SelectionStart)
                             + e.Text
                             + tb.Text.Substring(tb.SelectionStart + tb.SelectionLength);
            e.Handled = !EsCantidadValida(prospecto);
        }

        private static void CantidadPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox tb) { e.CancelCommand(); return; }
            if (!e.DataObject.GetDataPresent(typeof(string))) { e.CancelCommand(); return; }

            string pegado    = (string)e.DataObject.GetData(typeof(string))!;
            string prospecto = tb.Text.Substring(0, tb.SelectionStart)
                             + pegado
                             + tb.Text.Substring(tb.SelectionStart + tb.SelectionLength);
            if (!EsCantidadValida(prospecto)) e.CancelCommand();
        }

        // Válido: dígitos y, como mucho, una aparición del separador decimal de la
        // configuración regional actual (NumberDecimalSeparator: ',' o '.' según la PC).
        // El otro símbolo, letras, espacios o cualquier otro carácter se rechazan.
        private static bool EsCantidadValida(string s)
        {
            string sepDecimal  = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            char   decimalChar = sepDecimal.Length == 1 ? sepDecimal[0] : '.';
            bool   yaTieneDecimal = false;

            foreach (char c in s)
            {
                if (char.IsDigit(c)) continue;
                if (c == decimalChar && !yaTieneDecimal) { yaTieneDecimal = true; continue; }
                return false;
            }
            return true;
        }

        // ─── Restricción de entrada para código numérico (columna codigo es int) ──
        /// <summary>
        /// Refuerza un TextBox de solo dígitos contra dos vías que ValidarSoloNumeros en
        /// PreviewTextInput no cubre: (1) pegar texto (Ctrl+V, menú contextual, arrastrar y
        /// soltar) no dispara TextInput; (2) la barra espaciadora en WPF puede insertar el
        /// espacio antes de que el TextInput llegue al filtro, así que se bloquea aparte en
        /// PreviewKeyDown.
        /// </summary>
        public static void BloquearPegadoNoNumerico(TextBox tb)
        {
            DataObject.RemovePastingHandler(tb, SoloDigitosPasting);
            DataObject.AddPastingHandler(tb, SoloDigitosPasting);

            tb.PreviewKeyDown -= BloquearEspacio;
            tb.PreviewKeyDown += BloquearEspacio;
        }

        private static void SoloDigitosPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(typeof(string))) { e.CancelCommand(); return; }
            string pegado = (string)e.DataObject.GetData(typeof(string))!;
            if (!pegado.All(char.IsDigit)) e.CancelCommand();
        }

        private static void BloquearEspacio(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) e.Handled = true;
        }

        // ─── Equivalente a UnirVariables(...) ────────────────────────────────
        /// <summary>
        /// Une valores no vacíos separados por espacio.
        /// Equivalente a UnirVariables(a, b, c) en VBA.
        /// </summary>
        public static string UnirVariables(params string?[] variables)
            => string.Join(" ", variables
                .Select(v => v?.Trim() ?? "")
                .Where(v => v != ""));
    }
}
