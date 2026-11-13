using UnityEngine;

public class GemCollector : MonoBehaviour
{
    [SerializeField] GameObject doorToOpen;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    int gemsCollected;
    [SerializeField] int gemsNeeded;


    // Update is called once per frame
    void Update()
    {
        
    }
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Gem"))
        {
            gemsCollected++;
            if (gemsCollected >= gemsNeeded)
            {
                doorToOpen.SetActive(false);
            }
        }
    }
}
