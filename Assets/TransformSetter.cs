using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TransformSetter : MonoBehaviour
{
    public List<GameObject> SetTargets;

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
            target.transform.position
            target.transform.position = new Vector3(iteration, iteration, iteration);    
            target.transform.rotation = Quaternion.Euler(iteration, iteration, iteration);  
            target.transform.localScale = new Vector3(iteration, iteration, iteration); 
        }
    }

    void Start()
    {
        StartCoroutine(SetTransformValuesCoroutine());
    }
}
