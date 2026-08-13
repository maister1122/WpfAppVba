using System;
using Microsoft.Data.SqlClient;

#pragma warning disable IDE0130
namespace SistemaGestion.Data
{
    public static class DatabaseConnection
    {
        private static SqlConnection? _connection;

        private static string _server   = "";
        private static string _user     = "";
        private static string _password = "";
        private static string _database = "";

        // ─── Seguridad del canal TLS hacia SQL Server ─────────────────────────
        // La app se conecta DIRECTO a SQL Server por internet, así que este canal
        // es el que hay que proteger contra intercepción (man-in-the-middle).
        //   • Encrypt=True  → el tráfico siempre viaja cifrado (siempre activo).
        //   • TrustServerCertificate → si es False se VALIDA además el certificado
        //     del servidor, lo que cierra el MITM. Eso exige que SQL Server presente
        //     un certificado de confianza para esta PC (uno emitido por una CA, o el
        //     .cer del servidor instalado en el almacén de confianza del cliente).
        //
        // Mientras SQL Server siga con un certificado autofirmado sin instalar,
        // dejar esta constante en FALSE: si se pone en true sin ese cert, la app
        // NO conecta. Para cerrar el MITM: instalar un cert válido en SQL Server,
        // poner ValidarCertificadoServidor = true y publicar una versión nueva.
        private const bool ValidarCertificadoServidor = false;

        private static string SeguridadCanal =>
            $"Encrypt=True;TrustServerCertificate={(ValidarCertificadoServidor ? "False" : "True")};";

        // ─── Tiempo para ESTABLECER la conexión (no para ejecutar consultas) ──
        // La app se conecta por internet a un SQL Server con TLS, así que abrir la
        // conexión incluye el handshake previo al login, que en una red lenta tarda
        // varios segundos. Con 10 s el login llegó a fallar en producción por 29 ms:
        // "inicialización=1555; protocolo de enlace=8474" = 10029 ms. 20 s deja
        // margen sin volver eterna la espera cuando el servidor está caído de verdad
        // (peor caso con el reintento de abajo: ~42 s).
        private const int TimeoutConexionSeg = 20;

        private static string ConnectionString =>
            $"Server={_server};Database={_database};User Id={_user};Password={_password};" +
            $"Application Name=edber;Connect Timeout={TimeoutConexionSeg};Command Timeout=10;{SeguridadCanal}" +
            // Resiliencia ante red inestable: reconecta de forma transparente una
            // conexión idle que se rompió por un microcorte (idle connection resiliency).
            $"Connect Retry Count=3;Connect Retry Interval=10;Pooling=true;";

        // ─── Configurar credenciales ──────────────────────────────────────────
        public static void Configurar(string server, string database, string user, string password)
        {
            _server   = server;
            _database = database;
            _user     = user;
            _password = password;
            CerrarConexion();
        }

        // ─── Cadena de conexión actual (para clases que abren sus propias
        // conexiones sueltas en vez de reusar ObtenerConexion()) — las
        // credenciales ya están en memoria tras el login vía broker; nunca se
        // leen de disco acá.
        public static string ObtenerCadenaConexion() => ConnectionString;

        // ─── Obtener (o crear) la conexión ───────────────────────────────────
        public static SqlConnection ObtenerConexion()
        {
            if (_connection == null)
            {
                _connection = new SqlConnection(ConnectionString);
                AbrirConReintentos(_connection);
                return _connection;
            }

            if (_connection.State == System.Data.ConnectionState.Closed ||
                _connection.State == System.Data.ConnectionState.Broken)
            {
                try { AbrirConReintentos(_connection); }
                catch
                {
                    _connection.Dispose();
                    _connection = new SqlConnection(ConnectionString);
                    AbrirConReintentos(_connection);
                }
            }

            return _connection;
        }

        // ─── Abrir la conexión, reintentando los fallos de red ───────────────
        // Un tiempo de espera agotado o un corte durante el handshake es
        // transitorio: el segundo intento entra (es exactamente lo que hacía el
        // usuario a mano, volviendo a loguear). Se reintenta SOLO ante errores de
        // red/tiempo; una contraseña incorrecta o una base inexistente fallan al
        // primer intento, sin espera de más.
        private const int IntentosApertura = 2;

        private static void AbrirConReintentos(SqlConnection conn)
        {
            for (int intento = 1; ; intento++)
            {
                try
                {
                    conn.Open();
                    return;
                }
                catch (SqlException ex) when (intento < IntentosApertura && EsErrorTransitorio(ex))
                {
                    System.Threading.Thread.Sleep(2000);
                }
            }
        }

        // Códigos de error de red / tiempo agotado de SQL Server (el resto —
        // credenciales, permisos, base inexistente— no tiene sentido reintentarlos).
        private static bool EsErrorTransitorio(SqlException ex)
        {
            foreach (SqlError error in ex.Errors)
            {
                switch (error.Number)
                {
                    case -2:     // tiempo de espera agotado (el caso del handshake lento)
                    case 53:     // no se encuentra el servidor / red
                    case 121:    // el período de espera del semáforo expiró
                    case 232:
                    case 233:    // la conexión se cerró durante el establecimiento
                    case 258:
                    case 10053:  // el software anuló una conexión establecida
                    case 10054:  // conexión restablecida por el par
                    case 10060:  // no se pudo alcanzar el host
                    case 10061:  // conexión rechazada
                    case 11001:  // no se resolvió el nombre del host
                        return true;
                }
            }
            return false;
        }

        // ─── Sonda rápida de conexión (no toca la conexión compartida) ───────
        // Usa una conexión propia y desechable con timeout corto, para verificar el
        // estado de la red sin congelar la app los 20 s del timeout normal ni romper
        // la conexión global en uso. La usa el timer de ConexionEstado.
        public static bool Sondear(int timeoutSeg = 2)
        {
            if (string.IsNullOrEmpty(_server)) return false;

            string cs = $"Server={_server};Database={_database};User Id={_user};Password={_password};" +
                        $"Application Name=edber;Connect Timeout={timeoutSeg};Command Timeout={timeoutSeg};" +
                        SeguridadCanal;
            try
            {
                using var conn = new SqlConnection(cs);
                conn.Open();
                using var cmd = new SqlCommand("SELECT 1", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch { return false; }
        }

        // ─── Verificar si la conexión está activa (SELECT 1) ─────────────────
        public static bool ConexionEstaActiva()
        {
            try
            {
                var conn = ObtenerConexion();
                using var cmd = new SqlCommand("SELECT 1", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch { return false; }
        }

        // ─── Cerrar la conexión global ────────────────────────────────────────
        public static void CerrarConexion()
        {
            if (_connection != null)
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                    _connection.Close();
                _connection.Dispose();
                _connection = null;
            }
        }
    }
}
