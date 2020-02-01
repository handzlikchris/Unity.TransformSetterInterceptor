using Assets.TransformSetterCallRedirector.Attribute;
using UnityEngine;

[assembly: TransformSetterCallRedirector(
    nameof(TransformSetterCallInterceptor),
    ".*",
     ExcludeFullMethodNameRegex = ""
)]

public class TransformSetterCallInterceptor
{
    public static void InterceptSetPosition(Transform originalTransform, Vector3 setTo, Object callingObject, string callingMethodName)
    {
        LogIfMatchesFilter(nameof(InterceptSetPosition), originalTransform, setTo.ToString(), callingObject, callingMethodName);
        originalTransform.position = setTo;
    }

    public static void InterceptSetLocalPosition(Transform originalTransform, Vector3 setTo, Object callingObject, string callingMethodName)
    {
        LogIfMatchesFilter(nameof(InterceptSetLocalPosition), originalTransform, setTo.ToString(), callingObject, callingMethodName);
        originalTransform.localPosition = setTo;
    }

    public static void InterceptSetRotation(Transform originalTransform, Quaternion setTo, Object callingObject, string callingMethodName)
    {
        LogIfMatchesFilter(nameof(InterceptSetRotation), originalTransform, setTo.ToString(), callingObject, callingMethodName);
        originalTransform.rotation = setTo;
    }

    public static void InterceptSetScale(Transform originalTransform, Vector3 setTo, Object callingObject, string callingMethodName)
    {
        LogIfMatchesFilter(nameof(InterceptSetScale), originalTransform, setTo.ToString(), callingObject, callingMethodName);
        originalTransform.localScale = setTo;
    }

    private static void LogIfMatchesFilter(string interceptingForMethod, Transform originalTransform, string setTo, Object callingObject, string callingMethodName)
    {
        if (TransformSetterInterceptorFilter.LogForAllTransforms)
        {
            Debug.Log($"{interceptingForMethod} {originalTransform.gameObject.name}: set to: {setTo}, calling object: {callingObject}, method: {callingMethodName}", callingObject);
        }
        else if (TransformSetterInterceptorFilter.FilterToTransform == originalTransform)
        {
            Debug.Log($"{interceptingForMethod} {originalTransform.gameObject.name}: set to: {setTo}, calling object: {callingObject}, method: {callingMethodName}", callingObject);
        }
    }
}
