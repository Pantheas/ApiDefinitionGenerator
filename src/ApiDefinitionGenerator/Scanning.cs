using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;

// I do not know why, but VS preview needs this
namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}

namespace Pantheas.ApiDefinitionGenerator
{
    public record GeneratorOptions(
        bool GenerateOnBuildOnly,
        string? TargetNamespace,
        string? DestinationPath);
    
    public record Parameter(
        string FullTypeName);

    public record ParameterMapping(
        string Key,
        Parameter Parameter);

    public record ActionRoute(
        string Name,
        HttpMethod Method,
        string Route,
        ParameterMapping[] Mapping,
        ParameterMapping? Body);

    public record ControllerDefinition(
        string Name,
        string Namespace,
        string BaseRoute,
        ActionRoute[] Actions);
    

    public static class Scanner
    {

        public const string ApiControllerAttributeName = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
        public const string RouteAttributeName = "Microsoft.AspNetCore.Mvc.RouteAttribute";
        public const string HttpMethodAttributeName = "Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute";
        
        private const string ControllerSuffix = "Controller";
        private const string DefinitionSuffix = "Definition";

        public static ControllerDefinition ToControllerDefinition(
            INamedTypeSymbol classSymbol,
            CancellationToken cancellationToken)
        {
            var controllerClassName = classSymbol.Name.EndsWith(
                ControllerSuffix)
                ? classSymbol.Name.Substring(
                    0,
                    classSymbol.Name.Length - ControllerSuffix.Length)
                : classSymbol.Name;
            
            var actionMethods = ScanForActionMethods(
                    classSymbol,
                    cancellationToken)
                .ToArray();

            // Extract the route from the HttpActionAttribute
            var attribute = FindAttribute(
                classSymbol, 
                attribute => attribute.ToString() == RouteAttributeName);
            
            var route = attribute?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? string.Empty;
            
            
            return new ControllerDefinition(
                $"{controllerClassName}{DefinitionSuffix}",
                classSymbol.ContainingAssembly.Name,
                route,
                actionMethods);
        }

        private static IEnumerable<ActionRoute> ScanForActionMethods(
            INamedTypeSymbol classSymbol,
            CancellationToken cancellationToken)
        {
            foreach (var member in classSymbol
                         .GetMembers()
                         .Where(member => member.Kind == SymbolKind.Method))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (member is not IMethodSymbol methodSymbol ||
                    methodSymbol.MethodKind == MethodKind.Constructor ||
                    methodSymbol.MethodKind == MethodKind.StaticConstructor ||
                    methodSymbol.MethodKind == MethodKind.Destructor)
                {
                    continue;
                }
                
                var name = methodSymbol.Name;

                // Extract the route from the HttpActionAttribute
                var methodAttribute  = FindAttribute(
                    methodSymbol,
                    attribute => attribute.BaseType?.ToString() == HttpMethodAttributeName);
                
                var route = methodAttribute?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? string.Empty;
                
                var routeAttribute = FindAttribute(
                    methodSymbol,
                    attribute => attribute.BaseType?.ToString() == RouteAttributeName);

                if (routeAttribute != null)
                {
                    string routePrefix = routeAttribute.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? string.Empty;
                    route = $"{routePrefix}/{route}";
                }
                
                var method = methodAttribute?.AttributeClass?.Name switch
                {
                    "HttpGetAttribute" => HttpMethod.Get,
                    "HttpPutAttribute" => HttpMethod.Put,
                    "HttpPostAttribute" => HttpMethod.Post,
                    "HttpDeleteAttribute" => HttpMethod.Delete,
                    _ => throw new InvalidOperationException(
                        $"Unknown attribute {methodAttribute?.AttributeClass?.Name}")
                };

                var parameters = methodSymbol.Parameters
                    .Select(parameter =>
                        new ParameterMapping(
                            parameter.Name,
                            new Parameter(
                                parameter.Type.ToString())))
                    .ToArray();
                
                var bodyParameter = methodSymbol.Parameters
                    .Where(parameter => !IsPrimitive(parameter.Type))
                    .Select(parameter => 
                        new ParameterMapping(
                            parameter.Name,
                            new Parameter(
                                parameter.Type.ToString())))
                    .FirstOrDefault();

                yield return new ActionRoute(
                    name,
                    method,
                    route,
                    parameters,
                    bodyParameter);
            }
        }

        private static bool IsPrimitive(ITypeSymbol typeSymbol)
        {
            switch(typeSymbol.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Char:
                case SpecialType.System_String:
                    return true;
            }

            return typeSymbol.TypeKind switch
            {
                TypeKind.Enum => true,
                _ => false
            };
        }

        private static AttributeData? FindAttribute(
            ISymbol symbol,
            Func<INamedTypeSymbol, bool> selectAttribute)
            => symbol
                .GetAttributes()
                .LastOrDefault(a => a?.AttributeClass != null && selectAttribute(a.AttributeClass));
    }
}
