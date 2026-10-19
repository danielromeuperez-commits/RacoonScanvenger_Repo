using UnityEngine;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Curva <c>cubic-bezier(x1, y1, x2, y2)</c> de CSS, para que las animaciones del HUD sean las del mockup HTML.
    /// Admite rebotes (y fuera de 0-1).
    /// </summary>
    public readonly struct CubicBezier
    {
        readonly float ax, bx, cx, ay, by, cy;

        public CubicBezier(float x1, float y1, float x2, float y2)
        {
            cx = 3f * x1;
            bx = 3f * (x2 - x1) - cx;
            ax = 1f - cx - bx;
            cy = 3f * y1;
            by = 3f * (y2 - y1) - cy;
            ay = 1f - cy - by;
        }

        float X(float t) => ((ax * t + bx) * t + cx) * t;
        float Y(float t) => ((ay * t + by) * t + cy) * t;
        float DX(float t) => (3f * ax * t + 2f * bx) * t + cx;

        /// <summary>Progreso (0-1, puede pasarse) para un tiempo normalizado (0-1).</summary>
        public float Evaluate(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            // Newton y, si no converge, bisección (como los navegadores)
            float t = x;
            for (int i = 0; i < 8; i++)
            {
                float error = X(t) - x;
                if (Mathf.Abs(error) < 1e-5f) return Y(t);
                float d = DX(t);
                if (Mathf.Abs(d) < 1e-6f) break;
                t -= error / d;
            }
            float lo = 0f, hi = 1f;
            t = x;
            for (int i = 0; i < 24; i++)
            {
                float value = X(t);
                if (Mathf.Abs(value - x) < 1e-5f) break;
                if (x > value) lo = t;
                else hi = t;
                t = (lo + hi) * .5f;
            }
            return Y(t);
        }
    }

    /// <summary>Curvas y fotogramas clave de las animaciones del HUD (tiempos y valores del mockup, en px a 2560×1440).</summary>
    public static class HUDAnim
    {
        public static readonly CubicBezier Ease = new(.25f, .1f, .25f, 1f);
        public static readonly CubicBezier EaseIn = new(.42f, 0f, 1f, 1f);
        public static readonly CubicBezier EaseOut = new(0f, 0f, .58f, 1f);
        public static readonly CubicBezier EaseInOut = new(.42f, 0f, .58f, 1f);
        /// <summary>Cambio de dígito del rodillo: rebote mecánico.</summary>
        public static readonly CubicBezier Reel = new(.25f, 1.35f, .5f, 1f);
        /// <summary>Saltos e insignias (salto de los rodillos, TOP, llama).</summary>
        public static readonly CubicBezier Bounce = new(.3f, 1.8f, .5f, 1f);
        /// <summary>Entrada de un ítem en su caja.</summary>
        public static readonly CubicBezier Entry = new(.3f, 1.6f, .5f, 1f);
        /// <summary>Iconos que vuelan de una caja a otra.</summary>
        public static readonly CubicBezier Fly = new(.5f, 0f, .3f, 1.25f);

        /// <summary>Fase (0-1) de un bucle de <paramref name="period"/> segundos; <paramref name="delay"/> negativo lo adelanta, como en CSS.</summary>
        public static float Phase(float time, float period, float delay = 0f) => Mathf.Repeat((time - delay) / period, 1f);

        /// <summary>Tramo de un fotograma clave a otro con su curva (como la timing-function de cada keyframe en CSS).</summary>
        public static float Segment(float t, float t0, float t1, float v0, float v1, in CubicBezier curve) =>
            Mathf.LerpUnclamped(v0, v1, curve.Evaluate(Mathf.InverseLerp(t0, t1, t)));

        /// <summary>0 → <paramref name="peak"/> en <paramref name="at"/> → 0, cada tramo con su curva.</summary>
        public static float Peak(float t, float at, float peak, in CubicBezier curve) =>
            t < at ? Segment(t, 0f, at, 0f, peak, curve) : Segment(t, at, 1f, peak, 0f, curve);

        /// <summary>Balanceo de los rodillos: sube 6 px y vuelve (1,6 s).</summary>
        public static float ReelBob(float phase) => Peak(phase, .5f, 6f, EaseInOut);

        /// <summary>Caja principal flotando (2,2 s): giro respecto al reposo (P1; P2 al revés) y subida.</summary>
        public static void ItemFloat(float phase, out float degrees, out float rise)
        {
            rise = Peak(phase, .5f, 12f, EaseInOut);
            // CSS: 0 → +3° → 0 → −3° → 0 (sentido horario positivo)
            float quarter = phase * 4f;
            int index = Mathf.Min(3, (int)quarter);
            float local = quarter - index;
            float[] keys = FloatKeys;
            degrees = Mathf.LerpUnclamped(keys[index], keys[index + 1], EaseInOut.Evaluate(local));
        }

        static readonly float[] FloatKeys = { 0f, 3f, 0f, -3f, 0f };

        /// <summary>Latido de los dígitos del tiempo en los últimos segundos: 1 → 1,12 al 15 % → 1 (1 s).</summary>
        public static float Beat(float phase) => 1f + Peak(phase, .15f, .12f, Ease);
    }
}
