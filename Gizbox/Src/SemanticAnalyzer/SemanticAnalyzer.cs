using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Gizbox;
using Gizbox.LRParse;
using Gizbox.LALRGenerator;
using Gizbox.SemanticRule;
using Gizbox.IL;
using System.Runtime.CompilerServices;
using static Gizbox.SyntaxTree;




/// <summary>
/// 语义规则  
/// </summary>
namespace Gizbox.SemanticRule
{
    /// <summary>
    /// 语义动作  
    /// </summary>
    public class SemanticAction
    {
        public LRParser parserContext;

        public Production productionContext;

        public System.Action<LRParser, Production> action;

        public SemanticAction(LRParser parser, Production production, System.Action<LRParser, Production> action)
        {
            parserContext = parser;
            productionContext = production;
            this.action = action;
        }

        public void Execute()
        {
            action.Invoke(this.parserContext, this.productionContext);
        }
    }


    /// <summary>
    /// 自底向上的语法分析树构造器  
    /// </summary>
    public class BottomUpParseTreeBuilder
    {
        // 分析树  
        public ParseTree resultTree = new ParseTree();

        // 动作构建  
        public void BuildAction(LRParser parser, Production production)
        {
            //产生式头  
            ParseTree.Node newNode = (parser.newElement.attributes["cst_node"] = new ParseTree.Node() { isLeaf = false, name = production.head.name }) as ParseTree.Node;
            resultTree.allnodes.Add(newNode);

            //产生式体  
            for (int i = 0; i < production.body.Length; ++i)
            {
                int offset = parser.stack.Count - production.body.Length;
                var ele = parser.stack[offset + i];
                var symbol = production.body[i];

                //叶子节点（终结符节点）  
                if (symbol is Terminal)
                {
                    var node = (ele.attributes["cst_node"] = new ParseTree.Node() { isLeaf = true, name = symbol.name + "," + (ele.attributes["token"] as Token).attribute }) as ParseTree.Node;

                    resultTree.allnodes.Add(node);

                    node.parent = newNode;
                    newNode.children.Add(node);
                }
                //内部节点（非终结符节点）
                else
                {
                    var node = ele.attributes["cst_node"] as ParseTree.Node;

                    node.parent = newNode;
                    newNode.children.Add(node);
                }
            }


            if (parser.remainingInput.Count == 1 && parser.remainingInput.Peek().name == "$")
            {
                Accept(parser);
            }

        }

        // 完成  
        public void Accept(LRParser parser)
        {
            //设置根节点  
            resultTree.root = parser.newElement.attributes["cst_node"] as ParseTree.Node;

            //完成构建 （设置深度等操作）  
            resultTree.CompleteBuild();

            SemanticAnalyzer.Log("根节点:" + resultTree.root.name);
            SemanticAnalyzer.Log("节点数:" + resultTree.allnodes.Count);
        }
    }

    /// <summary>
    /// 语义动作执行器  
    /// </summary>
    public class SematicActionExecutor
    {
        private LRParser parserContext;

        public Dictionary<Production, List<SemanticAction>> translateScheme = new Dictionary<Production, List<SemanticAction>>();


        //语法分析树构造    
        public BottomUpParseTreeBuilder parseTreeBuilder;

        //抽象语法树构造    
        public SyntaxTree.ProgramNode syntaxRootNode;

        // 构造  
        public SematicActionExecutor(LRParser parser)
        {
            this.parserContext = parser;

            //语法树构造器  
            parseTreeBuilder = new BottomUpParseTreeBuilder();

            //构建语法分析树的语义动作    
            foreach (var p in parserContext.data.productions)
            {
                this.AddActionAtTail(p, parseTreeBuilder.BuildAction);
            }

            //return; //不附加其他语义规则-仅语法分析

            //构建抽象语法树(AST)的语义动作   
            AddActionAtTail("S -> importations namespaceusings statements", (psr, production) =>
            {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ProgramNode()
                {
                    importNodes = (List<SyntaxTree.ImportNode>)psr.stack[psr.stack.Top - 2].attributes["import_list"],
                    usingNamespaceNodes = (List<SyntaxTree.UsingNode>)psr.stack[psr.stack.Top - 1].attributes["using_list"],
                    statementsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top].attributes["ast_node"],


                    attributes = psr.newElement.attributes,
                };

                this.syntaxRootNode = (SyntaxTree.ProgramNode)psr.newElement.attributes["ast_node"];
            });

            AddActionAtTail("importations -> importations importation", (psr, production) =>
            {
                psr.newElement.attributes["import_list"] = psr.stack[psr.stack.Top - 1].attributes["import_list"];
                ((List<SyntaxTree.ImportNode>)psr.newElement.attributes["import_list"]).Add(
                    (SyntaxTree.ImportNode)psr.stack[psr.stack.Top].attributes["ast_node"]
                ); ;
            });
            AddActionAtTail("importations -> importation", (psr, production) =>
            {
                psr.newElement.attributes["import_list"] = new List<SyntaxTree.ImportNode>();
                ((List<SyntaxTree.ImportNode>)psr.newElement.attributes["import_list"]).Add(
                    (SyntaxTree.ImportNode)psr.stack[psr.stack.Top].attributes["ast_node"]
                ); ;
            });
            AddActionAtTail("importations -> ε", (psr, production) =>
            {
                psr.newElement.attributes["import_list"] = new List<SyntaxTree.ImportNode>();
            });
            AddActionAtTail("importation -> import < LITSTRING >", (psr, production) =>
            {
                string uriRaw = (psr.stack[psr.stack.Top - 1].attributes["token"] as Token).attribute;
                string uri = uriRaw.Substring(1, uriRaw.Length - 2);
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ImportNode()
                {
                    uri = uri,

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("namespaceusings -> namespaceusings namespaceusing", (psr, production) =>
            {
                psr.newElement.attributes["using_list"] = psr.stack[psr.stack.Top - 1].attributes["using_list"];
                ((List<SyntaxTree.UsingNode>)psr.newElement.attributes["using_list"]).Add(
                    (SyntaxTree.UsingNode)psr.stack[psr.stack.Top].attributes["ast_node"]
                ); ;
            });
            AddActionAtTail("namespaceusings -> namespaceusing", (psr, production) =>
            {
                psr.newElement.attributes["using_list"] = new List<SyntaxTree.UsingNode>();
                ((List<SyntaxTree.UsingNode>)psr.newElement.attributes["using_list"]).Add(
                    (SyntaxTree.UsingNode)psr.stack[psr.stack.Top].attributes["ast_node"]
                ); ;
            });
            AddActionAtTail("namespaceusings -> ε", (psr, production) =>
            {
                psr.newElement.attributes["using_list"] = new List<SyntaxTree.UsingNode>();
            });

            AddActionAtTail("namespaceusing -> using ID ;", (psr, production) =>
            {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.UsingNode()
                {
                    namespaceNameNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 1].attributes,
                        token = psr.stack[psr.stack.Top - 1].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Namespace,
                    }
                };
            });



            AddActionAtTail("statements -> statements stmt", (psr, production) => {

                var newStmt = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"];

                psr.newElement.attributes["ast_node"] = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"];
                ((SyntaxTree.StatementsNode)psr.newElement.attributes["ast_node"]).statements.Add(newStmt);
            });
            AddActionAtTail("statements -> stmt", (psr, production) => {

                var newStmt = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"];

                psr.newElement.attributes["ast_node"] = new SyntaxTree.StatementsNode()
                {

                    attributes = psr.newElement.attributes,
                };

                ((SyntaxTree.StatementsNode)psr.newElement.attributes["ast_node"]).statements.Add(newStmt);
            });
            AddActionAtTail("statements -> ε", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.StatementsNode()
                {
                    attributes = psr.newElement.attributes,
                };
            });


            AddActionAtTail("namespaceblock -> namespace ID { statements }", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.NamespaceNode()
                {
                    namepsaceNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 3].attributes,
                        token = psr.stack[psr.stack.Top - 3].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Namespace,
                    },
                    stmtsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("statementblock -> { statements }", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.StatementBlockNode()
                {

                    statements = ((SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"]).statements,

                    attributes = psr.newElement.attributes,
                };
            });



            AddActionAtTail("declstatements -> declstatements declstmt", (psr, production) => {

                var newDeclStmt = (SyntaxTree.DeclareNode)psr.stack[psr.stack.Top].attributes["ast_node"];

                psr.newElement.attributes["decl_stmts"] = (List<SyntaxTree.DeclareNode>)psr.stack[psr.stack.Top - 1].attributes["decl_stmts"];

                ((List<SyntaxTree.DeclareNode>)psr.newElement.attributes["decl_stmts"]).Add(newDeclStmt);
            });

            AddActionAtTail("declstatements -> declstmt", (psr, production) => {

                var newDeclStmt = (SyntaxTree.DeclareNode)psr.stack[psr.stack.Top].attributes["ast_node"];

                psr.newElement.attributes["decl_stmts"] = new List<SyntaxTree.DeclareNode>() { newDeclStmt };
            });

            AddActionAtTail("declstatements -> ε", (psr, production) => {

                psr.newElement.attributes["decl_stmts"] = new List<SyntaxTree.DeclareNode>() { };
            });

            AddActionAtTail("stmt -> namespaceblock", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("stmt -> statementblock", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("stmt -> declstmt", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });


            AddActionAtTail("stmt -> stmtexpr ;", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.SingleExprStmtNode()
                {
                    exprNode = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });


            AddActionAtTail("stmt -> break ;", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.BreakStmtNode()
                {
                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("stmt -> return expr ;", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ReturnStmtNode()
                {
                    returnExprNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            AddActionAtTail("stmt -> return ;", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ReturnStmtNode()
                {
                    returnExprNode = null,

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("stmt -> delete expr ;", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.DeleteStmtNode()
                {
                    objToDelete = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            AddActionAtTail("stmt -> while ( expr ) stmt", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.WhileStmtNode()
                {

                    conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    stmtNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });


            AddActionAtTail("stmt -> for ( stmt bexpr ; stmtexpr ) stmt", (psr, production) =>
            {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ForStmtNode()
                {
                    initializerNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top - 5].attributes["ast_node"],
                    conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 4].attributes["ast_node"],
                    iteratorNode = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    stmtNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });


            AddActionAtTail("stmt -> if ( expr ) stmt elifclauselist elseclause", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.IfStmtNode()
                {
                    conditionClauseList = new List<SyntaxTree.ConditionClauseNode>() {
                        new SyntaxTree.ConditionClauseNode(){
                            conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 4].attributes["ast_node"],
                            thenNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                        },
                    },

                    elseClause = (SyntaxTree.ElseClauseNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };

                ((SyntaxTree.IfStmtNode)psr.newElement.attributes["ast_node"]).conditionClauseList.AddRange(
                        (List<SyntaxTree.ConditionClauseNode>)psr.stack[psr.stack.Top - 1].attributes["condition_clause_list"]
                    );
            });


            AddActionAtTail("declstmt -> type ID = expr ;", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.VarDeclareNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 4].attributes["ast_node"],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 3].attributes,
                        token = psr.stack[psr.stack.Top - 3].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    initializerNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("declstmt -> const type ID = lit ;", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.ConstantDeclareNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 4].attributes["ast_node"],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 3].attributes,
                        token = psr.stack[psr.stack.Top - 3].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    litValNode = (SyntaxTree.LiteralNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };

            });

            AddActionAtTail("declstmt -> type ID ( params ) { statements }", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.FuncDeclareNode()
                {
                    returnTypeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 7].attributes["ast_node"],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 6].attributes,
                        token = psr.stack[psr.stack.Top - 6].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                    },
                    parametersNode = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 4].attributes["ast_node"],
                    statementsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("declstmt -> extern type ID ( params ) ;", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.ExternFuncDeclareNode()
                {
                    returnTypeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 5].attributes["ast_node"],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 4].attributes,
                        token = psr.stack[psr.stack.Top - 4].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                    },
                    parametersNode = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("declstmt -> class ID inherit { declstatements }", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ClassDeclareNode()
                {

                    classNameNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 4].attributes,
                        token = psr.stack[psr.stack.Top - 4].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Class,
                    },

                    memberDelareNodes = new List<SyntaxTree.DeclareNode>(),

                    attributes = psr.newElement.attributes,
                };


                if (psr.stack[psr.stack.Top - 3].attributes.ContainsKey("ast_node"))
                {
                    ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes["ast_node"]).baseClassNameNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top - 3].attributes["ast_node"];
                }
                else
                {
                    ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes["ast_node"]).baseClassNameNode = null;
                }

                ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes["ast_node"]).memberDelareNodes.AddRange(
                    (List<SyntaxTree.DeclareNode>)psr.stack[psr.stack.Top - 1].attributes["decl_stmts"]
                );
            });






            AddActionAtTail("elifclauselist -> ε", (psr, production) => {
                psr.newElement.attributes["condition_clause_list"] = new List<SyntaxTree.ConditionClauseNode>();
            });

            AddActionAtTail("elifclauselist -> elifclauselist elifclause", (psr, production) => {
                psr.newElement.attributes["condition_clause_list"] = psr.stack[psr.stack.Top - 1].attributes["condition_clause_list"];

                ((List<SyntaxTree.ConditionClauseNode>)(psr.newElement.attributes["condition_clause_list"])).Add(
                    (SyntaxTree.ConditionClauseNode)psr.stack[psr.stack.Top].attributes["ast_node"]
                    );
            });

            AddActionAtTail("elifclause -> else if ( expr ) stmt", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ConditionClauseNode()
                {
                    conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    thenNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("elseclause -> ε", (psr, production) => {
                psr.newElement.attributes["ast_node"] = null;
            });

            AddActionAtTail("elseclause -> else stmt", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ElseClauseNode()
                {

                    stmt = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });


            AddActionAtTail("assign -> lvalue = expr", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.AssignNode()
                {
                    op = "=",

                    lvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    rvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            string[] specialAssignOps = new string[] { "+=", "-=", "*=", "/=", "%=" };
            foreach (var assignOp in specialAssignOps)
            {
                AddActionAtTail("assign -> lvalue " + assignOp + " expr", (psr, production) => {

                    psr.newElement.attributes["ast_node"] = new SyntaxTree.AssignNode()
                    {
                        op = assignOp,

                        lvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                        rvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                        attributes = psr.newElement.attributes,
                    };
                });
            }


            AddActionAtTail("lvalue -> ID", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.IdentityNode()
                {
                    attributes = psr.stack[psr.stack.Top].attributes,
                    token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                };
            });

            AddActionAtTail("lvalue -> memberaccess", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ObjectMemberAccessNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("lvalue -> indexaccess", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ElementAccessNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("type -> arrtype", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ArrayTypeNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("type -> stype", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("arrtype -> stypeBracket", (psr, production) => {

                var node = psr.stack[psr.stack.Top].attributes["stype"];

                psr.newElement.attributes["ast_node"] = new SyntaxTree.ArrayTypeNode()
                {
                    elemtentType = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes["stype"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("stype -> ID", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ClassTypeNode()
                {

                    classname = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top].attributes,
                        token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Class,
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("stype -> primitive", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.PrimitiveTypeNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            string[] primiveProductions = new string[] { "void", "bool", "int", "float", "double", "char", "string" };
            foreach (var t in primiveProductions)
            {
                AddActionAtTail("primitive -> " + t, (psr, production) => {
                    psr.newElement.attributes["ast_node"] = new SyntaxTree.PrimitiveTypeNode()
                    {
                        attributes = psr.stack[psr.stack.Top].attributes,
                        token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                    };
                });
            }


            AddActionAtTail("expr -> assign", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("expr -> nexpr", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });




            AddActionAtTail("stmtexpr -> assign", (psr, production) => {

                psr.newElement.attributes["ast_node"] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("stmtexpr -> call", (psr, production) => {

                psr.newElement.attributes["ast_node"] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("stmtexpr -> incdec", (psr, production) => {

                psr.newElement.attributes["ast_node"] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("stmtexpr -> newobj", (psr, production) => {

                psr.newElement.attributes["ast_node"] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });




            AddActionAtTail("nexpr -> bexpr", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("nexpr -> aexpr", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            string[] logicOperators = new string[] { "||", "&&" };
            foreach (var opname in logicOperators)
            {
                AddActionAtTail("bexpr -> bexpr " + opname + " bexpr", (psr, production) => {
                    psr.newElement.attributes["ast_node"] = new SyntaxTree.BinaryOpNode()
                    {
                        op = opname,
                        leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                        rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                        attributes = psr.newElement.attributes,
                    };
                });
            }
            string[] compareOprators = new string[] { ">", "<", ">=", "<=", "==", "!=", };
            foreach (var opname in compareOprators)
            {
                AddActionAtTail("bexpr -> aexpr " + opname + " aexpr", (psr, production) => {
                    psr.newElement.attributes["ast_node"] = new SyntaxTree.BinaryOpNode()
                    {
                        op = opname,
                        leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                        rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                        attributes = psr.newElement.attributes,
                    };
                });
            }
            AddActionAtTail("bexpr -> factor", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });




            AddActionAtTail("aexpr -> aexpr + term", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.BinaryOpNode()
                {
                    op = "+",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            AddActionAtTail("aexpr -> aexpr - term", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.BinaryOpNode()
                {
                    op = "-",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            AddActionAtTail("aexpr -> term", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });


            AddActionAtTail("term -> term * factor", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.BinaryOpNode()
                {
                    op = "*",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            AddActionAtTail("term -> term / factor", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.BinaryOpNode()
                {
                    op = "/",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            AddActionAtTail("term -> term % factor", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.BinaryOpNode()
                {
                    op = "%",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });
            AddActionAtTail("term -> factor", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("factor -> incdec", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("factor -> ! factor", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.UnaryOpNode()
                {
                    op = "!",
                    exprNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("factor -> - factor", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.UnaryOpNode()
                {
                    op = "NEG",
                    exprNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("factor -> cast", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("factor -> primary", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });


            AddActionAtTail("primary -> ( expr )", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"];
            });
            AddActionAtTail("primary -> ID", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.IdentityNode()
                {
                    attributes = psr.stack[psr.stack.Top].attributes,
                    token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                };
            });
            AddActionAtTail("primary -> this", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ThisNode()
                {
                    attributes = psr.stack[psr.stack.Top].attributes,
                    token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                };
            });
            AddActionAtTail("primary -> memberaccess", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("primary -> newobj", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("primary -> newarr", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("primary -> indexaccess", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ElementAccessNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });

            AddActionAtTail("primary -> call", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });


            AddActionAtTail("primary -> lit", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"];
            });


            AddActionAtTail("incdec -> ++ ID", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.IncDecNode()
                {
                    op = "++",
                    isOperatorFront = true,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top].attributes,
                        token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("incdec -> -- ID", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.IncDecNode()
                {
                    op = "--",
                    isOperatorFront = true,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top].attributes,
                        token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("incdec -> ID ++", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.IncDecNode()
                {
                    op = "++",
                    isOperatorFront = false,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 1].attributes,
                        token = psr.stack[psr.stack.Top - 1].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("incdec -> ID --", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.IncDecNode()
                {
                    op = "--",
                    isOperatorFront = false,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 1].attributes,
                        token = psr.stack[psr.stack.Top - 1].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("call -> ID ( args )", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.CallNode()
                {
                    isMemberAccessFunction = false,
                    funcNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 3].attributes,
                        token = psr.stack[psr.stack.Top - 3].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod
                    },
                    argumantsNode = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],


                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("call -> memberaccess ( args )", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.CallNode()
                {
                    isMemberAccessFunction = true,
                    funcNode = (SyntaxTree.ObjectMemberAccessNode)psr.stack[psr.stack.Top - 3].attributes["ast_node"],
                    argumantsNode = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],


                    attributes = psr.newElement.attributes,
                };
            });


            AddActionAtTail("indexaccess -> idBracket", (psr, production) => {

                ((SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes["id"]).identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField;

                psr.newElement.attributes["ast_node"] = new SyntaxTree.ElementAccessNode()
                {
                    isMemberAccessContainer = false,
                    containerNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes["id"],
                    indexNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["optidx"],

                    attributes = psr.newElement.attributes,
                };

            });

            AddActionAtTail("indexaccess -> memberaccess [ aexpr ]", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.ElementAccessNode()
                {
                    isMemberAccessContainer = true,
                    containerNode = (SyntaxTree.ObjectMemberAccessNode)psr.stack[psr.stack.Top - 3].attributes["ast_node"],
                    indexNode = ((SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"]),

                    attributes = psr.newElement.attributes,
                };
            });



            AddActionAtTail("cast -> ( type ) factor", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.CastNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    factorNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"],

                    attributes = psr.newElement.attributes,
                };
            });


            AddActionAtTail("newobj -> new ID ( )", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.NewObjectNode()
                {
                    className = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top - 2].attributes,
                        token = psr.stack[psr.stack.Top - 2].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Class
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("newarr -> new stypeBracket", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.NewArrayNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes["stype"],
                    lengthNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["optidx"],

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("memberaccess -> primary . ID", (psr, production) => {

                psr.newElement.attributes["ast_node"] = new SyntaxTree.ObjectMemberAccessNode()
                {
                    objectNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"],
                    memberNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = psr.stack[psr.stack.Top].attributes,
                        token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                        isMemberIdentifier = true,
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            string[] litTypes = new string[] { "null", "LITINT", "LITFLOAT", "LITDOUBLE", "LITCHAR", "LITSTRING", "LITBOOL" };
            foreach (var litType in litTypes)
            {

                AddActionAtTail("lit -> " + litType, (psr, production) => {
                    psr.newElement.attributes["ast_node"] = new SyntaxTree.LiteralNode()
                    {
                        attributes = psr.stack[psr.stack.Top].attributes,
                        token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                    };
                });
            }


            AddActionAtTail("params -> ε", (psr, production) =>
            {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ParameterListNode()
                {
                    parameterNodes = new List<SyntaxTree.ParameterNode>(), //空的子节点  

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("params -> type ID", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ParameterListNode()
                {
                    parameterNodes = new List<SyntaxTree.ParameterNode>() {
                        new SyntaxTree.ParameterNode(){
                            typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],
                            identifierNode = new SyntaxTree.IdentityNode(){
                                attributes = psr.stack[psr.stack.Top].attributes,
                                token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                                identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                            },
                        }
                    },

                    attributes = psr.newElement.attributes,
                };
            });

            AddActionAtTail("params -> params , type ID", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 3].attributes["ast_node"];

                ((SyntaxTree.ParameterListNode)psr.newElement.attributes["ast_node"]).parameterNodes.Add(
                    new SyntaxTree.ParameterNode()
                    {
                        typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes["ast_node"],
                        identifierNode = new SyntaxTree.IdentityNode()
                        {
                            attributes = psr.stack[psr.stack.Top].attributes,
                            token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                            identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                        },
                    }
                );
            });

            AddActionAtTail("args -> ε", (psr, production) =>
            {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ArgumentListNode()
                {
                    arguments = new List<SyntaxTree.ExprNode>()//空的子节点  
                };
            });

            AddActionAtTail("args -> expr", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.ArgumentListNode()
                {
                    arguments = new List<SyntaxTree.ExprNode>() {
                        (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"]
                    }
                };
            });

            AddActionAtTail("args -> args , expr", (psr, production) => {
                psr.newElement.attributes["ast_node"] = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 2].attributes["ast_node"];

                ((SyntaxTree.ArgumentListNode)psr.newElement.attributes["ast_node"]).arguments.Add(
                    (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes["ast_node"]
                );
            });


            AddActionAtTail("stypeBracket -> idBracket", (psr, production) => {

                ((SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes["id"]).identiferType = SyntaxTree.IdentityNode.IdType.Class;

                psr.newElement.attributes["stype"] = new SyntaxTree.ClassTypeNode()
                {
                    classname = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes["id"],

                    attributes = psr.newElement.attributes
                };

                psr.newElement.attributes["optidx"] = psr.stack[psr.stack.Top].attributes["optidx"];
            });

            AddActionAtTail("stypeBracket -> primitiveBracket", (psr, production) => {
                psr.newElement.attributes["stype"] = psr.stack[psr.stack.Top].attributes["primitive"];
                psr.newElement.attributes["optidx"] = psr.stack[psr.stack.Top].attributes["optidx"];
            });
            AddActionAtTail("idBracket -> ID [ optidx ]", (psr, production) => {

                psr.newElement.attributes["id"] = new SyntaxTree.IdentityNode()
                {
                    attributes = psr.stack[psr.stack.Top - 3].attributes,
                    token = psr.stack[psr.stack.Top - 3].attributes["token"] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.Undefined
                };
                psr.newElement.attributes["optidx"] = psr.stack[psr.stack.Top - 1].attributes["ast_node"];
            });
            AddActionAtTail("primitiveBracket -> primitive [ optidx ]", (psr, production) => {
                psr.newElement.attributes["primitive"] = psr.stack[psr.stack.Top - 3].attributes["ast_node"];
                psr.newElement.attributes["optidx"] = psr.stack[psr.stack.Top - 1].attributes["ast_node"];
            });
            AddActionAtTail("optidx -> aexpr", (psr, production) => {
                psr.newElement.attributes["ast_node"] = psr.stack[psr.stack.Top].attributes["ast_node"];
            });
            AddActionAtTail("optidx -> ε", (psr, production) => {
                psr.newElement.attributes["ast_node"] = null;
            });



            AddActionAtTail("inherit -> : ID", (psr, production) => {
                psr.newElement.attributes["ast_node"] = new SyntaxTree.IdentityNode()
                {
                    attributes = psr.stack[psr.stack.Top].attributes,
                    token = psr.stack[psr.stack.Top].attributes["token"] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.Class
                };
            });
            AddActionAtTail("inherit -> ε", (psr, production) => {
            });
        }

        // 插入语义动作
        public void AddActionAtTail(string productionExpression, System.Action<LRParser, Production> act)
        {
            Production production = parserContext.data.productions.FirstOrDefault(p => p.ToExpression() == productionExpression);

            if (production == null) throw new GizboxException(ExceptioName.ProductionNotFound, productionExpression);

            AddActionAtTail(production, act);
        }
        public void AddActionAtTail(Production production, System.Action<LRParser, Production> act)
        {
            SemanticAction semanticAction = new SemanticAction(this.parserContext, production, act);

            if (translateScheme.ContainsKey(production) == false)
            {
                translateScheme[production] = new List<SemanticAction>();
            }

            translateScheme[production].Add(semanticAction);
        }


        // 执行语义动作  
        public void ExecuteSemanticAction(Production production)
        {
            if (translateScheme.ContainsKey(production) == false) return;

            foreach (var act in translateScheme[production])
            {
                act.Execute();
            }
        }
    }


    /// <summary>
    /// 语义分析器  
    /// </summary>
    public class SemanticAnalyzer//（补充的语义分析器，自底向上规约已经进行了部分语义分析）  
    {
        public Compiler compilerContext;

        public SyntaxTree ast;

        public IL.ILUnit ilUnit;

        private Gizbox.GStack<SymbolTable> envStack;


        //temp  
        private int blockCounter = 0;//Block自增  
        private int ifCounter = 0;//if语句标号自增  
        private int whileCounter = 0;//while语句标号自增  
        private int forCounter = 0;//for语句标号自增  

        //temp  
        private string currentNamespace = "";
        private List<string> namespaceUsings = new List<string>();


        /// <summary>
        /// 构造  
        /// </summary>
        public SemanticAnalyzer(SyntaxTree ast, IL.ILUnit ilUnit, Compiler compilerContext)
        {
            this.compilerContext = compilerContext;

            this.ast = ast;
            this.ilUnit = ilUnit;
        }


        /// <summary>
        /// 开始语义分析  
        /// </summary>
        public void Analysis()
        {
            //Libs    
            foreach (var importNode in ast.rootNode.importNodes)
            {
                if (importNode == null) throw new SemanticException(ExceptioName.EmptyImportNode, ast.rootNode, "");
                this.ilUnit.dependencies.Add(importNode.uri);
            }
            foreach (var lname in this.ilUnit.dependencies)
            {
                var lib = compilerContext.LoadLib(lname);

                foreach (var depNameOfLib in lib.dependencies)
                {
                    var libdep = compilerContext.LoadLib(depNameOfLib);
                    lib.AddDependencyLib(libdep);
                }

                this.ilUnit.AddDependencyLib(lib);
            }

            //global env  
            ast.rootNode.attributes["global_env"] = ilUnit.globalScope.env;


            //Pass1
            envStack = new GStack<SymbolTable>();
            envStack.Push(ilUnit.globalScope.env);
            Pass1_CollectGlobalSymbols(ast.rootNode);


            //Pass2
            envStack.Clear();
            envStack.Push(ilUnit.globalScope.env);
            Pass2_CollectOtherSymbols(ast.rootNode);

            if (Compiler.enableLogSemanticAnalyzer)
            {
                ilUnit.globalScope.env.Print();
                Log("符号表初步收集完毕");
                Compiler.Pause("符号表初步收集完毕");
            }

            //Pass3
            envStack.Clear();
            envStack.Push(ilUnit.globalScope.env);
            Pass3_AnalysisNode(ast.rootNode);
        }

        /// <summary>
        /// PASS1:递归向下顶层定义信息(静态变量、静态函数名、类名)      
        /// </summary>
        private void Pass1_CollectGlobalSymbols(SyntaxTree.Node node)
        {
            ///很多编译器从语法分析阶段甚至词法分析阶段开始初始化和管理符号表    
            ///为了降低复杂性、实现低耦合和模块化，在语义分析阶段和中间代码生成阶段管理符号表  

            bool isTopLevelAtNamespace = false;
            if (node.Parent != null && node.Parent.Parent != null)
            {
                if (node.Parent is SyntaxTree.StatementsNode && node.Parent.Parent is SyntaxTree.NamespaceNode)
                {
                    isTopLevelAtNamespace = true;
                }
            }
            bool isGlobalOrTopNamespace = isTopLevelAtNamespace || envStack.Peek().tableCatagory == SymbolTable.TableCatagory.GlobalScope; ;

            switch (node)
            {
                case SyntaxTree.ProgramNode programNode:
                    {
                        Pass1_CollectGlobalSymbols(programNode.statementsNode);
                    }
                    break;
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        foreach (var stmtNode in stmtsNode.statements)
                        {
                            Pass1_CollectGlobalSymbols(stmtNode);
                        }
                    }
                    break;
                case SyntaxTree.NamespaceNode namespaceNode:
                    {
                        currentNamespace = namespaceNode.namepsaceNode.token.attribute;
                        foreach (var stmtNode in namespaceNode.stmtsNode.statements)
                        {
                            Pass1_CollectGlobalSymbols(stmtNode);
                        }
                        currentNamespace = "";
                    }
                    break;
                //顶级常量声明语句  
                case SyntaxTree.ConstantDeclareNode constDeclNode:
                    {
                        if (isGlobalOrTopNamespace)
                        {
                            //附加命名空间名  
                            if (isTopLevelAtNamespace)
                                constDeclNode.identifierNode.SetPrefix(currentNamespace);

                            //新建符号表条目  
                            var newRec = envStack.Peek().NewRecord(
                                constDeclNode.identifierNode.FullName,
                                SymbolTable.RecordCatagory.Constant,
                                constDeclNode.typeNode.TypeExpression(),

                                initValue: constDeclNode.litValNode.token.attribute
                                );
                        }
                    }
                    break;
                //顶级变量声明语句
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        if (isGlobalOrTopNamespace)
                        {
                            //附加命名空间名  
                            if (isTopLevelAtNamespace)
                                varDeclNode.identifierNode.SetPrefix(currentNamespace);

                            //新建符号表条目  
                            var newRec = envStack.Peek().NewRecord(
                                varDeclNode.identifierNode.FullName,
                                SymbolTable.RecordCatagory.Variable,
                                varDeclNode.typeNode.TypeExpression()
                                );
                        }
                    }
                    break;
                //顶级函数声明语句
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        //（静态函数）    
                        if (isGlobalOrTopNamespace)
                        {

                            bool isMethod = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;
                            if (isMethod) throw new Exception();

                            //附加命名空间名  
                            if (isTopLevelAtNamespace)
                                funcDeclNode.identifierNode.SetPrefix(currentNamespace);


                            //形参类型补全  
                            foreach (var p in funcDeclNode.parametersNode.parameterNodes)
                            {
                                TryCompleteType(p.typeNode);
                            }

                            //符号的类型表达式  
                            string typeExpr = "";
                            for (int i = 0; i < funcDeclNode.parametersNode.parameterNodes.Count; ++i)
                            {
                                if (i != 0) typeExpr += ",";
                                var paramNode = funcDeclNode.parametersNode.parameterNodes[i];
                                typeExpr += (paramNode.typeNode.TypeExpression());
                            }
                            typeExpr += (" -> " + funcDeclNode.returnTypeNode.TypeExpression());


                            //函数修饰名称  
                            var paramTypeArr = funcDeclNode.parametersNode.parameterNodes.Select(n => n.typeNode.TypeExpression()).ToArray();
                            var funcMangledName = Utils.Mangle(funcDeclNode.identifierNode.FullName, paramTypeArr);
                            funcDeclNode.attributes["mangled_name"] = funcMangledName;

                            //新的作用域  
                            string envName = isMethod ? envStack.Peek().name + "." + funcMangledName : funcMangledName;

                            var newEnv = new SymbolTable(envName, SymbolTable.TableCatagory.FuncScope, envStack.Peek());
                            funcDeclNode.attributes["env"] = newEnv;


                            //添加条目  
                            var newRec = envStack.Peek().NewRecord(
                                funcMangledName,
                                SymbolTable.RecordCatagory.Function,
                                typeExpr,
                                newEnv
                                );
                            newRec.rawname = funcDeclNode.identifierNode.FullName;

                        }
                    }
                    break;
                //外部函数声明  
                case SyntaxTree.ExternFuncDeclareNode externFuncDeclNode:
                    {
                        //附加命名空间名称    
                        if (isGlobalOrTopNamespace)
                        {
                            //附加命名空间  
                            if (isTopLevelAtNamespace)
                                externFuncDeclNode.identifierNode.SetPrefix(currentNamespace);


                            //形参类型补全  
                            foreach (var p in externFuncDeclNode.parametersNode.parameterNodes)
                            {
                                TryCompleteType(p.typeNode);
                            }

                            //符号的类型表达式  
                            string typeExpr = "";
                            for (int i = 0; i < externFuncDeclNode.parametersNode.parameterNodes.Count; ++i)
                            {
                                if (i != 0) typeExpr += ",";
                                var paramNode = externFuncDeclNode.parametersNode.parameterNodes[i];
                                typeExpr += (paramNode.typeNode.TypeExpression());
                            }
                            typeExpr += (" -> " + externFuncDeclNode.returnTypeNode.TypeExpression());

                            //函数修饰名称  
                            var paramTypeArr = externFuncDeclNode.parametersNode.parameterNodes.Select(n => n.typeNode.TypeExpression()).ToArray();
                            var funcMangledName = Utils.Mangle(externFuncDeclNode.identifierNode.FullName, paramTypeArr);
                            externFuncDeclNode.attributes["mangled_name"] = funcMangledName;

                            //新的作用域  
                            string envName = funcMangledName;

                            var newEnv = new SymbolTable(envName, SymbolTable.TableCatagory.FuncScope, envStack.Peek());
                            externFuncDeclNode.attributes["env"] = newEnv;


                            //添加条目  
                            var newRec = envStack.Peek().NewRecord(
                                funcMangledName,
                                SymbolTable.RecordCatagory.Function,
                                typeExpr,
                                newEnv
                                );
                            newRec.rawname = externFuncDeclNode.identifierNode.FullName;
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.ExternFunctionGlobalOrNamespaceOnly, externFuncDeclNode, "");
                        }
                    }
                    break;

                case SyntaxTree.ClassDeclareNode classDeclNode:
                    {
                        //附加命名空间名称    
                        if (isGlobalOrTopNamespace)
                        {
                            if (isTopLevelAtNamespace)
                                classDeclNode.classNameNode.SetPrefix(currentNamespace);

                            if (classDeclNode.classNameNode.FullName == "Core::Object")
                            {
                                if (currentNamespace != "Core")
                                {
                                    throw new SemanticException(ExceptioName.ClassNameCannotBeObject, classDeclNode, "");
                                }
                            }

                            //新的作用域  
                            var newEnv = new SymbolTable(classDeclNode.classNameNode.FullName, SymbolTable.TableCatagory.ClassScope, envStack.Peek());
                            classDeclNode.attributes["env"] = newEnv;

                            //添加条目-类名    
                            var newRec = envStack.Peek().NewRecord(
                                classDeclNode.classNameNode.FullName,
                                SymbolTable.RecordCatagory.Class,
                                "",
                                newEnv
                                );
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.ClassDefinitionGlobalOrNamespaceOnly, classDeclNode, "");
                        }
                    }
                    break;
                default:
                    break;
            }

        }

        /// <summary>
        /// PASS2:递归向下收集其他符号信息    
        /// </summary>
        private void Pass2_CollectOtherSymbols(SyntaxTree.Node node)
        {
            bool isTopLevelAtNamespace = false;
            if (node.Parent != null && node.Parent.Parent != null)
            {
                if (node.Parent is SyntaxTree.StatementsNode && node.Parent.Parent is SyntaxTree.NamespaceNode)
                {
                    isTopLevelAtNamespace = true;
                }
            }

            switch (node)
            {
                case SyntaxTree.ProgramNode programNode:
                    {
                        Pass2_CollectOtherSymbols(programNode.statementsNode);
                    }
                    break;
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        foreach (var stmtNode in stmtsNode.statements)
                        {
                            Pass2_CollectOtherSymbols(stmtNode);
                        }
                    }
                    break;
                case SyntaxTree.NamespaceNode namespaceNode:
                    {
                        currentNamespace = namespaceNode.namepsaceNode.token.attribute;
                        foreach (var stmtNode in namespaceNode.stmtsNode.statements)
                        {
                            Pass2_CollectOtherSymbols(stmtNode);
                        }
                        currentNamespace = "";
                    }
                    break;
                case SyntaxTree.StatementBlockNode stmtBlockNode:
                    {
                        //进入作用域  
                        var newEnv = new SymbolTable("stmtblock" + (this.blockCounter++), SymbolTable.TableCatagory.StmtBlockScope, envStack.Peek());
                        stmtBlockNode.attributes["env"] = newEnv;
                        envStack.Push(newEnv);

                        foreach (var stmtNode in stmtBlockNode.statements)
                        {
                            Pass2_CollectOtherSymbols(stmtNode);
                        }

                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.ConstantDeclareNode constDeclNode:
                    {
                        //Id at env
                        constDeclNode.identifierNode.attributes["def_at_env"] = envStack.Peek();

                        //（非全局）不支持成员常量  
                        if (isTopLevelAtNamespace == false)
                        {
                            throw new SemanticException(ExceptioName.ConstantGlobalOrNamespaceOnly, constDeclNode, "");
                        }
                    }
                    break;
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        //Id at env
                        varDeclNode.identifierNode.attributes["def_at_env"] = envStack.Peek();


                        //（非全局变量）成员字段或者局部变量  
                        if (isTopLevelAtNamespace == false)
                        {
                            varDeclNode.identifierNode.SetPrefix(null);

                            //补全类型  
                            TryCompleteType(varDeclNode.typeNode);

                            //新建符号表条目  
                            var newRec = envStack.Peek().NewRecord(
                                varDeclNode.identifierNode.FullName,
                                SymbolTable.RecordCatagory.Variable,
                                varDeclNode.typeNode.TypeExpression()
                                );
                        }
                    }
                    break;
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        //Id at env
                        funcDeclNode.identifierNode.attributes["def_at_env"] = envStack.Peek();



                        //是否是实例成员函数  
                        bool isMethod = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;
                        string className = null;
                        if (isMethod) className = envStack.Peek().name;
                        if (isMethod && isTopLevelAtNamespace) throw new SemanticException(ExceptioName.NamespaceTopLevelNonMemberFunctionOnly, funcDeclNode, "");


                        //如果是成员函数 - 加入符号表  
                        if (isTopLevelAtNamespace == false && isMethod == true)
                        {
                            //使用原名（成员函数）  
                            funcDeclNode.identifierNode.SetPrefix(null);


                            //形参类型补全  
                            foreach (var p in funcDeclNode.parametersNode.parameterNodes)
                            {
                                TryCompleteType(p.typeNode);
                            }

                            //符号的类型表达式（成员函数）  
                            string typeExpr = "";
                            for (int i = 0; i < funcDeclNode.parametersNode.parameterNodes.Count; ++i)
                            {
                                if (i != 0) typeExpr += ",";
                                var paramNode = funcDeclNode.parametersNode.parameterNodes[i];
                                typeExpr += (paramNode.typeNode.TypeExpression());
                            }
                            typeExpr += (" -> " + funcDeclNode.returnTypeNode.TypeExpression());

                            //函数修饰名称（成员函数）  
                            var paramTypeArr = funcDeclNode.parametersNode.parameterNodes.Select(n => n.typeNode.TypeExpression()).ToArray();
                            var funcMangledName = Utils.Mangle(funcDeclNode.identifierNode.FullName, paramTypeArr);
                            funcDeclNode.attributes["mangled_name"] = funcMangledName;


                            //新的作用域（成员函数）  
                            string envName = isMethod ? envStack.Peek().name + "." + funcMangledName : funcMangledName;

                            var newEnv = new SymbolTable(envName, SymbolTable.TableCatagory.FuncScope, envStack.Peek());
                            funcDeclNode.attributes["env"] = newEnv;

                            //类符号表同名方法去重（成员函数）    
                            if (envStack.Peek().ContainRecordName(funcMangledName))
                            {
                                envStack.Peek().records.Remove(funcMangledName);
                            }

                            //添加到虚函数表（成员函数）    
                            this.ilUnit.vtables[className].NewRecord(funcMangledName, className);


                            //添加条目（成员函数）  
                            var newRec = envStack.Peek().NewRecord(
                                funcMangledName,
                                SymbolTable.RecordCatagory.Function,
                                typeExpr,
                                newEnv
                                );
                            newRec.rawname = funcDeclNode.identifierNode.FullName;

                        }


                        {
                            SymbolTable funcEnv = (SymbolTable)funcDeclNode.attributes["env"];

                            //进入函数作用域  
                            envStack.Push(funcEnv);

                            //返回值类型补全    
                            TryCompleteType(funcDeclNode.returnTypeNode);


                            //隐藏的this参数加入符号表    
                            if (isMethod)
                            {
                                funcEnv.NewRecord("this", SymbolTable.RecordCatagory.Param, className);
                            }

                            //形参加入符号表  
                            foreach (var paramNode in funcDeclNode.parametersNode.parameterNodes)
                            {
                                Pass2_CollectOtherSymbols(paramNode);
                            }

                            //局部变量加入符号表    
                            foreach (var stmtNode in funcDeclNode.statementsNode.statements)
                            {
                                Pass2_CollectOtherSymbols(stmtNode);
                            }

                            //离开函数作用域  
                            envStack.Pop();
                        }
                    }
                    break;

                case SyntaxTree.ExternFuncDeclareNode externFuncDeclNode:
                    {
                        //Id at env
                        externFuncDeclNode.identifierNode.attributes["def_at_env"] = envStack.Peek();


                        //PASS1止于添加符号条目  

                        //Env  
                        var funcEnv = (SymbolTable)externFuncDeclNode.attributes["env"];

                        //进入函数作用域  
                        envStack.Push(funcEnv);

                        //返回值类型补全    
                        TryCompleteType(externFuncDeclNode.returnTypeNode);


                        //形参加入符号表    
                        foreach (var paramNode in externFuncDeclNode.parametersNode.parameterNodes)
                        {
                            Pass2_CollectOtherSymbols(paramNode);
                        }
                        //离开函数作用域  
                        envStack.Pop();
                    }
                    break;

                case SyntaxTree.ParameterNode paramNode:
                    {
                        //Id at env
                        paramNode.identifierNode.attributes["def_at_env"] = envStack.Peek();


                        //形参加入函数作用域的符号表  
                        var newRec = envStack.Peek().NewRecord(
                            paramNode.identifierNode.FullName,
                            SymbolTable.RecordCatagory.Param,
                            paramNode.typeNode.TypeExpression()
                            );
                    }
                    break;
                case SyntaxTree.ClassDeclareNode classDeclNode:
                    {
                        //Id at env
                        classDeclNode.classNameNode.attributes["def_at_env"] = envStack.Peek();


                        //PASS1止于添加符号条目  

                        //ENV  
                        var newEnv = (SymbolTable)classDeclNode.attributes["env"];


                        //补全继承基类的类名    
                        if (classDeclNode.baseClassNameNode != null)
                            TryCompleteIdenfier(classDeclNode.baseClassNameNode);

                        //新建虚函数表  
                        string classname = classDeclNode.classNameNode.FullName;
                        var vtable = ilUnit.vtables[classname] = new VTable(classname);
                        GixConsole.LogLine("新的虚函数表：" + classname);

                        //进入类作用域  
                        envStack.Push(newEnv);

                        //有基类  
                        if (classDeclNode.classNameNode.FullName != "Core::Object")
                        {
                            //基类名    
                            string baseClassFullName;
                            if (classDeclNode.baseClassNameNode != null)
                            {
                                //尝试补全基类标记  
                                TryCompleteIdenfier(classDeclNode.baseClassNameNode);
                                baseClassFullName = classDeclNode.baseClassNameNode.FullName;
                            }
                            else
                            {
                                baseClassFullName = "Core::Object";
                            }


                            var baseRec = Query(baseClassFullName); if (baseRec == null) throw new SemanticException(ExceptioName.BaseClassNotFound, classDeclNode.baseClassNameNode, baseClassFullName);
                            var baseEnv = baseRec.envPtr;
                            newEnv.NewRecord("base", SymbolTable.RecordCatagory.Other, "(inherit)", baseEnv);


                            //基类符号表条目并入//仅字段  
                            foreach (var reckv in baseEnv.records)
                            {
                                if (reckv.Value.category == SymbolTable.RecordCatagory.Variable)
                                {
                                    newEnv.AddRecord(reckv.Key, reckv.Value);
                                }
                            }
                            //虚函数表克隆  
                            var baseVTable = this.ilUnit.QueryVTable(baseClassFullName);
                            if (baseVTable == null) throw new SemanticException(ExceptioName.BaseClassNotFound, classDeclNode.baseClassNameNode, baseClassFullName);
                            baseVTable.CloneDataTo(vtable);
                        }


                        //新定义的成员字段加入符号表
                        foreach (var declNode in classDeclNode.memberDelareNodes)
                        {
                            Pass2_CollectOtherSymbols(declNode);
                        }

                        //默认隐藏构造函数的符号表  
                        var ctorEnv = new SymbolTable(classDeclNode.classNameNode.FullName + ".ctor", SymbolTable.TableCatagory.FuncScope, newEnv);
                        ctorEnv.NewRecord("this", SymbolTable.RecordCatagory.Param, classDeclNode.classNameNode.FullName);


                        //离开类作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.IfStmtNode ifNode:
                    {
                        ifNode.attributes["uid"] = ifCounter++;

                        foreach (var clause in ifNode.conditionClauseList)
                        {
                            Pass2_CollectOtherSymbols(clause.thenNode);
                        }
                        if (ifNode.elseClause != null)
                        {
                            Pass2_CollectOtherSymbols(ifNode.elseClause.stmt);
                        }
                    }
                    break;
                case SyntaxTree.WhileStmtNode whileNode:
                    {
                        whileNode.attributes["uid"] = whileCounter++;

                        Pass2_CollectOtherSymbols(whileNode.stmtNode);
                    }
                    break;
                case SyntaxTree.ForStmtNode forNode:
                    {
                        forNode.attributes["uid"] = forCounter++;

                        //新的作用域  
                        var newEnv = new SymbolTable("ForLoop" + (int)forNode.attributes["uid"], SymbolTable.TableCatagory.LoopScope, envStack.Peek());
                        forNode.attributes["env"] = newEnv;

                        //进入FOR循环作用域  
                        envStack.Push(newEnv);

                        //收集初始化语句中的符号  
                        Pass2_CollectOtherSymbols(forNode.initializerNode);

                        //收集语句中符号  
                        Pass2_CollectOtherSymbols(forNode.stmtNode);

                        //离开FOR循环作用域  
                        envStack.Pop();
                    }
                    break;
                default:
                    break;
            }

        }

        /// <summary>
        /// PASS3:语义分析（类型检查等）      
        /// </summary>
        private void Pass3_AnalysisNode(SyntaxTree.Node node)
        {


            switch (node)
            {
                case SyntaxTree.ProgramNode programNode:
                    {
                        Pass3_AnalysisNode(programNode.statementsNode);
                    }
                    break;
                case SyntaxTree.UsingNode usingNode:
                    {
                        namespaceUsings.Add(usingNode.namespaceNameNode.token.name);
                    }
                    break;
                case SyntaxTree.NamespaceNode namespaceNode:
                    {
                        currentNamespace = namespaceNode.namepsaceNode.token.attribute;

                        Pass3_AnalysisNode(namespaceNode.stmtsNode);

                        currentNamespace = "";
                    }
                    break;
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        foreach (var stmtNode in stmtsNode.statements)
                        {
                            Pass3_AnalysisNode(stmtNode);
                        }
                    }
                    break;
                case SyntaxTree.StatementBlockNode stmtBlockNode:
                    {
                        //进入作用域 
                        envStack.Push(stmtBlockNode.attributes["env"] as SymbolTable);

                        foreach (var stmtNode in stmtBlockNode.statements)
                        {
                            //分析块中的语句  
                            Pass3_AnalysisNode(stmtNode);
                        }

                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.ConstantDeclareNode constDeclNode:
                    {
                        //分析常量字面值
                        Pass3_AnalysisNode(constDeclNode.litValNode);

                        //类型检查（初始值）  
                        bool valid = CheckType(constDeclNode.typeNode.TypeExpression(), AnalyzeTypeExpression(constDeclNode.litValNode));
                        if (!valid)
                            throw new SemanticException(ExceptioName.ConstantTypeDeclarationError, constDeclNode, "type:" + constDeclNode.typeNode.TypeExpression() + "  value type:" + AnalyzeTypeExpression(constDeclNode.litValNode));
                    }
                    break;
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        //分析初始化表达式  
                        Pass3_AnalysisNode(varDeclNode.initializerNode);

                        //类型检查（初始值）  
                        bool valid = CheckType(varDeclNode.typeNode.TypeExpression(), varDeclNode.initializerNode);
                        if (!valid) throw new SemanticException(ExceptioName.VariableTypeDeclarationError, varDeclNode, "type:" + varDeclNode.typeNode.TypeExpression() + "  intializer type:" + AnalyzeTypeExpression(varDeclNode.initializerNode));
                    }
                    break;
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        //进入作用域    
                        envStack.Push(funcDeclNode.attributes["env"] as SymbolTable);


                        //分析形参定义  
                        foreach (var paramNode in funcDeclNode.parametersNode.parameterNodes)
                        {
                            Pass3_AnalysisNode(paramNode);
                        }

                        //分析局部语句  
                        foreach (var stmtNode in funcDeclNode.statementsNode.statements)
                        {
                            Pass3_AnalysisNode(stmtNode);
                        }

                        //返回值类型检查（仅限非void的函数）  
                        if (!(funcDeclNode.returnTypeNode is SyntaxTree.PrimitiveTypeNode && (funcDeclNode.returnTypeNode as SyntaxTree.PrimitiveTypeNode).token.name == "void"))
                        {
                            ////检查返回语句和返回表达式    
                            if (CheckReturnStmt(funcDeclNode.statementsNode, funcDeclNode.returnTypeNode.TypeExpression()) == false)
                            {
                                throw new SemanticException(ExceptioName.MissingReturnStatement, funcDeclNode, "");
                            }

                            ////检查返回语句和返回表达式（临时）    
                            //var returnStmt = funcDeclNode.statementsNode.statements.FirstOrDefault(s => s is SyntaxTree.ReturnStmtNode);
                            //if (returnStmt == null) throw new SemanticException(ExceptioName.MissingReturnStatement, funcDeclNode, "");

                            //bool valid = CheckType(funcDeclNode.returnTypeNode.TypeExpression(), (returnStmt as SyntaxTree.ReturnStmtNode).returnExprNode);
                            //if (!valid) throw new SemanticException(ExceptioName.ReturnTypeError, funcDeclNode.returnTypeNode, "");
                        }



                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.ExternFuncDeclareNode externFuncDeclNode:
                    {
                        //进入作用域    
                        envStack.Push(externFuncDeclNode.attributes["env"] as SymbolTable);

                        //分析形参定义
                        foreach (var paramNode in externFuncDeclNode.parametersNode.parameterNodes)
                        {
                            Pass3_AnalysisNode(paramNode);
                        }
                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.ClassDeclareNode classdeclNode:
                    {
                        //进入作用域    
                        envStack.Push(classdeclNode.attributes["env"] as SymbolTable);

                        //成员分析  
                        foreach (var declNode in classdeclNode.memberDelareNodes)
                        {
                            Pass3_AnalysisNode(declNode);
                        }

                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.SingleExprStmtNode singleStmtNode:
                    {
                        //单语句语义分析  
                        Pass3_AnalysisNode(singleStmtNode.exprNode);
                    }
                    break;
                case SyntaxTree.IfStmtNode ifNode:
                    {
                        foreach (var clause in ifNode.conditionClauseList)
                        {
                            //检查条件是否为布尔类型  
                            CheckType("bool", clause.conditionNode);
                            //检查语句节点  
                            Pass3_AnalysisNode(clause.thenNode);
                        }

                        //检查语句  
                        if (ifNode.elseClause != null) Pass3_AnalysisNode(ifNode.elseClause.stmt);
                    }
                    break;
                case SyntaxTree.WhileStmtNode whileNode:
                    {
                        //检查条件是否为布尔类型    
                        CheckType("bool", whileNode.conditionNode);

                        //检查语句节点  
                        Pass3_AnalysisNode(whileNode.stmtNode);
                    }
                    break;
                case SyntaxTree.ForStmtNode forNode:
                    {
                        //进入FOR作用域    
                        envStack.Push(forNode.attributes["env"] as SymbolTable);

                        //检查初始化器和迭代器  
                        Pass3_AnalysisNode(forNode.initializerNode);
                        AnalyzeTypeExpression(forNode.iteratorNode);

                        //检查条件是否为布尔类型    
                        CheckType("bool", forNode.conditionNode);

                        //检查语句节点  
                        Pass3_AnalysisNode(forNode.stmtNode);

                        //离开FOR循环作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.ReturnStmtNode retNode:
                    {
                        //检查返回值  
                        Pass3_AnalysisNode(retNode.returnExprNode);
                    }
                    break;
                case SyntaxTree.DeleteStmtNode delNode:
                    {
                        //检查要删除的对象    
                        Pass3_AnalysisNode(delNode.objToDelete);
                        string objTypeExpr = (string)delNode.objToDelete.attributes["type"];

                        if (Utils.IsArrayType(objTypeExpr) == false)
                        {
                            if (Query(objTypeExpr) == null)
                            {
                                if ((objTypeExpr == "string") == false)
                                {
                                    throw new SemanticException(ExceptioName.InvalidDeleteStatement, delNode, "");
                                }
                            }
                        }
                    }
                    break;

                // ********************* 其他节点检查 *********************************
                case SyntaxTree.IndexerNode indexerNode:
                    {
                        Pass3_AnalysisNode(indexerNode.indexNode);
                    }
                    break;


                // ********************* 表达式检查 *********************************


                case SyntaxTree.IdentityNode idNode:
                    {
                        var rec = Query(idNode.FullName);
                        if (rec == null)
                            throw new SemanticException(ExceptioName.IdentifierNotFound, idNode, "");

                        //常量替换  
                        if (rec.category == SymbolTable.RecordCatagory.Constant)
                        {
                            idNode.replacement = new SyntaxTree.LiteralNode
                            {
                                token = new Token(null, PatternType.Literal, rec.initValue, -1, -1, -1),

                                attributes = new Dictionary<string, object>()
                            };

                            idNode.replacement.attributes["type"] = rec.typeExpression;
                            break;
                        }

                        AnalyzeTypeExpression(idNode);
                    }
                    break;
                case SyntaxTree.LiteralNode litNode:
                    {
                        AnalyzeTypeExpression(litNode);
                    }
                    break;
                case SyntaxTree.UnaryOpNode unaryNode:
                    {
                        Pass3_AnalysisNode(unaryNode.exprNode);
                        AnalyzeTypeExpression(unaryNode);
                    }
                    break;
                case SyntaxTree.BinaryOpNode binaryNode:
                    {
                        Pass3_AnalysisNode(binaryNode.leftNode);
                        Pass3_AnalysisNode(binaryNode.rightNode);
                        AnalyzeTypeExpression(binaryNode);
                    }
                    break;
                case SyntaxTree.IncDecNode incDecNode:
                    {
                        AnalyzeTypeExpression(incDecNode);
                    }
                    break;
                case SyntaxTree.CastNode castNode:
                    {
                        TryCompleteType(castNode.typeNode);
                        Pass3_AnalysisNode(castNode.factorNode);
                        AnalyzeTypeExpression(castNode);
                    }
                    break;
                case SyntaxTree.ElementAccessNode eleAccessNode:
                    {
                        AnalyzeTypeExpression(eleAccessNode);
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        //实参分析  
                        foreach (var argNode in callNode.argumantsNode.arguments)
                        {
                            Pass3_AnalysisNode(argNode);
                        }

                        //名称分析补全  
                        if (callNode.isMemberAccessFunction == false && callNode.funcNode is SyntaxTree.IdentityNode)
                        {
                            TryCompleteIdenfier((callNode.funcNode as SyntaxTree.IdentityNode));
                        }


                        //Func分析  
                        AnalyzeTypeExpression(callNode);

                        //参数个数检查暂无...

                        //参数重载对应检查暂无...
                    }
                    break;
                case SyntaxTree.AssignNode assignNode:
                    {
                        //!!setter属性替换  
                        if (assignNode.lvalueNode is SyntaxTree.ObjectMemberAccessNode)
                        {
                            var memberAccess = assignNode.lvalueNode as SyntaxTree.ObjectMemberAccessNode;

                            var className = AnalyzeTypeExpression(memberAccess.objectNode);

                            var classRec = Query(className);
                            if (classRec == null) throw new SemanticException(ExceptioName.ClassSymbolTableNotFound, assignNode, className);

                            var classEnv = classRec.envPtr;

                            var memberName = memberAccess.memberNode.FullName;

                            var memberRec = classEnv.GetMemberRecordInChain(memberName);
                            if (memberRec == null) //不存在同名字段  
                            {
                                var rvalType = AnalyzeTypeExpression(assignNode.rvalueNode);

                                var setterMethod = classEnv.GetMemberRecordInChain(Utils.Mangle(memberName, rvalType));
                                if (setterMethod != null)//存在setter函数  
                                {
                                    //替换节点  
                                    assignNode.replacement = new SyntaxTree.CallNode()
                                    {

                                        isMemberAccessFunction = true,
                                        funcNode = memberAccess,
                                        argumantsNode = new SyntaxTree.ArgumentListNode()
                                        {
                                            arguments = new List<SyntaxTree.ExprNode>() {
                                                assignNode.rvalueNode
                                            },
                                        },

                                        attributes = assignNode.attributes,
                                    };

                                    Pass3_AnalysisNode(assignNode.replacement);

                                    break;
                                }
                            }
                        }


                        //类型检查（赋值）  
                        {
                            Pass3_AnalysisNode(assignNode.lvalueNode);
                            Pass3_AnalysisNode(assignNode.rvalueNode);

                            bool valid = CheckType(assignNode.lvalueNode, assignNode.rvalueNode);
                            if (!valid) throw new SemanticException(ExceptioName.AssignmentTypeError, assignNode, "");
                        }
                    }
                    break;
                case SyntaxTree.ObjectMemberAccessNode objMemberAccessNode:
                    {
                        //!!getter属性替换  
                        {
                            var className = AnalyzeTypeExpression(objMemberAccessNode.objectNode);

                            var classRec = Query(className);
                            if (classRec == null) throw new SemanticException(ExceptioName.ClassSymbolTableNotFound, objMemberAccessNode, className);

                            var classEnv = classRec.envPtr;

                            var memberName = objMemberAccessNode.memberNode.FullName;

                            var memberRec = classEnv.GetMemberRecordInChain(memberName);
                            if (memberRec == null) //不存在同名字段  
                            {
                                var getterRec = classEnv.GetMemberRecordInChain(Utils.Mangle(memberName));
                                if (getterRec != null)//存在getter函数  
                                {

                                    //替换节点  
                                    objMemberAccessNode.replacement = new SyntaxTree.CallNode()
                                    {

                                        isMemberAccessFunction = true,
                                        funcNode = objMemberAccessNode,
                                        argumantsNode = new SyntaxTree.ArgumentListNode()
                                        {
                                            arguments = new List<SyntaxTree.ExprNode>()
                                            {
                                            },
                                        },

                                        attributes = objMemberAccessNode.attributes,
                                    };

                                    Pass3_AnalysisNode(objMemberAccessNode.replacement);

                                    break;
                                }
                            }
                        }

                        Pass3_AnalysisNode(objMemberAccessNode.objectNode);

                        //不能分析成员名称，当前作用域会找不到标识符。      
                        //Debug.Log("分析：" + objMemberAccessNode.memberNode.FirstToken().ToString());
                        //Pass3_AnalysisNode(objMemberAccessNode.memberNode);

                        AnalyzeTypeExpression(objMemberAccessNode);
                    }
                    break;
                case SyntaxTree.ThisNode thisObjNode:
                    {
                        AnalyzeTypeExpression(thisObjNode);
                    }
                    break;
                case SyntaxTree.NewObjectNode newobjNode:
                    {
                        TryCompleteIdenfier(newobjNode.className);
                    }
                    break;
                case SyntaxTree.NewArrayNode newArrNode:
                    {
                        TryCompleteType(newArrNode.typeNode);

                        Pass3_AnalysisNode(newArrNode.typeNode);
                        Pass3_AnalysisNode(newArrNode.lengthNode);
                    }
                    break;
                //其他节点     
                default:
                    {
                        //类型节点  --> 补全类型
                        if (node is SyntaxTree.TypeNode)
                        {
                            TryCompleteType(node as SyntaxTree.TypeNode);
                        }
                        //else if(node is SyntaxTree.ArgumentListNode)
                        //{
                        //}
                        //else if (node is SyntaxTree.ParameterListNode)
                        //{
                        //}
                        //else
                        //{
                        //    throw new Exception("未实现节点分析：" + node.GetType().Name);
                        //}
                    }
                    break;
            }
        }



        /// <summary>
        /// 获取表达式的类型表达式  
        /// </summary>
        private string AnalyzeTypeExpression(SyntaxTree.ExprNode exprNode)
        {
            if (exprNode == null) throw new GizboxException(ExceptioName.ExpressionNodeIsNull);
            if (exprNode.attributes == null) throw new SemanticException(ExceptioName.NodeNoInitializationPropertyList, exprNode, "");
            if (exprNode.attributes.ContainsKey("type")) return (string)exprNode.attributes["type"];

            string nodeTypeExprssion = "";

            switch (exprNode)
            {
                case SyntaxTree.IdentityNode idNode:
                    {
                        if (envStack.Count >= 2
                            && envStack[envStack.Top].tableCatagory == SymbolTable.TableCatagory.FuncScope
                            && envStack[envStack.Top - 1].tableCatagory == SymbolTable.TableCatagory.ClassScope
                            )
                        {
                            if (envStack[envStack.Top - 1].ContainRecordRawName(idNode.FullName))
                            {
                                throw new SemanticException(ExceptioName.ClassMemberFunctionThisKeywordMissing, idNode, "");
                            }
                        }

                        var result = Query(idNode.FullName);
                        if (result == null)
                        {
                            throw new SemanticException(ExceptioName.IdentifierNotFound, idNode, "");
                        }


                        nodeTypeExprssion = result.typeExpression;
                    }
                    break;
                case SyntaxTree.LiteralNode litNode:
                    {
                        nodeTypeExprssion = GetLitType(litNode.token);
                    }
                    break;
                case SyntaxTree.ThisNode thisnode:
                    {
                        var result = Query("this");
                        if (result == null) throw new SemanticException(ExceptioName.MissingThisPtrInSymbolTable, thisnode, "");

                        nodeTypeExprssion = result.typeExpression;
                    }
                    break;


                case SyntaxTree.ObjectMemberAccessNode accessNode:
                    {
                        var className = AnalyzeTypeExpression(accessNode.objectNode);

                        var classRec = Query(className);
                        if (classRec == null) throw new SemanticException(ExceptioName.ClassNameNotFound, accessNode.objectNode, className);

                        var classEnv = classRec.envPtr;
                        if (classEnv == null) throw new SemanticException(ExceptioName.ClassScopeNotFound, accessNode.objectNode, "");

                        var memberRec = classEnv.GetRecord(accessNode.memberNode.FullName);
                        if (memberRec == null) throw new SemanticException(ExceptioName.MemberFieldNotFound, accessNode.objectNode, accessNode.memberNode.FullName);

                        accessNode.attributes["class"] = className;//记录memberAccess节点的点左边类型
                        accessNode.attributes["member_name"] = accessNode.memberNode.FullName;//记录memberAccess节点的点右边名称

                        nodeTypeExprssion = memberRec.typeExpression;
                    }
                    break;
                case SyntaxTree.ElementAccessNode eleAccessNode:
                    {
                        string containerTypeExpr;
                        if (eleAccessNode.isMemberAccessContainer)
                        {
                            containerTypeExpr = AnalyzeTypeExpression(eleAccessNode.containerNode as SyntaxTree.ObjectMemberAccessNode);
                        }
                        else
                        {
                            var funcId = (eleAccessNode.containerNode as SyntaxTree.IdentityNode);
                            var idRec = Query(funcId.FullName);
                            if (idRec == null) throw new SemanticException(ExceptioName.FunctionNotFound, eleAccessNode.containerNode, funcId.FullName);

                            containerTypeExpr = idRec.typeExpression;
                        }

                        if (containerTypeExpr.EndsWith("[]"))
                        {
                            nodeTypeExprssion = containerTypeExpr.Substring(0, containerTypeExpr.Length - 2);
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.Unknown, eleAccessNode, "only array can use [] operator");
                        }
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        var argTypeArr = callNode.argumantsNode.arguments.Select(argN => AnalyzeTypeExpression(argN)).ToArray();
                        string mangledName;

                        if (callNode.isMemberAccessFunction)
                        {
                            var funcAccess = (callNode.funcNode as SyntaxTree.ObjectMemberAccessNode);
                            string funcName = funcAccess.memberNode.FullName;

                            var className = AnalyzeTypeExpression(funcAccess.objectNode);

                            var classRec = Query(className);
                            if (classRec == null) throw new SemanticException(ExceptioName.ClassNameNotFound, callNode, className);

                            var classEnv = classRec.envPtr;
                            if (classEnv == null) throw new SemanticException(ExceptioName.ClassScopeNotFound, callNode, "");

                            mangledName = Utils.Mangle(funcName, argTypeArr);

                            var memberRec = classEnv.GetMemberRecordInChain(mangledName);
                            if (memberRec == null) throw new SemanticException(ExceptioName.FunctionMemberNotFound, callNode, funcAccess.memberNode.FullName);

                            var typeExpr = memberRec.typeExpression;

                            if (typeExpr.Contains("->") == false) throw new SemanticException(ExceptioName.ObjectMemberNotFunction, callNode, typeExpr);
                            nodeTypeExprssion = typeExpr.Split(' ').LastOrDefault();
                        }
                        else
                        {
                            var funcId = (callNode.funcNode as SyntaxTree.IdentityNode);

                            mangledName = Utils.Mangle(funcId.FullName, argTypeArr);

                            var idRec = Query(mangledName);

                            if (idRec == null) throw new SemanticException(ExceptioName.FunctionNotFound, callNode, mangledName);

                            string typeExpr = idRec.typeExpression.Split(' ').LastOrDefault();

                            nodeTypeExprssion = typeExpr;
                        }

                        callNode.attributes["mangled_name"] = mangledName;
                    }
                    break;
                case SyntaxTree.NewObjectNode newObjNode:
                    {
                        string className = newObjNode.className.FullName;
                        if (Query(className) == null) throw new SemanticException(ExceptioName.ClassDefinitionNotFound, newObjNode.className, className);
                        nodeTypeExprssion = className;
                    }
                    break;
                case SyntaxTree.NewArrayNode newArrNode:
                    {
                        nodeTypeExprssion = newArrNode.typeNode.TypeExpression() + "[]";
                    }
                    break;
                case SyntaxTree.BinaryOpNode binaryOp:
                    {
                        var typeL = AnalyzeTypeExpression(binaryOp.leftNode);
                        var typeR = AnalyzeTypeExpression(binaryOp.rightNode);

                        string op = binaryOp.op;
                        //比较运算符
                        if (op == "<" || op == ">" || op == "<=" || op == ">=" || op == "==" || op == "!=")
                        {
                            if (CheckType(typeL, typeR) == false) throw new SemanticException(ExceptioName.InconsistentExpressionTypesCannotCompare, binaryOp, "");

                            nodeTypeExprssion = "bool";
                        }
                        //普通运算符  
                        else
                        {
                            if (typeL != typeR) throw new SemanticException(ExceptioName.BinaryOperationTypeMismatch, binaryOp, "");

                            nodeTypeExprssion = typeL;
                        }
                    }
                    break;
                case SyntaxTree.UnaryOpNode unaryOp:
                    {
                        nodeTypeExprssion = AnalyzeTypeExpression(unaryOp.exprNode);
                    }
                    break;
                case SyntaxTree.IncDecNode incDecOp:
                    {
                        nodeTypeExprssion = AnalyzeTypeExpression(incDecOp.identifierNode);
                    }
                    break;
                case SyntaxTree.CastNode castNode:
                    {
                        nodeTypeExprssion = castNode.typeNode.TypeExpression();
                    }
                    break;
                default:
                    throw new SemanticException(ExceptioName.CannotAnalyzeExpressionNodeType, exprNode, exprNode.GetType().Name);
            }

            exprNode.attributes["type"] = nodeTypeExprssion;

            return nodeTypeExprssion;
        }

        /// <summary>
        /// 检查类型
        /// </summary>
        private bool CheckType(string typeExpr, SyntaxTree.ExprNode exprNode)
        {
            return CheckType(typeExpr, AnalyzeTypeExpression(exprNode));
        }
        private bool CheckType(SyntaxTree.ExprNode exprNode1, SyntaxTree.ExprNode exprNode2)
        {
            return CheckType(AnalyzeTypeExpression(exprNode1), AnalyzeTypeExpression(exprNode2));
        }
        private bool CheckType(string typeExpr1, string typeExpr2)
        {
            if (typeExpr1 == "null" && Utils.IsPrimitiveType(typeExpr2) == false)
            {
                return true;
            }
            else if (typeExpr2 == "null" && Utils.IsPrimitiveType(typeExpr1) == false)
            {
                return true;
            }

            return typeExpr1 == typeExpr2;
        }

        private string GetLitType(Token token)
        {
            switch (token.name)
            {
                case "null":
                    return "null";
                case "LITBOOL":
                    return "bool";
                case "LITINT":
                    return "int";
                case "LITFLOAT":
                    return "float";
                case "LITDOUBLE":
                    return "double";
                case "LITCHAR":
                    return "char";
                case "LITSTRING":
                    return "string";
            }
            return default;
        }

        private SymbolTable.Record Query(string name)
        {
            //符号表链查找  
            var toList = envStack.ToList();
            for (int i = toList.Count - 1; i > -1; --i)
            {
                if (toList[i].ContainRecordName(name))
                {
                    return toList[i].GetRecord(name);
                }
            }
            //库依赖中查找  
            foreach (var lib in this.ilUnit.dependencyLibs)
            {
                var result = lib.QueryTopSymbol(name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private bool CheckReturnStmt(SyntaxTree.Node node, string returnType)
        {
            switch(node)
            {
                //语句块节点
                case SyntaxTree.StatementBlockNode stmtBlockNode:
                    {
                        bool anyReturnStmt = false;
                        for(int i = stmtBlockNode.statements.Count - 1; i > -1; --i)
                        {
                            var stmt = stmtBlockNode.statements[i];
                            if (CheckReturnStmt(stmt, returnType))
                            {
                                anyReturnStmt = true;//不break，确保所有return节点都被检查  
                            }
                        }
                        return anyReturnStmt;
                    }
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        bool anyReturnStmt = false;
                        for (int i = stmtsNode.statements.Count - 1; i > -1; --i)
                        {
                            var stmt = stmtsNode.statements[i];
                            if (CheckReturnStmt(stmt, returnType))
                            {
                                anyReturnStmt = true;
                            }
                        }
                        return anyReturnStmt;
                    }

                //分支节点  
                case SyntaxTree.IfStmtNode ifNode:
                    {
                        //没有else的if语法 ->不通过检查  
                        if(ifNode.elseClause == null)
                        {
                            return false;
                        }

                        //有else的if语法 ->检查所有路径是否能通过检查  
                        bool allPathValid = true;
                        if(CheckReturnStmt(ifNode.elseClause.stmt, returnType) == false)
                        {
                            return false;
                        }
                        foreach(var conditionClause in ifNode.conditionClauseList)
                        {
                            if (CheckReturnStmt(conditionClause.thenNode, returnType) == false)
                            {
                                allPathValid = false;
                                break;
                            }
                        }
                        return allPathValid;
                    }

                //返回节点  
                case SyntaxTree.ReturnStmtNode retNode:
                    {
                        //类型检查  
                        bool typeValid = CheckType(returnType, retNode.returnExprNode);
                        if(typeValid == false)
                            throw new SemanticException(ExceptioName.ReturnTypeError, retNode, "");

                        return true;
                    }
                //其他节点  
                default:
                    return false;
            }
        }

        private bool TryQueryIgnoreMangle(string name)
        {
            //符号表链查找  
            var toList = envStack.ToList();
            for (int i = toList.Count - 1; i > -1; --i)
            {
                if (toList[i].ContainRecordRawName(name))
                {
                    return true;
                }
            }
            //库依赖中查找  
            foreach (var lib in this.ilUnit.dependencyLibs)
            {
                var result = lib.QueryTopSymbol(name, ignoreMangle: true);
                if (result != null)
                {
                    return true;
                }
            }


            if (Compiler.enableLogParser) Log("TryQuery  库中未找到:" + name);
            return false;
        }

        private bool TryCompleteIdenfier(SyntaxTree.IdentityNode idNode)
        {
            bool found = false;
            string namevalid = null;
            //原名查找 
            {
                if (TryQueryIgnoreMangle(idNode.token.attribute))
                {
                    found = true;
                    namevalid = idNode.token.attribute;
                }
            }

            //尝试命名空间前缀   
            foreach (var namespaceUsing in ast.rootNode.usingNamespaceNodes)
            {
                string newname = namespaceUsing.namespaceNameNode.token.attribute + "::" + idNode.token.attribute;
                if (TryQueryIgnoreMangle(newname))
                {
                    if (found == true)
                    {
                        throw new SemanticException(ExceptioName.IdentifierAmbiguousBetweenNamespaces, idNode, newname + " vs " + namevalid);
                    }
                    found = true;
                    idNode.SetPrefix(namespaceUsing.namespaceNameNode.token.attribute);
                    if (Compiler.enableLogParser) Log(idNode.token.attribute + "补全为" + idNode.FullName);
                }
            }

            if (found)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void TryCompleteType(SyntaxTree.TypeNode typeNode)
        {
            switch (typeNode)
            {
                case SyntaxTree.PrimitiveTypeNode primitiveTypeNode:
                    break;
                case SyntaxTree.ClassTypeNode classTypeNode:
                    {
                        GixConsole.LogLine("类类型补全：" + classTypeNode.classname);
                        TryCompleteIdenfier(classTypeNode.classname);

                        GixConsole.LogLine("结果：" + classTypeNode.classname.FullName);
                    }
                    break;
                case SyntaxTree.ArrayTypeNode arrayTypeNpde:
                    {
                        TryCompleteType(arrayTypeNpde.elemtentType);
                    }
                    break;
            }
        }

        public static void Log(object content)
        {
            if (!Compiler.enableLogSemanticAnalyzer) return;
            GixConsole.LogLine("SematicAnalyzer >>>>" + content);
        }
    }
}



