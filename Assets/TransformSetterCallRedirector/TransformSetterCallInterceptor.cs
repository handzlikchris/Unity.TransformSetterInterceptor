using Assets.TransformSetterCallRedirector.Attribute;
using UnityEngine;

[assembly: TransformSetterCallRedirector(
    nameof(TransformSetterCallInterceptor), 
    ".*",
     ExcludeFullMethodNameRegex = ""
)]

public class TransformSetterCallInterceptor
{
    public static void InterceptSetPosition(Transform originalTransform, Vector3 setTo, object callingObject, string callingMethodName)
    {
        Debug.Log($"{originalTransform.gameObject.name}: set to: {setTo}, calling object: {callingObject}, method: {callingMethodName}");
        originalTransform.position = setTo; 
    }

    public static void InterceptSetLocalPosition(Transform originalTransform, Vector3 setTo, object callingObject, string callingMethodName)
    {
        Debug.Log($"{originalTransform.gameObject.name}: set to: {setTo}, calling object: {callingObject}, method: {callingMethodName}");
        originalTransform.localPosition = setTo;
    }

    public static void InterceptSetRotation(Transform originalTransform, Quaternion setTo, object callingObject, string callingMethodName)
    {
        Debug.Log($"{originalTransform.gameObject.name}: set to: {setTo}, calling object: {callingObject}, method: {callingMethodName}");
        originalTransform.rotation = setTo;
    }

    public static void InterceptSetScale(Transform originalTransform, Vector3 setTo, object callingObject, string callingMethodName)
    {
        Debug.Log($"{originalTransform.gameObject.name}: set to: {setTo}, calling object: {callingObject}, method: {callingMethodName}");
        originalTransform.localScale = setTo;
    }
}
