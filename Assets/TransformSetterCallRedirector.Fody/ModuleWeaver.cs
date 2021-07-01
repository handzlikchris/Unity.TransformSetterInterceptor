using Mono.Cecil.Rocks;
using UnityEngine;

namespace TransformSetterCallRedirector.Fody
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using global::Fody;
    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using System.Reflection;
    using Assets.TransformSetterCallRedirector.Attribute;

    public sealed class ModuleWeaver : BaseModuleWeaver
    {
        private static readonly string FullAttributeName = typeof(TransformSetterCallRedirectorAttribute).FullName;

        private const string FallbackSampleNameFormatName = "FallbackSampleNameFormat";
        private const string FallbackReplaceCallsFromNamespacesRegexName = "FallbackReplaceCallsFromNamespacesRegex";
        private const string FallbackExcludeFullMethodNameRegexName = "FallbackExcludeFullMethodNameRegex";

        private const string DefaultFallbackReplaceCallsFromNamespacesRegex = ".*";
        private const string DefaultFallbackExcludeFullMethodNameRegex = "";

        private TypeDefinition _interceptorType;
        private string _replaceCallsFromNamespacesRegex;
        private string _excludeFullMethodNameRegex;
        private MethodReference _unityObjectGetName;
        private MethodReference _objectGetType;
        private MethodReference _memberInfoGetName;
        private MethodReference _stringFormat;
        private CustomAttribute _callRedirectorAttribute;
        private MethodReference _transformSetPosition;
        private MethodReference _transformSetLocalPosition;
        private MethodReference _transformSetRotation;
        private MethodReference _transformSetScale;
        private MethodDefinition _interceptSetPosition;
        private MethodDefinition _interceptSetLocalPosition;
        private MethodDefinition _interceptSetRotation;
        private MethodDefinition _interceptSetScale;
        private MethodReference _debugLog;
        private MethodReference _sendMessage;
        private MethodReference _getTransform;
        private TypeReference _transformType;
        private TypeReference _objectArrayArgsType;
        private TypeReference _objectType;
        private TypeReference _vector3Type;
        private TypeReference _quaternionType;

        public override bool ShouldCleanReference => true;

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            yield return "UnityEngine";
        }


        public override void Execute()
        {
            if (!TryFindRequiredAssemblyAttribute())
            {
                LogInfo($"Assembly :{ModuleDefinition.Assembly.Name} does not specify attribute " +
                        $"{FullAttributeName} which is needed for processing");
                LoadXmlSetup();
            }
            else
            {
                LoadAttributeSetup();
            }

            FindReferences();

            var redirectMethodsSetup = new List<RedirectionMethodArg>()
            {
                new RedirectionMethodArg(_transformSetLocalPosition, _interceptSetLocalPosition, "localPosition", _vector3Type),
                new RedirectionMethodArg(_transformSetPosition, _interceptSetPosition, "position", _vector3Type),
                new RedirectionMethodArg(_transformSetRotation, _interceptSetRotation, "rotation", _quaternionType),
                new RedirectionMethodArg(_transformSetScale, _interceptSetScale, "scale", _vector3Type),
            };

            foreach (var arg in redirectMethodsSetup)
            {
                var methodWithInstructionsToReplace = FindMethodsWithInstructionsCalling(arg.RedirectCallsFrom.Resolve());
                ReplaceMethodCalls(methodWithInstructionsToReplace, arg.RedirectCallsTo, arg.RedirectingFor, arg.SetMethodParameterTypeReference);
            }
        }

        private void ReplaceMethodCalls(List<MethodDefinitionInstructionToReplacePair> methodWithInstructionsToReplace,
            MethodDefinition replaceWithInterceptorMethod, string rewritingFor, TypeReference setMethodParameterType)
        {
            foreach (var methodWithInstructionToReplace in methodWithInstructionsToReplace)
            {
                var method = methodWithInstructionToReplace.MethodDefinition;
                var instruction = methodWithInstructionToReplace.Instruction;
                var il = method.Body.GetILProcessor();

                method.Body.SimplifyMacros();
                if (replaceWithInterceptorMethod != null)
                {
                    if (method.IsStatic)
                        il.InsertBefore(instruction, il.Create(OpCodes.Ldstr, "Static:" + method.FullName));
                    else
                        il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));


                    il.InsertBefore(instruction, il.Create(OpCodes.Ldstr, method.Name));
                    il.Replace(instruction, il.Create(OpCodes.Call, replaceWithInterceptorMethod));

                    LogDebug($"Redirected: {method.DeclaringType.Name}::{method.Name} via interceptor");
                }
                else
                { 
                    var setMethodParameterVariable = new VariableDefinition(setMethodParameterType);
                    il.Body.Variables.Add(setMethodParameterVariable);
                    var transformVariable = new VariableDefinition(_transformType);
                    il.Body.Variables.Add(transformVariable);
                    var arrayArgsVariable = new VariableDefinition(_objectArrayArgsType);
                    il.Body.Variables.Add(arrayArgsVariable);

                    var callSendMessageGlobalHandlerInstructions = new Instruction[]
                    {
                        il.Create(OpCodes.Stloc, setMethodParameterVariable),
                        il.Create(OpCodes.Stloc, transformVariable),

                        //create Args array
                        il.Create(OpCodes.Ldc_I4_3),
                        il.Create(OpCodes.Newarr, _objectType),

                        //first index 'this' for calee
                        il.Create(OpCodes.Dup),
                        il.Create(OpCodes.Ldc_I4_0),
                        il.Create(OpCodes.Ldarg_0),
                        il.Create(OpCodes.Stelem_Ref),

                        //second index, calling method name
                        il.Create(OpCodes.Dup),
                        il.Create(OpCodes.Ldc_I4_1),
                        il.Create(OpCodes.Ldstr, $"{(method.IsStatic ? "Static:" : "")}{method.FullName}"),
                        il.Create(OpCodes.Stelem_Ref),

                        //throd index, value
                        il.Create(OpCodes.Dup),
                        il.Create(OpCodes.Ldc_I4_2),
                        il.Create(OpCodes.Ldloc, setMethodParameterVariable),
                        setMethodParameterType.IsValueType ? il.Create(OpCodes.Box, setMethodParameterType) : il.Create(OpCodes.Nop),
                        il.Create(OpCodes.Stelem_Ref),

                        il.Create(OpCodes.Stloc, arrayArgsVariable),

                        //call Component.SendMessage()
                        il.Create(OpCodes.Ldloc, transformVariable),
                        il.Create(OpCodes.Ldstr, "HandleGlobalInterceptorCallback"),
                        il.Create(OpCodes.Ldloc, arrayArgsVariable),
                        il.Create(OpCodes.Ldc_I4, (int)SendMessageOptions.DontRequireReceiver),
                        il.Create(OpCodes.Callvirt, _sendMessage),

                        //re-add args on stack so original method can call it
                        il.Create(OpCodes.Ldloc, transformVariable),
                        il.Create(OpCodes.Ldloc, setMethodParameterVariable)
                    };
                    
                    foreach (var i in callSendMessageGlobalHandlerInstructions)
                        il.InsertBefore(instruction, i);

                    LogDebug($"{ModuleDefinition.Assembly.Name} Redirected {rewritingFor}: {method.DeclaringType.Name}::{method.Name} via fallback inline IL");
                }
                method.Body.OptimizeMacros();

            }

            LogInfo($"{ModuleDefinition.Assembly.Name} Redirected {rewritingFor}: {methodWithInstructionsToReplace.Count} calls via {(replaceWithInterceptorMethod != null ? "interceptor" : "fallback inline IL")}");
        }

        private List<MethodDefinitionInstructionToReplacePair> FindMethodsWithInstructionsCalling(MethodDefinition callingToMethod)
        {
            var methodWithInstructionsToReplace = new List<MethodDefinitionInstructionToReplacePair>();

            foreach (var t in ModuleDefinition.Types
                .Where(t => string.IsNullOrEmpty(_replaceCallsFromNamespacesRegex) || Regex.IsMatch(t.Namespace, _replaceCallsFromNamespacesRegex))
                .Where(t => t != _interceptorType))
            {
                foreach (var method in t.Methods)
                {
                    if (!string.IsNullOrEmpty(_excludeFullMethodNameRegex)
                        && Regex.IsMatch($"{method.DeclaringType.FullName}::{method.FullName}", _excludeFullMethodNameRegex))
                    {
                        Console.WriteLine(
                            $"Skipping rewrite for excluded method: '{method.DeclaringType.FullName}::{method.FullName}'");
                        continue;
                    }

                    if (method.Body != null)
                    {
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if ((instruction.Operand as MethodReference)?.Resolve() == callingToMethod)
                            {
                                methodWithInstructionsToReplace.Add(
                                    new MethodDefinitionInstructionToReplacePair(method, instruction));
                            }
                        }
                    }
                }
            }

            return methodWithInstructionsToReplace;
        }

        private void FindReferences()
        {
            _unityObjectGetName = ImportPropertyGetter(typeof(UnityEngine.Object), p => p.Name == nameof(UnityEngine.Object.name));
            _objectGetType = ImportMethod(typeof(object), m => m.Name == nameof(object.GetType));
            _memberInfoGetName = ImportPropertyGetter(typeof(MemberInfo), m => m.Name == nameof(MemberInfo.Name));
            _stringFormat = ImportMethod(typeof(string),
                m => m.FullName == "System.String System.String::Format(System.String,System.Object,System.Object,System.Object)");
            _debugLog = ImportMethod(typeof(Debug), m => m.Name == nameof(Debug.Log) && m.Parameters.Count == 2);
            _sendMessage = ImportMethod(typeof(Component), m => m.Name == nameof(Component.SendMessage) && m.Parameters.Count == 3);

            _getTransform = ImportPropertyGetter(typeof(GameObject), m => m.Name == nameof(GameObject.transform));

            _transformType = ModuleDefinition.ImportReference(typeof(Transform));
            _objectArrayArgsType = ModuleDefinition.ImportReference(typeof(object[]));
            _objectType = ModuleDefinition.ImportReference(typeof(object));
            _vector3Type = ModuleDefinition.ImportReference(typeof(Vector3));
            _quaternionType = ModuleDefinition.ImportReference(typeof(Quaternion));

            _transformSetPosition = ImportPropertySetter(typeof(Transform), m => m.Name == nameof(Transform.position));
            _transformSetLocalPosition = ImportPropertySetter(typeof(Transform), m => m.Name == nameof(Transform.localPosition));
            _transformSetRotation = ImportPropertySetter(typeof(Transform), m => m.Name == nameof(Transform.rotation));
            _transformSetScale = ImportPropertySetter(typeof(Transform), m => m.Name == nameof(Transform.localScale));
        }

        private bool TryFindRequiredAssemblyAttribute()
        {
            _callRedirectorAttribute = ModuleDefinition.Assembly.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.FullName == FullAttributeName);

            return _callRedirectorAttribute != null;
        }

        private void LoadAttributeSetup()
        {
            var interceptorTypeName = _callRedirectorAttribute.ConstructorArguments[0].Value.ToString();

            _interceptorType = ModuleDefinition.Types.First(t => t.Name == interceptorTypeName);
            _interceptSetPosition = _interceptorType.Methods.First(m => m.Name == "InterceptSetPosition");
            _interceptSetLocalPosition = _interceptorType.Methods.First(m => m.Name == "InterceptSetLocalPosition");
            _interceptSetRotation = _interceptorType.Methods.First(m => m.Name == "InterceptSetRotation");
            _interceptSetScale = _interceptorType.Methods.First(m => m.Name == "InterceptSetScale");

            _replaceCallsFromNamespacesRegex = _callRedirectorAttribute.ConstructorArguments[1].Value?.ToString();
            _excludeFullMethodNameRegex = _callRedirectorAttribute.Properties.Single(p => p.Name == nameof(TransformSetterCallRedirectorAttribute.ExcludeFullMethodNameRegex)).Argument.Value?.ToString();
        }

        private void LoadXmlSetup()
        {
            _replaceCallsFromNamespacesRegex = Config.Attribute(FallbackReplaceCallsFromNamespacesRegexName)?.Value ?? DefaultFallbackReplaceCallsFromNamespacesRegex;
            _excludeFullMethodNameRegex = Config.Attribute(FallbackExcludeFullMethodNameRegexName)?.Value ?? DefaultFallbackExcludeFullMethodNameRegex;
        }

        private MethodReference ImportMethod(Type type, Func<MethodDefinition, bool> methodPredicate)
        {
            return ModuleDefinition.ImportReference(ModuleDefinition.ImportReference(type).Resolve().Methods.First(methodPredicate));
        }

        private MethodReference ImportPropertyGetter(Type type, Func<PropertyDefinition, bool> propertyPredicate)
        {
            var prop = ModuleDefinition.ImportReference(type).Resolve().Properties.First(propertyPredicate);
            return ModuleDefinition.ImportReference(prop.GetMethod);
        }

        private MethodReference ImportPropertySetter(Type type, Func<PropertyDefinition, bool> propertyPredicate)
        {
            var prop = ModuleDefinition.ImportReference(type).Resolve().Properties.First(propertyPredicate);
            return ModuleDefinition.ImportReference(prop.SetMethod);
        }

        private class RedirectionMethodArg
        {
            public MethodReference RedirectCallsFrom { get; }
            public MethodDefinition RedirectCallsTo { get; }
            public string RedirectingFor { get; }
            public TypeReference SetMethodParameterTypeReference { get; }

            public RedirectionMethodArg(MethodReference redirectCallsFrom, MethodDefinition redirectCallsTo, string redirectingFor,
                TypeReference setMethodParameterTypeReference)
            {
                RedirectCallsFrom = redirectCallsFrom;
                RedirectCallsTo = redirectCallsTo;
                RedirectingFor = redirectingFor;
                SetMethodParameterTypeReference = setMethodParameterTypeReference;
            }
        }

        private class MethodDefinitionInstructionToReplacePair
        {
            public MethodDefinition MethodDefinition { get; }
            public Instruction Instruction { get; }

            public MethodDefinitionInstructionToReplacePair(MethodDefinition methodDefinition, Instruction instruction)
            {
                MethodDefinition = methodDefinition;
                Instruction = instruction;
            }
        }
    }
}
