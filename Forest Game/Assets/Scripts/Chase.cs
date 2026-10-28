using UnityEngine;

public class Chase : MonoBehaviour
{
    public Transform target;
    public float speed = 5f;
    public float rotationSpeed = 25f;
    public float chaseDistance = 100f;

    void Update()
    {
        if (target == null)
            return;

        // Calculate direction toward the target
        Vector3 direction = target.position - transform.position;
        direction.y = 0f; // Keep rotation horizontal

        float distance = direction.magnitude;

        // Rotate to face the target
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        // Move toward the target
        if (distance > 0.1f && distance <= chaseDistance)
        {
            transform.position +=
                direction.normalized * speed * Time.deltaTime;
        }
    }
}