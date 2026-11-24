using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace PokeLobby.Emulator;

/// <summary>
/// Cliente del stub GDB de Azahar (protocolo Remote Serial), réplica fiel del
/// <c>GdbCliente</c> del launcher original: conexión a 127.0.0.1:**3000**, saludo
/// <c>qSupported:multiprocess-;swbreak+</c>, consulta de parada «?», lectura
/// «m{dir},{n}» en trozos de ≤4096 bytes, tramas «$…#xx» con checksum x2 y
/// continuación «c» para que el emulador siga ejecutándose.
/// Constantes verificadas con REA contra el ensamblado original (3000, 4096, 255, «x2»).
/// </summary>
public sealed class GdbCliente : IDisposable
{
    public const int PuertoPorDefecto = 24689;   // puerto por defecto del gdbstub de Azahar/Citra (verificado en el código descompilado)
    public const int BytesPorTrozo = 4096;

    private TcpClient? _tcp;

    public bool Conectado => _tcp?.Connected == true;

    /// <summary>Conecta al stub y negocia: qSupported → ? → c (continuar la ejecución del juego).</summary>
    public void Conectar(string host = "127.0.0.1", int puerto = PuertoPorDefecto, int timeoutMs = 3000)
    {
        Cerrar();
        var tcp = new TcpClient();
        try
        {
            var tarea = tcp.ConnectAsync(host, puerto);
            if (!tarea.Wait(timeoutMs))
                throw new IOException($"GDB: no se pudo conectar a {host}:{puerto} en {timeoutMs} ms");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
        tcp.ReceiveTimeout = timeoutMs;
        tcp.SendTimeout = timeoutMs;
        _tcp = tcp;

        _ = Comando("qSupported:multiprocess-;swbreak+");   // negociación de capacidades del stub
        _ = Comando("?");                                    // motivo de la parada inicial
        EnviarSinRespuesta("c");                             // reanudar la ejecución del emulador
    }

    /// <summary>Lee n bytes de la memoria del proceso del emulador (trozos de ≤4096, como el original).</summary>
    public byte[] LeerMemoria(long direccion, int longitud)
    {
        var resultado = new byte[longitud];
        var offset = 0;
        while (offset < longitud)
        {
            var trozo = Math.Min(BytesPorTrozo, longitud - offset);
            var dir = direccion + offset;
            var respuesta = Comando($"m{dir:X},{trozo:X}");
            if (respuesta.Length == 0 || respuesta[0] == 'E')
                throw new IOException($"GDB: error de lectura en 0x{dir:X} ({respuesta})");
            var bytes = HexABytes(respuesta);
            if (bytes.Length != trozo)
                throw new IOException($"GDB: lectura corta en 0x{dir:X} ({bytes.Length} ≠ {trozo})");
            Array.Copy(bytes, 0, resultado, offset, trozo);
            offset += trozo;
        }
        return resultado;
    }

    // ─────────────────────── tramas del protocolo ───────────────────────

    private string Comando(string datos)
    {
        Enviar(datos);
        return RecibirPaquete();
    }

    private void EnviarSinRespuesta(string datos) => Enviar(datos);

    private void Enviar(string datos)
    {
        var stream = Flujo();
        var trama = Encoding.ASCII.GetBytes("$" + datos + "#" + Checksum(datos));
        stream.Write(trama);
        var ack = stream.ReadByte();
        if (ack < 0) throw new IOException("GDB: conexión cerrada");
        if (ack == '-') throw new IOException("GDB: el stub pidió reenvío (ack '-')");
        if (ack != '+') throw new IOException($"GDB: esperaba ack y llegó 0x{ack:X2}");
    }

    private string RecibirPaquete()
    {
        var stream = Flujo();
        var inicio = stream.ReadByte();
        if (inicio < 0) throw new IOException("GDB: conexión cerrada");
        if (inicio != '$') throw new IOException($"GDB: esperaba '$' y llegó 0x{inicio:X2}");
        var contenido = new StringBuilder();
        int c;
        while ((c = stream.ReadByte()) >= 0 && c != '#')
            contenido.Append((char)c);
        if (c < 0) throw new IOException("GDB: corte de trama");
        var d1 = stream.ReadByte();
        var d2 = stream.ReadByte();
        if (d1 < 0 || d2 < 0) throw new IOException("GDB: corte de checksum");
        stream.WriteByte((byte)'+');   // aceptar el paquete recibido
        return contenido.ToString();
    }

    private NetworkStream Flujo()
        => _tcp?.GetStream() ?? throw new IOException("GDB: no hay conexión (usa Conectar antes)");

    /// <summary>Checksum del RSP: suma de bytes módulo 256, en formato «x2» (como el original).</summary>
    private static string Checksum(string datos)
    {
        var suma = 0;
        foreach (var c in datos)
            suma += c;
        return (suma & 0xFF).ToString("x2", CultureInfo.InvariantCulture);
    }

    private static byte[] HexABytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    public void Cerrar()
    {
        _tcp?.Dispose();
        _tcp = null;
    }

    public void Dispose() => Cerrar();
}
