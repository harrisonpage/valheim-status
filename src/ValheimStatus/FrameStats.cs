using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimStatus
{
    // Frame times between two writes, fed from Plugin.Update. Nothing here touches
    // game code, so a game update cannot break it.
    internal sealed class FrameStats
    {
        public const float SlowFrameSec = 0.05f;   // under 20 fps

        private readonly List<float> frames = new List<float>(1024);
        private float windowStart = -1f;

        public void Sample(float dt)
        {
            if (windowStart < 0f) windowStart = Time.realtimeSinceStartup;
            frames.Add(dt);
        }

        // Empties the window, so call once per write.
        public JsonObject TakeSnapshot()
        {
            float now = Time.realtimeSinceStartup;
            float window = windowStart < 0f ? 0f : now - windowStart;
            windowStart = now;

            var o = new JsonObject();
            int n = frames.Count;
            double sum = 0;
            int slow = 0;
            foreach (float f in frames)
            {
                sum += f;
                if (f > SlowFrameSec) slow++;
            }
            frames.Sort();
            o.Add("fps", window > 0f ? Math.Round(n / window, 1) : 0.0);
            o.Add("target_fps", Application.targetFrameRate);
            var ms = new JsonObject();
            ms.Add("avg", n > 0 ? Ms(sum / n) : 0.0);
            ms.Add("p99", n > 0 ? Ms(frames[Math.Min(n - 1, (int)(n * 0.99))]) : 0.0);
            ms.Add("max", n > 0 ? Ms(frames[n - 1]) : 0.0);
            o.Add("frame_ms", ms);
            o.Add("slow_frames", slow);
            o.Add("window_sec", Math.Round(window, 1));
            frames.Clear();
            return o;
        }

        private static double Ms(double sec) => Math.Round(sec * 1000.0, 1);
    }
}
