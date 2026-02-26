using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

using Gizbox;
using Gizbox.LRParse;
using Gizbox.LALRGenerator;
using Gizbox.SemanticRule;
using System.Runtime.CompilerServices;
using static Gizbox.SyntaxTree;
using Gizbox.IR;
using System.Xml.Linq;

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
            ParseTree.Node newNode = (parser.newElement.attributes[ParseAttr.cst_node] = new ParseTree.Node() { isLeaf = false, name = production.head.name }) as ParseTree.Node;
            resultTree.allnodes.Add(newNode);

            //产生式体  
            for(int i = 0; i < production.body.Length; ++i)
            {
                int offset = parser.stack.Count - production.body.Length;
                var ele = parser.stack[offset + i];
                var symbol = production.body[i];

                //叶子节点（终结符节点）  
                if(symbol is Terminal)
                {
                    var node = (ele.attributes[ParseAttr.cst_node] = new ParseTree.Node() { isLeaf = true, name = symbol.name + "," + (ele.attributes[ParseAttr.token] as Token).attribute }) as ParseTree.Node;

                    resultTree.allnodes.Add(node);

                    node.parent = newNode;
                    newNode.children.Add(node);
                }
                //内部节点（非终结符节点）
                else
                {
                    var node = ele.attributes[ParseAttr.cst_node] as ParseTree.Node;

                    node.parent = newNode;
                    newNode.children.Add(node);
                }
            }


            if(parser.remainingInput.Count == 1 && parser.remainingInput.Peek().name == "$")
            {
                Accept(parser);
            }

        }

        // 完成  
        public void Accept(LRParser parser)
        {
            //设置根节点  
            resultTree.root = parser.newElement.attributes[ParseAttr.cst_node] as ParseTree.Node;

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


        // 语法分析树构造    
        public BottomUpParseTreeBuilder parseTreeBuilder;

        // 抽象语法树构造    
        public SyntaxTree.ProgramNode syntaxRootNode;

        // 构造  
        public SematicActionExecutor(LRParser parser)
        {
            this.parserContext = parser;

            //语法树构造器  
            parseTreeBuilder = new BottomUpParseTreeBuilder();

            //构建语法分析树的语义动作    
            foreach(var p in parserContext.data.grammerSet.productions)
            {
                this.AddActionAtTail(p, parseTreeBuilder.BuildAction);
            }

            //return; //不附加其他语义规则-仅语法分析

            //构建抽象语法树(AST)的语义动作   
            AddActionAtTail("S -> importations namespaceusings statements", (psr, production) =>
            {
                var n = new SyntaxTree.ProgramNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                };

                n.importNodes.AddRange((List<SyntaxTree.ImportNode>)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.import_list]);
                n.usingNamespaceNodes.AddRange((List<SyntaxTree.UsingNode>)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.using_list]);
                n.statementsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];

                psr.newElement.attributes[ParseAttr.ast_node] = n;

                this.syntaxRootNode = (SyntaxTree.ProgramNode)psr.newElement.attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("importations -> importations importation", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.import_list] = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.import_list];
                ((List<SyntaxTree.ImportNode>)psr.newElement.attributes[ParseAttr.import_list]).Add(
                    (SyntaxTree.ImportNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );
                ;
            });
            AddActionAtTail("importations -> importation", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.import_list] = new List<SyntaxTree.ImportNode>();
                ((List<SyntaxTree.ImportNode>)psr.newElement.attributes[ParseAttr.import_list]).Add(
                    (SyntaxTree.ImportNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );
                ;
            });
            AddActionAtTail("importations -> ε", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.import_list] = new List<SyntaxTree.ImportNode>();
            });
            AddActionAtTail("importation -> import < LITSTRING >", (psr, production) =>
            {
                string uriRaw = (psr.stack[psr.stack.Top - 1].attributes[ParseAttr.token] as Token).attribute;
                string uri = uriRaw.Substring(1, uriRaw.Length - 2);
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ImportNode()
                {
                    uri = uri,

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 4].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj) && gpObj is List<SyntaxTree.IdentityNode> gpList && gpList.Count > 0)
                {
                    var classNode = (SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node];
                    classNode.isTemplateClass = true;
                    classNode.templateParameters.AddRange(gpList);
                }
            });

            AddActionAtTail("namespaceusings -> namespaceusings namespaceusing", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.using_list] = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.using_list];
                ((List<SyntaxTree.UsingNode>)psr.newElement.attributes[ParseAttr.using_list]).Add(
                    (SyntaxTree.UsingNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );
                ;
            });
            AddActionAtTail("namespaceusings -> namespaceusing", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.using_list] = new List<SyntaxTree.UsingNode>();
                ((List<SyntaxTree.UsingNode>)psr.newElement.attributes[ParseAttr.using_list]).Add(
                    (SyntaxTree.UsingNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );
                ;
            });
            AddActionAtTail("namespaceusings -> ε", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.using_list] = new List<SyntaxTree.UsingNode>();
            });

            AddActionAtTail("namespaceusing -> using ID ;", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.UsingNode()
                {
                    namespaceNameNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Namespace,
                    }
                };
            });



            AddActionAtTail("statements -> statements stmt", (psr, production) => {

                var newStmt = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];

                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node];
                ((SyntaxTree.StatementsNode)psr.newElement.attributes[ParseAttr.ast_node]).statements.Add(newStmt);
            });
            AddActionAtTail("statements -> stmt", (psr, production) => {

                var newStmt = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.StatementsNode()
                {

                    attributes = new Dictionary<AstAttr, object>(),
                };

                ((SyntaxTree.StatementsNode)psr.newElement.attributes[ParseAttr.ast_node]).statements.Add(newStmt);
            });
            AddActionAtTail("statements -> ε", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.StatementsNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });


            AddActionAtTail("namespaceblock -> namespace ID { statements }", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.NamespaceNode()
                {
                    namepsaceNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 3].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Namespace,
                    },
                    stmtsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("statementblock -> { statements }", (psr, production) => {
                var node = new SyntaxTree.StatementBlockNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                };

                node.statements.AddRange(((SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node]).statements.ToList());

                psr.newElement.attributes[ParseAttr.ast_node] = node;
            });



            AddActionAtTail("declstatements -> declstatements declstmt", (psr, production) => {

                psr.newElement.attributes[ParseAttr.decl_stmts] = (List<SyntaxTree.DeclareNode>)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.decl_stmts];

                var newDeclStmt = (SyntaxTree.DeclareNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
                ((List<SyntaxTree.DeclareNode>)psr.newElement.attributes[ParseAttr.decl_stmts]).Add(newDeclStmt);
            });

            AddActionAtTail("declstatements -> declstmt", (psr, production) => {

                var newDeclStmt = (SyntaxTree.DeclareNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
                psr.newElement.attributes[ParseAttr.decl_stmts] = new List<SyntaxTree.DeclareNode>() { newDeclStmt };
            });

            AddActionAtTail("declstatements -> ε", (psr, production) => {

                psr.newElement.attributes[ParseAttr.decl_stmts] = new List<SyntaxTree.DeclareNode>() { };
            });

            AddActionAtTail("stmt -> namespaceblock", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("stmt -> statementblock", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("stmt -> declstmt", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });


            AddActionAtTail("stmt -> stmtexpr ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.SingleExprStmtNode()
                {
                    exprNode = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });


            AddActionAtTail("stmt -> break ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BreakStmtNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("stmt -> return expr ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ReturnStmtNode()
                {
                    returnExprNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("stmt -> return ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ReturnStmtNode()
                {
                    returnExprNode = null,

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("stmt -> delete expr ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.DeleteStmtNode()
                {
                    isArrayDelete = false,

                    objToDelete = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });


            AddActionAtTail("stmt -> delete [ ] expr ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.DeleteStmtNode()
                {
                    isArrayDelete = true,

                    objToDelete = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("stmt -> while ( expr ) stmt", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.WhileStmtNode()
                {

                    conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    stmtNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });


            AddActionAtTail("stmt -> for ( stmt bexpr ; stmtexpr ) stmt", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ForStmtNode()
                {
                    initializerNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top - 5].attributes[ParseAttr.ast_node],
                    conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    iteratorNode = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    stmtNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });


            AddActionAtTail("stmt -> if ( expr ) stmt elifclauselist elseclause", (psr, production) => {
                var n = new SyntaxTree.IfStmtNode()
                {
                    elseClause = (SyntaxTree.ElseClauseNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };

                n.conditionClauseList.AddRange(
                    new List<SyntaxTree.ConditionClauseNode>() {
                        new SyntaxTree.ConditionClauseNode(){
                            conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                            thenNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                        },
                    });

                psr.newElement.attributes[ParseAttr.ast_node] = n;

                ((SyntaxTree.IfStmtNode)psr.newElement.attributes[ParseAttr.ast_node]).conditionClauseList.AddRange(
                        (List<SyntaxTree.ConditionClauseNode>)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.condition_clause_list]
                    );
            });


            AddActionAtTail("declstmt -> decltype ID = expr ;", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.VarDeclareNode()
                {
                    flags = (VarModifiers.None),
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 3].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    initializerNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });



            AddActionAtTail("declstmt -> tmodf decltype ID = expr ;", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.VarDeclareNode()
                {
                    flags = (VarModifiers)psr.stack[psr.stack.Top - 5].attributes[ParseAttr.tmodf],
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 3].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    initializerNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("declstmt -> const decltype ID = lit ;", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ConstantDeclareNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 3].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    litValNode = (SyntaxTree.LiteralNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };

            });


            AddActionAtTail("declstmt -> tmodf decltype ID = capture ( ID ) ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new OwnershipCaptureStmtNode()
                {
                    flags = (VarModifiers)psr.stack[psr.stack.Top - 8].attributes[ParseAttr.tmodf],
                    rIdentifier = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 2].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    lIdentifier = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 6].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 7].attributes[ParseAttr.ast_node],
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });



            AddActionAtTail("declstmt -> decltype ID = leak ( ID ) ;", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new OwnershipLeakStmtNode()
                {
                    rIdentifier = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 2].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    lIdentifier = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 6].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 7].attributes[ParseAttr.ast_node],
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });



            AddActionAtTail("declstmt -> decltype ID genparams ( params ) { statements }", (psr, production) => {
                var funcNode = new SyntaxTree.FuncDeclareNode()
                {
                    funcType = FunctionKind.Normal,
                    returnFlags = VarModifiers.None,

                    returnTypeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 8].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 7].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                    },
                    parametersNode = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    statementsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 6].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj)
                    && gpObj is List<SyntaxTree.IdentityNode> gpList
                    && gpList.Count > 0)
                {
                    funcNode.isTemplateFunction = true;
                    funcNode.templateParameters.AddRange(gpList);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = funcNode;
            });
            AddActionAtTail("declstmt -> tmodf decltype ID genparams ( params ) { statements }", (psr, production) => {
                var funcNode = new SyntaxTree.FuncDeclareNode()
                {
                    funcType = FunctionKind.Normal,
                    returnFlags = (VarModifiers)psr.stack[psr.stack.Top - 9].attributes[ParseAttr.tmodf],

                    returnTypeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 8].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 7].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                    },
                    parametersNode = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    statementsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 6].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj)
                    && gpObj is List<SyntaxTree.IdentityNode> gpList
                    && gpList.Count > 0)
                {
                    funcNode.isTemplateFunction = true;
                    funcNode.templateParameters.AddRange(gpList);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = funcNode;
            });

            AddActionAtTail("declstmt -> decltype operator ID genparams ( params ) { statements }", (psr, production) => {
                var funcNode = new SyntaxTree.FuncDeclareNode()
                {
                    funcType = FunctionKind.OperatorOverload,
                    returnFlags = VarModifiers.None,

                    returnTypeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 9].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 7].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                    },
                    parametersNode = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    statementsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 6].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj)
                    && gpObj is List<SyntaxTree.IdentityNode> gpList
                    && gpList.Count > 0)
                {
                    funcNode.isTemplateFunction = true;
                    funcNode.templateParameters.AddRange(gpList);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = funcNode;
            });
            AddActionAtTail("declstmt -> tmodf decltype operator ID genparams ( params ) { statements }", (psr, production) => {
                var funcNode = new SyntaxTree.FuncDeclareNode()
                {
                    funcType = FunctionKind.OperatorOverload,
                    returnFlags = (VarModifiers)psr.stack[psr.stack.Top - 10].attributes[ParseAttr.tmodf],

                    returnTypeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 9].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 7].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                    },
                    parametersNode = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    statementsNode = (SyntaxTree.StatementsNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 6].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj)
                    && gpObj is List<SyntaxTree.IdentityNode> gpList
                    && gpList.Count > 0)
                {
                    funcNode.isTemplateFunction = true;
                    funcNode.templateParameters.AddRange(gpList);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = funcNode;
            });

            AddActionAtTail("declstmt -> extern decltype ID genparams ( params ) ;", (psr, production) => {
                var externNode = new SyntaxTree.ExternFuncDeclareNode()
                {
                    returnTypeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 6].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 5].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                    },
                    parametersNode = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 4].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj)
                    && gpObj is List<SyntaxTree.IdentityNode> gpList
                    && gpList.Count > 0)
                {
                    throw new ParseException(ExceptioName.SyntaxAnalysisError, externNode.StartToken(), "extern function cannot have generic ");
                }

                psr.newElement.attributes[ParseAttr.ast_node] = externNode;
            });

            AddActionAtTail("declstmt -> class TYPE_NAME genparams inherit { declstatements }", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ClassDeclareNode()
                {
                    flags = TypeModifiers.None,
                    isTemplateClass = false,

                    classNameNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 5].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Class,
                    },

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 4].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj)
                    && gpObj is List<SyntaxTree.IdentityNode> gpList
                    && gpList.Count > 0)
                {
                    var classNode = (SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node];
                    classNode.isTemplateClass = true;
                    classNode.templateParameters.AddRange(gpList);
                }

                if(psr.stack[psr.stack.Top - 3].attributes.ContainsKey(ParseAttr.ast_node))
                {
                    ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node]).baseClassNameNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top - 3].attributes[ParseAttr.ast_node];
                }
                else
                {
                    ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node]).baseClassNameNode = null;
                }

                ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node]).memberDelareNodes.AddRange(
                    (List<SyntaxTree.DeclareNode>)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.decl_stmts]
                );
            });

            AddActionAtTail("declstmt -> class own TYPE_NAME genparams inherit { declstatements }", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ClassDeclareNode()
                {
                    flags = TypeModifiers.Own,
                    isTemplateClass = false,

                    classNameNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 5].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Class,
                    },


                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 4].attributes.TryGetValue(ParseAttr.generic_params, out var gpObj)
                    && gpObj is List<SyntaxTree.IdentityNode> gpList
                    && gpList.Count > 0)
                {
                    var classNode = (SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node];
                    classNode.isTemplateClass = true;
                    classNode.templateParameters.AddRange(gpList);
                }


                if(psr.stack[psr.stack.Top - 3].attributes.ContainsKey(ParseAttr.ast_node))
                {
                    ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node]).baseClassNameNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top - 3].attributes[ParseAttr.ast_node];
                }
                else
                {
                    ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node]).baseClassNameNode = null;
                }

                ((SyntaxTree.ClassDeclareNode)psr.newElement.attributes[ParseAttr.ast_node]).memberDelareNodes.AddRange(
                    (List<SyntaxTree.DeclareNode>)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.decl_stmts]
                );
            });


            AddActionAtTail("declstmt -> acmodif :", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.ast_node] = new AccessLabelNode()
                {
                    accessMofifier = (AccessMofifier)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.acmodif],
                };
            });

            AddActionAtTail("acmodif -> public", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.acmodif] = AccessMofifier.Public;
            });
            AddActionAtTail("acmodif -> private", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.acmodif] = AccessMofifier.Private;
            });


            AddActionAtTail("tmodf -> own", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.tmodf] = VarModifiers.Own;
            });
            AddActionAtTail("tmodf -> bor", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.tmodf] = VarModifiers.Bor;
            });

            AddActionAtTail("genparams -> GEN_LT genparamlist GEN_GT", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.generic_params] = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.generic_params];
            });
            AddActionAtTail("genparams -> ε", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.generic_params] = new List<SyntaxTree.IdentityNode>();
            });
            AddActionAtTail("genparamlist -> TYPE_NAME", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.generic_params] = new List<SyntaxTree.IdentityNode>
                {
                    new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Class,
                    }
                };
            });
            AddActionAtTail("genparamlist -> genparamlist , TYPE_NAME", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.generic_params] = psr.stack[psr.stack.Top - 2].attributes[ParseAttr.generic_params];
                ((List<SyntaxTree.IdentityNode>)psr.newElement.attributes[ParseAttr.generic_params]).Add(
                    new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.Class,
                    });
            });

            AddActionAtTail("genargs -> GEN_LT typearglist GEN_GT", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.generic_args] = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.generic_args];
            });

            AddActionAtTail("typearglist -> type", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.generic_args] = new List<SyntaxTree.TypeNode>
                {
                    (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                };
            });

            AddActionAtTail("typearglist -> typearglist , type", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.generic_args] = psr.stack[psr.stack.Top - 2].attributes[ParseAttr.generic_args];
                ((List<SyntaxTree.TypeNode>)psr.newElement.attributes[ParseAttr.generic_args]).Add(
                    (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );
            });



            AddActionAtTail("elifclauselist -> ε", (psr, production) => {
                psr.newElement.attributes[ParseAttr.condition_clause_list] = new List<SyntaxTree.ConditionClauseNode>();
            });

            AddActionAtTail("elifclauselist -> elifclauselist elifclause", (psr, production) => {
                psr.newElement.attributes[ParseAttr.condition_clause_list] = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.condition_clause_list];

                ((List<SyntaxTree.ConditionClauseNode>)(psr.newElement.attributes[ParseAttr.condition_clause_list])).Add(
                    (SyntaxTree.ConditionClauseNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                    );
            });

            AddActionAtTail("elifclause -> else if ( expr ) stmt", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ConditionClauseNode()
                {
                    conditionNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    thenNode = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("kwexpr -> default ( type )", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.DefaultValueNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("elseclause -> ε", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = null;
            });

            AddActionAtTail("elseclause -> else stmt", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ElseClauseNode()
                {

                    stmt = (SyntaxTree.StmtNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });


            AddActionAtTail("assign -> lvalue = expr", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.AssignNode()
                {
                    op = "=",

                    lvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    rvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            string[] specialAssignOps = new string[] { "+=", "-=", "*=", "/=", "%=" };
            foreach(var assignOp in specialAssignOps)
            {
                AddActionAtTail("assign -> lvalue " + assignOp + " expr", (psr, production) => {

                    psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.AssignNode()
                    {
                        op = assignOp,

                        lvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                        rvalueNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                        attributes = new Dictionary<AstAttr, object>(),
                    };
                });
            }


            AddActionAtTail("lvalue -> ID", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.IdentityNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                    token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                };
            });

            AddActionAtTail("lvalue -> memberaccess", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ObjectMemberAccessNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("lvalue -> indexaccess", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ElementAccessNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("decltype -> type", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("type -> arrtype", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ArrayTypeNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("type -> stype", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("type -> var", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.InferTypeNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("arrtype -> stypesb", (psr, production) => {

                var node = psr.stack[psr.stack.Top].attributes[ParseAttr.stype];

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ArrayTypeNode()
                {
                    elemtentType = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top].attributes[ParseAttr.stype],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("stype -> namedtype", (psr, production) => {
                var idNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes[ParseAttr.id];
                idNode.identiferType = SyntaxTree.IdentityNode.IdType.Class;

                var classTypeNode = new SyntaxTree.ClassTypeNode()
                {
                    classname = idNode,

                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top].attributes.TryGetValue(ParseAttr.generic_args, out var genericArgsObj)
                    && genericArgsObj is List<SyntaxTree.TypeNode> genericArgs
                    && genericArgs.Count > 0)
                {
                    classTypeNode.genericArguments.AddRange(genericArgs);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = classTypeNode;
            });

            AddActionAtTail("stype -> primitive", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.PrimitiveTypeNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("namedtype -> TYPE_NAME", (psr, production) => {
                psr.newElement.attributes[ParseAttr.id] = new SyntaxTree.IdentityNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                    token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.Undefined,
                };
                psr.newElement.attributes[ParseAttr.generic_args] = new List<SyntaxTree.TypeNode>();
            });
            AddActionAtTail("namedtype -> TYPE_NAME genargs", (psr, production) => {
                psr.newElement.attributes[ParseAttr.id] = new SyntaxTree.IdentityNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                    token = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.token] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.Undefined,
                };
                psr.newElement.attributes[ParseAttr.generic_args] = psr.stack[psr.stack.Top].attributes[ParseAttr.generic_args];
            });

            string[] primiveProductions = new string[] { "void", "bool", "byte", "int", "uint", "long", "ulong", "float", "double", "char", "string" };
            foreach(var t in primiveProductions)
            {
                AddActionAtTail("primitive -> " + t, (psr, production) => {
                    psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.PrimitiveTypeNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                    };
                });
            }


            AddActionAtTail("expr -> assign", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("expr -> nexpr", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });




            AddActionAtTail("stmtexpr -> assign", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("stmtexpr -> call", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("stmtexpr -> incdec", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("stmtexpr -> newobj", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });




            AddActionAtTail("nexpr -> bexpr", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("nexpr -> aexpr", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            string[] logicOperators = new string[] { "||", "&&" };
            foreach(var opname in logicOperators)
            {
                AddActionAtTail("bexpr -> bexpr " + opname + " bexpr", (psr, production) => {
                    psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BinaryOpNode()
                    {
                        op = opname,
                        leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                        rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                        attributes = new Dictionary<AstAttr, object>(),
                    };
                });
            }
            foreach(var opname in compareOprators)
            {
                AddActionAtTail("bexpr -> aexpr " + opname + " aexpr", (psr, production) => {
                    psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BinaryOpNode()
                    {
                        op = opname,
                        leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                        rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                        attributes = new Dictionary<AstAttr, object>(),
                    };
                });
            }
            AddActionAtTail("bexpr -> factor", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });




            AddActionAtTail("aexpr -> aexpr + term", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BinaryOpNode()
                {
                    op = "+",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("aexpr -> aexpr - term", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BinaryOpNode()
                {
                    op = "-",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("aexpr -> term", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });


            AddActionAtTail("term -> term * factor", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BinaryOpNode()
                {
                    op = "*",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("term -> term / factor", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BinaryOpNode()
                {
                    op = "/",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("term -> term % factor", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.BinaryOpNode()
                {
                    op = "%",
                    leftNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    rightNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("term -> factor", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("factor -> incdec", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("factor -> ! factor", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.UnaryOpNode()
                {
                    op = "!",
                    exprNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("factor -> - factor", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.UnaryOpNode()
                {
                    op = "NEG",
                    exprNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("factor -> cast", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("factor -> primary", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });


            AddActionAtTail("primary -> ( expr )", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("primary -> ID", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.IdentityNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                    token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                };
            });
            AddActionAtTail("primary -> this", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ThisNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                    token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                };
            });
            AddActionAtTail("primary -> memberaccess", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("primary -> newobj", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("primary -> newarr", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("primary -> kwexpr", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("primary -> indexaccess", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ElementAccessNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });

            AddActionAtTail("primary -> call", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.SpecialExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });


            AddActionAtTail("primary -> lit", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });


            AddActionAtTail("incdec -> ++ ID", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.IncDecNode()
                {
                    op = "++",
                    isOperatorFront = true,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("incdec -> -- ID", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.IncDecNode()
                {
                    op = "--",
                    isOperatorFront = true,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("incdec -> ID ++", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.IncDecNode()
                {
                    op = "++",
                    isOperatorFront = false,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("incdec -> ID --", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.IncDecNode()
                {
                    op = "--",
                    isOperatorFront = false,
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
                    },

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("call -> ID ( args )", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.CallNode()
                {
                    isMemberAccessFunction = false,
                    funcNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 3].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod
                    },
                    argumantsNode = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],


                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("call -> memberaccess ( args )", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.CallNode()
                {
                    isMemberAccessFunction = true,
                    funcNode = (SyntaxTree.ObjectMemberAccessNode)psr.stack[psr.stack.Top - 3].attributes[ParseAttr.ast_node],
                    argumantsNode = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],


                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            // call with generic args: ID genargs ( args )
            AddActionAtTail("call -> ID genargs ( args )", (psr, production) => {
                var callNode = new SyntaxTree.CallNode()
                {
                    isMemberAccessFunction = false,
                    funcNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top - 4].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod
                    },
                    argumantsNode = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 3].attributes.TryGetValue(ParseAttr.generic_args, out var gaObj)
                    && gaObj is List<SyntaxTree.TypeNode> gaList
                    && gaList.Count > 0)
                {
                    callNode.genericArguments.AddRange(gaList);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = callNode;
            });

            // call with generic args on member access: memberaccess genargs ( args )
            AddActionAtTail("call -> memberaccess genargs ( args )", (psr, production) => {
                var callNode = new SyntaxTree.CallNode()
                {
                    isMemberAccessFunction = true,
                    funcNode = (SyntaxTree.ObjectMemberAccessNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node],
                    argumantsNode = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 3].attributes.TryGetValue(ParseAttr.generic_args, out var gaObj)
                    && gaObj is List<SyntaxTree.TypeNode> gaList
                    && gaList.Count > 0)
                {
                    callNode.genericArguments.AddRange(gaList);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = callNode;
            });


            AddActionAtTail("indexaccess -> idsb", (psr, production) => {

                ((SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes[ParseAttr.id]).identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField;

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ElementAccessNode()
                {
                    containerNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes[ParseAttr.id],
                    indexNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.optidx],

                    attributes = new Dictionary<AstAttr, object>(),
                };

            });

            AddActionAtTail("indexaccess -> memberaccess [ aexpr ]", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ElementAccessNode()
                {
                    containerNode = (SyntaxTree.ObjectMemberAccessNode)psr.stack[psr.stack.Top - 3].attributes[ParseAttr.ast_node],
                    indexNode = ((SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node]),

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });



            AddActionAtTail("cast -> ( type ) factor", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.CastNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    factorNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });


            AddActionAtTail("newobj -> new namedtype ( )", (psr, production) => {

                var idNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.id];
                idNode.identiferType = SyntaxTree.IdentityNode.IdType.Class;

                var classTypeNode = new SyntaxTree.ClassTypeNode()
                {
                    classname = idNode,
                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 2].attributes.TryGetValue(ParseAttr.generic_args, out var genericArgsObj)
                    && genericArgsObj is List<SyntaxTree.TypeNode> genericArgs
                    && genericArgs.Count > 0)
                {
                    classTypeNode.genericArguments.AddRange(genericArgs);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.NewObjectNode()
                {
                    className = idNode,
                    typeNode = classTypeNode,

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("newarr -> new namedtype [ aexpr ]", (psr, production) => {

                var idNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top - 3].attributes[ParseAttr.id];
                idNode.identiferType = SyntaxTree.IdentityNode.IdType.Class;

                var classTypeNode = new SyntaxTree.ClassTypeNode()
                {
                    classname = idNode,
                    attributes = new Dictionary<AstAttr, object>(),
                };

                if(psr.stack[psr.stack.Top - 3].attributes.TryGetValue(ParseAttr.generic_args, out var genericArgsObj)
                    && genericArgsObj is List<SyntaxTree.TypeNode> genericArgs
                    && genericArgs.Count > 0)
                {
                    classTypeNode.genericArguments.AddRange(genericArgs);
                }

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.NewArrayNode()
                {
                    typeNode = classTypeNode,
                    lengthNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });
            AddActionAtTail("newarr -> new primitive [ aexpr ]", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.NewArrayNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 3].attributes[ParseAttr.ast_node],
                    lengthNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("memberaccess -> primary . ID", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ObjectMemberAccessNode()
                {
                    objectNode = (SyntaxTree.ExprNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node],
                    memberNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                        isMemberIdentifier = true,
                    },

                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("kwexpr -> typeof ( type )", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.TypeOfNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            AddActionAtTail("kwexpr -> sizeof ( type )", (psr, production) => {

                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.SizeOfNode()
                {
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });



            string[] litTypes = new string[] { "null", "LITINT", "LITUINT", "LITLONG", "LITULONG", "LITFLOAT", "LITDOUBLE", "LITCHAR", "LITSTRING", "LITBOOL" };
            foreach(var litType in litTypes)
            {

                AddActionAtTail("lit -> " + litType, (psr, production) => {
                    psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.LiteralNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                    };
                });
            }


            AddActionAtTail("param -> type ID", (psr, production) => {
                var node = new SyntaxTree.ParameterNode()
                {
                    flags = VarModifiers.None,
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    attributes = new(),
                };

                psr.newElement.attributes[ParseAttr.ast_node] = node;
            });

            AddActionAtTail("param -> tmodf type ID", (psr, production) => {
                var node = new SyntaxTree.ParameterNode()
                {
                    flags = (VarModifiers)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.tmodf],
                    typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
                    identifierNode = new SyntaxTree.IdentityNode()
                    {
                        attributes = new Dictionary<AstAttr, object>(),
                        token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
                        identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField,
                    },
                    attributes = new(),
                };

                psr.newElement.attributes[ParseAttr.ast_node] = node;
            });

            AddActionAtTail("params -> param", (psr, production) => {
                var list = new SyntaxTree.ParameterListNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                };
                list.parameterNodes.Add(
                    (SyntaxTree.ParameterNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );

                psr.newElement.attributes[ParseAttr.ast_node] = list;
            });

            AddActionAtTail("params -> params , param", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] =
                    (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node];

                ((SyntaxTree.ParameterListNode)psr.newElement.attributes[ParseAttr.ast_node]).parameterNodes.Add(
                    (SyntaxTree.ParameterNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );
            });

            AddActionAtTail("params -> ε", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ParameterListNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                };
            });

            //AddActionAtTail("params -> type ID", (psr, production) => {
            //    var node = new SyntaxTree.ParameterListNode()
            //    {
            //        attributes = new Dictionary<AstAttr, object>(),
            //    };

            //    node.parameterNodes.Add(new SyntaxTree.ParameterNode()
            //    {
            //        flags = VarModifiers.None,
            //        typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
            //        identifierNode = new SyntaxTree.IdentityNode()
            //        {
            //            attributes = new Dictionary<AstAttr, object>(),
            //            token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
            //            identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
            //        },
            //        attributes = new(),
            //    });

            //    psr.newElement.attributes[ParseAttr.ast_node] = node;

            //});
            //AddActionAtTail("params -> tmodf type ID", (psr, production) => {
            //    var node = new SyntaxTree.ParameterListNode()
            //    {
            //        attributes = new Dictionary<AstAttr, object>(),
            //    };

            //    node.parameterNodes.Add(new SyntaxTree.ParameterNode()
            //    {
            //        flags = (VarModifiers)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.tmodf],
            //        typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
            //        identifierNode = new SyntaxTree.IdentityNode()
            //        {
            //            attributes = new Dictionary<AstAttr, object>(),
            //            token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
            //            identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
            //        },
            //        attributes = new(),
            //    });

            //    psr.newElement.attributes[ParseAttr.ast_node] = node;
            //});

            //AddActionAtTail("params -> params , type ID", (psr, production) => {
            //    psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 3].attributes[ParseAttr.ast_node];

            //    ((SyntaxTree.ParameterListNode)psr.newElement.attributes[ParseAttr.ast_node]).parameterNodes.Add(
            //        new SyntaxTree.ParameterNode()
            //        {
            //            flags = VarModifiers.None,
            //            typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
            //            identifierNode = new SyntaxTree.IdentityNode()
            //            {
            //                attributes = new Dictionary<AstAttr, object>(),
            //                token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
            //                identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
            //            },
            //            attributes = new(),
            //        }
            //    );
            //});
            //AddActionAtTail("params -> params , tmodf type ID", (psr, production) => {
            //    psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ParameterListNode)psr.stack[psr.stack.Top - 4].attributes[ParseAttr.ast_node];

            //    ((SyntaxTree.ParameterListNode)psr.newElement.attributes[ParseAttr.ast_node]).parameterNodes.Add(
            //        new SyntaxTree.ParameterNode()
            //        {
            //            flags = (VarModifiers)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.tmodf],
            //            typeNode = (SyntaxTree.TypeNode)psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node],
            //            identifierNode = new SyntaxTree.IdentityNode()
            //            {
            //                attributes = new Dictionary<AstAttr, object>(),
            //                token = psr.stack[psr.stack.Top].attributes[ParseAttr.token] as Token,
            //                identiferType = SyntaxTree.IdentityNode.IdType.VariableOrField
            //            },
            //            attributes = new(),
            //        }
            //    );
            //});


            AddActionAtTail("args -> ε", (psr, production) =>
            {
                psr.newElement.attributes[ParseAttr.ast_node] = new SyntaxTree.ArgumentListNode()
                { };
            });

            AddActionAtTail("args -> expr", (psr, production) => {

                var node = new SyntaxTree.ArgumentListNode()
                { };

                node.arguments.Add((SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]);

                psr.newElement.attributes[ParseAttr.ast_node] = node;
            });

            AddActionAtTail("args -> args , expr", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = (SyntaxTree.ArgumentListNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node];

                ((SyntaxTree.ArgumentListNode)psr.newElement.attributes[ParseAttr.ast_node]).arguments.Add(
                    (SyntaxTree.ExprNode)psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node]
                );
            });


            AddActionAtTail("stypesb -> typeidsb", (psr, production) => {

                ((SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes[ParseAttr.id]).identiferType = SyntaxTree.IdentityNode.IdType.Class;

                var classTypeNode = new SyntaxTree.ClassTypeNode()
                {
                    classname = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes[ParseAttr.id],

                    attributes = new Dictionary<AstAttr, object>()
                };

                if(psr.stack[psr.stack.Top].attributes.TryGetValue(ParseAttr.generic_args, out var genericArgsObj)
                    && genericArgsObj is List<SyntaxTree.TypeNode> genericArgs
                    && genericArgs.Count > 0)
                {
                    classTypeNode.genericArguments.AddRange(genericArgs);
                }

                psr.newElement.attributes[ParseAttr.stype] = classTypeNode;

                psr.newElement.attributes[ParseAttr.optidx] = null;
            });

            AddActionAtTail("stypesb -> primitivesb", (psr, production) => {
                psr.newElement.attributes[ParseAttr.stype] = psr.stack[psr.stack.Top].attributes[ParseAttr.primitive];
                psr.newElement.attributes[ParseAttr.optidx] = null;
            });
            AddActionAtTail("idsb -> ID [ optidx ]", (psr, production) => {

                psr.newElement.attributes[ParseAttr.id] = new SyntaxTree.IdentityNode()
                {
                    attributes = new Dictionary<AstAttr, object>(),
                    token = psr.stack[psr.stack.Top - 3].attributes[ParseAttr.token] as Token,
                    identiferType = SyntaxTree.IdentityNode.IdType.Undefined
                };
                psr.newElement.attributes[ParseAttr.optidx] = psr.stack[psr.stack.Top - 1].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("typeidsb -> namedtype [ ]", (psr, production) => {

                var idNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top - 2].attributes[ParseAttr.id];
                idNode.identiferType = SyntaxTree.IdentityNode.IdType.Undefined;

                psr.newElement.attributes[ParseAttr.id] = idNode;
                psr.newElement.attributes[ParseAttr.generic_args] = psr.stack[psr.stack.Top - 2].attributes[ParseAttr.generic_args];
                psr.newElement.attributes[ParseAttr.optidx] = null;
            });
            AddActionAtTail("primitivesb -> primitive [ ]", (psr, production) => {
                psr.newElement.attributes[ParseAttr.primitive] = psr.stack[psr.stack.Top - 2].attributes[ParseAttr.ast_node];
                psr.newElement.attributes[ParseAttr.optidx] = null;
            });
            AddActionAtTail("optidx -> aexpr", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = psr.stack[psr.stack.Top].attributes[ParseAttr.ast_node];
            });
            AddActionAtTail("optidx -> ε", (psr, production) => {
                psr.newElement.attributes[ParseAttr.ast_node] = null;
            });



            AddActionAtTail("inherit -> : namedtype", (psr, production) => {
                var idNode = (SyntaxTree.IdentityNode)psr.stack[psr.stack.Top].attributes[ParseAttr.id];
                idNode.identiferType = SyntaxTree.IdentityNode.IdType.Class;

                psr.newElement.attributes[ParseAttr.ast_node] = idNode;
            });
            AddActionAtTail("inherit -> ε", (psr, production) => {
            });
        }

        // 插入语义动作
        public void AddActionAtTail(string productionExpression, System.Action<LRParser, Production> act)
        {
            Production production = parserContext.data.grammerSet.productions.FirstOrDefault(p => p.ToExpression() == productionExpression);

            if(production == null)
                throw new GizboxException(ExceptioName.ProductionNotFound, productionExpression);

            AddActionAtTail(production, act);
        }
        public void AddActionAtTail(Production production, System.Action<LRParser, Production> act)
        {
            SemanticAction semanticAction = new SemanticAction(this.parserContext, production, act);

            if(translateScheme.ContainsKey(production) == false)
            {
                translateScheme[production] = new List<SemanticAction>();
            }

            translateScheme[production].Add(semanticAction);
        }


        // 执行语义动作  
        public void ExecuteSemanticAction(Production production)
        {
            if(translateScheme.ContainsKey(production) == false)
                return;

            foreach(var act in translateScheme[production])
            {
                act.Execute();
            }
        }
    }
}
