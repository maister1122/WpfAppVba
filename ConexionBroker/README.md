# ConexionBroker

Servicio mínimo (ASP.NET Core, .NET 8) que reemplaza el flujo anterior de
login: antes, `SistemaGestion`/`VisorEmpresa` guardaban la cadena de conexión
real de SQL Server (servidor, usuario, contraseña) cifrada en
`%AppData%\SistemaGestion\conexion.dat` en cada PC, y había que cargarla a
mano la primera vez en cada máquina.

Ahora **la contraseña real de SQL Server vive únicamente acá, en el
servidor** (nunca en las PCs de los empleados). El flujo pasa a ser:

1. El empleado abre la app y pone su usuario/contraseña de siempre (los
   mismos de la tabla `usuarios`).
2. La app le manda esas credenciales a este servicio por HTTPS.
3. Este servicio valida contra `usuarios` (mismo hash PBKDF2 que ya usaba
   `AppLoader.ValidarLogin`) y, si son correctas, le devuelve a la app la
   conexión real de SQL Server — **solo para esa sesión, en memoria**. La app
   nunca la vuelve a escribir a disco.
4. Al cerrar la app se pierde; la próxima vez se vuelve a pedir con el mismo
   login.

La URL pública de este servicio queda fija en el código de la app
(`AuthBrokerClient.BrokerUrl`, en `SistemaGestion` y `VisorEmpresa`) — no hay
pantalla de "elegir servidor". Si esa URL cambia alguna vez, hay que
actualizar la constante y volver a publicar la app.

Estos pasos están probados en **Ubuntu 24.04** (mismo VPS donde corre SQL
Server 2022 para Linux). Si tu servidor es Windows, la idea es la misma
(publicar, correr como servicio, poner un reverse proxy con HTTPS delante),
pero los comandos concretos de servicio (`sc create` en vez de systemd) y de
reverse proxy (IIS en vez de Caddy) van a ser distintos.

## 1. Instalar .NET 8 en el servidor

```bash
apt update
apt install -y dotnet-sdk-8.0
```

## 2. Bajar el código y publicarlo

```bash
cd ~
git clone https://github.com/maister1122/WpfAppVba.git
cd WpfAppVba/ConexionBroker
```

## 3. Configurar las credenciales reales (nunca en el repo)

`appsettings.json` en este repo **solo tiene placeholders** — es público en
GitHub. Las credenciales reales van en uno de estos dos lugares (ninguno se
commitea, ver `.gitignore`):

**Opción A — archivo local, DENTRO de esta misma carpeta**
(`~/WpfAppVba/ConexionBroker/appsettings.Production.json` — importante: tiene
que estar en esta carpeta exacta, junto a `Program.cs`, no en el home del
usuario ni en ningún otro lado):

```bash
nano appsettings.Production.json
```

Contenido (completar con tus datos reales; `Servidor` puede ser `localhost`
si SQL Server corre en este mismo servidor):

```json
{
  "Sql": {
    "Servidor": "localhost",
    "BaseDatos": "TU_BASE_DE_DATOS",
    "Usuario": "TU_USUARIO_SQL",
    "Contrasena": "TU_CONTRASEÑA_SQL"
  }
}
```

**Opción B — variables de entorno** (equivalentes, útil si preferís no tener
ni siquiera ese archivo en el disco):

```
Sql__Servidor=localhost
Sql__BaseDatos=TU_BASE_DE_DATOS
Sql__Usuario=TU_USUARIO_SQL
Sql__Contrasena=TU_CONTRASEÑA_SQL
```

Usá un login de SQL Server con los permisos mínimos que la app necesita
(no el `sa`), si es posible.

## 4. Publicar y dejarlo corriendo como servicio (systemd)

```bash
dotnet publish -c Release -o /opt/conexionbroker
```

(Esto copia automáticamente `appsettings.Production.json` a
`/opt/conexionbroker/` si ya existía en el paso anterior.)

```bash
cat > /etc/systemd/system/conexionbroker.service << 'EOF'
[Unit]
Description=ConexionBroker
After=network.target

[Service]
WorkingDirectory=/opt/conexionbroker
ExecStart=/usr/bin/dotnet /opt/conexionbroker/ConexionBroker.dll --urls http://127.0.0.1:5080
Restart=always
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production
User=root

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable conexionbroker
systemctl start conexionbroker
```

Probar (desde el propio servidor):
```bash
curl -i http://127.0.0.1:5080/ping
```
Tiene que dar `200 OK`. Si da `503`, correr `systemctl status conexionbroker`
o `journalctl -u conexionbroker -n 50` — el motivo real del error queda
logueado ahí (credenciales mal, SQL Server no responde, etc.).

## 5. HTTPS con Caddy

Las credenciales viajan por acá — **nunca lo expongas en HTTP plano hacia
afuera**. Necesitás un dominio (o subdominio) apuntando a la IP pública del
servidor (registro DNS tipo `A`).

```bash
apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | tee /etc/apt/sources.list.d/caddy-stable.list
apt update
apt install -y caddy
```

Esto ya deja Caddy instalado y corriendo como servicio. Configurarlo:

```bash
cat > /etc/caddy/Caddyfile << 'EOF'
tu-subdominio.tudominio.com {
    reverse_proxy 127.0.0.1:5080
}
EOF
systemctl reload caddy
```

Caddy consigue el certificado HTTPS (Let's Encrypt) solo, la primera vez que
recarga con ese dominio en el Caddyfile — no hace falta ningún paso manual
extra. Probar desde afuera: `https://tu-subdominio.tudominio.com/ping` en el
navegador (página en blanco, sin advertencia de certificado, = éxito).

Si el servidor tiene `ufw` activo (`ufw status`), asegurate de permitir los
puertos 80 y 443.

## 6. Apuntar la app ahí

En `SistemaGestion/AuthBrokerClient.cs` y `VisorEmpresa/AuthBrokerClient.cs`,
la constante `BrokerUrl` tiene que tener exactamente esa URL HTTPS. Si cambia
el dominio en el futuro, se edita esa constante en los dos archivos y se
vuelve a publicar la app (Velopack) para que llegue a todas las PCs.

## Notas

- La migración automática de contraseñas antiguas en texto plano (si
  `usuarios.llave` no tiene el formato de hash pero coincide) se conserva
  igual que en `AppLoader.ValidarLogin` — queda re-hasheada tras el primer
  login exitoso por acá.
- Este servicio es un buen lugar para agregar, más adelante, un límite de
  intentos fallidos de login (hoy no lo tiene) — todos los logins ya pasan
  por un único punto.
- **No** le pongas `<InvariantGlobalization>true</InvariantGlobalization>` al
  `.csproj` — `Microsoft.Data.SqlClient` lo necesita desactivado (falla con
  "Globalization Invariant Mode is not supported" si se activa).
