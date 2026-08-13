/* ============================================================================
   codigo: NVARCHAR(100) → INT en las 6 tablas de documentos

   Motivo: desde la sesión 2026-07-29 el código de un documento es siempre el
   correlativo pelado (1, 2, 3…) — sin signo ni letras. Tanto CodigoDocumento
   (al crear) como CodigoRegenerator (al renumerar) escriben enteros. La columna
   NVARCHAR ya no representa nada que no sea un int, y ademas ordena mal
   ("10" < "9" como texto).

   ALCANCE — las 6 tablas de documentos:
       documentosP, documentosC, documentosF, documentosI, documentosL, documentosT

   NO SE TOCA:
     • articulos.codigo  → queda NVARCHAR(100). Lo escribe el usuario a mano y
                           admite letras; el regenerador ni lo toca.
     • Las maestras (usuarios, familias, productos, categorias, industrias,
       terceros, sucursales, regiones, empresas) → YA son INT.

   ORDEN DE EJECUCIÓN — importante:
     1º  Regenerar códigos desde VisorEmpresa (botón "🔢 Regenerar códigos"),
         UNA VEZ POR CADA EMPRESA. El regenerador trabaja sobre la empresa
         activa, así que si hay varias empresas hay que cambiar de empresa y
         repetir. Esto normaliza cualquier código viejo con signo ("A12", "V-3")
         que haya quedado.
     2º  Correr el PASO 0 de acá abajo: si devuelve filas, el ALTER va a fallar.
     3º  Correr el PASO 1 (los ALTER).
     4º  Correr el PASO 2 (verificación).
   ========================================================================== */

USE [edberBase];
GO

/* ══ PASO 0 — Pre-chequeo: ¿hay códigos que no sean un entero? ═════════════
   TIENE QUE DEVOLVER CERO FILAS. Cada fila que aparezca es un código que SQL
   Server no puede convertir, y hace fallar el ALTER entero.
   Ojo: '' (cadena vacía) SÍ convierte —da 0—, así que no aparece acá; si
   preferís que esos queden en NULL en vez de 0, corré el UPDATE comentado
   debajo antes del PASO 1.                                                   */
SELECT 'documentosP' AS tabla, id, codigo FROM dbo.documentosP WHERE codigo IS NOT NULL AND TRY_CAST(codigo AS int) IS NULL
UNION ALL SELECT 'documentosC', id, codigo FROM dbo.documentosC WHERE codigo IS NOT NULL AND TRY_CAST(codigo AS int) IS NULL
UNION ALL SELECT 'documentosF', id, codigo FROM dbo.documentosF WHERE codigo IS NOT NULL AND TRY_CAST(codigo AS int) IS NULL
UNION ALL SELECT 'documentosI', id, codigo FROM dbo.documentosI WHERE codigo IS NOT NULL AND TRY_CAST(codigo AS int) IS NULL
UNION ALL SELECT 'documentosL', id, codigo FROM dbo.documentosL WHERE codigo IS NOT NULL AND TRY_CAST(codigo AS int) IS NULL
UNION ALL SELECT 'documentosT', id, codigo FROM dbo.documentosT WHERE codigo IS NOT NULL AND TRY_CAST(codigo AS int) IS NULL;
GO

-- Opcional: dejar en NULL los códigos vacíos/en blanco (si no, quedan en 0).
-- UPDATE dbo.documentosP SET codigo = NULL WHERE LTRIM(RTRIM(codigo)) = '';
-- UPDATE dbo.documentosC SET codigo = NULL WHERE LTRIM(RTRIM(codigo)) = '';
-- UPDATE dbo.documentosF SET codigo = NULL WHERE LTRIM(RTRIM(codigo)) = '';
-- UPDATE dbo.documentosI SET codigo = NULL WHERE LTRIM(RTRIM(codigo)) = '';
-- UPDATE dbo.documentosL SET codigo = NULL WHERE LTRIM(RTRIM(codigo)) = '';
-- UPDATE dbo.documentosT SET codigo = NULL WHERE LTRIM(RTRIM(codigo)) = '';
GO

/* ══ PASO 1 — El cambio de tipo ═══════════════════════════════════════════
   Ninguna de estas columnas tiene índice, default ni constraint encima
   (la base no tiene un solo índice fuera de las PK), así que el ALTER es
   directo. Se mantiene NULL permitido, igual que hoy.                        */
ALTER TABLE dbo.documentosP ALTER COLUMN codigo int NULL;
GO
ALTER TABLE dbo.documentosC ALTER COLUMN codigo int NULL;
GO
ALTER TABLE dbo.documentosF ALTER COLUMN codigo int NULL;
GO
ALTER TABLE dbo.documentosI ALTER COLUMN codigo int NULL;
GO
ALTER TABLE dbo.documentosL ALTER COLUMN codigo int NULL;
GO
ALTER TABLE dbo.documentosT ALTER COLUMN codigo int NULL;
GO

/* ══ PASO 2 — Verificación ════════════════════════════════════════════════
   Las 6 de documentos tienen que decir "int"; articulos tiene que seguir
   diciendo "nvarchar"; las maestras ya decían "int" desde antes.             */
SELECT TABLE_NAME AS tabla, DATA_TYPE AS tipo, CHARACTER_MAXIMUM_LENGTH AS largo
FROM INFORMATION_SCHEMA.COLUMNS
WHERE COLUMN_NAME = 'codigo'
ORDER BY CASE WHEN TABLE_NAME LIKE 'documentos%' THEN 0
              WHEN TABLE_NAME = 'articulos'      THEN 1
              ELSE 2 END, TABLE_NAME;
GO
