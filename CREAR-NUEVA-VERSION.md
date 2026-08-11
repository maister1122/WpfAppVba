# Crear una nueva versión manualmente (desde GitHub, sin PC)

Guía para **publicar tú mismo** una versión nueva de la app usando solo el navegador.
No necesitas tu computadora, ni instalar nada, ni un token: GitHub compila la app en
un Windows en la nube.

Son **2 pasos**:

1. Subir el número de versión en `master`.
2. Lanzar el workflow que publica la release.

---

## Paso 1 — Subir el número de versión en `master`

1. Abre el archivo del proyecto:
   <https://github.com/maister1122/WpfAppVba/blob/master/WpfAppVba/WpfAppVba.csproj>

2. Arriba a la derecha del archivo, haz clic en el **lápiz ✏️** (*Edit this file*).

3. Busca esta línea (cerca de la línea 13):

   ```xml
   <Version>1.0.3</Version>
   ```

4. Cambia **solo los tres números** al número nuevo. Por ejemplo, si vienes de `1.0.3`:

   ```xml
   <Version>1.0.4</Version>
   ```

   > ⚠️ No borres `<Version>` ni `</Version>`. Solo cambia lo que está en medio.

5. Arriba a la derecha, botón verde **Commit changes…**.

6. En la ventana **Confirmar cambios**:
   - Deja marcada la **primera** opción: *"Comprométete directamente con el `master`rama"*
     (significa: confirmar directo en `master`). **Ya viene seleccionada.**
   - NO elijas "Crea una nueva rama…".
   - Haz clic en **Confirmar cambios** (verde).

✅ Con esto el número nuevo ya quedó en `master`.

---

## Paso 2 — Lanzar el workflow (publica la release)

1. Abre las Actions del repo:
   <https://github.com/maister1122/WpfAppVba/actions/workflows/release.yml>

   > Si no se abre directo: pestaña **Actions** → en la lista de la izquierda elige
   > **"Publicar release (Velopack)"**.

2. A la derecha, botón **Run workflow** (se despliega un cuadrito).

3. En el campo **version**:
   - Escribe el **mismo** número que pusiste en el csproj (ej. `1.0.4`), **o**
   - Déjalo **vacío** → tomará automáticamente el número del csproj. (Dejarlo vacío es
     lo más seguro, así nunca se descuadra.)

4. Clic en el botón verde **Run workflow**.

GitHub compila, empaqueta y publica la Release (tarda unos minutos). Cuando el workflow
aparezca en **verde ✓**, ya está publicada. Los usuarios con la app instalada verán el
botón **🔄 Actualizar** la próxima vez que la abran.

---

## Las 3 reglas para no equivocarte con el número

1. **Formato `X.Y.Z`** (tres números): `1.0.4` ✅ — `1.4` ❌ — `1.0` ❌.
2. **Siempre mayor** que la versión actual. Lo normal es sumar 1 al último número:
   - Arreglo / cambio pequeño: `1.0.3` → `1.0.4`
   - Cambio mediano (varias funciones): `1.0.3` → `1.1.0`
   - Cambio grande: `1.0.3` → `2.0.0`
3. **El mismo número** en el csproj (Paso 1) y en el campo *version* del workflow
   (Paso 2). Si dejas el campo vacío, usa solo el del csproj y no hay forma de equivocarse.

> Nunca repitas ni bajes un número. Si publicas una versión igual o menor que la
> instalada, Velopack la ignora y los usuarios no verán la actualización.

---

## Cómo saber qué versión es la actual

Mírala en la línea `<Version>...</Version>` del csproj
(<https://github.com/maister1122/WpfAppVba/blob/master/WpfAppVba/WpfAppVba.csproj>),
o en la última release publicada:
<https://github.com/maister1122/WpfAppVba/releases>.

---

## Si algo sale mal

- **El workflow terminó en rojo ✗**: entra al workflow fallido, abre el paso que falló
  y lee el mensaje. Lo más común es haber puesto un número con formato incorrecto.
- **Publiqué un número equivocado**: simplemente sube otro número **mayor** y vuelve a
  lanzar el workflow. No hace falta borrar nada.
- **Los usuarios no ven "Actualizar"**: confirma que la nueva versión es mayor que la
  que tienen instalada y que la Release aparece en la página de *Releases*. (En
  desarrollo, ejecutando desde Visual Studio, el botón nunca aparece: solo funciona en
  la app instalada.)

---

> Más detalle del flujo completo (incluida la forma manual desde tu PC con Velopack) en
> [`PUBLICAR-ACTUALIZACIONES.md`](PUBLICAR-ACTUALIZACIONES.md).
