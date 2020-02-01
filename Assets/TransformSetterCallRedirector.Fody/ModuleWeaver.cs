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
        private string _fallbackSampleFormat;
        private MethodReference _transformSetPosition;
        private MethodReference _transformSetLocalPosition;
        private MethodReference _transformSetRotation;
        private MethodReference _transformSetScale;
        private MethodDefinition _interceptSetPosition;
        private MethodDefinition _interceptSetLocalPosition;
        private MethodDefinition _interceptSetRotation;
        private MethodDefinition _interceptSetScale;
        private MethodReference _debugLog;
        private MethodReference _getTransform;
        private TypeReference _transformType;
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

                if (string.IsNullOrEmpty(_fallbackSampleFormat))
                {
                    LogInfo($"Assembly :{ModuleDefinition.Assembly.Name} no XML {FallbackSampleNameFormatName} string specified, rewrite not performed.");
                    return;
                }
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
                var methodWithInstructionsToReplace = FindMethodsWithInstructionsCallingTransformSetter(arg.RedirectCallsFrom.Resolve());
                InterceptUnityEventInvokeCalls(methodWithInstructionsToReplace, arg.RedirectCallsTo, arg.RedirectingFor, arg.SetMethodParameterTypeReference);
            }
        }

        private void InterceptUnityEventInvokeCalls(List<MethodDefinitionInstructionToReplacePair> methodWithInstructionsToReplace, 
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
                    il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(instruction, il.Create(OpCodes.Ldstr, method.Name));
                    il.Replace(instruction, il.Create(OpCodes.Call, replaceWithInterceptorMethod));

                    LogDebug($"Redirected: {method.DeclaringType.Name}::{method.Name} via interceptor");
                }
                else if(!string.IsNullOrEmpty(_fallbackSampleFormat))
                {
                    var setMethodParameterVariable = new VariableDefinition(setMethodParameterType);
                    il.Body.Variables.Add(setMethodParameterVariable);
                    var transformVariable = new VariableDefinition(_transformType);
                    il.Body.Variables.Add(transformVariable);


                    il.InsertBefore(instruction, il.Create(OpCodes.Stloc, setMethodParameterVariable));
                    il.InsertBefore(instruction,  il.Create(OpCodes.Stloc, transformVariable));
                    
                    var logInfoInstructions = new List<Instruction>
                    {
                        il.Create(OpCodes.Ldstr, $"{rewritingFor}: {_fallbackSampleFormat}"),
                        il.Create(OpCodes.Ldloc, transformVariable),
                        il.Create(OpCodes.Ldarg_0),
                        il.Create(OpCodes.Callvirt, _unityObjectGetName),
                        il.Create(OpCodes.Ldloc, setMethodParameterVariable),
                        il.Create(OpCodes.Box, setMethodParameterType),
                        il.Create(OpCodes.Call, _stringFormat),
                        il.Create(OpCodes.Ldarg_0),
                        il.Create(OpCodes.Call, _debugLog),
                    };

                    logInfoInstructions.ForEach(i => il.InsertBefore(instruction, i));

                    il.InsertBefore(instruction, il.Create(OpCodes.Ldloc, transformVariable));
                    il.InsertBefore(instruction, il.Create(OpCodes.Ldloc, setMethodParameterVariable));
                    

                    LogDebug($"{ModuleDefinition.Assembly.Name} Redirected {rewritingFor}: {method.DeclaringType.Name}::{method.Name} via fallback inline IL");
                }
                method.Body.OptimizeMacros();

            }

            LogInfo($"{ModuleDefinition.Assembly.Name} Redirected {rewritingFor}: {methodWithInstructionsToReplace.Count} calls via {(replaceWithInterceptorMethod != null ? "interceptor" : "fallback inline IL")}");
        }

        private List<MethodDefinitionInstructionToReplacePair> FindMethodsWithInstructionsCallingTransformSetter(MethodDefinition callingToMethod)
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

            _getTransform = ImportPropertyGetter(typeof(GameObject), m => m.Name == nameof(GameObject.transform));

            _transformType = ModuleDefinition.ImportReference(typeof(Transform));
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
            _fallbackSampleFormat = Config.Attribute(FallbackSampleNameFormatName)?.Value;
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
