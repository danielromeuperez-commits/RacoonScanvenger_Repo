using UnityEngine;

namespace Racoon.Feedback
{
    /// <summary>
    /// Copia visual (solo malla, sin física ni red) que sale despedida girando, cae con gravedad y se encoge
    /// hasta desaparecer. Sirve para que algo no se esfume de golpe: la cáscara de plátano al resbalar, etc.
    /// Cada cliente crea la suya; no se sincroniza.
    /// </summary>
    public class FlyAwayDebris : MonoBehaviour
    {
        Vector3 velocity;
        Vector3 spinAxis;
        float spinSpeed;
        float lifeTime;
        float age;
        Vector3 baseScale;

        /// <summary>
        /// Copia la malla y materiales de <paramref name="source"/> (con su posición, giro y escala del mundo)
        /// y la lanza con <paramref name="velocity"/>. Devuelve null si el origen no tiene malla.
        /// </summary>
        public static FlyAwayDebris Spawn(MeshRenderer source, Vector3 velocity, float spinSpeed = 720f, float lifeTime = 0.7f)
        {
            if (source == null || !source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                return null;

            Transform from = source.transform;
            var go = new GameObject(source.name + " (debris)");
            go.transform.SetPositionAndRotation(from.position, from.rotation);
            go.transform.localScale = from.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;

            var debris = go.AddComponent<FlyAwayDebris>();
            debris.velocity = velocity;
            debris.spinAxis = Random.onUnitSphere;
            debris.spinSpeed = spinSpeed;
            debris.lifeTime = Mathf.Max(0.05f, lifeTime);
            debris.baseScale = go.transform.localScale;
            return debris;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;

            velocity += Physics.gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(spinAxis, spinSpeed * dt, Space.World);

            // Mantiene el tamaño la mitad de su vida y luego se encoge hasta 0.
            float t = Mathf.Clamp01(age / lifeTime);
            float shrink = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
            transform.localScale = baseScale * shrink;

            if (age >= lifeTime) Destroy(gameObject);
        }
    }
}
