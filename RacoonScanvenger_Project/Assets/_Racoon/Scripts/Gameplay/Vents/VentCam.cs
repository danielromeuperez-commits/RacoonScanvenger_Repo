using System.Collections.Generic;
using Racoon.Player;
using Unity.Cinemachine;
using UnityEngine;

namespace Racoon.Cameras
{
    public class VentCam : MonoBehaviour
    {
        enum CameraMode
        {
            None,
            Group,
            SoloOutside,
            InsideVent
        }

        [Header("Cámara y Grupo")]
        [SerializeField] CinemachineCamera vcam;
        [SerializeField] Transform targetGroupTransform;

        [Header("Ajustes por estado")]
        [SerializeField] Vector3 groupOffset = new(0f, 6f, -12f);
        [SerializeField] float groupFov = 40f;

        [SerializeField] Vector3 soloOffset = new(0f, 5f, -10f);
        [SerializeField] float soloFov = 37f;

        [SerializeField] Vector3 ventZoomOffset = new(0f, 4.2f, -8.5f);
        [SerializeField] float ventZoomFov = 34f;

        [Header("Suavizado natural de transiciones")]
        [SerializeField] float steadySpeed = 9f;
        [SerializeField] float enterSoloSpeed = 5.5f;
        [SerializeField] float enterVentSpeed = 6.5f;
        [SerializeField] float returnToGroupSpeed = 4.2f;
        [SerializeField] float groupFramingDamping = 3f;

        CinemachineFollow followComp;
        CinemachineGroupFraming groupFramingComp;
        CinemachineRotationComposer rotationComposerComp;

        readonly List<VentilationPlayer> players = new();
        float refreshTimer;
        CameraMode currentMode = CameraMode.None;

        void Awake()
        {
            if (vcam != null)
            {
                followComp = vcam.GetComponent<CinemachineFollow>();
                groupFramingComp = vcam.GetComponent<CinemachineGroupFraming>();
                rotationComposerComp = vcam.GetComponent<CinemachineRotationComposer>();
            }
        }

        void Update()
        {
            if (vcam == null) return;

            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f || players.Count < 2)
            {
                refreshTimer = 0.25f;
                players.Clear();
                players.AddRange(FindObjectsByType<VentilationPlayer>(FindObjectsSortMode.None));
            }

            players.RemoveAll(p => p == null);

            VentilationPlayer localPlayer = null;
            int insideCount = 0;

            foreach (VentilationPlayer p in players)
            {
                if (!p.IsSpawned) continue;
                if (p.IsOwner) localPlayer = p;
                if (p.IsInsideVentSynced) insideCount++;
            }

            if (localPlayer == null) return;

            bool localInside = localPlayer.IsInsideVentSynced;
            bool someoneInside = insideCount > 0;

            CameraMode nextMode;
            Transform desiredTarget;
            Vector3 desiredOffset;
            float desiredFov;
            bool useGroupFraming;
            float blendSpeed;

            if (localInside)
            {
                nextMode = CameraMode.InsideVent;
                desiredTarget = localPlayer.transform;
                desiredOffset = ventZoomOffset;
                desiredFov = ventZoomFov;
                useGroupFraming = false;
                blendSpeed = currentMode != nextMode ? enterVentSpeed : steadySpeed;
            }
            else if (someoneInside)
            {
                nextMode = CameraMode.SoloOutside;
                desiredTarget = localPlayer.transform;
                desiredOffset = soloOffset;
                desiredFov = soloFov;
                useGroupFraming = false;
                blendSpeed = currentMode != nextMode ? enterSoloSpeed : steadySpeed;
            }
            else
            {
                nextMode = CameraMode.Group;
                desiredTarget = targetGroupTransform != null ? targetGroupTransform : vcam.Target.TrackingTarget;
                desiredOffset = groupOffset;
                desiredFov = groupFov;
                useGroupFraming = true;
                blendSpeed = currentMode != nextMode ? returnToGroupSpeed : steadySpeed;
            }

            currentMode = nextMode;
            ApplyCameraState(desiredTarget, desiredOffset, desiredFov, useGroupFraming, blendSpeed);
        }

        void ApplyCameraState(Transform target, Vector3 offset, float fov, bool useGroupFraming, float speed)
        {
            if (target != null && (vcam.Target.TrackingTarget != target || vcam.Target.CustomLookAtTarget))
            {
                vcam.Target = new CameraTarget
                {
                    TrackingTarget = target,
                    CustomLookAtTarget = false
                };
            }

            if (groupFramingComp != null)
            {
                if (useGroupFraming && !groupFramingComp.enabled)
                    groupFramingComp.Damping = Mathf.Max(groupFramingComp.Damping, groupFramingDamping);

                if (groupFramingComp.enabled != useGroupFraming)
                    groupFramingComp.enabled = useGroupFraming;
            }

            if (rotationComposerComp != null && rotationComposerComp.enabled != useGroupFraming)
                rotationComposerComp.enabled = useGroupFraming;

            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);

            if (followComp != null)
                followComp.FollowOffset = Vector3.Lerp(followComp.FollowOffset, offset, t);

            var lens = vcam.Lens;
            if (!lens.Orthographic)
            {
                lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, fov, t);
                vcam.Lens = lens;
            }
        }
    }
}