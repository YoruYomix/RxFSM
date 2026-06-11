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
        private const string ActionTableAttr = "RxFSM.ActionTableAttribute";
        private const string EnterAttr = "RxFSM.EnterStateAttribute";
        private const string ExitAttr = "RxFSM.ExitStateAttribute";
        private const string TickAttr = "RxFSM.TickStateAttribute";
        private const string EnterAsyncAttr = "RxFSM.EnterStateAsyncAttribute";
        private const string CancellationToken = "System.Threading.CancellationToken";

        private static readonly DiagnosticDescriptor MustBePartial = new DiagnosticDescriptor(
            "RXFSM001",
            "Action table class must be partial",
            "Class '{0}' is marked [ActionTable] but is not declared 'partial'; the source generator cannot emit its Register method.",
            "RxFSM",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InvalidSignature = new DiagnosticDescriptor(
            "RXFSM002",
            "Invalid action-table callback signature",
            "{0}",
            "RxFSM",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor StateMustBeEnum = new DiagnosticDescriptor(
            "RXFSM003",
            "ActionTable state must be an enum value",
            "Class '{0}': [ActionTable(...)] argument must be an enum value (e.g. [ActionTable(MyState.Foo)]).",
            "RxFSM",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetModel(ctx))
                .Where(static m => m is not null);

            context.RegisterSourceOutput(models, static (spc, model) => Emit(spc, model!));
        }

        private static Model? GetModel(GeneratorSyntaxContext ctx)
        {
            var decl = (ClassDeclarationSyntax)ctx.Node;
            if (ctx.SemanticModel.GetDeclaredSymbol(decl) is not INamedTypeSymbol symbol)
                return null;

            // Discover via [ActionTable(state)].
            AttributeData? tableAttr = null;
            foreach (var a in symbol.GetAttributes())
                if (a.AttributeClass?.ToDisplayString() == ActionTableAttr) { tableAttr = a; break; }
            if (tableAttr is null)
                return null;

            var isPartial = false;
            foreach (var m in decl.Modifiers)
                if (m.ValueText == "partial") { isPartial = true; break; }

            // The bound state determines TState (enum type) and the baked literal.
            if (tableAttr.ConstructorArguments.Length != 1)
                return new Model(null, symbol.Name, null, null, isPartial, decl.GetLocation(),
                    ImmutableArray<Binding>.Empty, ImmutableArray<DiagInfo>.Empty, stateNotEnum: true);

            var stateArg = tableAttr.ConstructorArguments[0];
            var tState = stateArg.Type;
            if (stateArg.Kind != TypedConstantKind.Enum || tState is not { TypeKind: TypeKind.Enum })
                return new Model(null, symbol.Name, null, null, isPartial, decl.GetLocation(),
                    ImmutableArray<Binding>.Empty, ImmutableArray<DiagInfo>.Empty, stateNotEnum: true);

            var tStateFq = tState.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var stateLiteral = $"({tStateFq}){stateArg.Value}";

            var bindings = ImmutableArray.CreateBuilder<Binding>();
            var diags = ImmutableArray.CreateBuilder<DiagInfo>();

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

                    // Match parameters by ROLE (type), independent of order:
                    //   TState        -> state    ('S')
                    //   struct/object -> trigger  ('T')   (object = any trigger, struct = typed filter)
                    //   CancellationToken -> ct   ('C')   (async only)
                    var roles = new StringBuilder();
                    var mode = TriggerMode.None;
                    string? triggerFq = null;
                    string? error = null;
                    int stateCount = 0, trigCount = 0, ctCount = 0;

                    foreach (var p in method.Parameters)
                    {
                        var pt = p.Type;
                        if (SymbolEqualityComparer.Default.Equals(pt, tState))
                        {
                            roles.Append('S'); stateCount++;
                        }
                        else if (pt.ToDisplayString() == CancellationToken)
                        {
                            roles.Append('C'); ctCount++;
                        }
                        else if (pt.SpecialType == SpecialType.System_Object)
                        {
                            roles.Append('T'); trigCount++;
                            mode = TriggerMode.Object;
                        }
                        else if (pt.IsValueType)
                        {
                            roles.Append('T'); trigCount++;
                            mode = TriggerMode.Typed;
                            triggerFq = pt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                        else
                        {
                            error = $"'{method.Name}': unsupported parameter type '{pt.ToDisplayString()}'. " +
                                    $"Callback parameters must be {tState.Name} (state), a trigger struct, object, or CancellationToken.";
                            break;
                        }
                    }

                    if (error == null)
                    {
                        if (stateCount > 1)
                            error = $"'{method.Name}': more than one state ({tState.Name}) parameter.";
                        else if (trigCount > 1)
                            error = $"'{method.Name}': more than one trigger parameter.";
                        else if (kind == Kind.EnterAsync && ctCount > 1)
                            error = $"'{method.Name}': more than one CancellationToken parameter.";
                        else if (kind != Kind.EnterAsync && ctCount > 0)
                            error = $"'{method.Name}': CancellationToken is only allowed on [EnterStateAsync].";
                    }

                    if (error != null)
                    {
                        var loc = method.Locations.Length > 0 ? method.Locations[0] : Location.None;
                        diags.Add(new DiagInfo(error, loc));
                        continue;
                    }

                    var op = 0; // TransitionOperation.Switch
                    if (kind == Kind.EnterAsync && attr.ConstructorArguments.Length > 0 &&
                        attr.ConstructorArguments[0].Value is int v)
                        op = v;

                    bindings.Add(new Binding(kind, method.Name, roles.ToString(), mode, triggerFq, op));
                }
            }

            string? ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } n
                ? n.ToDisplayString()
                : null;

            return new Model(
                ns,
                symbol.Name,
                tStateFq,
                stateLiteral,
                isPartial,
                decl.GetLocation(),
                bindings.ToImmutable(),
                diags.ToImmutable(),
                stateNotEnum: false);
        }

        private static void Emit(SourceProductionContext spc, Model model)
        {
            if (model.StateNotEnum)
            {
                spc.ReportDiagnostic(Diagnostic.Create(StateMustBeEnum, model.Location, model.ClassName));
                return;
            }

            foreach (var d in model.Diagnostics)
                spc.ReportDiagnostic(Diagnostic.Create(InvalidSignature, d.Location, d.Message));

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
                .Append(model.TStateFq).AppendLine("> fsm)");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).AppendLine("        FSM = fsm;");
            sb.Append(indent).Append("        var state = ").Append(model.StateLiteral).AppendLine(";");
            sb.Append(indent).AppendLine("        var cd = new global::RxFSM.FSMCompositeDisposable();");

            foreach (var b in model.Bindings)
                sb.Append(indent).Append("        ").AppendLine(BindingStatement(model, b));

            sb.Append(indent).AppendLine("        return cd;");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("}");

            if (model.Namespace is not null)
                sb.AppendLine("}");

            var hint = (model.Namespace is null ? "" : model.Namespace + ".") + model.ClassName + ".ActionTable.g.cs";
            spc.AddSource(hint, sb.ToString());
        }

        // Builds the user-method argument list in the declared (role) order.
        private static string CallArgs(string roles, string triggerArg)
        {
            var parts = new List<string>(roles.Length);
            foreach (var c in roles)
                parts.Add(c switch { 'S' => "__p", 'T' => triggerArg, 'C' => "__ct", _ => "" });
            return string.Join(", ", parts);
        }

        private static string BindingStatement(Model model, Binding b)
        {
            var m = b.MethodName;
            var s = model.TStateFq;
            const string CT = "global::System.Threading.CancellationToken";
            var op = $"(global::RxFSM.TransitionOperation){b.Operation}";

            switch (b.Kind)
            {
                case Kind.Enter:
                case Kind.Exit:
                {
                    var fn = b.Kind == Kind.Enter ? "EnterState" : "ExitState";
                    var args = CallArgs(b.Roles, "__t");
                    return b.Mode == TriggerMode.Typed
                        ? $"cd.Add(fsm.{fn}<{b.TriggerFq}>(state, ({s} __p, {b.TriggerFq} __t) => {m}({args})));"
                        : $"cd.Add(fsm.{fn}(state, ({s} __p, object __t) => {m}({args})));";
                }

                case Kind.Tick:
                {
                    if (b.Mode == TriggerMode.Typed)
                    {
                        var args = CallArgs(b.Roles, "__v");
                        return $"cd.Add(fsm.TickState(state, ({s} __p, object __t) => {{ if (__t is {b.TriggerFq} __v) {m}({args}); }}));";
                    }
                    var oargs = CallArgs(b.Roles, "__t");
                    return $"cd.Add(fsm.TickState(state, ({s} __p, object __t) => {m}({oargs})));";
                }

                case Kind.EnterAsync:
                {
                    var args = CallArgs(b.Roles, "__t");
                    return b.Mode switch
                    {
                        TriggerMode.Typed =>
                            $"cd.Add(fsm.EnterStateAsync<{b.TriggerFq}>(state, ({s} __p, {b.TriggerFq} __t, {CT} __ct) => {m}({args}).AsTask(), {op}));",
                        TriggerMode.Object =>
                            $"cd.Add(fsm.EnterStateAsync(state, ({s} __p, object __t, {CT} __ct) => {m}({args}).AsTask(), {op}));",
                        _ =>
                            $"cd.Add(fsm.EnterStateAsync(state, ({s} __p, {CT} __ct) => {m}({args}).AsTask(), {op}));",
                    };
                }

                default:
                    return string.Empty;
            }
        }

        private enum Kind { Enter, Exit, Tick, EnterAsync }
        private enum TriggerMode { None, Object, Typed }

        private readonly struct Binding
        {
            public readonly Kind Kind;
            public readonly string MethodName;
            public readonly string Roles;       // ordered chars: 'S' state, 'T' trigger, 'C' ct
            public readonly TriggerMode Mode;
            public readonly string? TriggerFq;
            public readonly int Operation;

            public Binding(Kind kind, string methodName, string roles, TriggerMode mode, string? triggerFq, int operation)
            {
                Kind = kind;
                MethodName = methodName;
                Roles = roles;
                Mode = mode;
                TriggerFq = triggerFq;
                Operation = operation;
            }
        }

        private readonly struct DiagInfo
        {
            public readonly string Message;
            public readonly Location Location;
            public DiagInfo(string message, Location location)
            {
                Message = message;
                Location = location;
            }
        }

        private sealed class Model
        {
            public readonly string? Namespace;
            public readonly string ClassName;
            public readonly string? TStateFq;
            public readonly string? StateLiteral;
            public readonly bool IsPartial;
            public readonly Location Location;
            public readonly ImmutableArray<Binding> Bindings;
            public readonly ImmutableArray<DiagInfo> Diagnostics;
            public readonly bool StateNotEnum;

            public Model(string? ns, string className, string? tStateFq, string? stateLiteral, bool isPartial,
                Location location, ImmutableArray<Binding> bindings, ImmutableArray<DiagInfo> diagnostics,
                bool stateNotEnum)
            {
                Namespace = ns;
                ClassName = className;
                TStateFq = tStateFq;
                StateLiteral = stateLiteral;
                IsPartial = isPartial;
                Location = location;
                Bindings = bindings;
                Diagnostics = diagnostics;
                StateNotEnum = stateNotEnum;
            }
        }
    }
}
