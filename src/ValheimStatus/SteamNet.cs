using System;
using System.Reflection;
using Steamworks;

namespace ValheimStatus
{
    // Steam's own view of a peer connection. The game's ZSteamSocket
    // GetConnectionQuality asks Steam's client API, which a dedicated server never
    // starts, so this asks the game-server API directly, with the connection handle
    // read from the private ZSteamSocket.m_con. Kept in its own class so a missing
    // Steamworks type or a renamed field loses only these keys.
    internal static class SteamNet
    {
        private static FieldInfo conField;

        public static void AddStatus(JsonObject o, ISocket socket)
        {
            if (!(socket is ZSteamSocket)) return;
            if (conField == null)
            {
                conField = typeof(ZSteamSocket).GetField("m_con", BindingFlags.NonPublic | BindingFlags.Instance);
                if (conField == null) throw new MissingFieldException("ZSteamSocket", "m_con");
            }
            var con = (HSteamNetConnection)conField.GetValue(socket);
            var st = default(SteamNetConnectionRealTimeStatus_t);
            var lanes = default(SteamNetConnectionRealTimeLaneStatus_t);
            EResult r = SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(con, ref st, 0, ref lanes);
            if (r != EResult.k_EResultOK) return;
            o.Add("ping_ms", st.m_nPing);
            o.Add("queue_ms", Math.Round((long)st.m_usecQueueTime / 1000.0, 1));
            o.Add("out_bytes_sec", (long)Math.Round(st.m_flOutBytesPerSec));
            o.Add("in_bytes_sec", (long)Math.Round(st.m_flInBytesPerSec));
            o.Add("send_rate_bytes_sec", st.m_nSendRateBytesPerSecond);
            o.Add("quality_local", Math.Round(st.m_flConnectionQualityLocal, 2));
            o.Add("quality_remote", Math.Round(st.m_flConnectionQualityRemote, 2));
        }
    }
}
