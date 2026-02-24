using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

public enum Precedence
{
    Lowest = 0,
    LogicalOr,
    LogicalAnd,
    Equality,
    Comparison,
    Sum,
    Product,
    Prefix,
    FunctionCall,
    Index,
    MemberAccess,
}

public class Parser
{
    private readonly ParserContext _context;
    private readonly StatementParser _statementParser;

    public Parser(Lexer lexer)
    {
        _context = new ParserContext(lexer);

        var typeParser = new TypeParser(_context);
        var expressionParser = new ExpressionParser(_context);
        var declarationParser = new DeclarationParser(_context);
        _statementParser = new StatementParser(_context);

        expressionParser.Initialize(_statementParser, typeParser);
        declarationParser.Initialize(_statementParser, expressionParser, typeParser);
        _statementParser.Initialize(declarationParser, expressionParser, typeParser);
    }

    public DiagnosticBag Diagnostics => _context.Diagnostics;

    public CompilationUnit ParseCompilationUnit()
    {
        var unit = new CompilationUnit();
        var start = _context.CurToken.Span.Start;

        while (!_context.CurTokenIs(TokenType.EndOfFile))
        {
            var statement = _statementParser.ParseStatement();
            if (statement != null)
            {
                unit.Statements.Add(statement);
            }

            _context.NextToken();
        }

        if (unit.Statements.Count > 0)
        {
            unit.Span = new Span(start, unit.Statements[^1].Span.End);
        }

        return unit;
    }
}
