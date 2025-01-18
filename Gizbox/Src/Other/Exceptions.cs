using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox
{
    public enum ExceptionType
    {
        Unknown,
        LibraryLoadPathNotSet,
        LibraryFileNotFound,
        LibraryDependencyCannotBeEmpty,
        LibraryFileNameMismatch,
        LabelNotFound,
        TypeNotFound,
        OperationTypeError,
        ExpressionNodeIsNull,
        ProductionNotFound,
        TypeConversionError,
        AssignmentTypeError,
        CannotAssignToConstant,
        CannotAssignToLiteral,
        TacError,
        UnknownConstant,
        UnknownLiteral,
        NotPointerToConstant,
        InvalidHeapWrite,
        StackOverflow,
        NoInstructionsToExecute,
        ArgumentError,
        AccessError,

        LexicalAnalysisError,

        SyntaxAnalysisError,

        SubExpressionNoReturnVariable,
        TypeNotSet,
        FunctionObfuscationNameNotSet,
        EmptyImportNode,
        ExternFunctionGlobalOrNamespaceOnly,
        ClassNameCannotBeObject,
        ClassDefinitionGlobalOrNamespaceOnly,
        NamespaceTopLevelNonMemberFunctionOnly,
        BaseClassNotFound,
        VariableTypeDeclarationError,
        MissingReturnStatement,
        ReturnTypeError,
        InvalidDeleteStatement,
        ClassSymbolTableNotFound,
        NodeNoInitializationPropertyList,
        ClassMemberFunctionThisKeywordMissing,
        IdentifierNotFound,
        LiteralTypeUnknown,
        MissingThisPtrInSymbolTable,
        ClassNameNotFound,
        ClassScopeNotFound,
        FunctionMemberNotFound,
        ObjectMemberNotFunction,
        FunctionNotFound,
        ClassDefinitionNotFound,
        InconsistentExpressionTypesCannotCompare,
        BinaryOperationTypeMismatch,
        CannotAnalyzeExpressionNodeType,
        IdentifierAmbiguousBetweenNamespaces,
        MemberFieldNotFound,

        ScriptRuntimeError,
        OnlyHeapObjectsCanBeFreed,
        AccessedObjectNotFound,
        ObjectTypeError,
        ObjectFieldNotInitialized,
        ParameterListParameterNotFound,
        LocalVariableNotInitialized,
        GlobalVariableNotInitialized,
        NotParameterOrLocalVariable,
        StackFrameAndGlobalSymbolTableNameNotFound,
        OnlyBooleanValuesCanBeNegated,
        TypeCannotBeNegative,
    }


    public class GizboxException: System.Exception
    {
        public ExceptionType exType;
        public string appendMsg;
        public GizboxException(ExceptionType extype = ExceptionType.Unknown, string appendMsg = "") : base(appendMsg) 
        { 
            this.exType = extype;
            this.appendMsg = appendMsg;
        }

        public override string Message
        {
            get
            {
                return "\n -> " + Localization.GetString(exType.ToString()) + " <- \n" + "(" + appendMsg + ")";
            }
        }
    }


    public class LexerException : GizboxException
    {
        public int line;
        public int charinline;
        public string scanning;
        public LexerException(ExceptionType etype, int line, int charInLine, string scanningText, string message) : base(etype, message) 
        {
            this.line = line;
            this.charinline = charInLine;
            this.scanning = scanningText;
        }

        public override string Message => "(line:" + line + "  scanning:\"" + scanning + "\")" +  base.Message;
    }
    public class ParseException : GizboxException
    {
        public Token token;
        public ParseException(ExceptionType etype, Token token, string message) : base(etype, message)
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
        public SemanticException(ExceptionType etype, SyntaxTree.Node astNode, string message): base(etype, message)
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
        public RuntimeException(ExceptionType etype, Gizbox.ScriptEngine.RuntimeCode c, string message):base(etype, message)
        {
            this.code = c;
        }
        public string TacMessage()
        {
            if(code != null)
            {
                return "\"" + code.ToExpression() + "\"";
            }
            else
            {
                return "";
            }
        }
        public override string Message => TacMessage() + base.Message;
    }
}
