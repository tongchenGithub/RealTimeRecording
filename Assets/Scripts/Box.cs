using UnityEngine;

public class Box : MonoBehaviour
{
    private Quaternion boxQuaternion;
    private int iter;

    void Start()
    {
        boxQuaternion = Quaternion.Euler(UnityEngine.Random.RandomRange(-90, 90), UnityEngine.Random.RandomRange(-90, 90), UnityEngine.Random.RandomRange(-90, 90));
    }
    
    void Update()
    {
    
        iter++;
    
        if (iter > 100)
        {
            boxQuaternion = Quaternion.Euler( UnityEngine.Random.RandomRange(-90 , 90) , UnityEngine.Random.RandomRange(-90, 90) , UnityEngine.Random.RandomRange(-90, 90));
            iter = 0;
        }
        this.transform.rotation = Quaternion.Lerp(this.transform.rotation, boxQuaternion , Time.deltaTime);
    }
}