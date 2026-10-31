using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimStatus
{
    // Counts ZDOMan.SendZDOs calls per peer: the only Harmony patch in the plugin,
    // and a postfix that changes nothing. The game's scheduler
    // (ZDOMan.SendZDOToPeers2) gives one peer a turn per frame and starts a new
    // round at most every 1/c_SendFPS seconds, so turns/sec is each player's real
    // update rate. A turn returns false when there was nothing to send or the
    // socket's queue was too full. Counting the call rather than reading the
    // scheduler keeps this meaningful under mods that replace the scheduler but
    // still send through SendZDOs.
    internal static class SendCounter
    {
        public static bool Installed { get; private set; }

        private static FieldInfo peerField;   // ZDOMan.ZDOPeer.m_peer; the type is private
        private static Dictionary<long, int> turns = new Dictionary<long, int>();
        private static Dictionary<long, int> sent = new Dictionary<long, int>();
        private static float windowStart = -1f;

        public static void Install(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(ZDOMan), "SendZDOs");
            if (target == null) throw new MissingMethodException("ZDOMan", "SendZDOs");
            ParameterInfo[] ps = target.GetParameters();
            if (ps.Length == 0) throw new MissingMethodException("ZDOMan", "SendZDOs(ZDOPeer, ...)");
            peerField = AccessTools.Field(ps[0].ParameterType, "m_peer");
            if (peerField == null) throw new MissingFieldException("ZDOMan.ZDOPeer", "m_peer");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SendCounter), nameof(Postfix)));
            windowStart = Time.realtimeSinceStartup;
            Installed = true;
        }

        // __0 by position, typed object: the parameter's type is private and its
        // name is not part of the contract.
        private static void Postfix(object __0, bool __result)
        {
            try
            {
                if (!(peerField.GetValue(__0) is ZNetPeer peer)) return;
                Inc(turns, peer.m_uid);
                if (__result) Inc(sent, peer.m_uid);
            }
            catch
            {
                // never throw into the game's send loop
            }
        }

        private static void Inc(Dictionary<long, int> d, long uid) =>
            d[uid] = d.TryGetValue(uid, out int n) ? n + 1 : 1;

        // Swaps out the counts since the previous call; returns the window length
        // in seconds, 0 when not installed.
        public static double TakeWindow(out Dictionary<long, int> turnsOut, out Dictionary<long, int> sentOut)
        {
            turnsOut = turns;
            sentOut = sent;
            turns = new Dictionary<long, int>();
            sent = new Dictionary<long, int>();
            if (!Installed) return 0;
            float now = Time.realtimeSinceStartup;
            double window = now - windowStart;
            windowStart = now;
            return window;
        }

        // ZDOMan.c_SendFPS is a const, read through reflection so a rename only
        // loses this key.
        public static object TargetFps()
        {
            FieldInfo f = AccessTools.Field(typeof(ZDOMan), "c_SendFPS");
            return f == null ? null : (object)Convert.ToDouble(f.GetValue(null));
        }
    }
}
