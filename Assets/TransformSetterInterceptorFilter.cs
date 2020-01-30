using UnityEngine;


public class TransformSetterInterceptorFilter : MonoBehaviour
{
    public static Transform FilterToTransform;
    public static bool LogForAllTransforms = true;

    public Transform filterToTransform;
    public bool logForAllTransforms;

    void Update()
    {
        if (FilterToTransform == null && filterToTransform != null)
        {
            logForAllTransforms = false;
        }

        FilterToTransform = filterToTransform;
        LogForAllTransforms = logForAllTransforms;
    }
}
