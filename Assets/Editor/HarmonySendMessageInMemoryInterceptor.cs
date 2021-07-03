using System.Collections;
using System.Collections.Generic;
using System.Reflection;
// using HarmonyLib;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class HarmonySendMessageInMemoryInterceptor : MonoBehaviour
{
    // Start is called before the first frame update
    // static HarmonySendMessageInMemoryInterceptor()
    // {
    //     Harmony.DEBUG = true;
    //     var harmony = new Harmony("HarmonySendMessageInMemoryInterceptor");
    //     harmony.PatchAll(Assembly.GetExecutingAssembly());
    //
    //     Debug.Log("HarmonySendMessageInMemoryInterceptor Initialized");
    // }
    //
    // [HarmonyPatch(typeof(UnityEngine.Component))]
    // [HarmonyPatch(nameof(Component.SendMessage))]
    // class PatchTransform
    // {
    //     static void Prefix(Component __instance, string methodName, object value, SendMessageOptions options)
    //     {
    //         __instance.SendMessage(methodName, value, options);
    //     }
    // }
}
