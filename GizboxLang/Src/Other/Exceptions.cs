using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox
{
    public class GizboxException: System.Exception
    {
        public GizboxException(string message) : base(message) { }
    }
    public class ParseException : GizboxException
    {
        public Token token;
        public ParseException(Token token, string message) : base(message)
        {
            this.token = token;
        }

        public string LineMessage()
        {
            return "(line:" + token.line + ")";
        }

        public override string Message => LineMessage() + base.Message;
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
        public Gizbox.IL.TAC tac;
        public RuntimeException(Gizbox.IL.TAC tac, string message):base(message)
        {
            this.tac = tac;
        }
        public string TacMessage()
        {
            return "\"" + tac.ToExpression() + "\"";
        }
        public override string Message => TacMessage() + base.Message;
    }
}
