using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TransformChangesListener : MonoBehaviour
{
    public void HandleGlobalInterceptorCallback(object[] args)
    {
        Debug.Log("HandleGlobalInterceptorCallback");
    }
}
