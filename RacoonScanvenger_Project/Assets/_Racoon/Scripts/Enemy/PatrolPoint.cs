using UnityEngine;

public class PatrolPoint : MonoBehaviour
{
    [Header("Configuración")]
    public bool isKeyPoint = false;

    [Min(0f)]
    public float waitTime = 3f;

    private void OnDrawGizmos()
    {
        Gizmos.color = isKeyPoint ? Color.red : Color.yellow;
        Gizmos.DrawSphere(transform.position, isKeyPoint ? 0.35f : 0.2f);
    }
}