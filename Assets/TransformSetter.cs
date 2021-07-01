using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TransformSetter : MonoBehaviour
{
    public List<GameObject> SetTargets; //tst 

    private int iteration = 0;
    IEnumerator SetTransformValuesCoroutine()
    {
        while (true)
        {
            iteration++;
            SetTransformValues(); 
            yield return new WaitForSeconds(1); 
        }
    }

    private void SetTransformValues()
    {
        foreach (var target in SetTargets)
        { 
            SetPosition(target);    
            target.transform.rotation = Quaternion.Euler(iteration, iteration, iteration);  
            target.transform.localScale = new Vector3(iteration, iteration, iteration); 
        }
    }

    private void SetPosition(GameObject target) 
    {
        target.transform.position = new Vector3(1, 2, 3);
    }

    void Start()  
    {
        SendMessage("HandleGlobalInterceptorCallback", new object [3] {"", null, (object)9}, SendMessageOptions.DontRequireReceiver);
        StartCoroutine(SetTransformValuesCoroutine());
    }
}
