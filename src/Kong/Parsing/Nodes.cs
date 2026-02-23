using System.Text;
using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

public class CompilationUnit : INode
{
    public Span Span { get; set; }
    public List<IStatement> Statements { get; set; } = [];

    public string TokenLiteral()
    {
        return Statements.Count > 0 ? Statements[0].TokenLiteral() : "";
    }

    public string String()
    {
        var sb = new StringBuilder();
        foreach (var s in Statements)
        {
            sb.Append(s.String());
        }
        return sb.ToString();
    }
}

public class LetStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Let token
    public Identifier Name { get; set; } = null!;
    public ITypeNode? TypeAnnotation { get; set; }
    public IExpression? Value { get; set; }
    public bool IsMutable { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append(TokenLiteral() + " ");
        sb.Append(Name.String());
        if (TypeAnnotation != null)
        {
            sb.Append(": ");
            sb.Append(TypeAnnotation.String());
        }
        sb.Append(" = ");
        if (Value != null)
        {
            sb.Append(Value.String());
        }
        sb.Append(';');
        return sb.ToString();
    }
}

public class AssignmentStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the identifier token on the LHS
    public Identifier Name { get; set; } = null!;
    public IExpression Value { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Name.String()} = {Value.String()};";
    }
}

public class IndexAssignmentStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '=' token
    public IndexExpression Target { get; set; } = null!;
    public IExpression Value { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Target.String()} = {Value.String()};";
    }
}

public class MemberAssignmentStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '=' token
    public MemberAccessExpression Target { get; set; } = null!;
    public IExpression Value { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Target.String()} = {Value.String()};";
    }
}

public class BreakStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String() => "break;";
}

public class ContinueStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String() => "continue;";
}

public class Identifier : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Identifier token
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Value;
}

public class FunctionParameter : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the parameter identifier token
    public string Name { get; set; } = "";
    public ITypeNode? TypeAnnotation { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        if (TypeAnnotation == null)
        {
            return Name;
        }

        return $"{Name}: {TypeAnnotation.String()}";
    }
}

public class ReturnStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'return' token
    public IExpression? ReturnValue { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append(TokenLiteral() + " ");
        if (ReturnValue != null)
        {
            sb.Append(ReturnValue.String());
        }
        sb.Append(';');
        return sb.ToString();
    }
}

public class FunctionDeclaration : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'fn' token
    public bool IsPublic { get; set; }
    public Identifier Name { get; set; } = null!;
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
            Name = Name.Value,
            Parameters = Parameters,
            ReturnTypeAnnotation = ReturnTypeAnnotation,
            Body = Body,
            Span = Span,
        };
    }
}

public class ExpressionStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the first token of the expression
    public IExpression? Expression { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return Expression?.String() ?? "";
    }
}

public class ImportStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'import' token
    public string QualifiedName { get; set; } = "";

    public string Alias
    {
        get
        {
            var lastDot = QualifiedName.LastIndexOf('.');
            return lastDot < 0 ? QualifiedName : QualifiedName[(lastDot + 1)..];
        }
    }

    public string TokenLiteral() => Token.Literal;

    public string String() => $"import {QualifiedName};";
}

public class NamespaceStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'namespace' token
    public string QualifiedName { get; set; } = "";

    public string TokenLiteral() => Token.Literal;

    public string String() => $"namespace {QualifiedName};";
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
    public List<FieldDeclaration> Fields { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var visibility = IsPublic ? "public " : "";
        return $"{visibility}class {Name.String()} {{ {string.Join(" ", Fields.Select(f => f.String()))} }}";
    }
}

public class FieldDeclaration : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the field identifier token
    public Identifier Name { get; set; } = null!;
    public ITypeNode TypeAnnotation { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;
    public string String() => $"{Name.String()}: {TypeAnnotation.String()};";
}

public class InterfaceDeclaration : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'interface' token
    public bool IsPublic { get; set; }
    public Identifier Name { get; set; } = null!;
    public List<InterfaceMethodSignature> Methods { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var visibility = IsPublic ? "public " : "";
        return $"{visibility}interface {Name.String()} {{ {string.Join(" ", Methods.Select(m => m.String()))} }}";
    }
}

public class InterfaceMethodSignature : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'fn' token
    public Identifier Name { get; set; } = null!;
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var returnType = ReturnTypeAnnotation == null ? string.Empty : $" -> {ReturnTypeAnnotation.String()}";
        return $"fn {Name.String()}({string.Join(", ", Parameters.Select(p => p.String()))}){returnType};";
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
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var visibility = IsPublic ? "public " : "";
        var returnType = ReturnTypeAnnotation == null ? string.Empty : $" -> {ReturnTypeAnnotation.String()}";
        return $"{visibility}fn {Name.String()}({string.Join(", ", Parameters.Select(p => p.String()))}){returnType} {Body.String()}";
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

public class ForInStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'for' token
    public Identifier Iterator { get; set; } = null!;
    public IExpression Iterable { get; set; } = null!;
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"for {Iterator.String()} in {Iterable.String()} {Body.String()}";
    }
}

public class IntegerLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public long Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class DoubleLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public double Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class CharLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public char Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class ByteLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public byte Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class PrefixExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The prefix token, e.g. !
    public string Operator { get; set; } = "";
    public IExpression Right { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"({Operator}{Right.String()})";
    }
}

public class InfixExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The operator token, e.g. +
    public IExpression Left { get; set; } = null!;
    public string Operator { get; set; } = "";
    public IExpression Right { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"({Left.String()} {Operator} {Right.String()})";
    }
}

public class BooleanLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public bool Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class IfExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The 'if' token
    public IExpression Condition { get; set; } = null!;
    public BlockStatement Consequence { get; set; } = null!;
    public BlockStatement? Alternative { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append("if");
        sb.Append(Condition.String());
        sb.Append(' ');
        sb.Append(Consequence.String());
        if (Alternative != null)
        {
            sb.Append("else ");
            sb.Append(Alternative.String());
        }
        return sb.ToString();
    }
}

public class MatchArm : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the variant identifier token
    public Identifier VariantName { get; set; } = null!;
    public List<Identifier> Bindings { get; set; } = [];
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var bindingText = Bindings.Count == 0
            ? ""
            : $"({string.Join(", ", Bindings.Select(b => b.String()))})";
        return $"{VariantName.String()}{bindingText} => {Body.String()}";
    }
}

public class MatchExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'match' token
    public IExpression Target { get; set; } = null!;
    public List<MatchArm> Arms { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"match ({Target.String()}) {{ {string.Join(", ", Arms.Select(a => a.String()))} }}";
    }
}

public class BlockStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the { token
    public List<IStatement> Statements { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        foreach (var s in Statements)
        {
            sb.Append(s.String());
        }
        return sb.ToString();
    }
}

public class FunctionLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The 'fn' token
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }
    public BlockStatement Body { get; set; } = null!;
    public string Name { get; set; } = "";

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        var paramStrings = Parameters.Select(p => p.String());
        sb.Append(TokenLiteral());
        if (Name != "")
        {
            sb.Append($"<{Name}>");
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
}

public class NamedType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Identifier token
    public string Name { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Name;
}

public class GenericType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the base type identifier token
    public string Name { get; set; } = "";
    public List<ITypeNode> TypeArguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;
    public string String() => $"{Name}<{string.Join(", ", TypeArguments.Select(t => t.String()))}>";
}

public class ArrayType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '[' token
    public ITypeNode ElementType { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;
    public string String() => $"{ElementType.String()}[]";
}

public class FunctionType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'fn' token
    public List<ITypeNode> ParameterTypes { get; set; } = [];
    public ITypeNode ReturnType { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var parameters = ParameterTypes.Select(p => p.String());
        return $"fn({string.Join(", ", parameters)}) -> {ReturnType.String()}";
    }
}

public enum CallArgumentModifier
{
    None,
    Out,
    Ref,
}

public class CallArgument : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public CallArgumentModifier Modifier { get; set; }
    public IExpression Expression { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return Modifier switch
        {
            CallArgumentModifier.Out => $"out {Expression.String()}",
            CallArgumentModifier.Ref => $"ref {Expression.String()}",
            _ => Expression.String(),
        };
    }
}

public class CallExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The '(' token
    public IExpression Function { get; set; } = null!; // Identifier or FunctionLiteral
    public List<CallArgument> Arguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var args = Arguments.Select(a => a.String());
        return $"{Function.String()}({string.Join(", ", args)})";
    }
}

public class MemberAccessExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The '.' token
    public IExpression Object { get; set; } = null!;
    public string Member { get; set; } = "";

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Object.String()}.{Member}";
    }
}

public class StringLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class ArrayLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '[' token
    public List<IExpression> Elements { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var elements = Elements.Select(e => e.String());
        return $"[{string.Join(", ", elements)}]";
    }
}

public class IndexExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The [ token
    public IExpression Left { get; set; } = null!;
    public IExpression Index { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"({Left.String()}[{Index.String()}])";
    }
}

public class NewExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'new' token
    public string TypePath { get; set; } = "";
    public List<IExpression> Arguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var args = Arguments.Select(a => a.String());
        return $"new {TypePath}({string.Join(", ", args)})";
    }
}
