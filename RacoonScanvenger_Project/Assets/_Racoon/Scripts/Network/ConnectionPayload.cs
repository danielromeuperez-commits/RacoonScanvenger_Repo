using System;
using System.Text;
using UnityEngine;

namespace Racoon.Network
{
    /// <summary>
    /// Datos que el jugador envía al host al conectarse (NetworkConfig.ConnectionData).
    /// El host los lee en la aprobación de conexión. Añade aquí más campos (nombre, color...).
    /// </summary>
    [Serializable]
    public struct ConnectionPayload
    {
        public int characterIndex;

        public byte[] Encode() => Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));

        public static ConnectionPayload Decode(byte[] data)
        {
            if (data == null || data.Length == 0) return default;
            try
            {
                return JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(data));
            }
            catch (Exception)
            {
                return default;
            }
        }
    }
}
