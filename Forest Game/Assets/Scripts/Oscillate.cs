using UnityEngine;

public class oscillate : MonoBehaviour
{
    public float displacement = 2f;
    public float speed = 2f;

    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        float offset = Mathf.Sin(Time.time * speed) * displacement;
        transform.position = startPosition + Vector3.right * offset;
    }
}
