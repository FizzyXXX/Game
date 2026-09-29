using UnityEngine;
using UnityEngine.InputSystem;
public class Camera : MonoBehaviour
{
    [SerializeField] float speed = 10;
    Rigidbody rb;
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }
    
    void Update()
    {

        
        if (Keyboard.current.wKey.isPressed)
        {
            transform.Translate(new Vector3(0, 0, 1*speed*Time.deltaTime));
        }
        if (Keyboard.current.aKey.isPressed)
        {
            transform.Translate(new Vector3(-1*speed*Time.deltaTime, 0, 0));
        }
        if (Keyboard.current.dKey.isPressed)
        {
            transform.Translate(new Vector3(1*speed*Time.deltaTime, 0, 0));
        }
        if (Keyboard.current.sKey.isPressed)
        {
            transform.Translate(new Vector3(0, 0, -1*speed*Time.deltaTime));
        }
        
        if (Keyboard.current.upArrowKey.isPressed)
        {
            transform.Translate(new Vector3(0, 1*speed*Time.deltaTime, 0));
            rb.AddForce(Vector3.up*3, ForceMode.Impulse);
        }
        
        if (Keyboard.current.downArrowKey.isPressed)
        {
            //transform.Translate(new Vector3(0, -1*speed*Time.deltaTime, 0));
        }

    }
}
