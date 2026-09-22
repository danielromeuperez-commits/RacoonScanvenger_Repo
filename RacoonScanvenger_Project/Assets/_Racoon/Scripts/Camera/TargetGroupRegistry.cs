using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Racoon.Cameras
{
    /// <summary>
    /// Va en el GameObject del CinemachineTargetGroup de la escena. Los jugadores se apuntan solos
    /// al spawnear. La cámara NO es un objeto de red: cada máquina tiene la suya y añade localmente
    /// a los dos jugadores, así ambos ven a los dos mapaches encuadrados.
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
        // Por si un jugador spawnea antes de que exista el registro.
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
            if (instance != null) instance.Add(member);
            else pending.Add(member);
        }

        public static void Unregister(Transform target)
        {
            pending.RemoveAll(m => m.Target == target);
            if (instance != null) instance.targetGroup.RemoveMember(target);
        }

        void Add(Member member)
        {
            if (targetGroup.FindMember(member.Target) < 0)
                targetGroup.AddMember(member.Target, member.Weight, member.Radius);
        }
    }
}
