using Kong.Common;
using Kong.Lexing;
using System.Globalization;

namespace Kong.Parsing;

internal sealed class DeclarationParser
{
    private readonly ParserContext _context;
    private StatementParser _statementParser = null!;
    private ExpressionParser _expressionParser = null!;
    private TypeParser _typeParser = null!;

    internal DeclarationParser(ParserContext context)
    {
        _context = context;
    }

    internal void Initialize(StatementParser statementParser, ExpressionParser expressionParser, TypeParser typeParser)
    {
        _statementParser = statementParser;
        _expressionParser = expressionParser;
        _typeParser = typeParser;
    }

    internal FunctionDeclaration? ParseFunctionDeclaration()
    {
        var startSpan = _context.CurToken.Span;
        var isPublic = false;

        if (_context.CurTokenIs(TokenType.Public))
        {
            isPublic = true;
            _context.NextToken();
        }

        if (!_context.CurTokenIs(TokenType.Function))
        {
            _context.Diagnostics.Report(_context.CurToken.Span,
                $"expected function declaration after visibility modifier, got {_context.CurToken.Type}",
                "P006");
            return null;
        }

        var declaration = new FunctionDeclaration
        {
            Token = _context.CurToken,
            IsPublic = isPublic,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        declaration.Name = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.LessThan))
        {
            declaration.TypeParameters = ParseTypeParameterList();
            if (declaration.TypeParameters.Count == 0)
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.LeftParenthesis))
        {
            return null;
        }

        declaration.Parameters = ParseFunctionParameters();

        if (_context.PeekTokenIs(TokenType.Arrow))
        {
            _context.NextToken();
            _context.NextToken();

            declaration.ReturnTypeAnnotation = _typeParser.ParseTypeNode();
            if (declaration.ReturnTypeAnnotation == null)
            {
                return null;
            }
        }
        else
        {
            declaration.ReturnTypeAnnotation = CreateImplicitVoidType(declaration.Name.Span);
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        declaration.Body = _statementParser.ParseBlockStatement();
        declaration.Span = new Span(startSpan.Start, declaration.Body.Span.End);
        return declaration;
    }

    internal EnumDeclaration? ParseEnumDeclaration()
    {
        var startSpan = _context.CurToken.Span;
        var declaration = new EnumDeclaration
        {
            Token = _context.CurToken,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        declaration.Name = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.LessThan))
        {
            declaration.TypeParameters = ParseTypeParameterList();
            if (declaration.TypeParameters.Count == 0)
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        declaration.Variants = ParseEnumVariants();
        if (declaration.Variants.Count == 0 && !_context.CurTokenIs(TokenType.RightBrace))
        {
            return null;
        }

        declaration.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return declaration;
    }

    internal ClassDeclaration? ParseClassDeclaration(bool isPublic = false)
    {
        var startSpan = _context.CurToken.Span;
        var declaration = new ClassDeclaration
        {
            Token = _context.CurToken,
            IsPublic = isPublic,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        declaration.Name = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.LessThan))
        {
            declaration.TypeParameters = ParseTypeParameterList();
            if (declaration.TypeParameters.Count == 0)
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        declaration.Fields = ParseClassFields();
        if (declaration.Fields.Count == 0 && !_context.CurTokenIs(TokenType.RightBrace))
        {
            return null;
        }

        declaration.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return declaration;
    }

    internal List<FieldDeclaration> ParseClassFields()
    {
        var fields = new List<FieldDeclaration>();
        if (_context.PeekTokenIs(TokenType.RightBrace))
        {
            _context.NextToken();
            return fields;
        }

        while (true)
        {
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return [];
            }

            var startSpan = _context.CurToken.Span;
            var field = new FieldDeclaration
            {
                Token = _context.CurToken,
                Name = new Identifier
                {
                    Token = _context.CurToken,
                    Value = _context.CurToken.Literal,
                    Span = _context.CurToken.Span,
                },
            };

            if (!_context.ExpectPeek(TokenType.Colon))
            {
                return [];
            }

            _context.NextToken();
            field.TypeAnnotation = _typeParser.ParseTypeNode()!;
            if (field.TypeAnnotation == null)
            {
                return [];
            }

            field.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
            fields.Add(field);

            if (_context.PeekTokenIs(TokenType.RightBrace))
            {
                _context.NextToken();
                break;
            }
        }

        return fields;
    }

    internal InterfaceDeclaration? ParseInterfaceDeclaration(bool isPublic = false)
    {
        var startSpan = _context.CurToken.Span;
        var declaration = new InterfaceDeclaration
        {
            Token = _context.CurToken,
            IsPublic = isPublic,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        declaration.Name = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.LessThan))
        {
            declaration.TypeParameters = ParseTypeParameterList();
            if (declaration.TypeParameters.Count == 0)
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        declaration.Methods = ParseInterfaceMethodSignatures();
        if (declaration.Methods.Count == 0 && !_context.CurTokenIs(TokenType.RightBrace))
        {
            return null;
        }

        declaration.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return declaration;
    }

    internal List<InterfaceMethodSignature> ParseInterfaceMethodSignatures()
    {
        var methods = new List<InterfaceMethodSignature>();
        if (_context.PeekTokenIs(TokenType.RightBrace))
        {
            _context.NextToken();
            return methods;
        }

        while (true)
        {
            if (!_context.ExpectPeek(TokenType.Function))
            {
                return [];
            }

            var method = ParseInterfaceMethodSignature();
            if (method == null)
            {
                return [];
            }

            methods.Add(method);

            if (_context.PeekTokenIs(TokenType.RightBrace))
            {
                _context.NextToken();
                break;
            }
        }

        return methods;
    }

    internal InterfaceMethodSignature? ParseInterfaceMethodSignature()
    {
        var startSpan = _context.CurToken.Span;
        var signature = new InterfaceMethodSignature
        {
            Token = _context.CurToken,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        signature.Name = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.LessThan))
        {
            signature.TypeParameters = ParseTypeParameterList();
            if (signature.TypeParameters.Count == 0)
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.LeftParenthesis))
        {
            return null;
        }

        signature.Parameters = ParseFunctionParameters();
        if (signature.Parameters == null)
        {
            return null;
        }

        if (_context.PeekTokenIs(TokenType.Arrow))
        {
            _context.NextToken();
            _context.NextToken();
            signature.ReturnTypeAnnotation = _typeParser.ParseTypeNode();
            if (signature.ReturnTypeAnnotation == null)
            {
                return null;
            }
        }
        else
        {
            signature.ReturnTypeAnnotation = CreateImplicitVoidType(signature.Name.Span);
        }

        signature.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return signature;
    }

    internal IStatement? ParseImplBlock()
    {
        var startSpan = _context.CurToken.Span;
        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        var firstName = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.For))
        {
            _context.NextToken();
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return null;
            }

            var typeName = new Identifier
            {
                Token = _context.CurToken,
                Value = _context.CurToken.Literal,
                Span = _context.CurToken.Span,
            };

            if (!_context.ExpectPeek(TokenType.LeftBrace))
            {
                return null;
            }

            var interfaceImpl = new InterfaceImplBlock
            {
                Token = new Token(TokenType.Impl, "impl", startSpan),
                InterfaceName = firstName,
                TypeName = typeName,
            };

            interfaceImpl.Methods = ParseImplMethods(allowInit: false, out _);
            if (interfaceImpl.Methods.Count == 0 && !_context.CurTokenIs(TokenType.RightBrace))
            {
                return null;
            }

            interfaceImpl.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
            return interfaceImpl;
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        var impl = new ImplBlock
        {
            Token = new Token(TokenType.Impl, "impl", startSpan),
            TypeName = firstName,
        };

        ParseInherentImplMembers(impl);
        if (impl.Methods.Count == 0 && impl.Constructor == null && !_context.CurTokenIs(TokenType.RightBrace))
        {
            return null;
        }

        impl.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return impl;
    }

    internal void ParseInherentImplMembers(ImplBlock impl)
    {
        var methods = ParseImplMethods(allowInit: true, constructor: out var constructor);
        impl.Constructor = constructor;
        impl.Methods = methods;
    }

    internal List<MethodDeclaration> ParseImplMethods(bool allowInit, out ConstructorDeclaration? constructor)
    {
        constructor = null;
        var methods = new List<MethodDeclaration>();

        if (_context.PeekTokenIs(TokenType.RightBrace))
        {
            _context.NextToken();
            return methods;
        }

        while (true)
        {
            _context.NextToken();

            if (allowInit && _context.CurTokenIs(TokenType.Init))
            {
                var parsedConstructor = ParseConstructorDeclaration();
                if (parsedConstructor == null)
                {
                    return [];
                }

                constructor = parsedConstructor;
            }
            else
            {
                var method = ParseMethodDeclaration();
                if (method == null)
                {
                    return [];
                }

                methods.Add(method);
            }

            if (_context.PeekTokenIs(TokenType.RightBrace))
            {
                _context.NextToken();
                break;
            }
        }

        return methods;
    }

    internal ConstructorDeclaration? ParseConstructorDeclaration()
    {
        var startSpan = _context.CurToken.Span;
        var declaration = new ConstructorDeclaration
        {
            Token = _context.CurToken,
        };

        if (!_context.ExpectPeek(TokenType.LeftParenthesis))
        {
            return null;
        }

        declaration.Parameters = ParseFunctionParameters();
        if (declaration.Parameters == null)
        {
            return null;
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        declaration.Body = _statementParser.ParseBlockStatement();
        declaration.Span = new Span(startSpan.Start, declaration.Body.Span.End);
        return declaration;
    }

    internal MethodDeclaration? ParseMethodDeclaration()
    {
        var startSpan = _context.CurToken.Span;
        var isPublic = false;
        if (_context.CurTokenIs(TokenType.Public))
        {
            isPublic = true;
            _context.NextToken();
        }

        if (!_context.CurTokenIs(TokenType.Function))
        {
            _context.Diagnostics.Report(_context.CurToken.Span,
                $"expected method declaration, got {_context.CurToken.Type}",
                "P006");
            return null;
        }

        var declaration = new MethodDeclaration
        {
            Token = _context.CurToken,
            IsPublic = isPublic,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        declaration.Name = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.LessThan))
        {
            declaration.TypeParameters = ParseTypeParameterList();
            if (declaration.TypeParameters.Count == 0)
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.LeftParenthesis))
        {
            return null;
        }

        declaration.Parameters = ParseFunctionParameters();
        if (declaration.Parameters == null)
        {
            return null;
        }

        if (_context.PeekTokenIs(TokenType.Arrow))
        {
            _context.NextToken();
            _context.NextToken();
            declaration.ReturnTypeAnnotation = _typeParser.ParseTypeNode();
            if (declaration.ReturnTypeAnnotation == null)
            {
                return null;
            }
        }
        else
        {
            declaration.ReturnTypeAnnotation = CreateImplicitVoidType(declaration.Name.Span);
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        declaration.Body = _statementParser.ParseBlockStatement();
        declaration.Span = new Span(startSpan.Start, declaration.Body.Span.End);
        return declaration;
    }

    internal List<Identifier> ParseTypeParameterList()
    {
        var parameters = new List<Identifier>();

        if (!_context.ExpectPeek(TokenType.LessThan))
        {
            return [];
        }

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return [];
        }

        parameters.Add(new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        });

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return [];
            }

            parameters.Add(new Identifier
            {
                Token = _context.CurToken,
                Value = _context.CurToken.Literal,
                Span = _context.CurToken.Span,
            });
        }

        if (!_context.ExpectPeek(TokenType.GreaterThan))
        {
            return [];
        }

        return parameters;
    }

    internal static NamedType CreateImplicitVoidType(Span span)
    {
        return new NamedType
        {
            Token = new Token(TokenType.Identifier, "void"),
            Name = "void",
            Span = span,
        };
    }

    internal List<EnumVariant> ParseEnumVariants()
    {
        var variants = new List<EnumVariant>();

        if (_context.PeekTokenIs(TokenType.RightBrace))
        {
            _context.NextToken();
            return variants;
        }

        while (true)
        {
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return [];
            }

            var variantStart = _context.CurToken.Span;
            var variant = new EnumVariant
            {
                Token = _context.CurToken,
                Name = new Identifier
                {
                    Token = _context.CurToken,
                    Value = _context.CurToken.Literal,
                    Span = _context.CurToken.Span,
                },
            };

            if (_context.PeekTokenIs(TokenType.LeftParenthesis))
            {
                _context.NextToken();
                variant.PayloadTypes = ParseVariantPayloadTypes();
                if (variant.PayloadTypes.Count == 0 && !_context.CurTokenIs(TokenType.RightParenthesis))
                {
                    return [];
                }
            }

            variant.Span = new Span(variantStart.Start, _context.CurToken.Span.End);
            variants.Add(variant);

            if (_context.PeekTokenIs(TokenType.Comma))
            {
                _context.NextToken();
                if (_context.PeekTokenIs(TokenType.RightBrace))
                {
                    _context.NextToken();
                    break;
                }

                continue;
            }

            if (!_context.ExpectPeek(TokenType.RightBrace))
            {
                return [];
            }

            break;
        }

        return variants;
    }

    internal List<ITypeNode> ParseVariantPayloadTypes()
    {
        var payloadTypes = new List<ITypeNode>();

        if (_context.PeekTokenIs(TokenType.RightParenthesis))
        {
            _context.NextToken();
            return payloadTypes;
        }

        _context.NextToken();
        var firstPayloadType = _typeParser.ParseTypeNode();
        if (firstPayloadType == null)
        {
            return [];
        }

        payloadTypes.Add(firstPayloadType);

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            _context.NextToken();

            var nextPayloadType = _typeParser.ParseTypeNode();
            if (nextPayloadType == null)
            {
                return [];
            }

            payloadTypes.Add(nextPayloadType);
        }

        if (!_context.ExpectPeek(TokenType.RightParenthesis))
        {
            return [];
        }

        return payloadTypes;
    }

    internal List<FunctionParameter> ParseFunctionParameters()
    {
        var parameters = new List<FunctionParameter>();

        if (_context.PeekTokenIs(TokenType.RightParenthesis))
        {
            _context.NextToken();
            return parameters;
        }

        _context.NextToken();

        var parameter = ParseFunctionParameter();
        if (parameter == null)
        {
            return null!;
        }
        parameters.Add(parameter);

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            _context.NextToken();
            parameter = ParseFunctionParameter();
            if (parameter == null)
            {
                return null!;
            }
            parameters.Add(parameter);
        }

        if (!_context.ExpectPeek(TokenType.RightParenthesis))
        {
            return null!;
        }

        return parameters;
    }

    internal FunctionParameter? ParseFunctionParameter()
    {
        if (!_context.CurTokenIs(TokenType.Identifier) && !_context.CurTokenIs(TokenType.Self))
        {
            _context.Diagnostics.Report(_context.CurToken.Span, $"expected parameter name, got {_context.CurToken.Type}", "P005");
            return null;
        }

        var parameter = new FunctionParameter
        {
            Token = _context.CurToken,
            Name = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.Colon))
        {
            _context.NextToken();
            _context.NextToken();

            parameter.TypeAnnotation = _typeParser.ParseTypeNode();
            if (parameter.TypeAnnotation == null)
            {
                return null;
            }

            parameter.Span = new Span(parameter.Span.Start, parameter.TypeAnnotation.Span.End);
        }

        return parameter;
    }

}
