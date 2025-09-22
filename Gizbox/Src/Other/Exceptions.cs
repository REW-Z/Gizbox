using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox
{
    public enum ExceptioName
    {
        Undefine,

        //0.
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
        NoInstructionsToExecute,
        ArgumentError,
        AccessError,


        //1.
        LexicalAnalysisError,

        //2.
        SyntaxAnalysisError,

        //3.
        SemanticAnalysysError,
        SubExpressionNoReturnVariable,
        TypeNotSet,
        FunctionObfuscationNameNotSet,
        EmptyImportNode,
        ExternFunctionGlobalOrNamespaceOnly,
        ConstantGlobalOrNamespaceOnly,
        ClassNameCannotBeObject,
        ClassDefinitionGlobalOrNamespaceOnly,
        NamespaceTopLevelNonMemberFunctionOnly,
        BaseClassNotFound,
        ConstantTypeDeclarationError,
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


        //4.
        ScriptRuntimeError,
        StackOverflow,
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

        //5.
        CodeGen,

        //6.
        Link
    }


    public class GizboxException: System.Exception
    {
        public ExceptioName exType;
        public string appendMsg;
        public GizboxException(ExceptioName extype = ExceptioName.Undefine, string appendMsg = "") : base(appendMsg) 
        { 
            this.exType = extype;
            this.appendMsg = appendMsg;
        }

        public override string Message
        {
            get
            {
                return "\n \"" + Localization.GetString(exType.ToString()) + "\" \n" + "(" + appendMsg + ")";
            }
        }
    }


    public class LexerException : GizboxException
    {
        public int line;
        public int charinline;
        public string scanning;
        public LexerException(ExceptioName etype, int line, int charInLine, string scanningText, string message) : base(etype, message) 
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
        public ParseException(ExceptioName etype, Token token, string message) : base(etype, message)
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
        public SemanticException(ExceptioName etype, SyntaxTree.Node astNode, string message): base(etype, message)
        {
            this.node = astNode;
        }
        public string LineMessage()
        {
            return "(line:" + (node?.StartToken()?.line ?? -1) + ")";
        }

        public override string Message => LineMessage() + base.Message;
    }

    public class RuntimeException : GizboxException
    {
        public Gizbox.ScriptEngine.RuntimeCode code;
        public RuntimeException(ExceptioName etype, Gizbox.ScriptEngine.RuntimeCode c, string message):base(etype, message)
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
