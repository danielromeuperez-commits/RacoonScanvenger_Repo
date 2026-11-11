using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Efecto sutil al recoger un <see cref="DroppedLoot"/>: una copia solo visual del producto (con su burbuja) sale
    /// volando hacia el jugador que lo ha cogido, girando un poco y encogiéndose, y desaparece en su pecho. La pieza
    /// de verdad ya se ha ido (despawn); esto es solo para que se vea cómo entra en la bolsa. Se crea y se destruye
    /// solo (<see cref="Play"/>), en cada cliente.
    /// </summary>
    public sealed class DroppedLootAbsorbFx : MonoBehaviour
    {
        // Altura del pecho del jugador a la que llega.
        const float TargetHeight = 0.55f;
        // Vueltas que da (grados) mientras viaja.
        const float Spin = 300f;
        // Al principio crece un pelín (tirón) antes de encogerse.
        const float PopScale = 1.12f;

        Transform target;
        float duration, elapsed;
        Vector3 startPosition, startScale;

        /// <summary>
        /// Copia <paramref name="model"/> y <paramref name="bubble"/> (los hijos visibles de <paramref name="root"/>)
        /// y los manda hacia <paramref name="target"/>.
        /// </summary>
        public static void Play(Transform root, Transform model, Transform bubble, Transform target, float duration)
        {
            if (root == null || target == null) return;

            var holder = new GameObject("DroppedLootAbsorb");
            holder.transform.SetPositionAndRotation(root.position, root.rotation);
            holder.transform.localScale = root.lossyScale;

            // Copias visuales: solo los modelos (no llevan scripts ni colliders).
            Copy(model, holder.transform);
            Copy(bubble, holder.transform);

            var fx = holder.AddComponent<DroppedLootAbsorbFx>();
            fx.target = target;
            fx.duration = Mathf.Max(0.05f, duration);
            fx.startPosition = holder.transform.position;
            fx.startScale = holder.transform.localScale;
        }

        static void Copy(Transform source, Transform parent)
        {
            if (source == null) return;
            Transform copy = Instantiate(source, parent, true);
            copy.gameObject.SetActive(true);
            foreach (Collider col in copy.GetComponentsInChildren<Collider>(true)) Destroy(col);
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (t >= 1f || target == null)
            {
                Destroy(gameObject);
                return;
            }

            // Va cada vez más rápido (como si lo succionaran) y sigue al jugador aunque se mueva.
            float travel = t * t * (3f - 2f * t);
            travel = Mathf.Lerp(travel, t * t, 0.5f);
            Vector3 end = target.position + Vector3.up * TargetHeight;
            transform.position = Vector3.Lerp(startPosition, end, travel);
            transform.Rotate(Vector3.up, Spin * Time.deltaTime, Space.World);

            float scale = t < 0.15f ? Mathf.Lerp(1f, PopScale, t / 0.15f) : Mathf.Lerp(PopScale, 0f, Mathf.Pow((t - 0.15f) / 0.85f, 1.6f));
            transform.localScale = startScale * scale;
        }
    }
}
