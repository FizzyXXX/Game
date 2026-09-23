using UnityEngine;
using UnityEngine.InputSystem;

public class PipeBomb : MonoBehaviour
{   
    [SerializeField] Vector3 forceDirection = Vector3.up;

    Rigidbody rb;
    float explosionForce = 50f;
    float rotationForce = 50f;
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
       //rb.AddForce(Vector3.up*20f, ForceMode.Impulse);
    }

    // Update is called once per frame
    void Update()
    {   
        forceDirection = new Vector3(UnityEngine.Random.Range(0, 100), UnityEngine.Random.Range(0, 100), UnityEngine.Random.Range(0, 100));
        Debug.Log(forceDirection);
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            //rb.AddForce(forceDirection, ForceMode.Impulse);
            Explode();
        }
    }
    void Explode()
    {
        Vector3 randomDirection = Random.insideUnitSphere;

        // Bias the explosion upward
        randomDirection.y += 1f;
        randomDirection.Normalize();

        rb.AddForce(randomDirection * explosionForce, ForceMode.Impulse);

        Vector3 randomTorque = Random.insideUnitSphere;
        rb.AddTorque(randomTorque * rotationForce, ForceMode.Impulse);
    }
}
