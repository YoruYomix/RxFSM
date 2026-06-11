using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RxFSM.SourceGenerator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ActionTableGenerator : IIncrementalGenerator
    {
        private const string InterfaceMetadataName = "RxFSM.IActionTable`1";
        private const string EnterAttr = "RxFSM.EnterStateAttribute";
        private const string ExitAttr = "RxFSM.ExitStateAttribute";
        private const string TickAttr = "RxFSM.TickStateAttribute";
        private const string EnterAsyncAttr = "RxFSM.EnterStateAsyncAttribute";

        private static readonly DiagnosticDescriptor MustBePartial = new DiagnosticDescriptor(
            "RXFSM001",
            "Action table class must be partial",
            "Class '{0}' implements IActionTable<TState> but is not declared 'partial'; the source generator cannot emit its Register method.",
            "RxFSM",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is ClassDeclarationSyntax c && c.BaseList != null,
                    transform: static (ctx, _) => GetModel(ctx))
                .Where(static m => m is not null);

            context.RegisterSourceOutput(models, static (spc, model) => Emit(spc, model!));
        }

        private static Model? GetModel(GeneratorSyntaxContext ctx)
        {
            var decl = (ClassDeclarationSyntax)ctx.Node;
            if (ctx.SemanticModel.GetDeclaredSymbol(decl) is not INamedTypeSymbol symbol)
                return null;

            // Find IActionTable<TState> among implemented interfaces.
            INamedTypeSymbol? iface = null;
            foreach (var i in symbol.AllInterfaces)
            {
                if (i.OriginalDefinition.ToDisplayString() == "RxFSM.IActionTable<TState>" ||
                    i.ConstructedFrom.MetadataName == "IActionTable`1" &&
                    i.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "RxFSM")
                {
                    iface = i;
                    break;
                }
            }

            if (iface is null || iface.TypeArguments.Length != 1)
                return null;

            var isPartial = false;
            foreach (var m in decl.Modifiers)
                if (m.ValueText == "partial") { isPartial = true; break; }

            var tStateFq = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var bindings = ImmutableArray.CreateBuilder<Binding>();

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IMethodSymbol method)
                    continue;

                foreach (var attr in method.GetAttributes())
                {
                    var name = attr.AttributeClass?.ToDisplayString();
                    Kind kind;
                    switch (name)
                    {
                        case EnterAttr: kind = Kind.Enter; break;
                        case ExitAttr: kind = Kind.Exit; break;
                        case TickAttr: kind = Kind.Tick; break;
                        case EnterAsyncAttr: kind = Kind.EnterAsync; break;
                        default: continue;
                    }

                    var triggerIndex = 1; // params: (TState, trigger[, ct])
                    string? triggerFq = null;
                    var isObjectTrigger = true;
                    if (method.Parameters.Length > triggerIndex)
                    {
                        var trig = method.Parameters[triggerIndex].Type;
                        isObjectTrigger = trig.SpecialType == SpecialType.System_Object;
                        triggerFq = trig.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }

                    var op = 0; // TransitionOperation.Switch
                    if (kind == Kind.EnterAsync && attr.ConstructorArguments.Length > 0 &&
                        attr.ConstructorArguments[0].Value is int v)
                        op = v;

                    bindings.Add(new Binding(kind, method.Name, isObjectTrigger, triggerFq, op));
                }
            }

            string? ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } n
                ? n.ToDisplayString()
                : null;

            return new Model(
                ns,
                symbol.Name,
                tStateFq,
                isPartial,
                decl.GetLocation(),
                bindings.ToImmutable());
        }

        private static void Emit(SourceProductionContext spc, Model model)
        {
            if (!model.IsPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(MustBePartial, model.Location, model.ClassName));
                return;
            }

            var hasAsync = false;
            foreach (var b in model.Bindings)
                if (b.Kind == Kind.EnterAsync) { hasAsync = true; break; }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable");
            // AsTask() is an extension method on UniTask (Cysharp.Threading.Tasks).
            if (hasAsync)
                sb.AppendLine("using Cysharp.Threading.Tasks;");
            sb.AppendLine();

            var indent = "    ";
            if (model.Namespace is not null)
            {
                sb.Append("namespace ").Append(model.Namespace).AppendLine();
                sb.AppendLine("{");
            }
            else
            {
                indent = "";
            }

            sb.Append(indent).Append("partial class ").Append(model.ClassName)
                .Append(" : global::RxFSM.IActionTable<").Append(model.TStateFq).AppendLine(">");
            sb.Append(indent).AppendLine("{");

            sb.Append(indent).Append("    public global::RxFSM.FSM<").Append(model.TStateFq)
                .AppendLine("> FSM { get; private set; }");
            sb.AppendLine();

            sb.Append(indent).Append("    public global::System.IDisposable Register(global::RxFSM.FSM<")
                .Append(model.TStateFq).Append("> fsm, ").Append(model.TStateFq).AppendLine(" state)");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).AppendLine("        FSM = fsm;");
            sb.Append(indent).AppendLine("        var cd = new global::RxFSM.FSMCompositeDisposable();");

            foreach (var b in model.Bindings)
            {
                sb.Append(indent).Append("        ").AppendLine(BindingStatement(model, b));
            }

            sb.Append(indent).AppendLine("        return cd;");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("}");

            if (model.Namespace is not null)
                sb.AppendLine("}");

            var hint = (model.Namespace is null ? "" : model.Namespace + ".") + model.ClassName + ".ActionTable.g.cs";
            spc.AddSource(hint, sb.ToString());
        }

        private static string BindingStatement(Model model, Binding b)
        {
            var m = b.MethodName;
            switch (b.Kind)
            {
                case Kind.Enter:
                    return b.IsObjectTrigger
                        ? $"cd.Add(fsm.EnterState(state, {m}));"
                        : $"cd.Add(fsm.EnterState<{b.TriggerFq}>(state, {m}));";

                case Kind.Exit:
                    return b.IsObjectTrigger
                        ? $"cd.Add(fsm.ExitState(state, {m}));"
                        : $"cd.Add(fsm.ExitState<{b.TriggerFq}>(state, {m}));";

                case Kind.Tick:
                    return b.IsObjectTrigger
                        ? $"cd.Add(fsm.TickState(state, {m}));"
                        : $"cd.Add(fsm.TickState(state, ({model.TStateFq} __p, object __t) => {{ if (__t is {b.TriggerFq} __v) {m}(__p, __v); }}));";

                case Kind.EnterAsync:
                    return $"cd.Add(fsm.EnterStateAsync<{b.TriggerFq}>(state, ({model.TStateFq} __p, {b.TriggerFq} __t, global::System.Threading.CancellationToken __ct) => {m}(__p, __t, __ct).AsTask(), (global::RxFSM.TransitionOperation){b.Operation}));";

                default:
                    return string.Empty;
            }
        }

        private enum Kind { Enter, Exit, Tick, EnterAsync }

        private readonly struct Binding
        {
            public readonly Kind Kind;
            public readonly string MethodName;
            public readonly bool IsObjectTrigger;
            public readonly string? TriggerFq;
            public readonly int Operation;

            public Binding(Kind kind, string methodName, bool isObjectTrigger, string? triggerFq, int operation)
            {
                Kind = kind;
                MethodName = methodName;
                IsObjectTrigger = isObjectTrigger;
                TriggerFq = triggerFq;
                Operation = operation;
            }
        }

        private sealed class Model
        {
            public readonly string? Namespace;
            public readonly string ClassName;
            public readonly string TStateFq;
            public readonly bool IsPartial;
            public readonly Location Location;
            public readonly ImmutableArray<Binding> Bindings;

            public Model(string? ns, string className, string tStateFq, bool isPartial,
                Location location, ImmutableArray<Binding> bindings)
            {
                Namespace = ns;
                ClassName = className;
                TStateFq = tStateFq;
                IsPartial = isPartial;
                Location = location;
                Bindings = bindings;
            }
        }
    }
}
