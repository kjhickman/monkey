using System.Text;
using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

public class FunctionDeclaration : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'fn' token
    public bool IsPublic { get; set; }
    public Identifier Name { get; set; } = null!;
    public List<Identifier> TypeParameters { get; set; } = [];
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        var paramStrings = Parameters.Select(p => p.String());
        if (IsPublic)
        {
            sb.Append("public ");
        }
        sb.Append("fn ");
        sb.Append(Name.String());
        if (TypeParameters.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", TypeParameters.Select(p => p.String())));
            sb.Append('>');
        }
        sb.Append('(');
        sb.Append(string.Join(", ", paramStrings));
        sb.Append(')');
        if (ReturnTypeAnnotation != null)
        {
            sb.Append(" -> ");
            sb.Append(ReturnTypeAnnotation.String());
        }
        sb.Append(' ');
        sb.Append(Body.String());
        return sb.ToString();
    }

    public FunctionLiteral ToFunctionLiteral()
    {
        return new FunctionLiteral
        {
            Token = Token,
            IsLambda = false,
            Name = Name.Value,
            Parameters = Parameters,
            ReturnTypeAnnotation = ReturnTypeAnnotation,
            Body = Body,
            Span = Span,
        };
    }
}

public class EnumDeclaration : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'enum' token
    public Identifier Name { get; set; } = null!;
    public List<Identifier> TypeParameters { get; set; } = [];
    public List<EnumVariant> Variants { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append("enum ");
        sb.Append(Name.String());
        if (TypeParameters.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", TypeParameters.Select(p => p.String())));
            sb.Append('>');
        }

        sb.Append(" { ");
        sb.Append(string.Join(", ", Variants.Select(v => v.String())));
        sb.Append(" }");
        return sb.ToString();
    }
}

public class EnumVariant : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the variant identifier token
    public Identifier Name { get; set; } = null!;
    public List<ITypeNode> PayloadTypes { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        if (PayloadTypes.Count == 0)
        {
            return Name.String();
        }

        return $"{Name.String()}({string.Join(", ", PayloadTypes.Select(t => t.String()))})";
    }
}

public class ClassDeclaration : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'class' token
    public bool IsPublic { get; set; }
    public Identifier Name { get; set; } = null!;
    public List<Identifier> TypeParameters { get; set; } = [];
    public List<FieldDeclaration> Fields { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var visibility = IsPublic ? "public " : "";
        var typeParameters = TypeParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", TypeParameters.Select(p => p.String()))}>";
        return $"{visibility}class {Name.String()}{typeParameters} {{ {string.Join(" ", Fields.Select(f => f.String()))} }}";
    }
}

public class FieldDeclaration : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the field identifier token
    public Identifier Name { get; set; } = null!;
    public ITypeNode TypeAnnotation { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;
    public string String() => $"{Name.String()}: {TypeAnnotation.String()}";
}

public class InterfaceDeclaration : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'interface' token
    public bool IsPublic { get; set; }
    public Identifier Name { get; set; } = null!;
    public List<Identifier> TypeParameters { get; set; } = [];
    public List<InterfaceMethodSignature> Methods { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var visibility = IsPublic ? "public " : "";
        var typeParameters = TypeParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", TypeParameters.Select(p => p.String()))}>";
        return $"{visibility}interface {Name.String()}{typeParameters} {{ {string.Join(" ", Methods.Select(m => m.String()))} }}";
    }
}

public class InterfaceMethodSignature : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'fn' token
    public Identifier Name { get; set; } = null!;
    public List<Identifier> TypeParameters { get; set; } = [];
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var returnType = ReturnTypeAnnotation == null ? string.Empty : $" -> {ReturnTypeAnnotation.String()}";
        var typeParameters = TypeParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", TypeParameters.Select(p => p.String()))}>";
        return $"fn {Name.String()}{typeParameters}({string.Join(", ", Parameters.Select(p => p.String()))}){returnType}";
    }
}

public class ConstructorDeclaration : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'init' token
    public List<FunctionParameter> Parameters { get; set; } = [];
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String() => $"init({string.Join(", ", Parameters.Select(p => p.String()))}) {Body.String()}";
}

public class MethodDeclaration : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'fn' token
    public bool IsPublic { get; set; }
    public Identifier Name { get; set; } = null!;
    public List<Identifier> TypeParameters { get; set; } = [];
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var visibility = IsPublic ? "public " : "";
        var returnType = ReturnTypeAnnotation == null ? string.Empty : $" -> {ReturnTypeAnnotation.String()}";
        var typeParameters = TypeParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", TypeParameters.Select(p => p.String()))}>";
        return $"{visibility}fn {Name.String()}{typeParameters}({string.Join(", ", Parameters.Select(p => p.String()))}){returnType} {Body.String()}";
    }
}

public class ImplBlock : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'impl' token
    public Identifier TypeName { get; set; } = null!;
    public ConstructorDeclaration? Constructor { get; set; }
    public List<MethodDeclaration> Methods { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var members = new List<string>();
        if (Constructor != null)
        {
            members.Add(Constructor.String());
        }

        members.AddRange(Methods.Select(m => m.String()));
        return $"impl {TypeName.String()} {{ {string.Join(" ", members)} }}";
    }
}

public class InterfaceImplBlock : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'impl' token
    public Identifier InterfaceName { get; set; } = null!;
    public Identifier TypeName { get; set; } = null!;
    public List<MethodDeclaration> Methods { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"impl {InterfaceName.String()} for {TypeName.String()} {{ {string.Join(" ", Methods.Select(m => m.String()))} }}";
    }
}
