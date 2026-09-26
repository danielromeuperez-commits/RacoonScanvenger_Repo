using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Racoon.Cameras
{
    /// <summary>
    /// Mete automáticamente a los jugadores en el CinemachineTargetGroup de la escena.
    /// No hace falta añadir este componente a mano: si no está, se añade solo al primer
    /// CinemachineTargetGroup que encuentre. La cámara NO es un objeto de red: cada máquina
    /// añade localmente a los dos jugadores.
    /// </summary>
    [RequireComponent(typeof(CinemachineTargetGroup))]
    public class TargetGroupRegistry : MonoBehaviour
    {
        struct Member
        {
            public Transform Target;
            public float Weight;
            public float Radius;
        }

        static TargetGroupRegistry instance;
        // Jugadores que aparecieron antes de que existiera el Target Group.
        static readonly List<Member> pending = new();

        CinemachineTargetGroup targetGroup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            pending.Clear();
        }

        void Awake()
        {
            instance = this;
            targetGroup = GetComponent<CinemachineTargetGroup>();
            // Quita miembros vacíos (el menú de Cinemachine suele dejar uno).
            targetGroup.Targets.RemoveAll(target => target.Object == null);

            foreach (Member member in pending)
                if (member.Target != null) Add(member);
            pending.Clear();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public static void Register(Transform target, float weight, float radius)
        {
            var member = new Member { Target = target, Weight = weight, Radius = radius };
            TargetGroupRegistry registry = FindOrCreate();
            if (registry != null) registry.Add(member);
            else pending.Add(member);
        }

        public static void Unregister(Transform target)
        {
            pending.RemoveAll(m => m.Target == target);
            if (instance != null) instance.targetGroup.RemoveMember(target);
        }

        static TargetGroupRegistry FindOrCreate()
        {
            if (instance != null) return instance;

            var group = FindAnyObjectByType<CinemachineTargetGroup>();
            if (group == null) return null;
            // AddComponent ejecuta Awake al momento, que asigna 'instance'.
            return group.TryGetComponent(out TargetGroupRegistry existing)
                ? existing
                : group.gameObject.AddComponent<TargetGroupRegistry>();
        }

        void Add(Member member)
        {
            if (targetGroup.FindMember(member.Target) < 0)
                targetGroup.AddMember(member.Target, member.Weight, member.Radius);
        }
    }
}
