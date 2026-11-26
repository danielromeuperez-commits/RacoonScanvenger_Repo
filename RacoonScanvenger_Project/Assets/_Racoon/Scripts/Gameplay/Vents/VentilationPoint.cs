using UnityEngine;

namespace Racoon.Player
{
    [RequireComponent(typeof(Collider))]
    public class VentilationPoint : MonoBehaviour
    {
        [Header("Conexiones")]
        public VentilationPoint left;
        public VentilationPoint right;

        [Header("Posiciones")]
        public Transform insidePoint;
        public Transform exitPoint;

        [SerializeField] float fallbackInteractRadius = 2f;
        public float FallbackInteractRadius => fallbackInteractRadius;

        public Vector3 InsidePosition => insidePoint != null ? insidePoint.position : transform.position;
        public Quaternion InsideRotation => insidePoint != null ? insidePoint.rotation : transform.rotation;
        public Vector3 ExitPosition => exitPoint != null ? exitPoint.position : transform.position;
        public Quaternion ExitRotation => exitPoint != null ? exitPoint.rotation : transform.rotation;

        void Awake()
        {
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
                Debug.LogWarning($"[VentilationPoint] El Collider en '{name}' debería tener 'Is Trigger' activado.", this);
        }

        void OnTriggerEnter(Collider other)
        {
            VentilationPlayer player = other.GetComponentInParent<VentilationPlayer>();
            if (player != null) player.RegisterNearby(this);
        }

        void OnTriggerExit(Collider other)
        {
            VentilationPlayer player = other.GetComponentInParent<VentilationPlayer>();
            if (player != null) player.UnregisterNearby(this);
        }
    }
}