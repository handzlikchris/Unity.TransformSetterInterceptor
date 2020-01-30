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

        private const string DefaultFallbackSampleNameFormat = "____{0} ({1}) <{2}>____";
        private const string DefaultFallbackReplaceCallsFromNamespacesRegex = ".*";
        private const string DefaultFallbackExcludeFullMethodNameRegex = "";
        
        private TypeDefinition interceptorType;
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
                        $"{FullAttributeName} which is needed for processing. Falling back to inline IL rewrite");
                LoadXmlSetup();
            }
            else
            {
                LoadAttributeSetup();
                LoadXmlSetup();
            }

            FindReferences();

            //TODO: allow to control which ones to intercept via attribute flag
            var methodCallToInterceptor = new Dictionary<MethodReference, MethodDefinition>()
            {
                [_transformSetLocalPosition] = _interceptSetLocalPosition,
                [_transformSetPosition] = _interceptSetPosition,
                [_transformSetRotation] = _interceptSetRotation,
                [_transformSetScale] = _interceptSetScale,
            };

            foreach (var kv in methodCallToInterceptor)
            {
                var methodWithInstructionsToReplace = FindMethodsWithInstructionsCallingTransformSetter(kv.Key.Resolve());
                InterceptUnityEventInvokeCalls(methodWithInstructionsToReplace, kv.Value);
            }
        }

        private void InterceptUnityEventInvokeCalls(List<MethodDefinitionInstructionToReplacePair> methodWithInstructionsToReplace, 
            MethodDefinition replaceWithInterceptorMethod)
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
                //else
                //{ 
                //var beginSampleInstructions = new List<Instruction>
                //{
                //    il.Create(OpCodes.Ldstr, _fallbackSampleFormat),
                //    il.Create(OpCodes.Ldarg_0),
                //    il.Create(OpCodes.Callvirt, _unityObjectGetName),
                //    il.Create(OpCodes.Ldarg_0),
                //    il.Create(OpCodes.Callvirt, _objectGetType),
                //    il.Create(OpCodes.Callvirt, _memberInfoGetName),
                //    il.Create(OpCodes.Ldstr, method.Name),
                //    il.Create(OpCodes.Call, _stringFormat),
                //    il.Create(OpCodes.Ldarg_0),
                //    il.Create(OpCodes.Call, _beginSample),
                //};

                //beginSampleInstructions.ForEach(i => il.InsertBefore(instruction, i));

                //il.InsertAfter(instruction, il.Create(OpCodes.Call, _endSample));

                //LogDebug($"{ModuleDefinition.Assembly.Name} Redirected: {method.DeclaringType.Name}::{method.Name} via fallback inline IL");
                //}
                method.Body.OptimizeMacros();

            }

            LogInfo($"{ModuleDefinition.Assembly.Name} Redirected: {methodWithInstructionsToReplace.Count} calls via {(replaceWithInterceptorMethod != null ? "interceptor" : "fallback inline IL")}");
        }

        private List<MethodDefinitionInstructionToReplacePair> FindMethodsWithInstructionsCallingTransformSetter(MethodDefinition callingToMethod)
        {
            var methodWithInstructionsToReplace = new List<MethodDefinitionInstructionToReplacePair>();

            foreach (var t in ModuleDefinition.Types
                .Where(t => Regex.IsMatch(t.Namespace, _replaceCallsFromNamespacesRegex))
                .Where(t => t != interceptorType))
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

            interceptorType = ModuleDefinition.Types.First(t => t.Name == interceptorTypeName);
            _interceptSetPosition = interceptorType.Methods.First(m => m.Name == "InterceptSetPosition");
            _interceptSetLocalPosition = interceptorType.Methods.First(m => m.Name == "InterceptSetLocalPosition");
            _interceptSetRotation = interceptorType.Methods.First(m => m.Name == "InterceptSetRotation");
            _interceptSetScale = interceptorType.Methods.First(m => m.Name == "InterceptSetScale");
            
            _replaceCallsFromNamespacesRegex = _callRedirectorAttribute.ConstructorArguments[1].Value?.ToString();
            _excludeFullMethodNameRegex = _callRedirectorAttribute.Properties.Single(p => p.Name == nameof(TransformSetterCallRedirectorAttribute.ExcludeFullMethodNameRegex)).Argument.Value?.ToString();
        }

        private void LoadXmlSetup()
        {
            _fallbackSampleFormat = Config.Attribute(FallbackSampleNameFormatName)?.Value ?? DefaultFallbackSampleNameFormat;
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
