using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox
{
    public class GizboxException: System.Exception
    {
        public GizboxException(string message) : base(message) { }
    }
    public class LexerException : GizboxException
    {
        public int line;
        public string scanning;
        public LexerException(int line, string scanningText, string message) : base(message) 
        {
            this.line = line;
            this.scanning = scanningText;
        }

        public override string Message => "(line:" + line + "  scanning:\"" + scanning + "\")" +  base.Message;
    }
    public class ParseException : GizboxException
    {
        public Token token;
        public ParseException(Token token, string message) : base(message)
        {
            this.token = token;
        }

        public string TokenMessage()
        {
            return "(token:" + token.ToString() + "  line:" + token.line + ")";
        }

        public override string Message => TokenMessage() + base.Message;
    }
    public class SemanticException : GizboxException
    {
        public SyntaxTree.Node node;
        public SemanticException(SyntaxTree.Node astNode, string message): base(message)
        {
            this.node = astNode;
        }
        public string LineMessage()
        {
            return "(line:" + node.FirstToken().line + ")";
        }

        public override string Message => LineMessage() + base.Message;
    }

    public class RuntimeException : GizboxException
    {
        public Gizbox.ScriptEngine.RuntimeCode code;
        public RuntimeException(Gizbox.ScriptEngine.RuntimeCode c, string message):base(message)
        {
            this.code = c;
        }
        public string TacMessage()
        {
            return "\"" + code.ToExpression() + "\"";
        }
        public override string Message => TacMessage() + base.Message;
    }
}
