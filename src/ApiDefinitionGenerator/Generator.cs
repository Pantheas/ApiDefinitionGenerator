using System;
using System.Diagnostics;
using System.IO;
using Microsoft.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pantheas.ApiDefinitionGenerator
{
    [Generator]
    public class Generator :
        IIncrementalGenerator
    {
        private const string GenerateOnBuildOnlyOptionsKey = "api_definition_generate_on_build_only";
        private const string TargetNamespaceOptionsKey = "api_definition_generator_target_namespace";
        private const string DestinationPathOptionsKey = "api_definition_generator_destination_path";


        public void Initialize(
            IncrementalGeneratorInitializationContext context)
        {
            var namespaceValueProvider = context.AnalyzerConfigOptionsProvider.Select(
                static (options, _) =>
                {
                    bool generateOnBuildOnly = true;
                    
                    if (options.GlobalOptions.TryGetValue(
                        GenerateOnBuildOnlyOptionsKey,
                        out string? generateOnBuildOnlyValue))
                    {
                        Boolean.TryParse(
                            generateOnBuildOnlyValue,
                            out generateOnBuildOnly);
                    }
                    
                    options.GlobalOptions.TryGetValue(
                        TargetNamespaceOptionsKey,
                        out string? targetNamespace);

                    options.GlobalOptions.TryGetValue(
                        DestinationPathOptionsKey,
                        out string? destinationPath);

                    return new GeneratorOptions(
                        generateOnBuildOnly,
                        targetNamespace,
                        destinationPath);
                });

            var controllerDefinitions = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    Scanner.ApiControllerAttributeName,
                    predicate: static (node, token) =>
                    {
                        if (node is not ClassDeclarationSyntax @class)
                        {
                            return false;
                        }

                        bool isPublic = @class.Modifiers.Any(
                            modifier => modifier.IsKind(SyntaxKind.PublicKeyword));

                        Debug.WriteLine(
                            $"{@class.Identifier.ValueText} is public: {isPublic}");

                        return isPublic;
                    },
                    GetControllerRoutes)
                .Where(definition => definition is not null);


            var valuesProvider = controllerDefinitions.Combine(
                namespaceValueProvider);

            context.RegisterImplementationSourceOutput(
                valuesProvider,
                static (context, values) =>
                {
                    if (values.Left is null ||
                        values.Right is null)
                    {
                        return;
                    }

                    var options = values.Right;
                    var definition = values.Left;

                    if (options.GenerateOnBuildOnly)
                    {
                        string? executingAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
                        if (executingAssemblyName == "csc")
                        {
                            return;
                        }
                    }
                        

                    Debug.WriteLine(
                        $"Generating output for {definition.Name} controller");

                    var source = new SourceStringBuilder();

                    string targetNamespace = options.TargetNamespace ??
                                             definition.Namespace;

                    SetUpSingleApi(
                        definition,
                        targetNamespace,
                        source);

                    string fileName = $"{definition.Name}.g.cs";

                    if (string.IsNullOrWhiteSpace(
                            options.DestinationPath))
                    {
                        context.AddSource(
                            fileName,
                            source.ToString());
                    }
                    else
                    {
                        string filePath = Path.Combine(
                            options.DestinationPath!,
                            fileName);
                        
                        using var x = new StreamWriter(
                            filePath);
                        
                        x.Write(source.ToString());
                    }
                });
        }

        private static void SetUpSingleApi(
            ControllerDefinition route,
            string name,
            SourceStringBuilder source)
        {
            source.AppendLine($"// generated by {typeof(Generator).FullName}");

            source.AppendLine("using System.Net.Http;");

            source.AppendLine($"namespace {name};");

            source.AppendLine($"public class {route.Name}");
            source.AppendOpenCurlyBracketLine();

            foreach (var action in route.Actions)
            {
                source.AppendLine($"public class {action.Name}");
                source.AppendOpenCurlyBracketLine();

                // properties
                source.AppendLine(@"public string Route { get; }");
                source.AppendLine(@"public HttpMethod Method { get; }");

                var mappings = action.Mapping
                    .Except([action.Body])
                    .ToArray();

                // constructor
                if (mappings
                    .Any())
                {
                    // source.AppendLine($"public {action.Name}(");

                    string parameters = string.Join(
                        ",",
                        action.Mapping.Select(
                            parameter => $"{parameter.Parameter.FullTypeName} {parameter.Key}"));

                    source.AppendLine($"public {action.Name}({parameters})");
                    source.AppendOpenCurlyBracketLine();

                    source.AppendLine($"Route = $\"{route.BaseRoute}/{action.Route}\";");
                }
                else
                {
                    source.AppendLine($"public {action.Name}()");
                    source.AppendOpenCurlyBracketLine();

                    source.AppendLine($"Route = \"{route.BaseRoute}\";");
                }

                source.AppendLine($"Method = new HttpMethod(\"{action.Method.Method}\");");

                source.AppendCloseCurlyBracketLine();
                source.AppendCloseCurlyBracketLine();
            }

            source.AppendCloseCurlyBracketLine();
        }

        private static ControllerDefinition? GetControllerRoutes(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            var namedTypeSymbol = Unsafe.As<INamedTypeSymbol>(
                context.TargetSymbol);

            if (namedTypeSymbol is null)
            {
                return null;
            }

            return Scanner.ToControllerDefinition(
                namedTypeSymbol,
                cancellationToken);
        }
    }
}