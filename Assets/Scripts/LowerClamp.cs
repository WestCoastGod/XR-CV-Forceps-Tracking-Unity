using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LowerClamp : MonoBehaviour
{
    [SerializeField]
    private ForcepsController parentForceps;

    private void OnTriggerEnter(Collider other)
    {
        parentForceps.OnLowerTriggerEnter(other.gameObject);
    }

    private void OnTriggerExit(Collider other)
    {
        parentForceps.OnLowerTriggerExit(other.gameObject);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
