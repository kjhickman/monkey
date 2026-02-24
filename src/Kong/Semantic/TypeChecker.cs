using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;

namespace Kong.Semantic;

public class TypeCheckResult
{
    public Dictionary<IExpression, TypeSymbol> ExpressionTypes { get; } = [];
    public Dictionary<CallExpression, string> ResolvedStaticMethodPaths { get; } = [];
    public Dictionary<MemberAccessExpression, string> ResolvedStaticValuePaths { get; } = [];
    public Dictionary<CallExpression, string> ResolvedInstanceMethodMembers { get; } = [];
    public Dictionary<MemberAccessExpression, string> ResolvedInstanceValueMembers { get; } = [];
    public Dictionary<NewExpression, string> ResolvedConstructorTypePaths { get; } = [];
    public Dictionary<LetStatement, TypeSymbol> VariableTypes { get; } = [];
    public Dictionary<FunctionLiteral, FunctionTypeSymbol> FunctionTypes { get; } = [];
    public Dictionary<FunctionDeclaration, FunctionTypeSymbol> DeclaredFunctionTypes { get; } = [];
    public Dictionary<string, ClassDefinitionSymbol> ClassDefinitions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InterfaceDefinitionSymbol> InterfaceDefinitions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, EnumDefinitionSymbol> EnumDefinitions { get; } = new(StringComparer.Ordinal);
    public Dictionary<CallExpression, EnumVariantConstruction> ResolvedEnumVariantConstructions { get; } = [];
    public Dictionary<MatchExpression, MatchResolution> ResolvedMatches { get; } = [];
    public DiagnosticBag Diagnostics { get; } = new();
}

public sealed record EnumVariantConstruction(
    string EnumName,
    string VariantName,
    int Tag,
    int MaxPayloadArity,
    IReadOnlyList<TypeSymbol> PayloadTypes);

public sealed record MatchArmResolution(
    string VariantName,
    int Tag,
    IReadOnlyList<TypeSymbol> PayloadTypes,
    IReadOnlyList<NameSymbol> BindingSymbols);

public sealed record MatchResolution(
    string EnumName,
    IReadOnlyList<MatchArmResolution> Arms);

public class TypeChecker
{
    private sealed class LoopContext
    {
        public LoopExpression? Expression { get; init; }
        public TypeSymbol? BreakType { get; set; }
    }

    private sealed class ClassDefinition
    {
        public required string Name { get; init; }
        public bool IsPublic { get; init; }
        public IReadOnlyList<string> TypeParameters { get; init; } = [];
        public Dictionary<string, TypeSymbol> Fields { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, UserMethodSignature> Methods { get; } = new(StringComparer.Ordinal);
        public UserMethodSignature? Constructor { get; set; }
        public HashSet<string> ImplementedInterfaces { get; } = new(StringComparer.Ordinal);
    }

    private sealed class InterfaceDefinition
    {
        public required string Name { get; init; }
        public bool IsPublic { get; init; }
        public IReadOnlyList<string> TypeParameters { get; init; } = [];
        public Dictionary<string, InterfaceMethodSignatureSymbol> Methods { get; } = new(StringComparer.Ordinal);
    }

    private readonly TypeCheckResult _result = new();
    private readonly Dictionary<NameSymbol, TypeSymbol> _symbolTypes = [];
    private readonly Stack<TypeSymbol> _currentFunctionReturnTypes = [];
    private readonly Stack<LoopContext> _loopContexts = [];
    private int _loopDepth;
    private NameResolution _names = null!;
    private IReadOnlyDictionary<string, FunctionTypeSymbol> _externalFunctionTypes =
        new Dictionary<string, FunctionTypeSymbol>(StringComparer.Ordinal);
    private readonly ControlFlowAnalyzer _controlFlowAnalyzer = new();
    private readonly List<EnumDeclaration> _externalEnumDeclarations = [];
    private readonly Dictionary<string, EnumTypeSymbol> _enumTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (EnumDefinitionSymbol Enum, EnumVariantDefinition Variant)> _variantSymbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeSymbol> _namedTypeSymbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassDefinition> _classDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InterfaceDefinition> _interfaceDefinitions = new(StringComparer.Ordinal);
    private readonly Stack<ClassTypeSymbol> _currentSelfTypes = [];

    public TypeCheckResult Check(CompilationUnit unit, NameResolution names)
    {
        return Check(
            unit,
            names,
            new Dictionary<string, FunctionTypeSymbol>(StringComparer.Ordinal),
            []);
    }

    public TypeCheckResult Check(
        CompilationUnit unit,
        NameResolution names,
        IReadOnlyDictionary<string, FunctionTypeSymbol> externalFunctionTypes,
        IReadOnlyList<EnumDeclaration> externalEnumDeclarations)
    {
        _names = names;
        _externalFunctionTypes = externalFunctionTypes;
        _externalEnumDeclarations.Clear();
        _enumTypes.Clear();
        _variantSymbols.Clear();
        _namedTypeSymbols.Clear();
        _classDefinitions.Clear();
        _interfaceDefinitions.Clear();
        _currentSelfTypes.Clear();
        _loopContexts.Clear();
        _result.EnumDefinitions.Clear();
        _result.ClassDefinitions.Clear();
        _result.InterfaceDefinitions.Clear();
        _result.ResolvedEnumVariantConstructions.Clear();
        _result.ResolvedMatches.Clear();
        foreach (var declaration in externalEnumDeclarations)
        {
            _externalEnumDeclarations.Add(declaration);
        }

        _result.Diagnostics.AddRange(names.Diagnostics);
        PredeclareEnumDeclarations(unit);
        PredeclareClassAndInterfaceDeclarations(unit);
        PredeclareImplBlocks(unit);
        foreach (var pair in _classDefinitions)
        {
            _result.ClassDefinitions[pair.Key] = new ClassDefinitionSymbol(
                pair.Value.Name,
                pair.Value.IsPublic,
                pair.Value.TypeParameters,
                new Dictionary<string, TypeSymbol>(pair.Value.Fields, StringComparer.Ordinal),
                new Dictionary<string, UserMethodSignature>(pair.Value.Methods, StringComparer.Ordinal),
                pair.Value.Constructor,
                pair.Value.ImplementedInterfaces.ToList());
        }

        foreach (var pair in _interfaceDefinitions)
        {
            _result.InterfaceDefinitions[pair.Key] = new InterfaceDefinitionSymbol(
                pair.Value.Name,
                pair.Value.IsPublic,
                pair.Value.TypeParameters,
                new Dictionary<string, InterfaceMethodSignatureSymbol>(pair.Value.Methods, StringComparer.Ordinal));
        }
        PredeclareFunctionDeclarations(unit);

        foreach (var statement in unit.Statements)
        {
            CheckStatement(statement);
        }

        return _result;
    }

    private void PredeclareEnumDeclarations(CompilationUnit unit)
    {
        foreach (var external in _externalEnumDeclarations)
        {
            var externalTypeParameters = external.TypeParameters
                .Select(p => (TypeSymbol)new GenericParameterTypeSymbol(p.Value))
                .ToList();
            _enumTypes[external.Name.Value] = new EnumTypeSymbol(external.Name.Value, externalTypeParameters);
            _namedTypeSymbols[external.Name.Value] = _enumTypes[external.Name.Value];
        }

        var allDeclarations = _externalEnumDeclarations.Concat(unit.Statements.OfType<EnumDeclaration>()).ToList();
        foreach (var declaration in allDeclarations)
        {
            if (_enumTypes.ContainsKey(declaration.Name.Value))
            {
                if (_externalEnumDeclarations.Contains(declaration))
                {
                    continue;
                }

                _result.Diagnostics.Report(declaration.Name.Span,
                    $"duplicate enum declaration '{declaration.Name.Value}'",
                    "T128");
                continue;
            }

            var typeParameters = declaration.TypeParameters
                .Select(p => (TypeSymbol)new GenericParameterTypeSymbol(p.Value))
                .ToList();
            _enumTypes[declaration.Name.Value] = new EnumTypeSymbol(declaration.Name.Value, typeParameters);
            _namedTypeSymbols[declaration.Name.Value] = _enumTypes[declaration.Name.Value];
        }

        foreach (var declaration in allDeclarations)
        {
            if (!_enumTypes.TryGetValue(declaration.Name.Value, out _))
            {
                continue;
            }

            var variants = new List<EnumVariantDefinition>(declaration.Variants.Count);
            var seenVariants = new HashSet<string>(StringComparer.Ordinal);
            var genericTypeBindings = new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal);
            foreach (var typeParameter in declaration.TypeParameters)
            {
                genericTypeBindings[typeParameter.Value] = new GenericParameterTypeSymbol(typeParameter.Value);
            }

            for (var i = 0; i < declaration.Variants.Count; i++)
            {
                var variant = declaration.Variants[i];
                if (!seenVariants.Add(variant.Name.Value))
                {
                    _result.Diagnostics.Report(variant.Name.Span,
                        $"duplicate enum variant '{variant.Name.Value}' in enum '{declaration.Name.Value}'",
                        "T128");
                    continue;
                }

                var payloadTypes = new List<TypeSymbol>(variant.PayloadTypes.Count);
                foreach (var payloadTypeNode in variant.PayloadTypes)
                {
                    payloadTypes.Add(TypeAnnotationBinder.Bind(payloadTypeNode, _result.Diagnostics, genericTypeBindings) ?? TypeSymbols.Error);
                }

                variants.Add(new EnumVariantDefinition(variant.Name.Value, payloadTypes, i));
            }

            var enumDefinition = new EnumDefinitionSymbol(
                declaration.Name.Value,
                declaration.TypeParameters.Select(tp => tp.Value).ToList(),
                variants);
            _result.EnumDefinitions[declaration.Name.Value] = enumDefinition;
            RegisterVariantSymbols(enumDefinition);
        }
    }

    private void RegisterVariantSymbols(EnumDefinitionSymbol enumDefinition)
    {
        foreach (var variant in enumDefinition.Variants)
        {
            _variantSymbols[variant.Name] = (enumDefinition, variant);
        }
    }

    private void PredeclareClassAndInterfaceDeclarations(CompilationUnit unit)
    {
        foreach (var declaration in unit.Statements.OfType<ClassDeclaration>())
        {
            if (_namedTypeSymbols.ContainsKey(declaration.Name.Value))
            {
                _result.Diagnostics.Report(declaration.Name.Span,
                    $"duplicate type declaration '{declaration.Name.Value}'",
                    "T132");
                continue;
            }

            var classTypeParameters = declaration.TypeParameters
                .Select(p => (TypeSymbol)new GenericParameterTypeSymbol(p.Value))
                .ToList();
            var classType = new ClassTypeSymbol(declaration.Name.Value, classTypeParameters);
            _namedTypeSymbols[declaration.Name.Value] = classType;

            var classDefinition = new ClassDefinition
            {
                Name = declaration.Name.Value,
                IsPublic = declaration.IsPublic,
                TypeParameters = declaration.TypeParameters.Select(p => p.Value).ToList(),
            };
            _classDefinitions[declaration.Name.Value] = classDefinition;
        }

        foreach (var declaration in unit.Statements.OfType<InterfaceDeclaration>())
        {
            if (_namedTypeSymbols.ContainsKey(declaration.Name.Value))
            {
                _result.Diagnostics.Report(declaration.Name.Span,
                    $"duplicate type declaration '{declaration.Name.Value}'",
                    "T132");
                continue;
            }

            var interfaceTypeParameters = declaration.TypeParameters
                .Select(p => (TypeSymbol)new GenericParameterTypeSymbol(p.Value))
                .ToList();
            var interfaceType = new InterfaceTypeSymbol(declaration.Name.Value, interfaceTypeParameters);
            _namedTypeSymbols[declaration.Name.Value] = interfaceType;

            var interfaceDefinition = new InterfaceDefinition
            {
                Name = declaration.Name.Value,
                IsPublic = declaration.IsPublic,
                TypeParameters = declaration.TypeParameters.Select(p => p.Value).ToList(),
            };
            _interfaceDefinitions[declaration.Name.Value] = interfaceDefinition;
        }

        foreach (var declaration in unit.Statements.OfType<ClassDeclaration>())
        {
            if (!_classDefinitions.TryGetValue(declaration.Name.Value, out var classDefinition))
            {
                continue;
            }

            foreach (var field in declaration.Fields)
            {
                if (classDefinition.Fields.ContainsKey(field.Name.Value))
                {
                    _result.Diagnostics.Report(field.Name.Span,
                        $"duplicate field '{field.Name.Value}' in class '{declaration.Name.Value}'",
                        "T132");
                    continue;
                }

                var genericTypeBindings = new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal);
                foreach (var typeParameter in declaration.TypeParameters)
                {
                    genericTypeBindings[typeParameter.Value] = new GenericParameterTypeSymbol(typeParameter.Value);
                }

                var fieldType = TypeAnnotationBinder.Bind(field.TypeAnnotation, _result.Diagnostics, genericTypeBindings) ?? TypeSymbols.Error;
                classDefinition.Fields[field.Name.Value] = fieldType;
            }
        }

        foreach (var declaration in unit.Statements.OfType<InterfaceDeclaration>())
        {
            if (!_interfaceDefinitions.TryGetValue(declaration.Name.Value, out var interfaceDefinition))
            {
                continue;
            }

            foreach (var method in declaration.Methods)
            {
                var genericTypeBindings = new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal);
                foreach (var typeParameter in declaration.TypeParameters)
                {
                    genericTypeBindings[typeParameter.Value] = new GenericParameterTypeSymbol(typeParameter.Value);
                }

                var signature = BindInterfaceMethodSignature(method, declaration.Name.Value, genericTypeBindings);
                if (signature == null)
                {
                    continue;
                }

                if (!interfaceDefinition.Methods.TryAdd(signature.Name, signature))
                {
                    _result.Diagnostics.Report(method.Name.Span,
                        $"duplicate method '{signature.Name}' in interface '{declaration.Name.Value}'",
                        "T132");
                }
            }
        }
    }

    private void PredeclareImplBlocks(CompilationUnit unit)
    {
        foreach (var implBlock in unit.Statements.OfType<ImplBlock>())
        {
            if (!_classDefinitions.TryGetValue(implBlock.TypeName.Value, out var classDefinition))
            {
                continue;
            }

            if (implBlock.Constructor != null)
            {
                var constructorParameters = new List<FunctionParameter>
                {
                    new()
                    {
                        Token = new Token(TokenType.Self, "self", implBlock.Constructor.Span),
                        Name = "self",
                        Span = implBlock.Constructor.Span,
                    },
                };
                constructorParameters.AddRange(implBlock.Constructor.Parameters);

                var classTypeBindings = new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal);
                foreach (var typeParameter in classDefinition.TypeParameters)
                {
                    classTypeBindings[typeParameter] = new GenericParameterTypeSymbol(typeParameter);
                }

                var constructorSignature = BindUserMethodSignatureCore(
                    new Identifier
                    {
                        Token = implBlock.Constructor.Token,
                        Value = "init",
                        Span = implBlock.Constructor.Span,
                    },
                    [],
                    constructorParameters,
                    null,
                    implBlock.TypeName.Value,
                    implBlock.Constructor.Span,
                    isPublic: false,
                    namedTypes: classTypeBindings);
                classDefinition.Constructor = constructorSignature;
            }

            foreach (var method in implBlock.Methods)
            {
                var classTypeBindings = new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal);
                foreach (var typeParameter in classDefinition.TypeParameters)
                {
                    classTypeBindings[typeParameter] = new GenericParameterTypeSymbol(typeParameter);
                }

                var signature = BindUserMethodSignature(method, implBlock.TypeName.Value, classTypeBindings);
                if (signature == null)
                {
                    continue;
                }

                if (!classDefinition.Methods.TryAdd(signature.Name, signature))
                {
                    _result.Diagnostics.Report(method.Name.Span,
                        $"duplicate method '{signature.Name}' in impl for class '{implBlock.TypeName.Value}'",
                        "T132");
                }
            }
        }

        foreach (var interfaceImplBlock in unit.Statements.OfType<InterfaceImplBlock>())
        {
            if (!_classDefinitions.TryGetValue(interfaceImplBlock.TypeName.Value, out var classDefinition) ||
                !_interfaceDefinitions.TryGetValue(interfaceImplBlock.InterfaceName.Value, out var interfaceDefinition))
            {
                continue;
            }

            if (interfaceDefinition.TypeParameters.Count > 0 && classDefinition.TypeParameters.Count > 0 &&
                interfaceDefinition.TypeParameters.Count != classDefinition.TypeParameters.Count)
            {
                _result.Diagnostics.Report(interfaceImplBlock.Span,
                    $"impl '{interfaceDefinition.Name} for {classDefinition.Name}' has incompatible generic arity",
                    "T133");
            }

            var implementedMethods = new Dictionary<string, UserMethodSignature>(StringComparer.Ordinal);
            foreach (var method in interfaceImplBlock.Methods)
            {
                if (method.IsPublic)
                {
                    _result.Diagnostics.Report(method.Span,
                        $"methods in 'impl {interfaceImplBlock.InterfaceName.Value} for {interfaceImplBlock.TypeName.Value}' are implicitly public; remove the 'public' modifier",
                        "T137");
                }

                var classTypeBindings = new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal);
                foreach (var typeParameter in classDefinition.TypeParameters)
                {
                    classTypeBindings[typeParameter] = new GenericParameterTypeSymbol(typeParameter);
                }

                var signature = BindUserMethodSignature(method, interfaceImplBlock.TypeName.Value, classTypeBindings);
                if (signature == null)
                {
                    continue;
                }

                if (!implementedMethods.TryAdd(signature.Name, signature))
                {
                    _result.Diagnostics.Report(method.Name.Span,
                        $"duplicate method '{signature.Name}' in impl '{interfaceImplBlock.InterfaceName.Value} for {interfaceImplBlock.TypeName.Value}'",
                        "T132");
                    continue;
                }

                if (!interfaceDefinition.Methods.TryGetValue(signature.Name, out var interfaceMethod))
                {
                    _result.Diagnostics.Report(method.Name.Span,
                        $"method '{signature.Name}' is not declared in interface '{interfaceDefinition.Name}'",
                        "T133");
                    continue;
                }

                var implTypeSubstitution = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
                var pairedTypeParameterCount = Math.Min(interfaceDefinition.TypeParameters.Count, classDefinition.TypeParameters.Count);
                for (var typeParameterIndex = 0; typeParameterIndex < pairedTypeParameterCount; typeParameterIndex++)
                {
                    implTypeSubstitution[interfaceDefinition.TypeParameters[typeParameterIndex]] =
                        new GenericParameterTypeSymbol(classDefinition.TypeParameters[typeParameterIndex]);
                }

                var expectedInterfaceParameterTypes = interfaceMethod.ParameterTypes
                    .Select(p => SubstituteGenericParameters(p, implTypeSubstitution))
                    .ToList();
                var expectedInterfaceReturnType = SubstituteGenericParameters(interfaceMethod.ReturnType, implTypeSubstitution);

                if (signature.ParameterTypes.Count != expectedInterfaceParameterTypes.Count ||
                    !TypeEquals(signature.ReturnType, expectedInterfaceReturnType))
                {
                    _result.Diagnostics.Report(method.Span,
                        $"method '{signature.Name}' in impl '{interfaceDefinition.Name} for {classDefinition.Name}' does not match interface signature",
                        "T133");
                    continue;
                }

                for (var i = 0; i < signature.ParameterTypes.Count; i++)
                {
                    if (!TypeEquals(signature.ParameterTypes[i], expectedInterfaceParameterTypes[i]))
                    {
                        _result.Diagnostics.Report(method.Span,
                            $"method '{signature.Name}' in impl '{interfaceDefinition.Name} for {classDefinition.Name}' does not match interface signature",
                            "T133");
                        break;
                    }
                }

                classDefinition.Methods[signature.Name] = signature;
            }

            foreach (var interfaceMethod in interfaceDefinition.Methods.Values)
            {
                if (!implementedMethods.ContainsKey(interfaceMethod.Name))
                {
                    _result.Diagnostics.Report(interfaceImplBlock.Span,
                        $"impl '{interfaceDefinition.Name} for {classDefinition.Name}' is missing method '{interfaceMethod.Name}'",
                        "T133");
                }
            }

            classDefinition.ImplementedInterfaces.Add(interfaceDefinition.Name);
        }
    }

    private InterfaceMethodSignatureSymbol? BindInterfaceMethodSignature(
        InterfaceMethodSignature method,
        string interfaceName,
        IReadOnlyDictionary<string, TypeSymbol>? namedTypes = null)
    {
        var signature = BindUserMethodSignatureCore(method.Name, method.TypeParameters, method.Parameters, method.ReturnTypeAnnotation, interfaceName, method.Span, namedTypes: namedTypes);
        if (signature == null)
        {
            return null;
        }

        return new InterfaceMethodSignatureSymbol(signature.Name, signature.TypeParameters, signature.ParameterTypes, signature.ReturnType);
    }

    private UserMethodSignature? BindUserMethodSignature(MethodDeclaration method, string className, IReadOnlyDictionary<string, TypeSymbol>? namedTypes = null)
    {
        return BindUserMethodSignatureCore(method.Name, method.TypeParameters, method.Parameters, method.ReturnTypeAnnotation, className, method.Span, method.IsPublic, namedTypes);
    }

    private UserMethodSignature? BindUserMethodSignatureCore(
        Identifier methodName,
        IReadOnlyList<Identifier> typeParameters,
        IReadOnlyList<FunctionParameter> parameters,
        ITypeNode? returnTypeAnnotation,
        string receiverClassName,
        Span methodSpan,
        bool isPublic = false,
        IReadOnlyDictionary<string, TypeSymbol>? namedTypes = null)
    {
        if (parameters.Count == 0 || !string.Equals(parameters[0].Name, "self", StringComparison.Ordinal))
        {
            _result.Diagnostics.Report(methodName.Span,
                $"method '{methodName.Value}' must declare 'self' as the first parameter",
                "T134");
            return null;
        }

        if (parameters[0].TypeAnnotation != null)
        {
            _result.Diagnostics.Report(parameters[0].Span,
                "'self' parameter must not declare a type annotation",
                "T134");
        }

        var genericTypeBindings = namedTypes == null
            ? new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal)
            : new Dictionary<string, TypeSymbol>(namedTypes, StringComparer.Ordinal);

        var methodTypeParameters = new List<string>(typeParameters.Count);
        foreach (var typeParameter in typeParameters)
        {
            methodTypeParameters.Add(typeParameter.Value);
            genericTypeBindings[typeParameter.Value] = new GenericParameterTypeSymbol(typeParameter.Value);
        }

        var parameterTypes = new List<TypeSymbol>();
        for (var i = 1; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.TypeAnnotation == null)
            {
                _result.Diagnostics.Report(parameter.Span,
                    $"missing type annotation for parameter '{parameter.Name}'",
                    "T105");
                parameterTypes.Add(TypeSymbols.Error);
                continue;
            }

            parameterTypes.Add(TypeAnnotationBinder.Bind(parameter.TypeAnnotation, _result.Diagnostics, genericTypeBindings) ?? TypeSymbols.Error);
        }

        var returnType = returnTypeAnnotation == null
            ? TypeSymbols.Void
            : TypeAnnotationBinder.Bind(returnTypeAnnotation, _result.Diagnostics, genericTypeBindings) ?? TypeSymbols.Error;

        return new UserMethodSignature(methodName.Value, methodTypeParameters, parameterTypes, returnType, isPublic);
    }

    private void PredeclareFunctionDeclarations(CompilationUnit unit)
    {
        foreach (var statement in unit.Statements)
        {
            if (statement is not FunctionDeclaration declaration)
            {
                continue;
            }

            var genericTypeBindings = new Dictionary<string, TypeSymbol>(_namedTypeSymbols, StringComparer.Ordinal);
            foreach (var typeParameter in declaration.TypeParameters)
            {
                genericTypeBindings[typeParameter.Value] = new GenericParameterTypeSymbol(typeParameter.Value);
            }

            var parameterTypes = new List<TypeSymbol>(declaration.Parameters.Count);
            foreach (var parameter in declaration.Parameters)
            {
                if (parameter.TypeAnnotation == null)
                {
                    _result.Diagnostics.Report(parameter.Span,
                        $"missing type annotation for parameter '{parameter.Name}'",
                        "T105");
                    parameterTypes.Add(TypeSymbols.Error);
                    continue;
                }

                parameterTypes.Add(TypeAnnotationBinder.Bind(parameter.TypeAnnotation, _result.Diagnostics, genericTypeBindings) ?? TypeSymbols.Error);
            }

            var returnType = declaration.ReturnTypeAnnotation == null
                ? TypeSymbols.Void
                : TypeAnnotationBinder.Bind(declaration.ReturnTypeAnnotation, _result.Diagnostics, genericTypeBindings) ?? TypeSymbols.Error;

            var functionType = new FunctionTypeSymbol(parameterTypes, returnType);
            _result.DeclaredFunctionTypes[declaration] = functionType;

            if (_names.IdentifierSymbols.TryGetValue(declaration.Name, out var symbol))
            {
                _symbolTypes[symbol] = functionType;
            }
        }
    }

    private void CheckStatement(IStatement statement)
    {
        switch (statement)
        {
            case LetStatement letStatement:
                CheckLetStatement(letStatement);
                break;
            case AssignmentStatement assignmentStatement:
                CheckAssignmentStatement(assignmentStatement);
                break;
            case IndexAssignmentStatement indexAssignmentStatement:
                CheckIndexAssignmentStatement(indexAssignmentStatement);
                break;
            case MemberAssignmentStatement memberAssignmentStatement:
                CheckMemberAssignmentStatement(memberAssignmentStatement);
                break;
            case ForInStatement forInStatement:
                CheckForInStatement(forInStatement);
                break;
            case WhileStatement whileStatement:
                CheckWhileStatement(whileStatement);
                break;
            case BreakStatement breakStatement:
                CheckBreakStatement(breakStatement);
                break;
            case ContinueStatement continueStatement:
                CheckContinueStatement(continueStatement);
                break;
            case FunctionDeclaration functionDeclaration:
                CheckFunctionDeclaration(functionDeclaration);
                break;
            case EnumDeclaration:
            case ClassDeclaration:
            case InterfaceDeclaration:
                break;
            case ImplBlock implBlock:
                CheckImplBlock(implBlock);
                break;
            case InterfaceImplBlock interfaceImplBlock:
                CheckInterfaceImplBlock(interfaceImplBlock);
                break;
            case ReturnStatement returnStatement:
                CheckReturnStatement(returnStatement);
                break;
            case ImportStatement:
            case NamespaceStatement:
                break;
            case ExpressionStatement expressionStatement:
                if (expressionStatement.Expression != null)
                {
                    CheckExpression(expressionStatement.Expression);
                }
                break;
            case BlockStatement blockStatement:
                CheckBlockStatement(blockStatement);
                break;
        }
    }

    private void CheckFunctionDeclaration(FunctionDeclaration declaration)
    {
        var functionLiteral = declaration.ToFunctionLiteral();
        if (!_result.DeclaredFunctionTypes.TryGetValue(declaration, out var functionType))
        {
            var symbol = _names.IdentifierSymbols.TryGetValue(declaration.Name, out var resolvedSymbol)
                ? resolvedSymbol
                : (NameSymbol?)null;
            functionType = CheckFunctionLiteral(functionLiteral, symbol);
            _result.DeclaredFunctionTypes[declaration] = functionType;
            return;
        }

        _result.FunctionTypes[functionLiteral] = functionType;
        CheckFunctionBody(functionLiteral, functionType, functionType.ParameterTypes);
    }

    private void CheckLetStatement(LetStatement statement)
    {
        TypeSymbol declaredType;
        if (statement.TypeAnnotation == null)
        {
            var diagnosticsBeforeInitializer = _result.Diagnostics.Count;
            var initializerType = statement.Value != null
                ? CheckExpression(statement.Value)
                : TypeSymbols.Error;
            var initializerHadErrors = _result.Diagnostics.Count > diagnosticsBeforeInitializer;
            declaredType = InferLetType(statement, initializerType, initializerHadErrors);
        }
        else
        {
            declaredType = TypeAnnotationBinder.Bind(statement.TypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;

            var initializerType = statement.Value == null
                ? TypeSymbols.Error
                : declaredType is EnumTypeSymbol expectedEnumType && statement.Value is CallExpression enumCall
                    ? CheckEnumVariantConstructorCall(enumCall, expectedEnumType, null)
                    : CheckExpression(statement.Value);

            if (!IsAssignable(initializerType, declaredType))
            {
                _result.Diagnostics.Report(statement.Span,
                    $"cannot assign expression of type '{initializerType}' to variable '{statement.Name.Value}' of type '{declaredType}'",
                    "T102");
            }
        }

        _result.VariableTypes[statement] = declaredType;

        if (_names.IdentifierSymbols.TryGetValue(statement.Name, out var symbol))
        {
            _symbolTypes[symbol] = declaredType;
        }
    }

    private TypeSymbol InferLetType(LetStatement statement, TypeSymbol initializerType, bool initializerHadErrors)
    {
        if (initializerHadErrors || initializerType == TypeSymbols.Error)
        {
            return TypeSymbols.Error;
        }

        if (initializerType == TypeSymbols.Null)
        {
            _result.Diagnostics.Report(
                statement.Name.Span,
                $"cannot infer type for '{statement.Name.Value}' from null initializer",
                "T119");
            return TypeSymbols.Error;
        }

        if (initializerType is ArrayTypeSymbol { ElementType: ErrorTypeSymbol })
        {
            _result.Diagnostics.Report(
                statement.Name.Span,
                $"cannot infer element type for '{statement.Name.Value}' from empty array initializer",
                "T120");
            return TypeSymbols.Error;
        }

        return initializerType;
    }

    private void CheckAssignmentStatement(AssignmentStatement statement)
    {
        if (!_names.IdentifierSymbols.TryGetValue(statement.Name, out var symbol))
        {
            return;
        }

        if (!_names.MutableSymbols.Contains(symbol))
        {
            _result.Diagnostics.Report(statement.Span,
                $"cannot assign to immutable variable '{statement.Name.Value}'; declare with 'var' to allow reassignment",
                "T123");
        }

        if (!_symbolTypes.TryGetValue(symbol, out var targetType))
        {
            _result.Diagnostics.Report(statement.Span,
                $"cannot resolve type for assignment target '{statement.Name.Value}'",
                "T124");
            return;
        }

        var valueType = statement.Value is CallExpression enumCall && targetType is EnumTypeSymbol expectedEnumType
            ? CheckEnumVariantConstructorCall(enumCall, expectedEnumType, null)
            : CheckExpression(statement.Value);
        if (!IsAssignable(valueType, targetType))
        {
            _result.Diagnostics.Report(statement.Value.Span,
                $"cannot assign expression of type '{valueType}' to variable '{statement.Name.Value}' of type '{targetType}'",
                "T102");
        }
    }

    private void CheckForInStatement(ForInStatement statement)
    {
        var iterableType = CheckExpression(statement.Iterable);
        var elementType = iterableType is ArrayTypeSymbol arrayType
            ? arrayType.ElementType
            : TypeSymbols.Error;

        if (iterableType is not ArrayTypeSymbol && iterableType != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(statement.Iterable.Span,
                $"for-in iterable must be an array, got '{iterableType}'",
                "T125");
        }

        if (_names.IdentifierSymbols.TryGetValue(statement.Iterator, out var symbol))
        {
            _symbolTypes[symbol] = elementType;
        }

        _loopDepth++;
        _loopContexts.Push(new LoopContext());
        CheckBlockStatement(statement.Body);
        _loopContexts.Pop();
        _loopDepth--;
    }

    private void CheckWhileStatement(WhileStatement statement)
    {
        var conditionType = CheckExpression(statement.Condition);
        if (conditionType != TypeSymbols.Bool && conditionType != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(statement.Condition.Span,
                $"while condition must be 'bool', got '{conditionType}'",
                "T109");
        }

        _loopDepth++;
        _loopContexts.Push(new LoopContext());
        CheckBlockStatement(statement.Body);
        _loopContexts.Pop();
        _loopDepth--;
    }

    private void CheckIndexAssignmentStatement(IndexAssignmentStatement statement)
    {
        var left = CheckExpression(statement.Target.Left);
        var index = CheckExpression(statement.Target.Index);
        var valueType = CheckExpression(statement.Value);

        if (index != TypeSymbols.Int && index != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(statement.Target.Index.Span,
                $"array index must be 'int', got '{index}'",
                "T115");
        }

        if (left is not ArrayTypeSymbol arrayType)
        {
            if (left != TypeSymbols.Error)
            {
                _result.Diagnostics.Report(statement.Target.Left.Span,
                    $"cannot index-assign expression of type '{left}'",
                    "T116");
            }

            return;
        }

        if (!IsAssignable(valueType, arrayType.ElementType))
        {
            _result.Diagnostics.Report(statement.Value.Span,
                $"cannot assign expression of type '{valueType}' to array element of type '{arrayType.ElementType}'",
                "T102");
        }
    }

    private void CheckMemberAssignmentStatement(MemberAssignmentStatement statement)
    {
        var receiverType = CheckExpression(statement.Target.Object);
        var valueType = CheckExpression(statement.Value);
        if (receiverType is not ClassTypeSymbol classType)
        {
            if (receiverType != TypeSymbols.Error)
            {
                _result.Diagnostics.Report(statement.Target.Span,
                    $"member assignment requires class receiver type, got '{receiverType}'",
                    "T135");
            }

            return;
        }

        if (!_classDefinitions.TryGetValue(classType.ClassName, out var classDefinition) ||
            !classDefinition.Fields.TryGetValue(statement.Target.Member, out var fieldType))
        {
            _result.Diagnostics.Report(statement.Target.Span,
                $"class '{classType.ClassName}' has no field '{statement.Target.Member}'",
                "T135");
            return;
        }

        var classSubstitution = BuildTypeParameterSubstitution(classDefinition.TypeParameters, classType.TypeArguments);
        fieldType = SubstituteGenericParameters(fieldType, classSubstitution);

        if (!IsAssignable(valueType, fieldType))
        {
            _result.Diagnostics.Report(statement.Value.Span,
                $"cannot assign expression of type '{valueType}' to field '{statement.Target.Member}' of type '{fieldType}'",
                "T102");
        }
    }

    private void CheckImplBlock(ImplBlock implBlock)
    {
        if (!_classDefinitions.TryGetValue(implBlock.TypeName.Value, out var classDefinition))
        {
            return;
        }

        if (implBlock.Constructor != null)
        {
            CheckConstructorDeclaration(implBlock.Constructor, classDefinition);
        }

        foreach (var method in implBlock.Methods)
        {
            CheckMethodDeclaration(method, classDefinition);
        }
    }

    private void CheckInterfaceImplBlock(InterfaceImplBlock interfaceImplBlock)
    {
        if (!_classDefinitions.TryGetValue(interfaceImplBlock.TypeName.Value, out var classDefinition))
        {
            return;
        }

        foreach (var method in interfaceImplBlock.Methods)
        {
            CheckMethodDeclaration(method, classDefinition);
        }
    }

    private void CheckConstructorDeclaration(ConstructorDeclaration declaration, ClassDefinition classDefinition)
    {
        var selfTypeArguments = classDefinition.TypeParameters
            .Select(p => (TypeSymbol)new GenericParameterTypeSymbol(p))
            .ToList();
        _currentSelfTypes.Push(new ClassTypeSymbol(classDefinition.Name, selfTypeArguments));
        try
        {
            var constructorParameterTypes = classDefinition.Constructor?.ParameterTypes ?? [];
            for (var i = 0; i < declaration.Parameters.Count; i++)
            {
                var parameter = declaration.Parameters[i];
                if (parameter.TypeAnnotation == null)
                {
                    _result.Diagnostics.Report(parameter.Span,
                        $"missing type annotation for parameter '{parameter.Name}'",
                        "T105");
                    continue;
                }

                if (_names.ParameterSymbols.TryGetValue(parameter, out var parameterSymbol))
                {
                    _symbolTypes[parameterSymbol] = i < constructorParameterTypes.Count
                        ? constructorParameterTypes[i]
                        : TypeSymbols.Error;
                }
            }

            _currentFunctionReturnTypes.Push(TypeSymbols.Void);
            CheckBlockStatement(declaration.Body);
            _currentFunctionReturnTypes.Pop();
        }
        finally
        {
            _currentSelfTypes.Pop();
        }
    }

    private void CheckMethodDeclaration(MethodDeclaration declaration, ClassDefinition classDefinition)
    {
        var methodSignature = classDefinition.Methods.GetValueOrDefault(declaration.Name.Value);
        var parameterTypes = methodSignature?.ParameterTypes ?? [];
        var returnType = methodSignature?.ReturnType ?? TypeSymbols.Error;

        var selfTypeArguments = classDefinition.TypeParameters
            .Select(p => (TypeSymbol)new GenericParameterTypeSymbol(p))
            .ToList();
        _currentSelfTypes.Push(new ClassTypeSymbol(classDefinition.Name, selfTypeArguments));
        try
        {
            if (declaration.Parameters.Count > 0 &&
                _names.ParameterSymbols.TryGetValue(declaration.Parameters[0], out var selfParameterSymbol))
            {
                _symbolTypes[selfParameterSymbol] = new ClassTypeSymbol(classDefinition.Name, selfTypeArguments);
            }

            for (var i = 1; i < declaration.Parameters.Count && i - 1 < parameterTypes.Count; i++)
            {
                if (_names.ParameterSymbols.TryGetValue(declaration.Parameters[i], out var parameterSymbol))
                {
                    _symbolTypes[parameterSymbol] = parameterTypes[i - 1];
                }
            }

            _currentFunctionReturnTypes.Push(returnType);
            CheckBlockStatement(declaration.Body);
            _currentFunctionReturnTypes.Pop();
        }
        finally
        {
            _currentSelfTypes.Pop();
        }
    }

    private void CheckBreakStatement(BreakStatement statement)
    {
        TypeSymbol? breakValueType = null;
        if (statement.Value != null)
        {
            breakValueType = CheckExpression(statement.Value);
        }

        if (_loopDepth == 0)
        {
            _result.Diagnostics.Report(statement.Span,
                "'break' can only be used inside a loop",
                "T126");
            return;
        }

        if (_loopContexts.Count == 0)
        {
            return;
        }

        var loopContext = _loopContexts.Peek();
        if (loopContext.Expression == null)
        {
            if (statement.Value != null)
            {
                _result.Diagnostics.Report(statement.Span,
                    "'break' with a value is only allowed inside loop expressions",
                    "T136");
            }

            return;
        }

        var currentBreakType = statement.Value == null ? TypeSymbols.Void : breakValueType ?? TypeSymbols.Error;
        if (loopContext.BreakType == null)
        {
            loopContext.BreakType = currentBreakType;
            return;
        }

        if (!AreCompatible(currentBreakType, loopContext.BreakType))
        {
            _result.Diagnostics.Report(statement.Span,
                $"loop break types must match, got '{currentBreakType}' and '{loopContext.BreakType}'",
                "T137");
        }
    }

    private void CheckContinueStatement(ContinueStatement statement)
    {
        if (_loopDepth == 0)
        {
            _result.Diagnostics.Report(statement.Span,
                "'continue' can only be used inside a loop",
                "T127");
        }
    }

    private void CheckReturnStatement(ReturnStatement statement)
    {
        if (_currentFunctionReturnTypes.Count == 0)
        {
            _result.Diagnostics.Report(statement.Span, "return statement outside of function", "T107");
            return;
        }

        var expectedReturnType = _currentFunctionReturnTypes.Peek();
        var actualReturnType = statement.ReturnValue != null
            ? statement.ReturnValue is CallExpression enumCall && expectedReturnType is EnumTypeSymbol expectedEnumType
                ? CheckEnumVariantConstructorCall(enumCall, expectedEnumType, null)
                : CheckExpression(statement.ReturnValue)
            : TypeSymbols.Void;

        if (!IsAssignable(actualReturnType, expectedReturnType))
        {
            _result.Diagnostics.Report(statement.Span,
                $"return type mismatch: expected '{expectedReturnType}', got '{actualReturnType}'",
                "T104");
        }
    }

    private void CheckBlockStatement(BlockStatement blockStatement)
    {
        foreach (var statement in blockStatement.Statements)
        {
            CheckStatement(statement);
        }
    }

    private TypeSymbol CheckExpression(IExpression expression)
    {
        var type = expression switch
        {
            IntegerLiteral integerLiteral => CheckIntegerLiteral(integerLiteral),
            DoubleLiteral => TypeSymbols.Double,
            CharLiteral => TypeSymbols.Char,
            ByteLiteral => TypeSymbols.Byte,
            StringLiteral => TypeSymbols.String,
            BooleanLiteral => TypeSymbols.Bool,
            Identifier identifier => CheckIdentifier(identifier),
            PrefixExpression prefixExpression => CheckPrefixExpression(prefixExpression),
            InfixExpression infixExpression => CheckInfixExpression(infixExpression),
            IfExpression ifExpression => CheckIfExpression(ifExpression),
            LoopExpression loopExpression => CheckLoopExpression(loopExpression),
            MatchExpression matchExpression => CheckMatchExpression(matchExpression),
            FunctionLiteral functionLiteral => CheckFunctionLiteral(functionLiteral),
            CallExpression callExpression => CheckCallExpression(callExpression),
            MemberAccessExpression memberAccessExpression => CheckMemberAccessExpression(memberAccessExpression),
            NewExpression newExpression => CheckNewExpression(newExpression),
            ArrayLiteral arrayLiteral => CheckArrayLiteral(arrayLiteral),
            IndexExpression indexExpression => CheckIndexExpression(indexExpression),
            _ => TypeSymbols.Error,
        };

        _result.ExpressionTypes[expression] = type;
        return type;
    }

    private TypeSymbol CheckIdentifier(Identifier identifier)
    {
        if (!_names.IdentifierSymbols.TryGetValue(identifier, out var symbol))
        {
            return TypeSymbols.Error;
        }

        if (symbol.Kind == NameSymbolKind.EnumVariant)
        {
            _result.Diagnostics.Report(identifier.Span,
                $"enum variant '{identifier.Value}' must be called like '{identifier.Value}(...)'",
                "T128");
            return TypeSymbols.Error;
        }

        if (_symbolTypes.TryGetValue(symbol, out var declarationType))
        {
            return declarationType;
        }

        if (symbol.Kind == NameSymbolKind.Parameter &&
            string.Equals(symbol.Name, "self", StringComparison.Ordinal) &&
            _currentSelfTypes.Count > 0)
        {
            var selfType = _currentSelfTypes.Peek();
            _symbolTypes[symbol] = selfType;
            return selfType;
        }

        if (symbol.Kind == NameSymbolKind.Global &&
            _externalFunctionTypes.TryGetValue(symbol.Name, out var externalFunctionType))
        {
            _symbolTypes[symbol] = externalFunctionType;
            return externalFunctionType;
        }

        _result.Diagnostics.Report(identifier.Span,
            $"symbol '{identifier.Value}' is resolved but has no known type",
            "T108");
        return TypeSymbols.Error;
    }

    private TypeSymbol CheckIntegerLiteral(IntegerLiteral literal)
    {
        if (literal.Value is < int.MinValue or > int.MaxValue)
        {
            _result.Diagnostics.Report(literal.Span,
                $"integer literal '{literal.Value}' is out of range for type 'int'",
                "T121");
            return TypeSymbols.Error;
        }

        return TypeSymbols.Int;
    }

    private TypeSymbol CheckPrefixExpression(PrefixExpression expression)
    {
        var right = CheckExpression(expression.Right);

        return expression.Operator switch
        {
            "!" when right == TypeSymbols.Bool => TypeSymbols.Bool,
            "-" when TryGetUnaryNegationResultType(right, out var resultType) => resultType,
            "!" => ReportPrefixTypeError(expression, right, "bool"),
            "-" => ReportPrefixTypeError(expression, right, "numeric type"),
            _ => ReportUnknownOperator(expression, expression.Operator),
        };
    }

    private TypeSymbol CheckInfixExpression(InfixExpression expression)
    {
        var left = CheckExpression(expression.Left);
        var right = CheckExpression(expression.Right);

        return expression.Operator switch
        {
            "+" or "-" or "*" or "/" =>
                RequireNumericOperands(expression, left, right),
            "&&" or "||" =>
                RequireBothBool(expression, left, right),
            "<" or ">" =>
                RequireComparableNumericTypes(expression, left, right),
            "==" or "!=" =>
                RequireComparable(expression, left, right),
            _ => ReportUnknownOperator(expression, expression.Operator),
        };
    }

    private TypeSymbol CheckIfExpression(IfExpression expression)
    {
        var conditionType = CheckExpression(expression.Condition);
        if (conditionType != TypeSymbols.Bool && conditionType != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Condition.Span,
                $"if condition must be 'bool', got '{conditionType}'",
                "T109");
        }

        var consequenceType = CheckBlockExpressionType(expression.Consequence);
        if (expression.Alternative == null)
        {
            return TypeSymbols.Null;
        }

        var alternativeType = CheckBlockExpressionType(expression.Alternative);
        if (AreCompatible(consequenceType, alternativeType))
        {
            return consequenceType;
        }

        _result.Diagnostics.Report(expression.Span,
            $"if branches must have matching types, got '{consequenceType}' and '{alternativeType}'",
            "T110");
        return TypeSymbols.Error;
    }

    private TypeSymbol CheckLoopExpression(LoopExpression expression)
    {
        var context = new LoopContext { Expression = expression };
        _loopDepth++;
        _loopContexts.Push(context);
        CheckBlockStatement(expression.Body);
        _loopContexts.Pop();
        _loopDepth--;

        return context.BreakType ?? TypeSymbols.Void;
    }

    private TypeSymbol CheckMatchExpression(MatchExpression expression)
    {
        var targetType = CheckExpression(expression.Target);
        if (targetType is not EnumTypeSymbol enumType)
        {
            if (targetType != TypeSymbols.Error)
            {
                _result.Diagnostics.Report(expression.Target.Span,
                    $"match target must be an enum value, got '{targetType}'",
                    "T130");
            }

            return TypeSymbols.Error;
        }

        if (!_result.EnumDefinitions.TryGetValue(enumType.EnumName, out var enumDefinition))
        {
            _result.Diagnostics.Report(expression.Span,
                $"unknown enum type '{enumType.EnumName}' in match expression",
                "T130");
            return TypeSymbols.Error;
        }

        if (expression.Arms.Count == 0)
        {
            _result.Diagnostics.Report(expression.Span,
                $"match expression for enum '{enumDefinition.Name}' must declare at least one arm",
                "T131");
            return TypeSymbols.Error;
        }

        var seenVariants = new HashSet<string>(StringComparer.Ordinal);
        var resolvedArms = new List<MatchArmResolution>(expression.Arms.Count);
        var armTypes = new List<TypeSymbol>(expression.Arms.Count);
        var substitution = BuildTypeParameterSubstitution(enumDefinition, enumType);

        foreach (var arm in expression.Arms)
        {
            var variant = enumDefinition.Variants.FirstOrDefault(v => string.Equals(v.Name, arm.VariantName.Value, StringComparison.Ordinal));
            if (variant == null)
            {
                _result.Diagnostics.Report(arm.VariantName.Span,
                    $"unknown enum variant '{arm.VariantName.Value}' for enum '{enumDefinition.Name}'",
                    "T130");
                continue;
            }

            if (!seenVariants.Add(variant.Name))
            {
                _result.Diagnostics.Report(arm.VariantName.Span,
                    $"duplicate match arm for enum variant '{variant.Name}'",
                    "T131");
                continue;
            }

            var concretePayloadTypes = variant.PayloadTypes
                .Select(payload => SubstituteGenericParameters(payload, substitution))
                .ToList();

            if (arm.Bindings.Count != concretePayloadTypes.Count)
            {
                _result.Diagnostics.Report(arm.Span,
                    $"match arm '{variant.Name}' expects {concretePayloadTypes.Count} binding(s), got {arm.Bindings.Count}",
                    "T131");
                continue;
            }

            var bindingSymbols = new List<NameSymbol>(arm.Bindings.Count);
            for (var i = 0; i < arm.Bindings.Count; i++)
            {
                var binding = arm.Bindings[i];
                if (_names.IdentifierSymbols.TryGetValue(binding, out var bindingSymbol))
                {
                    _symbolTypes[bindingSymbol] = concretePayloadTypes[i];
                    bindingSymbols.Add(bindingSymbol);
                }
            }

            var armType = CheckBlockExpressionType(arm.Body);
            armTypes.Add(armType);
            resolvedArms.Add(new MatchArmResolution(variant.Name, variant.Tag, concretePayloadTypes, bindingSymbols));
        }

        if (seenVariants.Count != enumDefinition.Variants.Count)
        {
            _result.Diagnostics.Report(expression.Span,
                $"match expression for enum '{enumDefinition.Name}' is not exhaustive",
                "T131");
        }

        if (armTypes.Count == 0)
        {
            return TypeSymbols.Error;
        }

        var resultType = armTypes[0];
        for (var i = 1; i < armTypes.Count; i++)
        {
            if (!AreCompatible(resultType, armTypes[i]))
            {
                _result.Diagnostics.Report(expression.Span,
                    $"match arms must have matching types, got '{resultType}' and '{armTypes[i]}'",
                    "T110");
                return TypeSymbols.Error;
            }
        }

        _result.ResolvedMatches[expression] = new MatchResolution(enumDefinition.Name, resolvedArms);
        return resultType;
    }

    private TypeSymbol CheckBlockExpressionType(BlockStatement block)
    {
        TypeSymbol? lastType = null;

        foreach (var statement in block.Statements)
        {
            CheckStatement(statement);

            if (statement is ExpressionStatement { Expression: not null } expressionStatement)
            {
                lastType = _result.ExpressionTypes.GetValueOrDefault(expressionStatement.Expression, TypeSymbols.Error);
            }
        }

        return lastType ?? TypeSymbols.Void;
    }

    private FunctionTypeSymbol CheckFunctionLiteral(FunctionLiteral functionLiteral, NameSymbol? declaredSymbol = null)
    {
        var parameterTypes = new List<TypeSymbol>(functionLiteral.Parameters.Count);
        foreach (var parameter in functionLiteral.Parameters)
        {
            var parameterType = CheckFunctionParameter(parameter);
            parameterTypes.Add(parameterType);
        }

        for (var i = 0; i < functionLiteral.Parameters.Count && i < parameterTypes.Count; i++)
        {
            var parameter = functionLiteral.Parameters[i];
            if (_names.ParameterSymbols.TryGetValue(parameter, out var parameterSymbol))
            {
                _symbolTypes[parameterSymbol] = parameterTypes[i];
            }
        }

        TypeSymbol returnType;
        if (functionLiteral.IsLambda)
        {
            var bodyType = CheckBlockExpressionType(functionLiteral.Body);
            returnType = functionLiteral.ReturnTypeAnnotation == null
                ? bodyType
                : TypeAnnotationBinder.Bind(functionLiteral.ReturnTypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;
        }
        else if (functionLiteral.ReturnTypeAnnotation == null)
        {
            _result.Diagnostics.Report(functionLiteral.Span,
                "missing return type annotation for function",
                "T106");
            returnType = TypeSymbols.Error;
        }
        else
        {
            returnType = TypeAnnotationBinder.Bind(functionLiteral.ReturnTypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;
        }

        var functionType = new FunctionTypeSymbol(parameterTypes, returnType);
        _result.FunctionTypes[functionLiteral] = functionType;

        if (declaredSymbol != null)
        {
            _symbolTypes[declaredSymbol.Value] = functionType;
        }

        CheckFunctionBody(functionLiteral, functionType, parameterTypes);

        return functionType;
    }

    private void CheckFunctionBody(FunctionLiteral functionLiteral, FunctionTypeSymbol functionType, IReadOnlyList<TypeSymbol> parameterTypes)
    {
        for (var i = 0; i < functionLiteral.Parameters.Count && i < parameterTypes.Count; i++)
        {
            var parameter = functionLiteral.Parameters[i];
            if (_names.ParameterSymbols.TryGetValue(parameter, out var parameterSymbol))
            {
                _symbolTypes[parameterSymbol] = parameterTypes[i];
            }
        }

        _currentFunctionReturnTypes.Push(functionType.ReturnType);
        CheckBlockStatement(functionLiteral.Body);
        _currentFunctionReturnTypes.Pop();

        _controlFlowAnalyzer.AnalyzeFunction(
            functionLiteral,
            functionType.ReturnType,
            _result.ExpressionTypes,
            _result.Diagnostics);
    }

    private TypeSymbol CheckFunctionParameter(FunctionParameter parameter)
    {
        if (parameter.TypeAnnotation == null)
        {
            _result.Diagnostics.Report(parameter.Span,
                $"missing type annotation for parameter '{parameter.Name}'",
                "T105");
            return TypeSymbols.Error;
        }

        return TypeAnnotationBinder.Bind(parameter.TypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;
    }

    private TypeSymbol CheckCallExpression(CallExpression expression)
    {
        var clrArguments = BuildClrCallArguments(expression);
        var argumentTypes = clrArguments.Select(a => a.Type).ToList();

        if (expression.Function is MemberAccessExpression userMemberAccessExpression &&
            TryCheckUserInstanceMethodCall(userMemberAccessExpression, argumentTypes, out var userInstanceReturnType))
        {
            return userInstanceReturnType;
        }

        if (expression.Function is MemberAccessExpression memberAccessExpression &&
            TryCheckInstanceMethodCall(expression, memberAccessExpression, clrArguments, out var instanceReturnType))
        {
            return instanceReturnType;
        }

        if (expression.Function is MemberAccessExpression memberAccessExpression2 &&
            TryCheckStaticMethodCall(expression, memberAccessExpression2, clrArguments, out var staticReturnType))
        {
            return staticReturnType;
        }

        if (expression.Arguments.Any(a => a.Modifier != CallArgumentModifier.None))
        {
            _result.Diagnostics.Report(expression.Span,
                "out/ref modifiers are supported only for CLR interop method calls",
                "T122");
        }

        if (expression.Function is Identifier identifier &&
            _names.IdentifierSymbols.TryGetValue(identifier, out var symbol) &&
            symbol.Kind == NameSymbolKind.EnumVariant &&
            _variantSymbols.TryGetValue(symbol.Name, out _))
        {
            return CheckEnumVariantConstructorCall(expression, expectedEnumType: null, argumentTypes);
        }

        var functionType = CheckExpression(expression.Function);

        if (functionType is not FunctionTypeSymbol fn)
        {
            _result.Diagnostics.Report(expression.Span,
                $"cannot call expression of type '{functionType}'",
                "T111");
            return TypeSymbols.Error;
        }

        if (argumentTypes.Count != fn.ParameterTypes.Count)
        {
            _result.Diagnostics.Report(expression.Span,
                $"wrong number of arguments: expected {fn.ParameterTypes.Count}, got {argumentTypes.Count}",
                "T112");
            return fn.ReturnType;
        }

        var callParameterTypes = fn.ParameterTypes;
        var callReturnType = fn.ReturnType;
        if (fn.ParameterTypes.Any(ContainsGenericParameters) || ContainsGenericParameters(fn.ReturnType))
        {
            var inference = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
            for (var i = 0; i < argumentTypes.Count; i++)
            {
                if (!TryInferGenericType(fn.ParameterTypes[i], argumentTypes[i], inference))
                {
                    _result.Diagnostics.Report(expression.Span,
                        "cannot infer generic function type arguments from call arguments",
                        "T130");
                    return TypeSymbols.Error;
                }
            }

            callParameterTypes = fn.ParameterTypes.Select(p => SubstituteGenericParameters(p, inference)).ToList();
            callReturnType = SubstituteGenericParameters(fn.ReturnType, inference);
        }

        for (var i = 0; i < argumentTypes.Count; i++)
        {
            if (!IsAssignable(argumentTypes[i], callParameterTypes[i]))
            {
                _result.Diagnostics.Report(expression.Arguments[i].Span,
                    $"argument {i + 1} type mismatch: expected '{callParameterTypes[i]}', got '{argumentTypes[i]}'",
                    "T113");
            }
        }

        return callReturnType;
    }

    private TypeSymbol CheckEnumVariantConstructorCall(
        CallExpression expression,
        EnumTypeSymbol? expectedEnumType,
        IReadOnlyList<TypeSymbol>? precomputedArgumentTypes)
    {
        var argumentTypes = precomputedArgumentTypes?.ToList() ?? expression.Arguments.Select(a => CheckExpression(a.Expression)).ToList();

        if (expression.Function is not Identifier identifier ||
            !_names.IdentifierSymbols.TryGetValue(identifier, out var symbol) ||
            symbol.Kind != NameSymbolKind.EnumVariant ||
            !_variantSymbols.TryGetValue(symbol.Name, out var variantBinding))
        {
            _result.Diagnostics.Report(expression.Span,
                "internal error: expected enum variant constructor call",
                "T130");
            return TypeSymbols.Error;
        }

        if (expectedEnumType != null && !string.Equals(expectedEnumType.EnumName, variantBinding.Enum.Name, StringComparison.Ordinal))
        {
            _result.Diagnostics.Report(
                expression.Span,
                $"enum variant '{variantBinding.Variant.Name}' does not belong to expected enum '{expectedEnumType.EnumName}'",
                "T113");
            return expectedEnumType;
        }

        var inferredTypeArguments = InferEnumTypeArguments(variantBinding.Enum, variantBinding.Variant, argumentTypes, expectedEnumType);
        if (inferredTypeArguments == null)
        {
            return new EnumTypeSymbol(variantBinding.Enum.Name, []);
        }

        var concreteEnumType = new EnumTypeSymbol(variantBinding.Enum.Name, inferredTypeArguments);
        var substitution = BuildTypeParameterSubstitution(variantBinding.Enum, concreteEnumType);
        var concretePayloadTypes = variantBinding.Variant.PayloadTypes
            .Select(payloadType => SubstituteGenericParameters(payloadType, substitution))
            .ToList();

        if (argumentTypes.Count != concretePayloadTypes.Count)
        {
            _result.Diagnostics.Report(
                expression.Span,
                $"wrong number of arguments for enum variant '{variantBinding.Variant.Name}': expected {concretePayloadTypes.Count}, got {argumentTypes.Count}",
                "T112");
            return concreteEnumType;
        }

        for (var i = 0; i < concretePayloadTypes.Count; i++)
        {
            if (!IsAssignable(argumentTypes[i], concretePayloadTypes[i]))
            {
                _result.Diagnostics.Report(
                    expression.Arguments[i].Span,
                    $"argument {i + 1} type mismatch for enum variant '{variantBinding.Variant.Name}': expected '{concretePayloadTypes[i]}', got '{argumentTypes[i]}'",
                    "T113");
            }
        }

        _result.ResolvedEnumVariantConstructions[expression] = new EnumVariantConstruction(
            variantBinding.Enum.Name,
            variantBinding.Variant.Name,
            variantBinding.Variant.Tag,
            variantBinding.Enum.MaxPayloadArity,
            concretePayloadTypes);
        _result.ExpressionTypes[expression] = concreteEnumType;
        return concreteEnumType;
    }

    private List<TypeSymbol>? InferEnumTypeArguments(
        EnumDefinitionSymbol enumDefinition,
        EnumVariantDefinition variant,
        IReadOnlyList<TypeSymbol> argumentTypes,
        EnumTypeSymbol? expectedEnumType)
    {
        if (enumDefinition.TypeParameters.Count == 0)
        {
            return [];
        }

        if (expectedEnumType != null)
        {
            if (expectedEnumType.TypeArguments.Count != enumDefinition.TypeParameters.Count)
            {
                _result.Diagnostics.Report(
                    Span.Empty,
                    $"internal error: expected enum '{expectedEnumType.EnumName}' has wrong arity",
                    "T130");
                return null;
            }

            return expectedEnumType.TypeArguments.ToList();
        }

        if (argumentTypes.Count != variant.PayloadTypes.Count)
        {
            _result.Diagnostics.Report(
                Span.Empty,
                $"cannot infer generic enum type arguments for '{enumDefinition.Name}.{variant.Name}' due to argument count mismatch",
                "T112");
            return null;
        }

        var inference = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        for (var i = 0; i < variant.PayloadTypes.Count; i++)
        {
            if (!TryInferGenericType(variant.PayloadTypes[i], argumentTypes[i], inference))
            {
                _result.Diagnostics.Report(
                    Span.Empty,
                    $"cannot infer generic enum type arguments for '{enumDefinition.Name}.{variant.Name}'",
                    "T130");
                return null;
            }
        }

        var typeArguments = new List<TypeSymbol>(enumDefinition.TypeParameters.Count);
        foreach (var typeParameter in enumDefinition.TypeParameters)
        {
            if (!inference.TryGetValue(typeParameter, out var inferred))
            {
                _result.Diagnostics.Report(
                    Span.Empty,
                    $"cannot infer type argument '{typeParameter}' for enum '{enumDefinition.Name}'",
                    "T130");
                return null;
            }

            typeArguments.Add(inferred);
        }

        return typeArguments;
    }

    private static Dictionary<string, TypeSymbol> BuildTypeParameterSubstitution(
        EnumDefinitionSymbol enumDefinition,
        EnumTypeSymbol concreteEnumType)
    {
        var substitution = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        for (var i = 0; i < enumDefinition.TypeParameters.Count && i < concreteEnumType.TypeArguments.Count; i++)
        {
            substitution[enumDefinition.TypeParameters[i]] = concreteEnumType.TypeArguments[i];
        }

        return substitution;
    }

    private static Dictionary<string, TypeSymbol> BuildTypeParameterSubstitution(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<TypeSymbol> typeArguments)
    {
        var substitution = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        for (var i = 0; i < typeParameters.Count && i < typeArguments.Count; i++)
        {
            substitution[typeParameters[i]] = typeArguments[i];
        }

        return substitution;
    }

    private static TypeSymbol SubstituteGenericParameters(TypeSymbol type, IReadOnlyDictionary<string, TypeSymbol> substitution)
    {
        if (type is GenericParameterTypeSymbol genericParameter)
        {
            return substitution.TryGetValue(genericParameter.ParameterName, out var concrete)
                ? concrete
                : type;
        }

        if (type is ArrayTypeSymbol array)
        {
            return new ArrayTypeSymbol(SubstituteGenericParameters(array.ElementType, substitution));
        }

        if (type is FunctionTypeSymbol function)
        {
            var parameters = function.ParameterTypes.Select(p => SubstituteGenericParameters(p, substitution)).ToList();
            var returnType = SubstituteGenericParameters(function.ReturnType, substitution);
            return new FunctionTypeSymbol(parameters, returnType);
        }

        if (type is EnumTypeSymbol enumType)
        {
            var args = enumType.TypeArguments.Select(a => SubstituteGenericParameters(a, substitution)).ToList();
            return new EnumTypeSymbol(enumType.EnumName, args);
        }

        if (type is ClassTypeSymbol classType)
        {
            var args = classType.TypeArguments.Select(a => SubstituteGenericParameters(a, substitution)).ToList();
            return new ClassTypeSymbol(classType.ClassName, args);
        }

        if (type is InterfaceTypeSymbol interfaceType)
        {
            var args = interfaceType.TypeArguments.Select(a => SubstituteGenericParameters(a, substitution)).ToList();
            return new InterfaceTypeSymbol(interfaceType.InterfaceName, args);
        }

        return type;
    }

    private static bool TryInferGenericType(
        TypeSymbol template,
        TypeSymbol actual,
        Dictionary<string, TypeSymbol> inference)
    {
        if (template is GenericParameterTypeSymbol parameter)
        {
            if (inference.TryGetValue(parameter.ParameterName, out var existing))
            {
                return TypeEquals(existing, actual);
            }

            inference[parameter.ParameterName] = actual;
            return true;
        }

        if (template is ArrayTypeSymbol templateArray && actual is ArrayTypeSymbol actualArray)
        {
            return TryInferGenericType(templateArray.ElementType, actualArray.ElementType, inference);
        }

        if (template is EnumTypeSymbol templateEnum && actual is EnumTypeSymbol actualEnum)
        {
            if (!string.Equals(templateEnum.EnumName, actualEnum.EnumName, StringComparison.Ordinal) ||
                templateEnum.TypeArguments.Count != actualEnum.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < templateEnum.TypeArguments.Count; i++)
            {
                if (!TryInferGenericType(templateEnum.TypeArguments[i], actualEnum.TypeArguments[i], inference))
                {
                    return false;
                }
            }

            return true;
        }

        if (template is FunctionTypeSymbol templateFunction && actual is FunctionTypeSymbol actualFunction)
        {
            if (templateFunction.ParameterTypes.Count != actualFunction.ParameterTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < templateFunction.ParameterTypes.Count; i++)
            {
                if (!TryInferGenericType(templateFunction.ParameterTypes[i], actualFunction.ParameterTypes[i], inference))
                {
                    return false;
                }
            }

            return TryInferGenericType(templateFunction.ReturnType, actualFunction.ReturnType, inference);
        }

        return TypeEquals(template, actual);
    }

    private static bool TryInstantiateGenericMethodSignature(
        IReadOnlyList<string> methodTypeParameters,
        IReadOnlyList<TypeSymbol> methodParameterTypes,
        TypeSymbol methodReturnType,
        IReadOnlyList<TypeSymbol> argumentTypes,
        out IReadOnlyList<TypeSymbol> instantiatedParameterTypes,
        out TypeSymbol instantiatedReturnType)
    {
        instantiatedParameterTypes = methodParameterTypes;
        instantiatedReturnType = methodReturnType;

        if (methodTypeParameters.Count == 0)
        {
            return true;
        }

        if (methodParameterTypes.Count != argumentTypes.Count)
        {
            return false;
        }

        var inference = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        for (var i = 0; i < methodParameterTypes.Count; i++)
        {
            if (!TryInferGenericType(methodParameterTypes[i], argumentTypes[i], inference))
            {
                return false;
            }
        }

        foreach (var typeParameter in methodTypeParameters)
        {
            if (!inference.ContainsKey(typeParameter))
            {
                return false;
            }
        }

        instantiatedParameterTypes = methodParameterTypes
            .Select(p => SubstituteGenericParameters(p, inference))
            .ToList();
        instantiatedReturnType = SubstituteGenericParameters(methodReturnType, inference);
        return true;
    }

    private static bool ContainsGenericParameters(TypeSymbol type)
    {
        if (type is GenericParameterTypeSymbol)
        {
            return true;
        }

        if (type is ArrayTypeSymbol arrayType)
        {
            return ContainsGenericParameters(arrayType.ElementType);
        }

        if (type is FunctionTypeSymbol functionType)
        {
            return functionType.ParameterTypes.Any(ContainsGenericParameters) ||
                   ContainsGenericParameters(functionType.ReturnType);
        }

        if (type is EnumTypeSymbol enumType)
        {
            return enumType.TypeArguments.Any(ContainsGenericParameters);
        }

        if (type is ClassTypeSymbol classType)
        {
            return classType.TypeArguments.Any(ContainsGenericParameters);
        }

        if (type is InterfaceTypeSymbol interfaceType)
        {
            return interfaceType.TypeArguments.Any(ContainsGenericParameters);
        }

        return false;
    }

    private TypeSymbol CheckNewExpression(NewExpression expression)
    {
        if (_namedTypeSymbols.TryGetValue(expression.TypePath, out var userType) && userType is ClassTypeSymbol classType)
        {
            if (expression.TypeArguments.Count > 0)
            {
                var genericTypeNode = new GenericType
                {
                    Token = expression.Token,
                    Name = expression.TypePath,
                    TypeArguments = expression.TypeArguments,
                    Span = expression.Span,
                };

                var boundClassType = TypeAnnotationBinder.Bind(genericTypeNode, _result.Diagnostics, _namedTypeSymbols);
                if (boundClassType is ClassTypeSymbol boundConcreteClass)
                {
                    classType = boundConcreteClass;
                }
            }

            var argumentTypes = expression.Arguments.Select(CheckExpression).ToList();
            if (_classDefinitions.TryGetValue(classType.ClassName, out var classDefinition))
            {
                if (classType.TypeArguments.Count > 0 && classType.TypeArguments.All(t => t is GenericParameterTypeSymbol))
                {
                    var constructorTemplateParameters = classDefinition.Constructor?.ParameterTypes ?? [];
                    if (constructorTemplateParameters.Count == argumentTypes.Count)
                    {
                        var inference = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
                        for (var i = 0; i < constructorTemplateParameters.Count; i++)
                        {
                            _ = TryInferGenericType(constructorTemplateParameters[i], argumentTypes[i], inference);
                        }

                        var inferredClassArguments = new List<TypeSymbol>(classDefinition.TypeParameters.Count);
                        var allInferred = true;
                        foreach (var typeParameter in classDefinition.TypeParameters)
                        {
                            if (!inference.TryGetValue(typeParameter, out var inferred))
                            {
                                allInferred = false;
                                break;
                            }

                            inferredClassArguments.Add(inferred);
                        }

                        if (allInferred)
                        {
                            classType = new ClassTypeSymbol(classType.ClassName, inferredClassArguments);
                        }
                    }
                }

                var classSubstitution = BuildTypeParameterSubstitution(classDefinition.TypeParameters, classType.TypeArguments);
                var expectedParameters = classDefinition.Constructor?.ParameterTypes
                    .Select(p => SubstituteGenericParameters(p, classSubstitution))
                    .ToList() ?? [];
                if (argumentTypes.Count != expectedParameters.Count)
                {
                    _result.Diagnostics.Report(expression.Span,
                        $"wrong number of arguments for constructor '{classDefinition.Name}.init': expected {expectedParameters.Count}, got {argumentTypes.Count}",
                        "T112");
                    return classType;
                }

                for (var i = 0; i < argumentTypes.Count; i++)
                {
                    if (!IsAssignable(argumentTypes[i], expectedParameters[i]))
                    {
                        _result.Diagnostics.Report(expression.Arguments[i].Span,
                            $"argument {i + 1} type mismatch for constructor '{classDefinition.Name}.init': expected '{expectedParameters[i]}', got '{argumentTypes[i]}'",
                            "T113");
                    }
                }
            }

            return classType;
        }

        var clrArgumentTypes = expression.Arguments.Select(CheckExpression).ToList();
        if (!TryResolveImportedTypePath(expression.TypePath, out var resolvedTypePath, out var diagnostic))
        {
            _result.Diagnostics.Report(expression.Span, diagnostic, "T122");
            return TypeSymbols.Error;
        }

        if (!ConstructorClrResolver.TryResolve(
                resolvedTypePath,
                clrArgumentTypes,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(expression.Span, resolutionMessage, code);
            return TypeSymbols.Error;
        }

        _result.ResolvedConstructorTypePaths[expression] = resolvedTypePath;
        return binding.ConstructedType;
    }

    private TypeSymbol CheckMemberAccessExpression(MemberAccessExpression expression)
    {
        if (TryCheckUserInstanceValueAccess(expression, out var userInstanceType))
        {
            return userInstanceType;
        }

        if (TryCheckInstanceValueAccess(expression, out var instanceType))
        {
            return instanceType;
        }

        if (!TryGetQualifiedMemberPath(expression, out var memberPath))
        {
            _result.Diagnostics.Report(
                expression.Span,
                "static member access target must be a member path like 'System.Environment.NewLine' or 'Environment.NewLine'",
                "T122");
            return TypeSymbols.Error;
        }

        if (!TryResolveImportedPath(memberPath, StaticClrMethodResolver.IsKnownValuePath, out var resolvedPath, out var resolutionDiagnostic))
        {
            _result.Diagnostics.Report(expression.Span, resolutionDiagnostic, "T122");
            return TypeSymbols.Error;
        }

        if (!StaticClrMethodResolver.TryResolveValue(resolvedPath, out var binding, out var resolutionError, out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(expression.Span, resolutionMessage, code);
            return TypeSymbols.Error;
        }

        _result.ResolvedStaticValuePaths[expression] = resolvedPath;
        return binding.Type;
    }

    private bool TryCheckInstanceValueAccess(MemberAccessExpression expression, out TypeSymbol valueType)
    {
        valueType = TypeSymbols.Error;

        if (!IsPotentialInstanceReceiver(expression.Object))
        {
            return false;
        }

        var receiverType = CheckExpression(expression.Object);
        if (receiverType == TypeSymbols.Error)
        {
            return true;
        }

        if (!InstanceClrMemberResolver.TryResolveValue(
                receiverType,
                expression.Member,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(expression.Span, resolutionMessage, code);
            valueType = TypeSymbols.Error;
            return true;
        }

        _result.ResolvedInstanceValueMembers[expression] = expression.Member;
        valueType = binding.Type;
        return true;
    }

    private bool TryCheckUserInstanceValueAccess(MemberAccessExpression expression, out TypeSymbol valueType)
    {
        valueType = TypeSymbols.Error;

        if (!IsPotentialInstanceReceiver(expression.Object))
        {
            return false;
        }

        var receiverType = CheckExpression(expression.Object);
        if (receiverType is not ClassTypeSymbol classType)
        {
            return false;
        }

        if (!_classDefinitions.TryGetValue(classType.ClassName, out var classDefinition))
        {
            _result.Diagnostics.Report(expression.Span,
                $"unknown class '{classType.ClassName}'",
                "T135");
            return true;
        }

        if (!classDefinition.Fields.TryGetValue(expression.Member, out var fieldType))
        {
            _result.Diagnostics.Report(expression.Span,
                $"class '{classType.ClassName}' has no field '{expression.Member}'",
                "T135");
            valueType = TypeSymbols.Error;
            return true;
        }

        var classSubstitution = BuildTypeParameterSubstitution(classDefinition.TypeParameters, classType.TypeArguments);
        valueType = SubstituteGenericParameters(fieldType, classSubstitution);

        return true;
    }

    private bool TryCheckUserInstanceMethodCall(
        MemberAccessExpression memberAccessExpression,
        IReadOnlyList<TypeSymbol> argumentTypes,
        out TypeSymbol returnType)
    {
        returnType = TypeSymbols.Error;

        if (!IsPotentialInstanceReceiver(memberAccessExpression.Object))
        {
            return false;
        }

        var receiverType = CheckExpression(memberAccessExpression.Object);
        if (receiverType is not ClassTypeSymbol classType)
        {
            if (receiverType is InterfaceTypeSymbol interfaceType)
            {
                if (_interfaceDefinitions.TryGetValue(interfaceType.InterfaceName, out var interfaceDefinition) &&
                    interfaceDefinition.Methods.TryGetValue(memberAccessExpression.Member, out var interfaceMethod))
                {
                    var interfaceSubstitution = BuildTypeParameterSubstitution(interfaceDefinition.TypeParameters, interfaceType.TypeArguments);
                    var interfaceMethodParameterTypes = interfaceMethod.ParameterTypes
                        .Select(p => SubstituteGenericParameters(p, interfaceSubstitution))
                        .ToList();
                    var interfaceMethodReturnType = SubstituteGenericParameters(interfaceMethod.ReturnType, interfaceSubstitution);
                    if (!TryInstantiateGenericMethodSignature(interfaceMethod.TypeParameters, interfaceMethodParameterTypes, interfaceMethodReturnType, argumentTypes, out var expectedParameterTypes, out var concreteReturnType))
                    {
                        _result.Diagnostics.Report(memberAccessExpression.Span,
                            $"cannot infer generic type arguments for method '{interfaceMethod.Name}'",
                            "T130");
                        return true;
                    }

                    if (argumentTypes.Count != expectedParameterTypes.Count)
                    {
                        _result.Diagnostics.Report(memberAccessExpression.Span,
                            $"wrong number of arguments for method '{interfaceMethod.Name}': expected {expectedParameterTypes.Count}, got {argumentTypes.Count}",
                            "T112");
                        returnType = concreteReturnType;
                        return true;
                    }

                    for (var i = 0; i < argumentTypes.Count; i++)
                    {
                        if (!IsAssignable(argumentTypes[i], expectedParameterTypes[i]))
                        {
                            _result.Diagnostics.Report(memberAccessExpression.Span,
                                $"argument {i + 1} type mismatch for method '{interfaceMethod.Name}': expected '{expectedParameterTypes[i]}', got '{argumentTypes[i]}'",
                                "T113");
                        }
                    }

                    returnType = concreteReturnType;
                    return true;
                }
            }

            return false;
        }

        if (!_classDefinitions.TryGetValue(classType.ClassName, out var classDefinition))
        {
            _result.Diagnostics.Report(memberAccessExpression.Span,
                $"unknown class '{classType.ClassName}'",
                "T135");
            return true;
        }

        if (!classDefinition.Methods.TryGetValue(memberAccessExpression.Member, out var methodSignature))
        {
            _result.Diagnostics.Report(memberAccessExpression.Span,
                $"class '{classType.ClassName}' has no method '{memberAccessExpression.Member}'",
                "T135");
            return true;
        }

        var classSubstitution = BuildTypeParameterSubstitution(classDefinition.TypeParameters, classType.TypeArguments);
        var classMethodParameterTypes = methodSignature.ParameterTypes
            .Select(p => SubstituteGenericParameters(p, classSubstitution))
            .ToList();
        var classMethodReturnType = SubstituteGenericParameters(methodSignature.ReturnType, classSubstitution);
        if (!TryInstantiateGenericMethodSignature(methodSignature.TypeParameters, classMethodParameterTypes, classMethodReturnType, argumentTypes, out var expectedClassParameterTypes, out var concreteClassReturnType))
        {
            _result.Diagnostics.Report(memberAccessExpression.Span,
                $"cannot infer generic type arguments for method '{methodSignature.Name}'",
                "T130");
            return true;
        }

        if (argumentTypes.Count != expectedClassParameterTypes.Count)
        {
            _result.Diagnostics.Report(memberAccessExpression.Span,
                $"wrong number of arguments for method '{methodSignature.Name}': expected {expectedClassParameterTypes.Count}, got {argumentTypes.Count}",
                "T112");
            returnType = concreteClassReturnType;
            return true;
        }

        for (var i = 0; i < argumentTypes.Count; i++)
        {
            if (!IsAssignable(argumentTypes[i], expectedClassParameterTypes[i]))
            {
                _result.Diagnostics.Report(memberAccessExpression.Span,
                    $"argument {i + 1} type mismatch for method '{methodSignature.Name}': expected '{expectedClassParameterTypes[i]}', got '{argumentTypes[i]}'",
                    "T113");
            }
        }

        returnType = concreteClassReturnType;
        return true;
    }

    private bool TryCheckInstanceMethodCall(
        CallExpression callExpression,
        MemberAccessExpression memberAccessExpression,
        IReadOnlyList<ClrCallArgument> arguments,
        out TypeSymbol returnType)
    {
        returnType = TypeSymbols.Error;

        if (!IsPotentialInstanceReceiver(memberAccessExpression.Object))
        {
            return false;
        }

        var receiverType = CheckExpression(memberAccessExpression.Object);
        if (receiverType == TypeSymbols.Error)
        {
            return true;
        }

        if (!InstanceClrMemberResolver.TryResolveMethod(
                receiverType,
                memberAccessExpression.Member,
                arguments,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(memberAccessExpression.Span, resolutionMessage, code);
            return true;
        }

        _result.ResolvedInstanceMethodMembers[callExpression] = memberAccessExpression.Member;
        returnType = binding.ReturnType;
        return true;
    }

    private bool TryCheckStaticMethodCall(
        CallExpression callExpression,
        MemberAccessExpression memberAccessExpression,
        IReadOnlyList<ClrCallArgument> arguments,
        out TypeSymbol returnType)
    {
        returnType = TypeSymbols.Error;

        if (!TryGetQualifiedMemberPath(memberAccessExpression, out var methodPath))
        {
            _result.Diagnostics.Report(
                memberAccessExpression.Span,
                "static method call target must be a member path like 'System.Console.WriteLine' or 'Console.WriteLine'",
                "T122");
            return true;
        }

        if (!TryResolveImportedPath(methodPath, StaticClrMethodResolver.IsKnownMethodPath, out var resolvedMethodPath, out var resolutionDiagnostic))
        {
            _result.Diagnostics.Report(memberAccessExpression.Span, resolutionDiagnostic, "T122");
            return true;
        }

        if (!StaticClrMethodResolver.TryResolve(
                resolvedMethodPath,
                arguments,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(memberAccessExpression.Span, resolutionMessage, code);
            return true;
        }

        _result.ResolvedStaticMethodPaths[callExpression] = resolvedMethodPath;
        returnType = binding.ReturnType;

        return true;
    }

    private List<ClrCallArgument> BuildClrCallArguments(CallExpression expression)
    {
        var arguments = new List<ClrCallArgument>(expression.Arguments.Count);
        foreach (var argument in expression.Arguments)
        {
            var type = CheckExpression(argument.Expression);

            if (argument.Modifier is CallArgumentModifier.Out or CallArgumentModifier.Ref)
            {
                ValidateByRefArgument(argument);
            }

            arguments.Add(new ClrCallArgument(type, argument.Modifier));
        }

        return arguments;
    }

    private void ValidateByRefArgument(CallArgument argument)
    {
        if (argument.Expression is not Identifier identifier)
        {
            _result.Diagnostics.Report(argument.Span,
                "out/ref arguments must be identifier variables",
                "T122");
            return;
        }

        if (!_names.IdentifierSymbols.TryGetValue(identifier, out var symbol))
        {
            return;
        }

        if (!_names.MutableSymbols.Contains(symbol))
        {
            _result.Diagnostics.Report(argument.Span,
                $"out/ref argument '{identifier.Value}' must be declared with 'var'",
                "T123");
        }
    }

    private static bool TryGetQualifiedMemberPath(MemberAccessExpression expression, out string methodPath)
    {
        var segments = new Stack<string>();
        IExpression current = expression;

        while (current is MemberAccessExpression memberAccess)
        {
            if (string.IsNullOrWhiteSpace(memberAccess.Member))
            {
                methodPath = string.Empty;
                return false;
            }

            segments.Push(memberAccess.Member);
            current = memberAccess.Object;
        }

        if (current is not Identifier identifier)
        {
            methodPath = string.Empty;
            return false;
        }

        segments.Push(identifier.Value);
        methodPath = string.Join('.', segments);
        return true;
    }

    private bool TryResolveImportedPath(
        string memberPath,
        Func<string, bool> isKnownCandidate,
        out string resolvedPath,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        resolvedPath = memberPath;

        if (isKnownCandidate(memberPath))
        {
            return true;
        }

        var firstDot = memberPath.IndexOf('.');
        if (firstDot <= 0)
        {
            return true;
        }

        var root = memberPath[..firstDot];
        var suffix = memberPath[firstDot..];
        var candidates = new HashSet<string>();

        if (!_names.ImportedTypeAliases.TryGetValue(root, out var qualifiedTypeName))
        {
            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + memberPath);
            }
        }
        else
        {
            candidates.Add(qualifiedTypeName + suffix);

            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + memberPath);
            }
        }

        var knownCandidates = candidates
            .Where(isKnownCandidate)
            .ToList();

        if (knownCandidates.Count == 0)
        {
            return true;
        }

        if (knownCandidates.Count == 1)
        {
            resolvedPath = knownCandidates[0];
            return true;
        }

        diagnostic = $"ambiguous static member reference '{memberPath}' from imports; matching candidates: {string.Join(", ", knownCandidates.OrderBy(c => c))}";
        return false;
    }

    private bool TryResolveImportedTypePath(string typePath, out string resolvedPath, out string diagnostic)
    {
        diagnostic = string.Empty;
        resolvedPath = typePath;

        if (ConstructorClrResolver.IsKnownTypePath(typePath))
        {
            return true;
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var firstDot = typePath.IndexOf('.');

        if (firstDot > 0)
        {
            var root = typePath[..firstDot];
            var suffix = typePath[firstDot..];
            if (_names.ImportedTypeAliases.TryGetValue(root, out var qualifiedTypeName))
            {
                candidates.Add(qualifiedTypeName + suffix);
            }

            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + typePath);
            }
        }
        else
        {
            if (_names.ImportedTypeAliases.TryGetValue(typePath, out var qualifiedTypeName))
            {
                candidates.Add(qualifiedTypeName);
            }

            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + typePath);
            }
        }

        var knownCandidates = candidates.Where(ConstructorClrResolver.IsKnownTypePath).ToList();
        if (knownCandidates.Count == 0)
        {
            return true;
        }

        if (knownCandidates.Count == 1)
        {
            resolvedPath = knownCandidates[0];
            return true;
        }

        diagnostic = $"ambiguous type reference '{typePath}' from imports; matching candidates: {string.Join(", ", knownCandidates.OrderBy(c => c))}";
        return false;
    }

    private bool IsPotentialInstanceReceiver(IExpression expression)
    {
        if (expression is Identifier identifier)
        {
            return _names.IdentifierSymbols.ContainsKey(identifier);
        }

        if (expression is MemberAccessExpression memberAccess)
        {
            return IsPotentialInstanceReceiver(memberAccess.Object);
        }

        return true;
    }

    private TypeSymbol CheckArrayLiteral(ArrayLiteral expression)
    {
        if (expression.Elements.Count == 0)
        {
            return new ArrayTypeSymbol(TypeSymbols.Error);
        }

        var elementType = CheckExpression(expression.Elements[0]);
        for (var i = 1; i < expression.Elements.Count; i++)
        {
            var current = CheckExpression(expression.Elements[i]);
            if (!AreCompatible(elementType, current))
            {
                _result.Diagnostics.Report(expression.Elements[i].Span,
                    $"array elements must have matching types, expected '{elementType}', got '{current}'",
                    "T114");
                return new ArrayTypeSymbol(TypeSymbols.Error);
            }
        }

        return new ArrayTypeSymbol(elementType);
    }

    private TypeSymbol CheckIndexExpression(IndexExpression expression)
    {
        var left = CheckExpression(expression.Left);
        var index = CheckExpression(expression.Index);

        if (index != TypeSymbols.Int && index != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Index.Span,
                $"array index must be 'int', got '{index}'",
                "T115");
        }

        if (left is ArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType;
        }

        if (left != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Left.Span,
                $"cannot index expression of type '{left}'",
                "T116");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol RequireNumericOperands(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (TryGetBinaryNumericResultType(left, right, out var resultType))
        {
            return resultType;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires matching numeric operands, got '{left}' and '{right}'",
                "T103");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol RequireComparableNumericTypes(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (TryGetBinaryNumericResultType(left, right, out _))
        {
            return TypeSymbols.Bool;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires matching numeric operands, got '{left}' and '{right}'",
                "T103");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol RequireComparable(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (AreCompatible(left, right))
        {
            return TypeSymbols.Bool;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires compatible operand types, got '{left}' and '{right}'",
                "T103");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol RequireBothBool(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (left == TypeSymbols.Bool && right == TypeSymbols.Bool)
        {
            return TypeSymbols.Bool;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires 'bool' operands, got '{left}' and '{right}'",
                "T103");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol ReportPrefixTypeError(PrefixExpression expression, TypeSymbol actualType, string expectedTypeName)
    {
        if (actualType != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires '{expectedTypeName}', got '{actualType}'",
                "T103");
        }
        return TypeSymbols.Error;
    }

    private TypeSymbol ReportUnknownOperator(IExpression expression, string op)
    {
        _result.Diagnostics.Report(expression.Span, $"unknown operator '{op}'", "T103");
        return TypeSymbols.Error;
    }

    private bool IsAssignable(TypeSymbol source, TypeSymbol target)
    {
        if (source == TypeSymbols.Error || target == TypeSymbols.Error)
        {
            return true;
        }

        if (CanWidenNumeric(source, target))
        {
            return true;
        }

        if (source is ClassTypeSymbol classType && target is InterfaceTypeSymbol interfaceType)
        {
            if (!_classDefinitions.TryGetValue(classType.ClassName, out var classDefinition) ||
                !classDefinition.ImplementedInterfaces.Contains(interfaceType.InterfaceName))
            {
                return false;
            }

            if (interfaceType.TypeArguments.Count == 0)
            {
                return true;
            }

            if (_interfaceDefinitions.TryGetValue(interfaceType.InterfaceName, out var interfaceDefinition) &&
                interfaceDefinition.TypeParameters.Count == classType.TypeArguments.Count &&
                interfaceType.TypeArguments.Count == classType.TypeArguments.Count)
            {
                for (var i = 0; i < interfaceType.TypeArguments.Count; i++)
                {
                    if (!TypeEquals(interfaceType.TypeArguments[i], classType.TypeArguments[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        return TypeEquals(source, target);
    }

    private static bool AreCompatible(TypeSymbol left, TypeSymbol right)
    {
        if (left == TypeSymbols.Error || right == TypeSymbols.Error)
        {
            return true;
        }

        return TypeEquals(left, right);
    }

    private static bool TypeEquals(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is ArrayTypeSymbol leftArray && right is ArrayTypeSymbol rightArray)
        {
            return TypeEquals(leftArray.ElementType, rightArray.ElementType);
        }

        if (left is FunctionTypeSymbol leftFunction && right is FunctionTypeSymbol rightFunction)
        {
            if (!TypeEquals(leftFunction.ReturnType, rightFunction.ReturnType))
            {
                return false;
            }

            if (leftFunction.ParameterTypes.Count != rightFunction.ParameterTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftFunction.ParameterTypes.Count; i++)
            {
                if (!TypeEquals(leftFunction.ParameterTypes[i], rightFunction.ParameterTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is EnumTypeSymbol leftEnum && right is EnumTypeSymbol rightEnum)
        {
            if (!string.Equals(leftEnum.EnumName, rightEnum.EnumName, StringComparison.Ordinal) ||
                leftEnum.TypeArguments.Count != rightEnum.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftEnum.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftEnum.TypeArguments[i], rightEnum.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is ClassTypeSymbol leftClass && right is ClassTypeSymbol rightClass)
        {
            if (!string.Equals(leftClass.ClassName, rightClass.ClassName, StringComparison.Ordinal) ||
                leftClass.TypeArguments.Count != rightClass.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftClass.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftClass.TypeArguments[i], rightClass.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is InterfaceTypeSymbol leftInterface && right is InterfaceTypeSymbol rightInterface)
        {
            if (!string.Equals(leftInterface.InterfaceName, rightInterface.InterfaceName, StringComparison.Ordinal) ||
                leftInterface.TypeArguments.Count != rightInterface.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftInterface.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftInterface.TypeArguments[i], rightInterface.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return left == right;
    }

    private static bool IsNumericType(TypeSymbol type)
    {
        return type == TypeSymbols.Int ||
               type == TypeSymbols.Long ||
               type == TypeSymbols.Float ||
               type == TypeSymbols.Double;
    }

    private static bool TryGetUnaryNegationResultType(TypeSymbol operandType, out TypeSymbol resultType)
    {
        resultType = TypeSymbols.Error;
        if (operandType != TypeSymbols.Int && operandType != TypeSymbols.Long && operandType != TypeSymbols.Float && operandType != TypeSymbols.Double)
        {
            return false;
        }

        resultType = operandType;
        return true;
    }

    private static bool TryGetBinaryNumericResultType(TypeSymbol left, TypeSymbol right, out TypeSymbol resultType)
    {
        resultType = TypeSymbols.Error;
        if (!IsNumericType(left) || !IsNumericType(right))
        {
            return false;
        }

        if (!TypeEquals(left, right))
        {
            return false;
        }

        resultType = left;
        return true;
    }

    private static bool CanWidenNumeric(TypeSymbol source, TypeSymbol target)
    {
        if (source == TypeSymbols.Char && target == TypeSymbols.Char)
        {
            return true;
        }

        if (source == TypeSymbols.Char)
        {
            return target == TypeSymbols.Int || target == TypeSymbols.Long || target == TypeSymbols.Float || target == TypeSymbols.Double;
        }

        if (source == TypeSymbols.Byte)
        {
            return target == TypeSymbols.Byte || target == TypeSymbols.Int || target == TypeSymbols.Long || target == TypeSymbols.Float || target == TypeSymbols.Double;
        }

        if (!IsNumericType(source) || !IsNumericType(target))
        {
            return false;
        }

        return GetNumericRank(source) <= GetNumericRank(target);
    }

    private static int GetNumericRank(TypeSymbol type)
    {
        if (type == TypeSymbols.Byte)
        {
            return 1;
        }

        if (type == TypeSymbols.Int)
        {
            return 2;
        }

        if (type == TypeSymbols.Long)
        {
            return 3;
        }

        if (type == TypeSymbols.Float)
        {
            return 4;
        }

        if (type == TypeSymbols.Double)
        {
            return 5;
        }

        return int.MaxValue;
    }
}
