using Unity.VisualScripting;
using UnityEngine;

public class Spin : MonoBehaviour
{

    [SerializeField]
    Vector3 rotationDirection;
    void Start()
    {
        
    }

    void Update()
    {
        transform.Rotate(rotationDirection*Time.deltaTime);
    }
}
