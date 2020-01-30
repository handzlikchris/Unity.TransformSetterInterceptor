using System.Collections;
using System.Linq;
using UnityEngine;

public class TransformSetter : MonoBehaviour
{
    public GameObject SetTarget;

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
        SetTarget.transform.position = new Vector3(iteration, iteration, iteration);
        SetTarget.transform.rotation = Quaternion.Euler(iteration, iteration, iteration);
        SetTarget.transform.localScale = new Vector3(iteration, iteration, iteration);    
    }

    void Start()
    {
        StartCoroutine(SetTransformValuesCoroutine());
    }
}
