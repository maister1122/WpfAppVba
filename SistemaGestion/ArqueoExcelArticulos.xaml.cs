using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Win32;
using SistemaGestion.Data;

namespace SistemaGestion
{
    public partial class ArqueoExcelArticulos : Window
    {
        private static SqlData Sql => SqlData.Instance;

        public ArqueoExcelArticulos()
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
                ? $"{prefijoFecha} arqueo"
                : $"{prefijoFecha} arqueo {nombre}";

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

        // ─── Handlers de cambio ───────────────────────────────────────────────
        private void TxtNombre_TextChanged(object sender, TextChangedEventArgs e)
            => ActualizarNombreArchivo();

        private void TxtFechaHora_TextChanged(object sender, TextChangedEventArgs e)
            => ActualizarNombreArchivo();

        // ─── Botón Crear ──────────────────────────────────────────────────────
        private void BtnCrearArqueo_Click(object sender, RoutedEventArgs e)
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
                ? $"{prefijoFecha} arqueo"
                : $"{prefijoFecha} arqueo {nombre}";
            nombreBase = SanitizarNombre(nombreBase);

            // ── Explorador de guardado ────────────────────────────────────
            var dlg = new SaveFileDialog
            {
                Title            = "Guardar arqueo Excel",
                FileName         = nombreBase,
                DefaultExt       = ".xlsx",
                Filter           = "Excel (*.xlsx)|*.xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog(this) != true) return;

            // ── Auto-numeración si el archivo ya existe ───────────────────
            string directorio = Path.GetDirectoryName(dlg.FileName) ?? "";
            string sinExt      = Path.GetFileNameWithoutExtension(dlg.FileName);
            string rutaFinal   = ResolverRutaUnica(directorio, sinExt);

            try
            {
                BtnCrearArqueo.IsEnabled = false;
                BtnCrearArqueo.Content   = "Generando…";

                GenerarExcel(rutaFinal, fechaCorte);

                Close();
                Process.Start(new ProcessStartInfo(rutaFinal) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el arqueo:\n{ex.Message}", "Consola",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCrearArqueo.IsEnabled = true;
                BtnCrearArqueo.Content   = "Crear Arqueo";
            }
        }

        // ─── Generación del Excel ─────────────────────────────────────────────
        // Columna "sistema" = stock a la fecha de corte (StockCalculator, igual que
        // Informe Excel); "revicion"/"hoja"/"referecia"/"observacion" quedan en blanco
        // para completar a mano durante el conteo. El resto de columnas son fórmulas
        // de la tabla "Tabla1" que comparan sistema vs. lo contado y arman los totales.
        private void GenerarExcel(string filePath, DateTime fechaCorte)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Arqueo");

            // ── Resumen (fecha / informe / estado / no revisados / errores) ────
            ws.Cell(1, 1).Value  = "FECHA";
            ws.Cell(1, 2).Value  = "INFORME";
            ws.Cell(1, 3).Value  = "ESTADO ";
            ws.Cell(1, 13).Value = "NO REVISADOS";
            ws.Cell(1, 14).Value = "ERRORES";

            string empresaDesc  = Sql.EmpresasObj.ObtenerItem("descripcion",  AppState.EmpresaActiva)?.ToString() ?? "";
            string sucursalDesc = Sql.SucursalesObj.ObtenerItem("descripcion", AppState.SucursalActiva)?.ToString() ?? "";
            string informe = string.IsNullOrEmpty(sucursalDesc) ? empresaDesc : $"{empresaDesc} - {sucursalDesc}";

            ws.Cell(2, 1).Value                    = fechaCorte;
            ws.Cell(2, 1).Style.DateFormat.Format   = "dd/mm/yyyy hh:mm:ss";
            ws.Cell(2, 2).Value                     = informe;
            ws.Cell(2, 3).FormulaA1                 = "=IF(AND(M2=0,N2=0),\"COMPLETADO\",\"PENDIENTE\")";
            ws.Cell(2, 13).FormulaA1                = "=COUNTIFS(Tabla1[estado],\"NO REVISADO\")";
            ws.Cell(2, 14).FormulaA1                = "=COUNTIFS(Tabla1[estado],\"ERROR\")";

            AplicarCuadricula(ws.Range(1, 1, 2, 3));   // FECHA / INFORME / ESTADO
            AplicarCuadricula(ws.Range(1, 13, 2, 14)); // NO REVISADOS / ERRORES

            // ── Totales por categoría (todas las categorías registradas actualmente,
            // no una lista fija) ────────────────────────────────────────────────
            var categorias = new List<(string Id, string Desc)>();
            int ufCat = Sql.CategoriasObj.ContarFilas;
            for (int i = 1; i <= ufCat; i++)
            {
                var idObj = Sql.CategoriasObj.Mover(i);
                if (idObj == null) continue;
                string id = idObj.ToString()!;
                categorias.Add((id, Sql.CategoriasObj.ObtenerItem("descripcion", id)?.ToString() ?? ""));
            }

            const int filaEncabezadoCat = 4;
            ws.Cell(filaEncabezadoCat, 1).Value = "CATEGORÍA";
            ws.Cell(filaEncabezadoCat, 2).Value = "SISTEMA";
            ws.Cell(filaEncabezadoCat, 3).Value = "INVENTARIO";

            int primeraCat = filaEncabezadoCat + 1;
            int filaCat    = primeraCat;
            foreach (var cat in categorias)
            {
                ws.Cell(filaCat, 1).Value     = cat.Desc;
                ws.Cell(filaCat, 2).FormulaA1 = $"=SUMIFS(Tabla1[sistema],Tabla1[categoria],A{filaCat})";
                ws.Cell(filaCat, 3).FormulaA1 = $"=SUMIFS(Tabla1[inventario],Tabla1[categoria],A{filaCat})";
                filaCat++;
            }
            int ultimaCat    = filaCat - 1;
            int filaTotalCat = filaCat; // = ultimaCat + 1

            // La lista de categorías es también una tabla, con su fila de totales
            // (Total / suma / suma) activada igual que Tabla1. Esto hay que hacerlo
            // ANTES de escribir nada en filaTotalCat en adelante: activar
            // ShowTotalsRow inserta una fila nueva justo debajo de la tabla, y
            // necesitamos que esa fila sea exactamente filaTotalCat (todavía vacía
            // en este punto) para no desplazar el resto del layout (FALTA/SOBRA/TOTAL
            // y la tabla de artículos, que se escriben después).
            if (ultimaCat >= primeraCat)
            {
                var rangoCategorias = ws.Range(filaEncabezadoCat, 1, ultimaCat, 3);
                var tablaCategorias = rangoCategorias.CreateTable("TablaCategorias");
                tablaCategorias.ShowTotalsRow = true;
                tablaCategorias.Field("CATEGORÍA").TotalsRowLabel     = "Total";
                tablaCategorias.Field("SISTEMA").TotalsRowFunction    = XLTotalsRowFunction.Sum;
                tablaCategorias.Field("INVENTARIO").TotalsRowFunction = XLTotalsRowFunction.Sum;

                ws.Cell(ultimaCat, 12).Value = "FALTA";
                ws.Cell(ultimaCat, 13).Value = "SOBRA";
                ws.Cell(ultimaCat, 14).Value = "TOTAL";
            }
            else
            {
                ws.Cell(filaTotalCat, 1).Value = "TOTAL";
            }

            ws.Cell(filaTotalCat, 12).FormulaA1 = "=SUMIF(Tabla1[diferencia],\"FALTA\",Tabla1[cantidad])";
            ws.Cell(filaTotalCat, 13).FormulaA1 = "=SUMIF(Tabla1[diferencia],\"SOBRA\",Tabla1[cantidad])";
            ws.Cell(filaTotalCat, 14).FormulaA1 = $"=M{filaTotalCat}-L{filaTotalCat}";

            AplicarCuadricula(ws.Range(filaEncabezadoCat, 1, filaTotalCat, 3)); // CATEGORÍA / SISTEMA / INVENTARIO
            if (ultimaCat >= primeraCat)
                AplicarCuadricula(ws.Range(ultimaCat, 12, filaTotalCat, 14));   // FALTA / SOBRA / TOTAL

            // ── Tabla de artículos ──────────────────────────────────────────────
            // "linea" (antes "id"): número de línea secuencial desde 1.
            // "articulo": código real del artículo (antes tenía el número de línea).
            int filaHeaders = filaTotalCat + 2;
            string[] encabezados =
            {
                "linea", "articulo", "categoria", "familia", "descripcion", "sistema",
                "revicion", "inventario", "estado", "hoja", "referecia", "observacion",
                "diferencia", "cantidad"
            };
            for (int c = 0; c < encabezados.Length; c++)
                ws.Cell(filaHeaders, c + 1).Value = encabezados[c];

            // Mismo criterio de recolección/orden que Informe Excel (Producto → Familia → Id).
            int uf = Sql.ArticulosObj.ContarFilas;
            var datos = new List<(string Id, string Codigo, string ProdDesc, string FamDesc, string CatDesc, string DescCompleta, double Stock, int Indice)>();
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
                string catDesc  = Sql.CategoriasObj.ObtenerItem("descripcion", catId)?.ToString() ?? "";

                string descCompleta = FuncionesComunes.UnirVariables(desc, famDesc, modelo);
                double stock         = StockCalculator.ContarStock(id, fechaCorte);

                // Índice del artículo dentro de su familia: es el tercer criterio de
                // orden del informe (antes desempataba por id, un GUID, o sea al azar).
                int indice = int.TryParse(Sql.ArticulosObj.ObtenerItem("indice", id)?.ToString(), out int ix) ? ix : 0;

                datos.Add((id, codigo, prodDesc, famDesc, catDesc, descCompleta, stock, indice));
            }

            // Producto → Familia → Índice dentro de la familia. El índice es
            // numérico, así que se compara como número (ordenarlo como texto pondría
            // el 10 antes del 2). El id queda solo como desempate final, para que el
            // orden sea estable si dos artículos comparten índice.
            datos.Sort((a, b) =>
            {
                int cmp = string.Compare(a.ProdDesc, b.ProdDesc, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.FamDesc, b.FamDesc, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                cmp = a.Indice.CompareTo(b.Indice);
                if (cmp != 0) return cmp;
                return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });

            int filaDatosInicio = filaHeaders + 1;
            int filaActual = filaDatosInicio;
            int n = 0;
            foreach (var item in datos)
            {
                n++;
                ws.Cell(filaActual, 1).Value = n;                 // linea
                ws.Cell(filaActual, 2).Value = item.Codigo;       // articulo (código real)
                ws.Cell(filaActual, 3).Value = item.CatDesc;      // categoria
                ws.Cell(filaActual, 4).Value = item.FamDesc;      // familia
                ws.Cell(filaActual, 5).Value = item.DescCompleta; // descripcion
                ws.Cell(filaActual, 6).Value = item.Stock;        // sistema
                // G (revicion), J (hoja), K (referecia), L (observacion): en blanco a
                // propósito — los completa quien hace el conteo físico.
                ws.Cell(filaActual, 8).FormulaA1 =
                    $"=IF(AND(Tabla1[[#This Row],[sistema]]=\"\",Tabla1[[#This Row],[revicion]]=\"\"),\"\"," +
                    $"IF(AND(Tabla1[[#This Row],[sistema]]=0,Tabla1[[#This Row],[revicion]]=\"\"),0," +
                    $"IF(G{filaActual}=\"X\",F{filaActual},IF(G{filaActual}<>\"X\",G{filaActual}))))";
                ws.Cell(filaActual, 9).FormulaA1 =
                    $"=IF(AND(G{filaActual}=\"\",F{filaActual}=\"\"),\"\"," +
                    $"IF(AND(G{filaActual}=\"\",F{filaActual}=0),\"IGUALA\"," +
                    $"IF(AND(G{filaActual}=\"\",F{filaActual}<>\"\"),\"NO REVISADO\"," +
                    $"IF(AND(G{filaActual}=\"X\",F{filaActual}<>\"\"),\"IGUALA\",\"ERROR\"))))";
                ws.Cell(filaActual, 13).FormulaA1 =
                    "=IF(OR(Tabla1[[#This Row],[estado]]=\"\",Tabla1[[#This Row],[estado]]=\"NO REVISADO\"),\"\"," +
                    "IF(Tabla1[[#This Row],[inventario]]<Tabla1[[#This Row],[sistema]],\"FALTA\"," +
                    "IF(Tabla1[[#This Row],[inventario]]>Tabla1[[#This Row],[sistema]],\"SOBRA\",\"\")))";
                ws.Cell(filaActual, 14).FormulaA1 =
                    "=IF(Tabla1[[#This Row],[diferencia]]<>\"\",ABS(Tabla1[[#This Row],[sistema]]-Tabla1[[#This Row],[inventario]]),\"\")";
                filaActual++;
            }
            int filaDatosFin = filaActual - 1;

            if (filaDatosFin >= filaDatosInicio)
            {
                var rangoTabla = ws.Range(filaHeaders, 1, filaDatosFin, encabezados.Length);
                var tabla = rangoTabla.CreateTable("Tabla1");

                // ── Fila de totales, igual que la plantilla: Total (etiqueta) en
                // "articulo", conteo de filas en "categoria", suma en sistema/inventario.
                tabla.ShowTotalsRow = true;
                tabla.Field("articulo").TotalsRowLabel      = "Total";
                tabla.Field("categoria").TotalsRowFunction  = XLTotalsRowFunction.Count;
                tabla.Field("sistema").TotalsRowFunction    = XLTotalsRowFunction.Sum;
                tabla.Field("inventario").TotalsRowFunction = XLTotalsRowFunction.Sum;

                // ── Formatos condicionales (solo columna estado), igual que la plantilla:
                // ERROR en rojo negrita, NO REVISADO con relleno violeta.
                var rangoEstado = ws.Range(filaDatosInicio, 9, filaDatosFin, 9); // solo I (estado)
                rangoEstado.AddConditionalFormat()
                    .WhenIsTrue($"NOT(ISERROR(SEARCH(\"ERROR\",$I{filaDatosInicio})))")
                    .Font.SetFontColor(XLColor.Red).Font.SetBold();
                rangoEstado.AddConditionalFormat()
                    .WhenIsTrue($"NOT(ISERROR(SEARCH(\"NO REVISADO\",$I{filaDatosInicio})))")
                    .Fill.SetBackgroundColor(XLColor.FromArgb(0x70, 0x30, 0xA0))
                    .Font.SetFontColor(XLColor.White);
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        // ─── Bordes finos en todas las celdas del rango (igual que el resumen de
        // la plantilla: fecha/informe/estado, no revisados/errores, categorías,
        // falta/sobra/total) ────────────────────────────────────────────────────
        private static void AplicarCuadricula(IXLRange range)
        {
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder  = XLBorderStyleValues.Thin;
        }
    }
}
