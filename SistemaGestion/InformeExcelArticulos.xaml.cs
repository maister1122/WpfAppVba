using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Win32;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class InformeExcelArticulos : Window
    {
        private static SqlData Sql => SqlData.Instance;

        public InformeExcelArticulos()
        {
            InitializeComponent();
            var ahora = DateTime.Now;
            TxtFecha.Text = ahora.ToString("dd/MM/yyyy");
            TxtHora.Text  = ahora.ToString("HH:mm:ss");
            ActualizarNombreArchivo();
        }

        // ─── Actualiza la preview del nombre de archivo ───────────────────────
        private void ActualizarNombreArchivo()
        {
            string nombre = TxtNombre.Text.Trim();
            string prefijoFecha = ObtenerPrefijoFecha();
            string nombreBase   = string.IsNullOrEmpty(nombre)
                ? $"{prefijoFecha} informe"
                : $"{prefijoFecha} informe {nombre}";

            TxtNombrePreview.Text = $"Nombre de archivo: {SanitizarNombre(nombreBase)}.xlsx";
        }

        private string ObtenerPrefijoFecha()
        {
            string fechaStr = TxtFecha.Text.Trim();
            string horaStr  = TxtHora.Text.Trim();

            if (DateTime.TryParseExact(fechaStr,
                    new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime fecha))
            {
                if (TimeSpan.TryParse(horaStr, out TimeSpan hora))
                {
                    var dt = fecha.Date + hora;
                    return dt.ToString("yyyyMMdd HHmmss");
                }
                return fecha.ToString("yyyyMMdd") + " 000000";
            }
            return DateTime.Now.ToString("yyyyMMdd HHmmss");
        }

        private static string SanitizarNombre(string nombre)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                nombre = nombre.Replace(c, '_');
            return nombre;
        }

        // ─── Auto-numeración si el archivo ya existe ──────────────────────────
        private static string ResolverRutaUnica(string directorio, string nombreBase)
        {
            string ruta = Path.Combine(directorio, $"{nombreBase}.xlsx");
            if (!File.Exists(ruta)) return ruta;

            int n = 1;
            while (true)
            {
                ruta = Path.Combine(directorio, $"{nombreBase} ({n}).xlsx");
                if (!File.Exists(ruta)) return ruta;
                n++;
            }
        }

        // ─── Condición (filtro de artículos incluidos en el informe) ──────────
        private string CondicionSeleccionada()
            => (CmbCondicion.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Todos los artículos";

        private static bool CumpleCondicion(double stock, string condicion) => condicion switch
        {
            "Artículos con stock negativo" => stock < 0,
            "Artículos con stock"          => stock > 0,
            "Artículos con stock 0"        => stock == 0,
            _                               => true, // "Todos los artículos"
        };

        // ─── Handlers de cambio ───────────────────────────────────────────────
        private void TxtNombre_TextChanged(object sender, TextChangedEventArgs e)
            => ActualizarNombreArchivo();

        private void TxtFechaHora_TextChanged(object sender, TextChangedEventArgs e)
            => ActualizarNombreArchivo();

        // ─── Botón Crear ──────────────────────────────────────────────────────
        private void BtnCrearInforme_Click(object sender, RoutedEventArgs e)
        {
            string nombre = TxtNombre.Text.Trim();

            // ── Validar fecha ─────────────────────────────────────────────
            if (!DateTime.TryParseExact(TxtFecha.Text.Trim(),
                    new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime fechaBase))
            {
                MessageBox.Show("Fecha inválida. Use el formato dd/mm/aaaa.", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtFecha.Focus();
                return;
            }

            // ── Combinar fecha + hora de corte ────────────────────────────
            DateTime fechaCorte = fechaBase.Date;
            if (TimeSpan.TryParse(TxtHora.Text.Trim(), out TimeSpan hora))
                fechaCorte = fechaBase.Date + hora;

            // ── Construir nombre base del archivo ─────────────────────────
            string prefijoFecha = ObtenerPrefijoFecha();
            string nombreBase   = string.IsNullOrEmpty(nombre)
                ? $"{prefijoFecha} informe"
                : $"{prefijoFecha} informe {nombre}";
            nombreBase = SanitizarNombre(nombreBase);

            // ── Explorador de guardado ────────────────────────────────────
            var dlg = new SaveFileDialog
            {
                Title            = "Guardar informe Excel",
                FileName         = nombreBase,
                DefaultExt       = ".xlsx",
                Filter           = "Excel (*.xlsx)|*.xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog(this) != true) return;

            // ── Auto-numeración si el archivo ya existe ───────────────────
            string directorio   = Path.GetDirectoryName(dlg.FileName) ?? "";
            string sinExt       = Path.GetFileNameWithoutExtension(dlg.FileName);
            string rutaFinal    = ResolverRutaUnica(directorio, sinExt);

            try
            {
                BtnCrearInforme.IsEnabled = false;
                BtnCrearInforme.Content   = "Generando…";

                GenerarExcel(rutaFinal, fechaCorte);

                Close();
                Process.Start(new ProcessStartInfo(rutaFinal) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el informe:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCrearInforme.IsEnabled = true;
                BtnCrearInforme.Content   = "Crear Informe";
            }
        }

        // ─── Generación del Excel ─────────────────────────────────────────────
        private void GenerarExcel(string filePath, DateTime fechaCorte)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Artículos");

            string nombreInforme = TxtNombre.Text.Trim();
            string tituloInforme = string.IsNullOrEmpty(nombreInforme) ? "Informe de Artículos" : nombreInforme;
            string condicion     = CondicionSeleccionada();

            // ── Bloque de título ──────────────────────────────────────────
            ws.Cell(1, 1).Value = tituloInforme;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Fecha de corte: {fechaCorte:dd/MM/yyyy HH:mm:ss}";
            ws.Cell(3, 1).Value = $"Condición: {condicion}";

            // ── Encabezados ───────────────────────────────────────────────
            const int filaEncabezado = 5;
            ws.Cell(filaEncabezado, 1).Value = "Productos";
            ws.Cell(filaEncabezado, 2).Value = "Código";
            ws.Cell(filaEncabezado, 3).Value = "Categoría";
            ws.Cell(filaEncabezado, 4).Value = "Familia";
            ws.Cell(filaEncabezado, 5).Value = "Descripción Completa";
            ws.Cell(filaEncabezado, 6).Value = "Stock";

            // ── Recolectar datos (según la Condición elegida) ─────────────
            int uf = Sql.ArticulosObj.ContarFilas;
            var datos = new List<(string id, string codigo, string prodDesc, string catDesc, string famDesc, string descCompleta, double stock, int indice)>();

            for (int i = 1; i <= uf; i++)
            {
                var idObj = Sql.ArticulosObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;

                string codigo = Sql.ArticulosObj.ObtenerItem("codigo",      id)?.ToString() ?? "";
                string desc   = Sql.ArticulosObj.ObtenerItem("descripcion", id)?.ToString() ?? "";
                string modelo = Sql.ArticulosObj.ObtenerItem("modelo",      id)?.ToString() ?? "";
                string famId  = Sql.ArticulosObj.ObtenerItem("familia",     id)?.ToString() ?? "";
                string catId  = Sql.ArticulosObj.ObtenerItem("Categoria",   id)?.ToString() ?? "";

                string famDesc  = Sql.FamiliasObj.ObtenerItem("descripcion",   famId)?.ToString() ?? "";
                string prodId   = Sql.FamiliasObj.ObtenerItem("producto",      famId)?.ToString() ?? "";
                string prodDesc = Sql.ProductosObj.ObtenerItem("descripcion",  prodId)?.ToString() ?? "";
                string catDesc  = string.IsNullOrEmpty(catId)
                    ? "(sin categoría)"
                    : (Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "(sin categoría)");

                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);
                double stock        = StockCalculator.ContarStock(id, fechaCorte);

                if (!CumpleCondicion(stock, condicion)) continue;

                // Índice del artículo dentro de su familia: es el tercer criterio de
                // orden del informe (antes desempataba por id, un GUID, o sea al azar).
                int indice = int.TryParse(Sql.ArticulosObj.ObtenerItem("indice", id)?.ToString(), out int ix) ? ix : 0;

                datos.Add((id, codigo, prodDesc, catDesc, famDesc, descCompleta, stock, indice));
            }

            // ── Ordenar por Producto → Familia → Índice dentro de la familia ──
            // El índice es numérico, así que se compara como número (ordenarlo como
            // texto pondría el 10 antes del 2). El id queda solo como desempate
            // final, para que el orden sea estable si dos artículos comparten índice.
            datos.Sort((a, b) =>
            {
                int cmp = string.Compare(a.prodDesc, b.prodDesc, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.famDesc, b.famDesc, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                cmp = a.indice.CompareTo(b.indice);
                if (cmp != 0) return cmp;
                return string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase);
            });

            // ── Escribir datos (una fila por artículo) ────────────────────
            int row = filaEncabezado + 1;
            foreach (var item in datos)
            {
                ws.Cell(row, 1).Value = item.prodDesc;
                ws.Cell(row, 2).Value = item.codigo;
                ws.Cell(row, 3).Value = item.catDesc;
                ws.Cell(row, 4).Value = item.famDesc;
                ws.Cell(row, 5).Value = item.descCompleta;
                ws.Cell(row, 6).Value = item.stock;
                row++;
            }

            // ── Totales por categoría (separados de la lista de artículos por una
            //    fila en blanco), con fórmulas SUMIF/SUM sobre el mismo conjunto ya
            //    filtrado por Condición — mismo diseño (bandas de color, celdas
            //    combinadas, fórmulas) que InventariosGeneral.GenerarExcelInventario. ──
            int primerFilaDatos = filaEncabezado + 1;
            int ultimaFilaDatos = row - 1;

            if (ultimaFilaDatos >= primerFilaDatos)
            {
                row++; // fila en blanco

                ws.Cell(row, 1).Value = "Totales por categoría";
                ws.Range(row, 1, row, 6).Merge();
                ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromArgb(191, 219, 254);
                ws.Range(row, 1, row, 6).Style.Font.Bold = true;
                row++;

                ws.Range(row, 1, row, 5).Merge();
                ws.Cell(row, 1).Value = "Categoría";
                ws.Cell(row, 6).Value = "Stock total";
                ws.Range(row, 1, row, 6).Style.Font.Bold = true;
                ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249);
                row++;

                var categoriasDistintas = datos
                    .Select(d => d.catDesc)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

                foreach (var categoria in categoriasDistintas)
                {
                    string catEscapada = categoria.Replace("\"", "\"\"");
                    ws.Range(row, 1, row, 5).Merge();
                    ws.Cell(row, 1).Value = categoria;
                    ws.Cell(row, 6).FormulaA1 =
                        $"=SUMIF(C{primerFilaDatos}:C{ultimaFilaDatos},\"{catEscapada}\",F{primerFilaDatos}:F{ultimaFilaDatos})";
                    row++;
                }

                ws.Range(row, 1, row, 5).Merge();
                ws.Cell(row, 1).Value = "Total general";
                ws.Cell(row, 6).FormulaA1 = $"=SUM(F{primerFilaDatos}:F{ultimaFilaDatos})";
                ws.Range(row, 1, row, 6).Style.Font.Bold = true;
                ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromArgb(191, 219, 254);
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }
    }
}
