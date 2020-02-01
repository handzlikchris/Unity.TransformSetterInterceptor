




# What is changing the tranform (position / rotation / scale)?

When working with Unity you've probably asked that question when a transform position/rotation/scale is changing and you have no idea why.

Generally you'd set a breakpoint and debug it this way but for transform you cannot easily do that. The best way seem to be replacing all calls that modify it directly, eg
`transform.postion = newPosition`
to some interceptor code that you can control, like
`Interceptor.SetPosition(transfrom, newPosition)`

Unfortunately this is not always possible or easy.

This tool will help you do exactly that **but automatically and without modifying any of your source code**. 
![Transform Setter Interceptor Workflow](/_github/TransforSetterInterceptorWorkflow.gif)


## Approach
The tool will use IL Weaving and will redirect all the set calls to `transform.position`, `transform.rotation` and `transform.scale` to `TransformSetterCallInterceptor` where you could add any actions needed. 

eg.
Method signature and default implementation:
```
public static void InterceptSetPosition(Transform originalTransform, Vector3 setTo, Object callingObject, string callingMethodName)
    {
        //do whatever you want to do
        originalTransform.position = setTo;
    }
```

Method will give you access to
- original transform
- value that it'd be set to
- object that does that
- and calling method name


## Setup
You can clone this repository and run it in Unity as an example.

To import into your project:
1) In Unity add a package dependency to [Malimbe]([https://github.com/ExtendRealityLtd/Malimbe](https://github.com/ExtendRealityLtd/Malimbe)) which will hook up to Unity build process so the weaver code can work on your assemblies after Unity is done compiling them.

You can do that via `manifest.json` file located in `/Packages` folder. You'll have to add following entries (as per Malimbe page)
```
  
  "scopedRegistries": [
    {
      "name": "npmjs",
      "url": "https://registry.npmjs.org/",
      "scopes": [
        "io.extendreality"
      ]
    }
  ],
  "dependencies": {
    "io.extendreality.malimbe": "9.6.5",
    ...
  }
}
```

2)  Download and import [UnityEventCallRedirector.unitypackage](https://github.com/handzlikchris/Unity.TransformSetterInterceptor/raw/master/_github/TransformSetterInterceptor.unitypackage)
3) Recompile
- If you see an error
`'A configuration lists 'TransformSetterCallRedirector' but the assembly file wasn't found in the search paths'`
That means `TransformSetterCallRedirector.Fody` is not compiled, you can go to `ModuleWeaver.cs` and make some non-relevant change (like adding a space) followed by saving to make sure DLL is actually compiled
4) Now changes to your scripts will trigger recompile which will in turn trigger IL Weaving to intercept your calls


### Filtering to specific transform
There's a simple script `TransformSetterInterceptorFilter` that'll allow you to further narrow down to exact transform that you're interested in via simple drag and drop. From there it'll be very easy to trace what's happening via logging, stack-trace or whatever custom approach you adopt.

### Runtime Performance
At build time calls to transform setters will be intercepted and directed to static interceptor class, that should not have significant impact on performance as it's simply adding static method call.

For fallback weaving IL instructions are added at every place that is redirected, additionally Debug.Log will be called, this call actually gets StackTrace which is rather quite costly.

### Configuring Interception
In the package, you'll find `TransformSetterCallInterceptor.cs` with intercept methods. You can adjust that as needed. There's also an assembly attribute specified `TransformSetterCallRedirector` where you can configure some more options.

- `eventInterceptorTypeName` - if different than `TransformSetterCallInterceptor`
- `replaceCallsFromNamespacesRegex` - it'll narrow down types to be looked at when searching for set transform calls
- `ExcludeFullMethodNameRegex` - full method name (including) type regex to be excluded. eg. `MyType.+::MyMehtod`

### Using asmdef / Fallback Interception
Separate assemblies will not have access to that `TransformSetterCallInterceptor` when that happens interception will still be performed but using IL as defined in `ModuleWeaver` - it'll use `Debug.Log` with 
- target transform that's being changed
- object that sets it
- value

Then you can further narrow it down from log console window and find exactly what you're after.

This is helpful if you like to weave external packages that you don't control (and don't wish to embed) - if you have control over assembly you can copy `TransformSetterCallInterceptor.cs` class with assembly attribute there.

### Configuring fallback interception
You can control few parameters of fallback IL weaving, it's done via XML attributes in `FodyWeavers.xml`
- `FallbackSampleNameFormat` - will control how you see entries in console, there are 3 tokens that can be used and **refer to object that invokes the event**:
    - `{0}` - target transform that's being changed
    - `{1}` - object that sets it
    - `{2}` - value
 - `FallbackReplaceCallsFromNamespacesRegex` - it'll narrow down types to be looked at when searching for set transform calls
 - `FallbackExcludeFullMethodNameRegex` - full method name (including) type regex to be excluded. eg. `MyType.+::MyMehtod`


### Configuring Malimbe
You can further configure Malimbe via `FodyWeavers.xml` file, you'll find the details in their repository.


## Known issues
- Right now tool works just for direct property setters - it doesn't work with `.Set` methods (although it'd be rather straight forward to add)
- You may see some errors that weaving assembly is not available when starting Unity, this is to do with compilation order and should not cause you issues, it'll be gone on next compilation. I've included weaver as source code with asmdef so it's easier to modify weaver. Ideally compiled DLL should just be included
- There are some DLLs included with the package and also included in Malimbe package, it's not easy to get them referenced without embedding Malimbe package. It shouldn't give you troubles but if something funny happens it'd be worth to look if that's not the cause. 