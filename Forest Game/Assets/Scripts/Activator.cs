using UnityEngine;

public class Activator : MonoBehaviour
{
    [SerializeField] private GameObject objectToActivate;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            objectToActivate.SetActive(true);
        }
    }
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            objectToActivate.SetActive(false);
        }    
    }
}
