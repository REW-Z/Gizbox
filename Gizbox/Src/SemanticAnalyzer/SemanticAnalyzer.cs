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



namespace Gizbox
{
    /// <summary>
    /// 语法分析阶段属性枚举
    /// </summary>
    public enum ParseAttr
    {
        token,
        start,
        end,

        cst_node,
        ast_node,

        import_list,
        using_list,
        decl_stmts,
        condition_clause_list,
        tmodf,
        id,
        stype,
        optidx,
        primitive,
        generic_params,
        generic_args,

        acmodif,
    }

    /// <summary>
    /// 语义分析阶段属性枚举
    /// </summary>
    public enum AstAttr
    {
        start,
        end,

        env,
        global_env,
        klass,
        member_name,
        member_access_modifiers,
        uid,
        type,
        mangled_name,
        extern_name,
        def_at_env,
        var_rec,
        const_rec,
        func_rec,
        class_rec,
        obj_class_rec,
        not_a_property,
        name_completed,

        __codegen_mark_min,

        drop_var_exit_env,
        drop_var_before_return,
        drop_var_before_assign_stmt,
        drop_field_before_assign_stmt,
        drop_expr_result_after_stmt,
        store_expr_result,
        set_null_after_stmt,
        set_null_after_call,

        set_null_field_after_stmt,


        __codegen_mark_max,


        ret,
    }
}

/// <summary>
/// 语义规则  
/// </summary>
namespace Gizbox.SemanticRule
{
    /// <summary>
    /// 语义分析器  
    /// </summary>
    public partial class SemanticAnalyzer//（补充的语义分析器，自底向上规约已经进行了部分语义分析）  
    {
        public Compiler compilerContext;

        public SyntaxTree ast;

        public IRUnit ilUnit;

        private Gizbox.GStack<SymbolTable> envStack;




        private static readonly SymbolTable.RecordFlag OwnershipModelMask =
            SymbolTable.RecordFlag.OwnerVar | SymbolTable.RecordFlag.BorrowVar | SymbolTable.RecordFlag.ManualVar;


        //temp  
        private int blockCounter = 0;//Block自增  
        private int ifCounter = 0;//if语句标号自增  
        private int whileCounter = 0;//while语句标号自增  
        private int forCounter = 0;//for语句标号自增  

        //temp  
        private string currentNamespace = "";
        private List<string> namespaceUsings = new List<string>();

        //temp  
        private LifetimeInfo lifeTimeInfo = new();


        //可用命名空间前缀列表(示例："XXX::YYY::")    
        private List<string> availableNamespacePrefixList = new();


        /// <summary>
        /// 构造  
        /// </summary>
        public SemanticAnalyzer(SyntaxTree ast, IRUnit ilUnit, Compiler compilerContext)
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
                lib.EnsureAst();

                foreach (var depNameOfLib in lib.dependencies)
                {
                    var libdep = compilerContext.LoadLib(depNameOfLib);
                    libdep.EnsureAst();
                    lib.AddDependencyLib(libdep);
                }

                this.ilUnit.AddDependencyLib(lib);
            }

            this.ilUnit.astRoot = ast.rootNode;

            //global env  
            ast.rootNode.attributes[AstAttr.global_env] = ilUnit.globalScope.env;


            //可用命名空间前缀收集  
            CollectAllUsingNamespacePrefix();
            //模板特化（AST层面）
            SpecializeClassTemplates();
            SpecializeFunctionTemplates();

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

            //应用所有树节点重写  
            ast.ApplyAllOverrides();

            //Pass4
            envStack.Clear();
            envStack.Push(ilUnit.globalScope.env);
            lifeTimeInfo.mainBranch.scopeStack.Push(new LifetimeInfo.ScopeInfo());
            Pass4_OwnershipLifetime(ast.rootNode);
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
                            constDeclNode.attributes[AstAttr.const_rec] = newRec;
                        }
                    }
                    break;
                //顶级变量声明语句
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        if (isGlobalOrTopNamespace)
                        {
                            //全局变量不允许有所有权修饰符
                            if(varDeclNode.flags.HasFlag(VarModifiers.Own) || varDeclNode.flags.HasFlag(VarModifiers.Bor))
                                throw new SemanticException(ExceptioName.OwnershipError, varDeclNode, "global variable can not have ownership modifiers");

                            //附加命名空间名  
                            if (isTopLevelAtNamespace)
                                varDeclNode.identifierNode.SetPrefix(currentNamespace);

                            //补全类型名  
                            TryCompleteType(varDeclNode.typeNode);

                            //是否初始值是常量
                            string initVal = string.Empty;
                            if(varDeclNode.initializerNode is LiteralNode lit)
                            {
                                initVal = lit.token.attribute;
                            }

                            //新建符号表条目  
                            var newRec = envStack.Peek().NewRecord(
                                varDeclNode.identifierNode.FullName,
                                SymbolTable.RecordCatagory.Variable,
                                varDeclNode.typeNode.TypeExpression(),
                                initValue:initVal
                                );
                            varDeclNode.attributes[AstAttr.var_rec] = newRec;
                        }
                    }
                    break;
                //所有权Capture语句
                case SyntaxTree.OwnershipCaptureStmtNode captureNode:
                    {
                        if(isGlobalOrTopNamespace)
                        {
                            //所有权capture语句不能在全局作用域使用。只能在局部作用域使用。  
                            throw new SemanticException(ExceptioName.OwnershipError, captureNode, "ownership capture stmt can not be used in global scope");
                        }
                    }
                    break;
                //所有权Leak语句  
                case SyntaxTree.OwnershipLeakStmtNode leakNode:
                    {
                        if(isGlobalOrTopNamespace)
                        {
                            //所有权leak语句不能在全局作用域使用。只能在局部作用域使用。
                            throw new SemanticException(ExceptioName.OwnershipError, leakNode, "ownership leak stmt can not be used in global scope");
                        }
                    }
                    break;
                //顶级函数声明语句
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {  
                        //函数模板  
                        if(funcDeclNode.isTemplateFunction)
                        {
                            //附加命名空间名  
                            if(isTopLevelAtNamespace == false)
                            {
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "template function can only declare at global env.");
                            }

                            funcDeclNode.identifierNode.SetPrefix(currentNamespace);
                            ilUnit.templateFunctions.Add(funcDeclNode.identifierNode.FullName);
                            break;
                        }
                            


                        if (isGlobalOrTopNamespace)
                        {

                            bool isMethod = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;
                            if (isMethod) throw new Exception();//顶层函数不可能是方法

                            //附加命名空间名  
                            if (isTopLevelAtNamespace)
                                funcDeclNode.identifierNode.SetPrefix(currentNamespace);


                            //形参类型补全  
                            foreach (var p in funcDeclNode.parametersNode.parameterNodes)
                            {
                                TryCompleteType(p.typeNode);
                            }
                            //返回类型补全
                            TryCompleteType(funcDeclNode.returnTypeNode);

                            //符号的类型表达式  
                            string typeExpr = "";
                            for (int i = 0; i < funcDeclNode.parametersNode.parameterNodes.Count; ++i)
                            {
                                if (i != 0) typeExpr += ",";
                                var paramNode = funcDeclNode.parametersNode.parameterNodes[i];
                                typeExpr += (paramNode.typeNode.TypeExpression());
                            }
                            typeExpr += (" => " + funcDeclNode.returnTypeNode.TypeExpression());


                            //函数修饰名称  
                            var paramTypeArr = funcDeclNode.parametersNode.parameterNodes.Select(n => n.typeNode.TypeExpression()).ToArray();
                            var funcMangledName = funcDeclNode.identifierNode.FullName;
                            if(funcMangledName != "main")
                            {
                                funcMangledName = Utils.Mangle(funcDeclNode.identifierNode.FullName, paramTypeArr);
                            }
                            

                            funcDeclNode.attributes[AstAttr.mangled_name] = funcMangledName;

                            //新的作用域  
                            string envName = isMethod ? envStack.Peek().name + "." + funcMangledName : funcMangledName;

                            var newEnv = new SymbolTable(envName, SymbolTable.TableCatagory.FuncScope, envStack.Peek());
                            funcDeclNode.attributes[AstAttr.env] = newEnv;


                            //添加条目  
                            var newRec = envStack.Peek().NewRecord(
                                funcMangledName,
                                SymbolTable.RecordCatagory.Function,
                                typeExpr,
                                newEnv
                                );
                            newRec.rawname = funcDeclNode.identifierNode.FullName;

                            funcDeclNode.attributes[AstAttr.func_rec] = newRec;

                            //重载函数  
                            if(funcDeclNode.funcType == FunctionKind.OperatorOverload)
                            {
                                newRec.flags |= SymbolTable.RecordFlag.OperatorOverloadFunc;
                            }
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
                            //返回类型补全  
                            TryCompleteType(externFuncDeclNode.returnTypeNode);

                            //符号的类型表达式  
                            string typeExpr = "";
                            for (int i = 0; i < externFuncDeclNode.parametersNode.parameterNodes.Count; ++i)
                            {
                                if (i != 0) typeExpr += ",";
                                var paramNode = externFuncDeclNode.parametersNode.parameterNodes[i];
                                typeExpr += (paramNode.typeNode.TypeExpression());
                            }
                            typeExpr += (" => " + externFuncDeclNode.returnTypeNode.TypeExpression());

                            //函数修饰名称  
                            var paramTypeArr = externFuncDeclNode.parametersNode.parameterNodes.Select(n => n.typeNode.TypeExpression()).ToArray();
                            //var funcFullName = Utils.Mangle(externFuncDeclNode.identifierNode.FullName, paramTypeArr);
                            var funcFullName = Utils.ToExternFuncName(externFuncDeclNode.identifierNode.FullName);
                            externFuncDeclNode.attributes[AstAttr.extern_name] = funcFullName;

                            //新的作用域  
                            string envName = funcFullName;

                            var newEnv = new SymbolTable(envName, SymbolTable.TableCatagory.FuncScope, envStack.Peek());
                            externFuncDeclNode.attributes[AstAttr.env] = newEnv;


                            //添加条目  
                            var newRec = envStack.Peek().NewRecord(
                                funcFullName,
                                SymbolTable.RecordCatagory.Function,
                                typeExpr,
                                newEnv
                                );
                            newRec.rawname = externFuncDeclNode.identifierNode.FullName;
                            newRec.flags |= SymbolTable.RecordFlag.ExternFunc;
                            externFuncDeclNode.attributes[AstAttr.func_rec] = newRec;
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.ExternFunctionGlobalOrNamespaceOnly, externFuncDeclNode, "");
                        }
                    }
                    break;
                //类声明  
                case SyntaxTree.ClassDeclareNode classDeclNode:
                    {
                        //类模板  
                        if(classDeclNode.isTemplateClass)
                        {
                            //附加命名空间名  
                            if(isTopLevelAtNamespace)
                                classDeclNode.classNameNode.SetPrefix(currentNamespace);
                            ilUnit.templateClasses.Add(classDeclNode.classNameNode.FullName);
                            break;
                        }

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
                            classDeclNode.attributes[AstAttr.env] = newEnv;

                            //添加条目-类名    
                            var newRec = envStack.Peek().NewRecord(
                                classDeclNode.classNameNode.FullName,
                                SymbolTable.RecordCatagory.Class,
                                "",
                                newEnv
                                );
                            classDeclNode.attributes[AstAttr.class_rec] = newRec;

                            //所有权模型  
                            if(classDeclNode.flags.HasFlag(TypeModifiers.Own))
                                newRec.flags |= SymbolTable.RecordFlag.OwnershipClass;
                            else
                                newRec.flags |= SymbolTable.RecordFlag.ManualClass;
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.ClassDefinitionGlobalOrNamespaceOnly, classDeclNode, "");
                        }
                    }
                    break;
                case SyntaxTree.AccessLabelNode accessLabelNode:
                    {
                        throw new SemanticException(ExceptioName.SemanticAnalysysError, accessLabelNode, "access label node can only declare in class scope.");
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
            if(node.Parent != null && node.Parent.Parent != null)
            {
                if(node.Parent is SyntaxTree.StatementsNode && node.Parent.Parent is SyntaxTree.NamespaceNode)
                {
                    isTopLevelAtNamespace = true;
                }
            }
            bool isGlobal = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.GlobalScope;
            bool isGlobalOrTopAtNamespace = isTopLevelAtNamespace || isGlobal;



            switch(node)
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
                        stmtBlockNode.attributes[AstAttr.env] = newEnv;
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
                        constDeclNode.identifierNode.attributes[AstAttr.def_at_env] = envStack.Peek();

                        //（非全局）不支持成员常量  
                        if (isGlobalOrTopAtNamespace == false)
                        {
                            throw new SemanticException(ExceptioName.ConstantGlobalOrNamespaceOnly, constDeclNode, "");
                        }
                    }
                    break;
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        //Id at env
                        varDeclNode.identifierNode.attributes[AstAttr.def_at_env] = envStack.Peek();


                        //（非全局变量）成员字段或者局部变量  
                        if (isGlobalOrTopAtNamespace == false)
                        {
                            //使用原名  
                            varDeclNode.identifierNode.SetPrefix(null);

                            //补全类型  
                            TryCompleteType(varDeclNode.typeNode);

                            //访问控制修饰符  
                            bool isPrivate = (varDeclNode.attributes.TryGetValue(AstAttr.member_access_modifiers, out object accessmodif) && (AccessMofifier)accessmodif == AccessMofifier.Private);

                            //新建符号表条目  
                            var newRec = envStack.Peek().NewRecord(
                                varDeclNode.identifierNode.FullName,
                                SymbolTable.RecordCatagory.Variable,
                                varDeclNode.typeNode.TypeExpression()
                                );
                            newRec.accessFlags = isPrivate ? SymbolTable.AccessFlag.Private : SymbolTable.AccessFlag.Public;
                            
                            varDeclNode.attributes[AstAttr.var_rec] = newRec;

                            //是成员字段定义 -> 预先读取所有权模型  
                            if(envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope)
                            {
                                var ownershipModel = GetOwnershipModel(varDeclNode.flags, varDeclNode.typeNode);
                                newRec.flags |= ownershipModel;

                                if(ownershipModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))//成员字段不能是借用类型  
                                    throw new SemanticException(ExceptioName.OwnershipError_MemberVarCannotBeBorrow, varDeclNode, newRec.name);
                            }
                        }
                    }
                    break;
                //所有权Capture语句
                case SyntaxTree.OwnershipCaptureStmtNode captureNode:
                    {
                        //Id at env
                        captureNode.lIdentifier.attributes[AstAttr.def_at_env] = envStack.Peek();

                        //（非全局变量）成员字段或者局部变量  
                        if(isGlobalOrTopAtNamespace == false)
                        {
                            //使用原名  
                            captureNode.lIdentifier.SetPrefix(null);

                            //补全类型  
                            TryCompleteType(captureNode.typeNode);

                            //新建符号表条目  
                            var newRec = envStack.Peek().NewRecord(
                                captureNode.lIdentifier.FullName,
                                SymbolTable.RecordCatagory.Variable,
                                captureNode.typeNode.TypeExpression()
                                );
                            captureNode.attributes[AstAttr.var_rec] = newRec;
                        }
                    }
                    break;
                //所有权Leak语句  
                case SyntaxTree.OwnershipLeakStmtNode leakNode:
                    {
                        //Id at env
                        leakNode.lIdentifier.attributes[AstAttr.def_at_env] = envStack.Peek();

                        //（非全局变量）成员字段或者局部变量  
                        if(isGlobalOrTopAtNamespace == false)
                        {
                            //使用原名  
                            leakNode.lIdentifier.SetPrefix(null);

                            //补全类型  
                            TryCompleteType(leakNode.typeNode);

                            //新建符号表条目  
                            var newRec = envStack.Peek().NewRecord(
                                leakNode.lIdentifier.FullName,
                                SymbolTable.RecordCatagory.Variable,
                                leakNode.typeNode.TypeExpression()
                                );
                            leakNode.attributes[AstAttr.var_rec] = newRec;
                        }
                    }
                    break;
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        if(funcDeclNode.isTemplateFunction)
                            break;

                        //Id at env
                        funcDeclNode.identifierNode.attributes[AstAttr.def_at_env] = envStack.Peek();


                        //是否是实例成员函数  
                        bool isMethod = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;
                        string className = null;
                        if (isMethod) className = envStack.Peek().name;
                        if (isMethod && isGlobalOrTopAtNamespace) throw new SemanticException(ExceptioName.NamespaceTopLevelNonMemberFunctionOnly, funcDeclNode, "");


                        //如果是成员函数 - 加入符号表  
                        if (isGlobalOrTopAtNamespace == false && isMethod == true)
                        {
                            //使用原名（成员函数）  
                            funcDeclNode.identifierNode.SetPrefix(null);


                            //形参类型补全  
                            foreach (var p in funcDeclNode.parametersNode.parameterNodes)
                            {
                                TryCompleteType(p.typeNode);
                            }
                            //返回值类型补全    
                            TryCompleteType(funcDeclNode.returnTypeNode);

                            //形参列表 （成员函数）(不包含this类型)  
                            var paramTypeArr = funcDeclNode.parametersNode.parameterNodes.Select(n => n.typeNode.TypeExpression()).ToArray();


                            //符号的类型表达式（成员函数）  
                            string typeExpr = "";
                            for (int i = 0; i < paramTypeArr.Length; ++i)
                            {
                                if (i != 0) typeExpr += ",";
                                typeExpr += (paramTypeArr[i]);
                            }
                            typeExpr += (" => " + funcDeclNode.returnTypeNode.TypeExpression());

                            //函数修饰名称（成员函数）(要加上this基类型)  
                            var funcMangledName = Utils.Mangle(funcDeclNode.identifierNode.FullName, paramTypeArr);
                            funcDeclNode.attributes[AstAttr.mangled_name] = funcMangledName;


                            //新的作用域（成员函数）  
                            string envName = isMethod ? envStack.Peek().name + "." + funcMangledName : funcMangledName;

                            var newEnv = new SymbolTable(envName, SymbolTable.TableCatagory.FuncScope, envStack.Peek());
                            funcDeclNode.attributes[AstAttr.env] = newEnv;

                            //类符号表同名方法去重（成员函数）    
                            if (envStack.Peek().ContainRecordName(funcMangledName))
                            {
                                envStack.Peek().records.Remove(funcMangledName);
                            }

                            //添加到虚函数表（成员函数）    
                            this.ilUnit.vtables[className].NewRecord(funcMangledName, className);

                            //成员访问控制修饰符  
                            bool isPrivate = (funcDeclNode.attributes.TryGetValue(AstAttr.member_access_modifiers, out object accessmodif) && (AccessMofifier)accessmodif == AccessMofifier.Private);

                            //添加条目（成员函数）  
                            var newRec = envStack.Peek().NewRecord(
                                funcMangledName,
                                SymbolTable.RecordCatagory.Function,
                                typeExpr,
                                newEnv
                                );
                            newRec.rawname = funcDeclNode.identifierNode.FullName;
                            newRec.accessFlags = isPrivate ? SymbolTable.AccessFlag.Private : SymbolTable.AccessFlag.Public;

                            funcDeclNode.attributes[AstAttr.func_rec] = newRec;
                        }





                        {
                            SymbolTable funcEnv = (SymbolTable)funcDeclNode.attributes[AstAttr.env];

                            //进入函数作用域  
                            envStack.Push(funcEnv);



                            //隐藏的this参数加入符号表    
                            if(isMethod)
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
                        externFuncDeclNode.identifierNode.attributes[AstAttr.def_at_env] = envStack.Peek();


                        //PASS1止于添加符号条目  

                        //Env  
                        var funcEnv = (SymbolTable)externFuncDeclNode.attributes[AstAttr.env];

                        //进入函数作用域  
                        envStack.Push(funcEnv);


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
                        paramNode.identifierNode.attributes[AstAttr.def_at_env] = envStack.Peek();

                        //参数类型补全    
                        TryCompleteType(paramNode.typeNode);

                        //形参加入函数作用域的符号表  
                        var newRec = envStack.Peek().NewRecord(
                            paramNode.identifierNode.FullName,
                            SymbolTable.RecordCatagory.Param,
                            paramNode.typeNode.TypeExpression()
                            );
                        paramNode.attributes[AstAttr.var_rec] = newRec;
                    }
                    break;
                case SyntaxTree.ClassDeclareNode classDeclNode:
                    {
                        if(classDeclNode.isTemplateClass)
                            break;

                        //Id at env
                        classDeclNode.classNameNode.attributes[AstAttr.def_at_env] = envStack.Peek();


                        //PASS1止于添加符号条目  

                        //ENV  
                        var newEnv = (SymbolTable)classDeclNode.attributes[AstAttr.env];


                        //补全继承基类的类名    
                        if (classDeclNode.baseClassNameNode != null)
                            TryCompleteIdenfier(classDeclNode.baseClassNameNode);

                        //新建虚函数表  
                        string classFullName = classDeclNode.classNameNode.FullName;
                        var vtable = ilUnit.vtables[classFullName] = new VTable(classFullName);
                        Log("新的虚函数表：" + classFullName);

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


                        //新定义的成员字段设定访问控制(默认public)  
                        AccessMofifier currentAccessModif = AccessMofifier.Public;
                        foreach(var declNode in classDeclNode.memberDelareNodes)
                        {
                            if(declNode is AccessLabelNode accessLabelNode)
                            {
                                currentAccessModif = accessLabelNode.accessMofifier;
                                continue;
                            }

                            declNode.attributes[AstAttr.member_access_modifiers] = currentAccessModif;
                        }

                        //新定义的成员字段加入符号表
                        foreach (var declNode in classDeclNode.memberDelareNodes)
                        {
                            Pass2_CollectOtherSymbols(declNode);
                        }

                        //构造函数  
                        {
                            //默认隐式构造函数的符号表  
                            var ctorEnv = new SymbolTable(classDeclNode.classNameNode.FullName + "::ctor", SymbolTable.TableCatagory.FuncScope, newEnv);
                            ctorEnv.NewRecord("this", SymbolTable.RecordCatagory.Param, classDeclNode.classNameNode.FullName);
                            //添加条目-构造函数    
                            var ctorRec = newEnv.NewRecord(
                                classDeclNode.classNameNode.FullName + "::ctor",
                                SymbolTable.RecordCatagory.Function,
                                GType.GenFuncType(GType.Parse("void"), GType.Parse(classDeclNode.classNameNode.FullName)).ToString(),
                                ctorEnv
                            );
                            ctorRec.rawname = classDeclNode.classNameNode.FullName + "::ctor";
                            ctorRec.flags |= SymbolTable.RecordFlag.Ctor;
                        }

                        //析构函数  
                        {
                            //默认隐式析构函数的符号表  
                            var dtorEnv = new SymbolTable(classDeclNode.classNameNode.FullName + "::dtor", SymbolTable.TableCatagory.FuncScope, newEnv);
                            dtorEnv.NewRecord("this", SymbolTable.RecordCatagory.Param, classDeclNode.classNameNode.FullName);
                            //添加条目-析构函数
                            var dtorRec = newEnv.NewRecord(
                                classDeclNode.classNameNode.FullName + "::dtor",
                                SymbolTable.RecordCatagory.Function,
                                GType.GenFuncType(GType.Parse("void"), GType.Parse(classDeclNode.classNameNode.FullName)).ToString(),
                                dtorEnv
                            );
                            dtorRec.rawname = classDeclNode.classNameNode.FullName + "::dtor";
                            dtorRec.flags |= SymbolTable.RecordFlag.Dtor;
                        }

                        //生成类的内存布局  
                        GenClassLayoutInfo(classDeclNode.attributes[AstAttr.class_rec] as SymbolTable.Record);


                        //离开类作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.IfStmtNode ifNode:
                    {
                        ifNode.attributes[AstAttr.uid] = ifCounter++;

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
                        whileNode.attributes[AstAttr.uid] = whileCounter++;

                        Pass2_CollectOtherSymbols(whileNode.stmtNode);
                    }
                    break;
                case SyntaxTree.ForStmtNode forNode:
                    {
                        forNode.attributes[AstAttr.uid] = forCounter++;

                        //新的作用域  
                        var newEnv = new SymbolTable("ForLoop" + (int)forNode.attributes[AstAttr.uid], SymbolTable.TableCatagory.LoopScope, envStack.Peek());
                        forNode.attributes[AstAttr.env] = newEnv;

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
        /// PASS3:语义分析（类型检查、树节点重写等）      
        /// </summary>
        private void Pass3_AnalysisNode(SyntaxTree.Node node)
        {
            if(node.overrideNode != null)
            {
                Pass3_AnalysisNode(node.overrideNode);
                return;
            }

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
                        envStack.Push(stmtBlockNode.attributes[AstAttr.env] as SymbolTable);

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

                        //常量类型不支持推断  
                        if (constDeclNode.typeNode is SyntaxTree.InferTypeNode)
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, constDeclNode.typeNode, "");

                        //类型检查（初始值）  
                        bool valid = CheckType_Equal(constDeclNode.typeNode.TypeExpression(), AnalyzeTypeExpression(constDeclNode.litValNode));
                        if (!valid)
                            throw new SemanticException(ExceptioName.ConstantTypeDeclarationError, constDeclNode, "type:" + constDeclNode.typeNode.TypeExpression() + "  value type:" + AnalyzeTypeExpression(constDeclNode.litValNode));
                    }
                    break;
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        //分析初始化表达式  
                        Pass3_AnalysisNode(varDeclNode.initializerNode);

                        //类型推断  
                        if (varDeclNode.typeNode is InferTypeNode typeNode)
                        {
                            var typeExpr = InferType(typeNode, varDeclNode.initializerNode);
                            var record = envStack.Peek().GetRecord(varDeclNode.identifierNode.FullName);
                            record.typeExpression = typeExpr;
                        }
                        //类型检查（初始值）  
                        else
                        {
                            bool valid = CheckType_Equal(varDeclNode.typeNode.TypeExpression(), varDeclNode.initializerNode);
                            if(!valid)
                            {
                                var a = varDeclNode.typeNode.TypeExpression();
                                var b = AnalyzeTypeExpression(varDeclNode.initializerNode);
                                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, varDeclNode, "type:" + varDeclNode.typeNode.TypeExpression() + "  intializer type:" + AnalyzeTypeExpression(varDeclNode.initializerNode));
                            }
                                
                        }
                    }
                    break;
                case SyntaxTree.OwnershipCaptureStmtNode captureNode:
                    {
                        //分析右边变量 
                        Pass3_AnalysisNode(captureNode.rIdentifier);

                        //类型推断  
                        if(captureNode.typeNode is InferTypeNode typeNode)
                        {
                            var typeExpr = InferType(typeNode, captureNode.rIdentifier);
                            var record = envStack.Peek().GetRecord(captureNode.lIdentifier.FullName);
                            record.typeExpression = typeExpr;
                        }
                        //类型检查（初始值）  
                        else
                        {
                            bool valid = CheckType_Equal(captureNode.typeNode.TypeExpression(), captureNode.rIdentifier);
                            if(!valid)
                            {
                                var a = captureNode.typeNode.TypeExpression();
                                var b = AnalyzeTypeExpression(captureNode.rIdentifier);
                                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, captureNode, "type:" + captureNode.typeNode.TypeExpression() + "  r-idendifier type:" + AnalyzeTypeExpression(captureNode.rIdentifier));
                            }

                        }

                        //capture左边必须是own声明  
                        if(captureNode.flags.HasFlag(VarModifiers.Own) == false)
                            throw new SemanticException(ExceptioName.OwnershipError, captureNode, "capture left identifier must be declared as own");

                        //capture不能用于值类型  
                        GType gtype = GType.Parse(captureNode.typeNode.TypeExpression());
                        if(gtype.IsReferenceType == false)
                        {
                            throw new SemanticException(ExceptioName.OwnershipError, captureNode, "capture can not be used on value type");
                        }
                    }
                    break;
                case SyntaxTree.OwnershipLeakStmtNode leakNode:
                    {
                        //分析右边变量 
                        Pass3_AnalysisNode(leakNode.rIdentifier);
                        //类型推断  
                        if(leakNode.typeNode is InferTypeNode typeNode)
                        {
                            var typeExpr = InferType(typeNode, leakNode.rIdentifier);
                            var record = envStack.Peek().GetRecord(leakNode.lIdentifier.FullName);
                            record.typeExpression = typeExpr;
                        }
                        //类型检查（初始值）  
                        else
                        {
                            bool valid = CheckType_Equal(leakNode.typeNode.TypeExpression(), leakNode.rIdentifier);
                            if(!valid)
                            {
                                var a = leakNode.typeNode.TypeExpression();
                                var b = AnalyzeTypeExpression(leakNode.rIdentifier);
                                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, leakNode, "type:" + leakNode.typeNode.TypeExpression() + "  r-idendifier type:" + AnalyzeTypeExpression(leakNode.rIdentifier));
                            }
                        }

                        //leak不能用于值类型  
                        GType gtype = GType.Parse(leakNode.typeNode.TypeExpression());
                        if(gtype.IsReferenceType == false)
                        {
                            throw new SemanticException(ExceptioName.OwnershipError, leakNode, "leak can not be used on value type");
                        }
                    }
                    break;
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        if(funcDeclNode.isTemplateFunction)
                            break;

                        //进入作用域    
                        envStack.Push(funcDeclNode.attributes[AstAttr.env] as SymbolTable);


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

                        //返回类型不支持推断  
                        if(funcDeclNode.returnTypeNode is SyntaxTree.InferTypeNode)
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode.returnTypeNode, "");

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
                        envStack.Push(externFuncDeclNode.attributes[AstAttr.env] as SymbolTable);

                        //分析形参定义
                        foreach (var paramNode in externFuncDeclNode.parametersNode.parameterNodes)
                        {
                            Pass3_AnalysisNode(paramNode);
                        }


                        //返回类型不支持推断  
                        if(externFuncDeclNode.returnTypeNode is SyntaxTree.InferTypeNode)
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, externFuncDeclNode.returnTypeNode, "");


                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.ClassDeclareNode classdeclNode:
                    {
                        if(classdeclNode.isTemplateClass)
                            break;

                        //进入作用域    
                        envStack.Push(classdeclNode.attributes[AstAttr.env] as SymbolTable);

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
                            CheckType_Equal("bool", clause.conditionNode);
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
                        CheckType_Equal("bool", whileNode.conditionNode);

                        //检查语句节点  
                        Pass3_AnalysisNode(whileNode.stmtNode);
                    }
                    break;
                case SyntaxTree.ForStmtNode forNode:
                    {
                        //进入FOR作用域    
                        envStack.Push(forNode.attributes[AstAttr.env] as SymbolTable);

                        //检查初始化器和迭代器  
                        Pass3_AnalysisNode(forNode.initializerNode);
                        AnalyzeTypeExpression(forNode.iteratorNode);

                        //检查条件是否为布尔类型    
                        CheckType_Equal("bool", forNode.conditionNode);

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

                        string objTypeExpr = (string)delNode.objToDelete.attributes[AstAttr.type];

                        var type = GType.Parse(objTypeExpr);
                        if(type.IsArray == true && delNode.isArrayDelete == false)
                            throw new SemanticException(ExceptioName.InvalidDeleteStatement, delNode, "delete array must use delete[]");
                        else if(type.IsArray == false && delNode.isArrayDelete == true)
                            throw new SemanticException(ExceptioName.InvalidDeleteStatement, delNode, "delete non-array cannot use delete[]");


                        if (GType.Parse(objTypeExpr).Category != GType.Kind.Array)
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


                // ********************* 表达式检查 *********************************


                case SyntaxTree.IdentityNode idNode:
                    {
                        var rec = Query(idNode.FullName);
                        if (rec == null)
                            rec = Query_IgnoreMangle(idNode.FullName);
                        if (rec == null)
                            throw new SemanticException(ExceptioName.IdentifierNotFound, idNode, (idNode?.FullName ?? "???"));

                        //常量替换  
                        if (rec.category == SymbolTable.RecordCatagory.Constant)
                        {
                            idNode.overrideNode = new SyntaxTree.LiteralNode
                            {
                                token = new Token(null, PatternType.Literal, rec.initValue, -1, -1, -1),

                                attributes = new Dictionary<AstAttr, object>()
                            };

                            idNode.overrideNode.attributes[AstAttr.type] = rec.typeExpression;
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
                case SyntaxTree.DefaultValueNode defaultValNode:
                    {
                        var litnode = GenDefaultLitNode(defaultValNode.typeNode);
                        defaultValNode.overrideNode = litnode;
                        AnalyzeTypeExpression(litnode);
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
                        string typeExprL = AnalyzeTypeExpression(binaryNode.leftNode);
                        string typeExprR = AnalyzeTypeExpression(binaryNode.rightNode);
                        var typeL = GType.Parse(typeExprL);
                        var typeR = GType.Parse(typeExprR);

                        // !! 操作符重载
                        if(typeL.IsNumberType == false || typeR.IsNumberType == false)
                        {
                            var operatorRec = Query_OperatorOverload(binaryNode.GetOpName(), typeExprL, typeExprR);
                            if(operatorRec == null)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, binaryNode, $"operator overload not exist:{Utils.Mangle(binaryNode.GetOpName(), typeExprL, typeExprR)}");

                            var overrideNode = new SyntaxTree.CallNode()
                            {
                                isMemberAccessFunction = false,
                                funcNode = new SyntaxTree.IdentityNode()
                                {
                                    attributes = new Dictionary<AstAttr, object>(),
                                    token = new Token("ID", PatternType.Id, operatorRec.rawname, default, default, default),
                                    identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                                },
                                argumantsNode = new SyntaxTree.ArgumentListNode()
                                {
                                },
                                attributes = new(),
                            };
                            overrideNode.argumantsNode.arguments.AddRange(new List<SyntaxTree.ExprNode>() { binaryNode.leftNode, binaryNode.rightNode, });
                            binaryNode.overrideNode = overrideNode;
                            binaryNode.overrideNode.Parent = binaryNode.Parent;

                            Pass3_AnalysisNode(binaryNode.overrideNode);
                            Console.WriteLine(binaryNode.overrideNode.attributes.Count);
                        }
                        else
                        {
                            Pass3_AnalysisNode(binaryNode.leftNode);
                            Pass3_AnalysisNode(binaryNode.rightNode);

                            AnalyzeTypeExpression(binaryNode);
                        }
                    }
                    break;
                case SyntaxTree.IncDecNode incDecNode:
                    {
                        AnalyzeTypeExpression(incDecNode);
                    }
                    break;
                case SyntaxTree.CastNode castNode:
                    {
                        // !!特殊的转换需要重写为函数调用
                        Pass3_AnalysisNode(castNode.factorNode);
                        AnalyzeTypeExpression(castNode.factorNode);

                        var srcType = GType.Parse((string)castNode.factorNode.attributes[AstAttr.type]);
                        var targetType = GType.Parse(castNode.typeNode.TypeExpression());

                        if(targetType.Category == GType.Kind.String)
                        {
                            var overrideNode = new SyntaxTree.CallNode()
                            {
                                isMemberAccessFunction = false,
                                funcNode = new SyntaxTree.IdentityNode() 
                                {
                                    attributes = new Dictionary<AstAttr, object>(),
                                    token = new Token("ID", PatternType.Id, srcType.ExternConvertStringFunction, default, default, default),
                                    identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                                },
                                argumantsNode = new SyntaxTree.ArgumentListNode()
                                { },
                                attributes = new(),
                            };
                            overrideNode.argumantsNode.arguments.Add(castNode.factorNode);
                            castNode.overrideNode = overrideNode;
                            castNode.overrideNode.Parent = castNode.Parent;

                            Pass3_AnalysisNode(castNode.overrideNode);

                            break;
                        }
                        else if(targetType.Category == GType.Kind.Array)
                        {
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, castNode, "cast to array not support.");
                        }

                        TryCompleteType(castNode.typeNode);
                        Pass3_AnalysisNode(castNode.factorNode);
                        AnalyzeTypeExpression(castNode);
                    }
                    break;
                case SyntaxTree.ElementAccessNode eleAccessNode:
                    {
                        Pass3_AnalysisNode(eleAccessNode.containerNode);
                        Pass3_AnalysisNode(eleAccessNode.indexNode);
                        AnalyzeTypeExpression(eleAccessNode);
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        if(CheckIntrinsicCall(callNode, out var replaceNode))
                        {
                            callNode.Parent.ReplaceChild(callNode, replaceNode);//需要立刻替换  
                            Pass3_AnalysisNode(replaceNode);
                            break;
                        }

                        //实参分析  
                        foreach (var argNode in callNode.argumantsNode.arguments)
                        {
                            Pass3_AnalysisNode(argNode);
                        }

                        //名称分析补全（是不是不应该放在Pass3 ??）  
                        if (callNode.isMemberAccessFunction == false && callNode.funcNode is SyntaxTree.IdentityNode)
                        {
                            TryCompleteIdenfier((callNode.funcNode as SyntaxTree.IdentityNode));
                        }

                        //函数分析(需要先补全名称)  
                        
                        callNode.funcNode.attributes[AstAttr.not_a_property] = null;//防止被当作属性替换  
                        Pass3_AnalysisNode(callNode.funcNode);


                        //Func分析  
                        AnalyzeTypeExpression(callNode);

                        //参数个数检查暂无...

                        //参数重载对应检查暂无...
                    }
                    break;
                case SyntaxTree.ReplaceNode replaceNode:
                    {
                        Pass3_AnalysisNode(replaceNode.targetNode);
                        Pass3_AnalysisNode(replaceNode.newValueNode);

                        if(replaceNode.targetNode is not SyntaxTree.ObjectMemberAccessNode)
                            throw new SemanticException(ExceptioName.OwnershipError, replaceNode, "replace target must be a field access.");

                        bool valid = CheckType_Equal(replaceNode.targetNode, replaceNode.newValueNode);
                        if(!valid)
                            throw new SemanticException(ExceptioName.AssignmentTypeError, replaceNode, "replace value type mismatch.");

                        AnalyzeTypeExpression(replaceNode);
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

                            var memberRec = classEnv.Class_GetMemberRecordInChain(memberName);
                            if (memberRec == null) //不存在同名字段  
                            {
                                var rvalType = AnalyzeTypeExpression(assignNode.rvalueNode);

                                var setterMethod = classEnv.Class_GetMemberRecordInChain(Utils.Mangle(memberName, rvalType));
                                if (setterMethod != null)//存在setter函数  
                                {
                                    //替换节点  
                                    var overrideNode = new SyntaxTree.CallNode()
                                    {

                                        isMemberAccessFunction = true,
                                        funcNode = memberAccess,
                                        argumantsNode = new SyntaxTree.ArgumentListNode()
                                        {},

                                        attributes = new(),
                                    };
                                    overrideNode.argumantsNode.arguments.Add(assignNode.rvalueNode);
                                    assignNode.overrideNode = overrideNode;
                                    assignNode.overrideNode.Parent = assignNode.Parent;

                                    Pass3_AnalysisNode(assignNode.overrideNode);

                                    break;
                                }
                            }
                        }


                        //类型检查（赋值）  
                        {
                            Pass3_AnalysisNode(assignNode.lvalueNode);
                            Pass3_AnalysisNode(assignNode.rvalueNode);

                            bool valid = CheckType_Equal(assignNode.lvalueNode, assignNode.rvalueNode);
                            if (!valid) throw new SemanticException(ExceptioName.AssignmentTypeError, assignNode, "");
                        }
                    }
                    break;
                case SyntaxTree.ObjectMemberAccessNode objMemberAccessNode:
                    {
                        Pass3_AnalysisNode(objMemberAccessNode.objectNode);

                        var className = AnalyzeTypeExpression(objMemberAccessNode.objectNode);
                        var classRec = Query(className);
                        if(classRec == null) throw new SemanticException(ExceptioName.ClassSymbolTableNotFound, objMemberAccessNode, className);
                        var classEnv = classRec.envPtr;

                        //!!getter属性替换  
                        if(objMemberAccessNode.attributes.ContainsKey(AstAttr.not_a_property) == false)
                        {
                            var fieldName = objMemberAccessNode.memberNode.FullName;
                            var fieldRec = classEnv.Class_GetMemberRecordInChain(fieldName);
                            if(fieldRec == null)
                            {
                                var getterRec = classEnv.Class_GetMemberRecordInChain(Utils.Mangle(fieldName));
                                if(getterRec != null)//存在getter函数  
                                {

                                    //替换节点  
                                    objMemberAccessNode.overrideNode = new SyntaxTree.CallNode()
                                    {
                                        isMemberAccessFunction = true,
                                        funcNode = objMemberAccessNode,
                                        argumantsNode = new SyntaxTree.ArgumentListNode()
                                        {
                                        },

                                        attributes = objMemberAccessNode.attributes,
                                    };
                                    objMemberAccessNode.overrideNode.Parent = objMemberAccessNode.Parent;
                                    Pass3_AnalysisNode(objMemberAccessNode.overrideNode);
                                    break;
                                }
                            }
                        }


                        //成员访问控制  
                        var currClassEnv = TryGetClassEnv();
                        var memberName = objMemberAccessNode.memberNode.FullName;
                        var memberRec = classEnv.Class_GetMemberRecordInChainByRawname(memberName);
                        if(memberRec == null)
                        {
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, objMemberAccessNode, "member not found:" + memberName);
                        }
                        if(currClassEnv != classEnv && memberRec.accessFlags.HasFlag(SymbolTable.AccessFlag.Private))
                        {
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, objMemberAccessNode, "can not access private member:" + memberName);
                        }

                        objMemberAccessNode.attributes[AstAttr.obj_class_rec] = classRec;

                        AnalyzeTypeExpression(objMemberAccessNode);
                    }
                    break;
                case SyntaxTree.ThisNode thisObjNode:
                    {
                        AnalyzeTypeExpression(thisObjNode);
                    }
                    break;
                case SyntaxTree.SizeOfNode sizeofNode:
                    {
                        AnalyzeTypeExpression(sizeofNode);
                    }
                    break;
                case SyntaxTree.TypeOfNode typeofNode:
                    {
                        TryCompleteType(typeofNode.typeNode);
                        Pass3_AnalysisNode(typeofNode.typeNode);
                        AnalyzeTypeExpression(typeofNode);
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
        /// PASS4:所有权与生命周期分析  
        /// </summary>
        private void Pass4_OwnershipLifetime(SyntaxTree.Node node)
        {
            if(node == null)
                return;

            if(node.overrideNode != null)
            {
                throw new SemanticException(ExceptioName.Undefine, node, $"apply override error:{node.GetType().Name} still has override:{node.overrideNode.GetType().Name}!  rawNode??:{(node.rawNode != null ? node.rawNode.GetType().Name : "null")}   parent:{(node.Parent != null ? node.Parent.GetType().Name : "null")}   isOverrideReplaced:{node.isOverrideReplaced}");
            }

            bool isTopLevelAtNamespace = false;
            if(node.Parent != null && node.Parent.Parent != null)
            {
                if(node.Parent is SyntaxTree.StatementsNode && node.Parent.Parent is SyntaxTree.NamespaceNode)
                {
                    isTopLevelAtNamespace = true;
                }
            }
            bool isGlobal = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.GlobalScope;
            bool isGlobalOrTopAtNamespace = isTopLevelAtNamespace || isGlobal;



            switch(node)
            {
                case SyntaxTree.ProgramNode programNode:
                    {
                        lifeTimeInfo.currBranch = lifeTimeInfo.mainBranch;

                        Pass4_OwnershipLifetime(programNode.statementsNode);
                        break;
                    }
                case SyntaxTree.NamespaceNode nsNode:
                    {
                        Pass4_OwnershipLifetime(nsNode.stmtsNode);
                        break;
                    }
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        foreach(var s in stmtsNode.statements)
                            Pass4_OwnershipLifetime(s);
                        break;
                    }
                case SyntaxTree.StatementBlockNode blockNode:
                    {
                        // 进入block作用域  
                        envStack.Push(blockNode.attributes[AstAttr.env] as SymbolTable);
                        lifeTimeInfo.currBranch.scopeStack.Push(new LifetimeInfo.ScopeInfo());

                        foreach(var s in blockNode.statements)
                        {
                            Pass4_OwnershipLifetime(s);
                        }

                        // 作用域退出需要删除的Owner变量
                        var toDelete = new List<(LifetimeInfo.VarStatus status, string varname)>();
                        foreach(var (varname, varstatus) in lifeTimeInfo.currBranch.scopeStack.Peek().localVariableStatusDict)
                        {
                            if(varstatus == LifetimeInfo.VarStatus.Alive)
                                toDelete.Add((LifetimeInfo.VarStatus.Alive, varname));
                            if(varstatus == LifetimeInfo.VarStatus.PossiblyDead)
                                toDelete.Add((LifetimeInfo.VarStatus.PossiblyDead, varname));
                        }

                        if(toDelete.Count > 0)
                            blockNode.attributes[AstAttr.drop_var_exit_env] = toDelete;

                        lifeTimeInfo.currBranch.scopeStack.Pop();
                        envStack.Pop();
                        break;
                    }
                case SyntaxTree.ForStmtNode forNode:
                    {
                        // 入循环作用域
                        envStack.Push(forNode.attributes[AstAttr.env] as SymbolTable);
                        lifeTimeInfo.currBranch.scopeStack.Push(new LifetimeInfo.ScopeInfo());

                        // initializer/condition/iterator/body
                        Pass4_OwnershipLifetime(forNode.initializerNode);
                        Pass4_OwnershipLifetime(forNode.conditionNode);
                        Pass4_OwnershipLifetime(forNode.iteratorNode);
                        //for作用域内initializerNode/conditionNode/iteratorNode不产生Owner变量，只有body内可能产生    


                        // 保存入口状态（会在合并中被就地更新）  
                        var saved = lifeTimeInfo.currBranch;

                        int itCount = 0;
                        while(true)
                        {
                            if(itCount++ > 99)
                                throw new GizboxException(ExceptioName.OwnershipError, "lifetime analysis not converge.");
                            
                            // 跑一轮循环体  
                            var loopBranch = lifeTimeInfo.NewBranch(saved);
                            lifeTimeInfo.currBranch = loopBranch;
                            Pass4_OwnershipLifetime(forNode.stmtNode);

                            // 合并分支 ->如果收敛则退出否则继续迭代  
                            lifeTimeInfo.currBranch = saved;
                            bool isConverged = lifeTimeInfo.MergeBranchesTo(saved, new List<LifetimeInfo.Branch> { loopBranch });
                            if(isConverged)
                                break;
                        }
                        // 恢复currBranch  
                        lifeTimeInfo.currBranch = saved;


                        lifeTimeInfo.currBranch.scopeStack.Pop();
                        envStack.Pop();
                        break;
                    }
                case SyntaxTree.WhileStmtNode whileNode:
                    {
                        Pass4_OwnershipLifetime(whileNode.conditionNode);

                        // 保存入口状态（会在合并中被就地更新）  
                        var saved = lifeTimeInfo.currBranch;

                        if(saved.scopeStack.Count == 0)
                            throw new SemanticException(ExceptioName.OwnershipError, whileNode, "");

                        int itCount = 0;
                        while(true)
                        {
                            if(itCount++ > 99)
                                throw new GizboxException(ExceptioName.OwnershipError, "lifetime analysis not converge.");

                            // 跑一轮循环体  
                            var loopBranch = lifeTimeInfo.NewBranch(saved);//bug
                            lifeTimeInfo.currBranch = loopBranch;
                            Pass4_OwnershipLifetime(whileNode.stmtNode);

                            // 合并分支 ->如果收敛则退出否则继续迭代  
                            lifeTimeInfo.currBranch = saved;
                            bool isConverged = lifeTimeInfo.MergeBranchesTo(saved, new List<LifetimeInfo.Branch> { loopBranch });
                            if(isConverged)
                                break;
                        }

                        // 恢复currBranch  
                        lifeTimeInfo.currBranch = saved;
                        break;
                    }
                case SyntaxTree.IfStmtNode ifNode:
                    {
                        var lastCurrTemp = lifeTimeInfo.currBranch;
                        List<LifetimeInfo.Branch> branches = new();


                        foreach(var c in ifNode.conditionClauseList)
                        {
                            Pass4_OwnershipLifetime(c.conditionNode);

                            //条件分支  
                            var condBranch = lifeTimeInfo.NewBranch(lastCurrTemp);
                            lifeTimeInfo.currBranch = condBranch;
                            Pass4_OwnershipLifetime(c.thenNode);

                            branches.Add(condBranch);
                        }
                        if(ifNode.elseClause != null)
                        {
                            //条件分支  
                            var condBranch = lifeTimeInfo.NewBranch(lastCurrTemp);
                            lifeTimeInfo.currBranch = condBranch;
                            Pass4_OwnershipLifetime(ifNode.elseClause.stmt);

                            branches.Add(condBranch);
                        }

                        //回归主分支  
                        lifeTimeInfo.currBranch = lastCurrTemp;
                        lifeTimeInfo.MergeBranchesTo(lastCurrTemp, branches);

                        break;
                    }
                case SyntaxTree.ClassDeclareNode classDecl:
                    {
                        if(classDecl.isTemplateClass)
                            break;

                        //进入作用域    
                        envStack.Push(classDecl.attributes[AstAttr.env] as SymbolTable);

                        // 类作用域成员字段的初始化表达式做所有权合法性检查
                        foreach(var decl in classDecl.memberDelareNodes)
                        {
                            if(decl is VarDeclareNode fvar)
                            {
                                // 成员字段初始化  
                                var rec = fvar.attributes[AstAttr.var_rec] as SymbolTable.Record;
                                if(rec != null)
                                {
                                    SymbolTable.RecordFlag lModel = rec.flags & OwnershipModelMask;
                                    CheckOwnershipCompare_Core(fvar, lModel, rec.name, fvar.initializerNode, out var rModel);
                                }
                            }
                            else if(decl is FuncDeclareNode fdecl)
                            {
                                // 函数成员的所有权检查递归
                                Pass4_OwnershipLifetime(decl);
                            }
                        }

                        //离开作用域
                        envStack.Pop();

                        break;
                    }
                case SyntaxTree.FuncDeclareNode funcDecl:
                    {
                        if(funcDecl.isTemplateFunction)
                            break;

                        // 进入函数作用域
                        envStack.Push(funcDecl.attributes[AstAttr.env] as SymbolTable);
                        lifeTimeInfo.currBranch.scopeStack.Push(new LifetimeInfo.ScopeInfo());

                        // 函数参数所有权模型  
                        foreach(var paramNode in funcDecl.parametersNode.parameterNodes)
                        {
                            Pass4_OwnershipLifetime(paramNode);
                        }

                        // 函数返回值所有权模型  
                        if(funcDecl.attributes.ContainsKey(AstAttr.func_rec) == false)
                            throw new GizboxException(ExceptioName.Undefine, "func record not found.");
                        var frec = funcDecl.attributes[AstAttr.func_rec] as SymbolTable.Record;
                        frec.flags |= GetOwnershipModel(funcDecl.returnFlags, funcDecl.returnTypeNode);

                    // NOTE: 允许借用返回值。健全性(safety)需要通过逃逸/生命周期检查保证。


                        // 更新当前函数返回值信息  
                        lifeTimeInfo.currentFuncReturnFlag = SymbolTable.RecordFlag.None;
                        lifeTimeInfo.currentFuncParams = null;

                        lifeTimeInfo.currentFuncReturnFlag =
                            frec.flags & (SymbolTable.RecordFlag.OwnerVar | SymbolTable.RecordFlag.ManualVar | SymbolTable.RecordFlag.BorrowVar);

                        // 将形参加入当前作用域跟踪（仅Owner需要释放）
                        var funcEnv = envStack.Peek();
                        var paramRecs = funcEnv.GetByCategory(SymbolTable.RecordCatagory.Param);
                        lifeTimeInfo.currentFuncParams = paramRecs;
                        if(paramRecs != null)
                        {
                            foreach(var p in paramRecs)
                            {
                                if(p.name == "this")
                                    continue; // this 不托管
                                if(p.flags.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                    lifeTimeInfo.currBranch.scopeStack.Peek().localVariableStatusDict[p.name] = LifetimeInfo.VarStatus.Alive;
                            }
                        }

                        // 递归语句体
                        foreach(var s in funcDecl.statementsNode.statements)
                        {
                            Pass4_OwnershipLifetime(s);
                        }

                        // 函数正常退出需要回收的 Owner
                        var exitDel = new List<(LifetimeInfo.VarStatus status, string varname)>();
                        foreach(var (varname, varstatus) in lifeTimeInfo.currBranch.scopeStack.Peek().localVariableStatusDict)
                        {
                            if(varstatus == LifetimeInfo.VarStatus.Alive)
                                exitDel.Add((LifetimeInfo.VarStatus.Alive, varname));
                            if(varstatus == LifetimeInfo.VarStatus.PossiblyDead)
                                exitDel.Add((LifetimeInfo.VarStatus.PossiblyDead, varname));
                        }

                        if(exitDel.Count > 0)
                            funcDecl.attributes[AstAttr.drop_var_exit_env] = exitDel;

                        lifeTimeInfo.currBranch.scopeStack.Pop();
                        envStack.Pop();
                        break;
                    }
                case SyntaxTree.ExternFuncDeclareNode _:
                    // 无需处理(外部函数不应该使用所有权)
                    break;
                case SyntaxTree.ParameterNode paramNode:
                    {
                        var prec =  paramNode.attributes[AstAttr.var_rec] as SymbolTable.Record;
                        if(prec == null)
                            throw new GizboxException(ExceptioName.Undefine, "param record not found.");
                        
                        //所有权模型
                        var model = GetOwnershipModel(paramNode.flags, paramNode.typeNode);
                        prec.flags |= model;

                        break;
                    }
                case SyntaxTree.VarDeclareNode varDecl:
                    {
                        // 先处理右值中的调用/参数（可能触发move）
                        Pass4_OwnershipLifetime(varDecl.initializerNode);

                        var rec = varDecl.attributes[AstAttr.var_rec] as SymbolTable.Record;// Query(varDecl.identifierNode.FullName);
                        if(rec == null)
                            throw new GizboxException(ExceptioName.Undefine, "var record not found.");

                        // 值类型不用处理所有权
                        if(GType.Parse(rec.typeExpression).IsReferenceType == false)
                            break;


                        // 检查：变量左值和初始值的所有权模型对比  
                        CheckOwnershipCompare_VarDecl(varDecl, rec, out var lmodel, out var rmodel);

                        // 记录变量的所有权模型  
                        rec.flags |= lmodel;


                        // 检查：全局变量不能定义为own/borrow类型  
                        if(isGlobalOrTopAtNamespace && lmodel != SymbolTable.RecordFlag.ManualVar)
                            throw new SemanticException(ExceptioName.OwnershipError_GlobalVarMustBeManual, varDecl, string.Empty);

                        // 检查：等号右边能否moveout
                        if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            CheckOwnershipCanMoveOut(varDecl.initializerNode);

                        // 记录owner类型的局部变量  
                        if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            lifeTimeInfo.currBranch.scopeStack.Peek().localVariableStatusDict[rec.name] = LifetimeInfo.VarStatus.Alive;

                        // 所有权own类型初始化处理  
                        if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                        {
                            //move源：临时对象 - New   
                            if(varDecl.initializerNode is NewObjectNode newobjNode)
                            {
                                //无需处理  
                            }
                            //move源：临时对象 - 函数返回(owner)
                            else if(varDecl.initializerNode is CallNode callnode)
                            {
                                //无需处理  
                            }
                            //move源：变量
                            else if(varDecl.initializerNode is IdentityNode idrvalue)
                            {
                                //标记为Dead  
                                var rrec = Query(idrvalue.FullName);
                                lifeTimeInfo.currBranch.SetVarStatus(rrec.name, LifetimeInfo.VarStatus.Dead);

                                //需要插入Null语句  
                                varDecl.attributes[AstAttr.set_null_after_stmt] = new List<string> { rrec.name };
                            }
                            //move源：字段
                            else if(varDecl.initializerNode is ObjectMemberAccessNode fieldRvalue)
                            {
                                varDecl.attributes[AstAttr.set_null_field_after_stmt] = fieldRvalue;
                            }

                        }

                        // 所有权借用类型      
                        if(lmodel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        {
                            //无需处理  
                        }

                        break;
                    }
                case SyntaxTree.OwnershipCaptureStmtNode captureNode:
                    {
                        //检查：变量左值和初始值的所有权模型对比（必须own <- manual） 
                        var recL = captureNode.attributes[AstAttr.var_rec] as SymbolTable.Record;
                        var recR = Query(captureNode.rIdentifier.FullName);
                        var lModel = GetOwnershipModel(VarModifiers.Own, captureNode.typeNode);
                        var rModel = recR.flags & OwnershipModelMask;

                        recL.flags |= lModel;

                        if(lModel != SymbolTable.RecordFlag.OwnerVar)
                            throw new SemanticException(ExceptioName.OwnershipError, captureNode, "capture left side must be own type.");
                        if(rModel != SymbolTable.RecordFlag.ManualVar)
                            throw new SemanticException(ExceptioName.OwnershipError, captureNode, "capture right side must be manual type.");


                        //记录owner类型的局部变量   
                        lifeTimeInfo.currBranch.scopeStack.Peek().localVariableStatusDict[recL.name] = LifetimeInfo.VarStatus.Alive;

                        //需要插入Null语句（原变量不可再用）  
                        captureNode.attributes[AstAttr.set_null_after_stmt] = new List<string> { recR.name };
                    }
                    break;
                case SyntaxTree.OwnershipLeakStmtNode leakNode:
                    {
                        //所有权模型检查（必须是manual <- own）
                        var recL = leakNode.attributes[AstAttr.var_rec] as SymbolTable.Record;
                        var recR = Query(leakNode.rIdentifier.FullName);
                        var lModel = GetOwnershipModel(VarModifiers.None, leakNode.typeNode);
                        var rModel = recR.flags & OwnershipModelMask;

                        recL.flags |= lModel;

                        if(lModel != SymbolTable.RecordFlag.ManualVar)
                            throw new SemanticException(ExceptioName.OwnershipError, leakNode, "leak left side must be manual type.");
                        if(rModel != SymbolTable.RecordFlag.OwnerVar)
                            throw new SemanticException(ExceptioName.OwnershipError, leakNode, "leak right side must be own type.");

                        //右边变量标记为Dead  
                        lifeTimeInfo.currBranch.SetVarStatus(recR.name, LifetimeInfo.VarStatus.Dead);

                        //需要插入Null语句（原变量不可再用）  
                        leakNode.attributes[AstAttr.set_null_after_stmt] = new List<string> { recR.name };
                    }
                    break;
                case SyntaxTree.AssignNode assignNode:
                    {
                        // 先处理右值中的调用/参数（可能触发move）
                        Pass4_OwnershipLifetime(assignNode.rvalueNode);

                        // 变量被赋值  
                        if(assignNode.lvalueNode is SyntaxTree.IdentityNode lid)
                        {
                            var lrec = Query(lid.FullName);
                            if(lrec == null)
                                throw new GizboxException(ExceptioName.Undefine, "var record not found.");

                            // 值类型不用处理所有权
                            if(GType.Parse(lrec.typeExpression).IsReferenceType == false)
                                break;

                            // 检查：变量左值和初始值的所有权模型对比  
                            CheckOwnershipCompare_Assign(assignNode, lrec, out var lmodel, out var rmodel);

                            // 检查：成员所有权不能被 moveout
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                CheckOwnershipCanMoveOut(assignNode.rvalueNode);
                            }
                            else
                            {
                                GixConsole.WriteLine("?");
                            }
                                

                            // 如果目标是owner且不为Dead，则先删，然后设置为Alive  
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                LifetimeInfo.VarStatus alive = LifetimeInfo.VarStatus.Dead;
                                if(lifeTimeInfo.currBranch.scopeStack.Any() && lifeTimeInfo.currBranch.scopeStack.Peek().localVariableStatusDict.TryGetValue(lrec.name, out var st))
                                {
                                    alive = st;
                                }

                                if(alive != LifetimeInfo.VarStatus.Dead)
                                {
                                    var delList = new List<(LifetimeInfo.VarStatus, string)>();
                                    if(assignNode.attributes.ContainsKey(AstAttr.drop_var_before_assign_stmt))
                                    {
                                        delList = (List<(LifetimeInfo.VarStatus, string)>)assignNode.attributes[AstAttr.drop_var_before_assign_stmt];
                                    }
                                    delList.Add((alive, lrec.name));
                                    assignNode.attributes[AstAttr.drop_var_before_assign_stmt] = delList;

                                    lifeTimeInfo.currBranch.SetVarStatus(lrec.name, LifetimeInfo.VarStatus.Dead);
                                }

                                // 被赋值的own变量设置为Alive  
                                lifeTimeInfo.currBranch.scopeStack.Peek().localVariableStatusDict[lrec.name] = LifetimeInfo.VarStatus.Alive;
                            }

                            // 所有权own类型赋值处理  
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                //move源：临时对象 - New  
                                if(assignNode.rvalueNode is SyntaxTree.NewObjectNode newobjNode)
                                {
                                    //无需处理  
                                }
                                //move源：临时对象 - 函数返回(owner)
                                else if(assignNode.rvalueNode is SyntaxTree.CallNode callnode)
                                {
                                    //无需处理  
                                }
                                //move源：变量  
                                else if(assignNode.rvalueNode is SyntaxTree.IdentityNode idrvalueNode)
                                {
                                    //加入moved  
                                    var rrec = Query(idrvalueNode.FullName);
                                    lifeTimeInfo.currBranch.SetVarStatus(rrec.name, LifetimeInfo.VarStatus.Dead);

                                    //需要插入Null语句
                                    assignNode.attributes[AstAttr.set_null_after_stmt] = new List<string> { rrec.name };
                                }
                                //move源：字段
                                else if(assignNode.rvalueNode is ObjectMemberAccessNode fieldRvalue)
                                {
                                    assignNode.attributes[AstAttr.set_null_field_after_stmt] = fieldRvalue;
                                }
                            }

                            // 所有权借用类型      
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                            {
                                //无需处理  
                            }
                        }
                        // 成员字段被赋值  
                        else if(assignNode.lvalueNode is ObjectMemberAccessNode laccess)
                        {
                            var objClassRec = (SymbolTable.Record)laccess.attributes[AstAttr.obj_class_rec];
                            var lrec = objClassRec.envPtr.Class_GetMemberRecordInChain(laccess.memberNode.FullName); 
                            if(lrec == null)
                                throw new GizboxException(ExceptioName.Undefine, $"field record {laccess.memberNode.FullName} not found.");

                            // 值类型不用处理所有权
                            if(GType.Parse(lrec.typeExpression).IsReferenceType == false)
                                break;
                            
                            // 检查：变量左值和初始值的所有权模型对比  
                            CheckOwnershipCompare_Assign(assignNode, lrec, out var lmodel, out var rmodel);

                            // 检查：成员所有权不能被 moveout
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                CheckOwnershipCanMoveOut(assignNode.rvalueNode);


                            // 如果字段是owner且不为null，则先删
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                assignNode.attributes[AstAttr.drop_field_before_assign_stmt] = 0;
                            }


                            // 所有权own类型赋值处理  
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                //move源：临时对象 - New  
                                if(assignNode.rvalueNode is SyntaxTree.NewObjectNode newobjNode)
                                {
                                    //无需处理  
                                }
                                //move源：临时对象 - 函数返回(owner)
                                else if(assignNode.rvalueNode is SyntaxTree.CallNode callnode)
                                {
                                    //无需处理  
                                }
                                //move源：变量  
                                else if(assignNode.rvalueNode is SyntaxTree.IdentityNode idrvalueNode)
                                {
                                    //加入moved  
                                    var rrec = Query(idrvalueNode.FullName);
                                    lifeTimeInfo.currBranch.SetVarStatus(rrec.name, LifetimeInfo.VarStatus.Dead);

                                    //需要插入Null语句
                                    assignNode.attributes[AstAttr.set_null_after_stmt] = new List<string> { rrec.name };
                                }
                                //move源：字段
                                else if(assignNode.rvalueNode is ObjectMemberAccessNode fieldRvalue)
                                {
                                    assignNode.attributes[AstAttr.set_null_field_after_stmt] = fieldRvalue;
                                }
                            }

                            // 所有权借用类型      
                            if(lmodel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                            {
                                //无需处理  
                            }
                        }
                        else
                        {
                            // 继续递归子节点，避免遗漏
                            Pass4_OwnershipLifetime(assignNode.lvalueNode);
                        }
                        break;
                    }
                case SyntaxTree.DeleteStmtNode del:
                    {  
                        if(del.isArrayDelete == false && del.objToDelete != null)
                        {
                            // 检查：禁止删除非Manual类型变量  
                            if(del.objToDelete is SyntaxTree.IdentityNode dId)
                            {
                                var drec = Query(dId.FullName);
                                if(drec != null)
                                {
                                    var dmodel = drec.flags & (SymbolTable.RecordFlag.OwnerVar | SymbolTable.RecordFlag.BorrowVar | SymbolTable.RecordFlag.ManualVar);
                                    if(dmodel.HasFlag(SymbolTable.RecordFlag.BorrowVar) || dmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                        throw new SemanticException(ExceptioName.OwnershipError, del, "own/borrow cannot be deleted");
                                }
                            }

                            // 检查：禁止释放own类型成员字段  
                            else if(del.objToDelete is SyntaxTree.ObjectMemberAccessNode dAccess)
                            {
                                var objTypeExpr = (string)dAccess.objectNode.attributes[AstAttr.type];
                                var objClassRec = Query(objTypeExpr);
                                if(objClassRec == null || objClassRec.envPtr == null)
                                    throw new SemanticException(ExceptioName.ClassSymbolTableNotFound, del, objTypeExpr);

                                var fieldRec = objClassRec.envPtr.Class_GetMemberRecordInChain(dAccess.memberNode.FullName);
                                if(fieldRec == null)
                                    throw new SemanticException(ExceptioName.MemberFieldNotFound, del, dAccess.memberNode.FullName);

                                var fieldModel = fieldRec.flags & OwnershipModelMask;
                                if(fieldModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                    throw new SemanticException(ExceptioName.OwnershipError, del, "own field cannot be deleted");
                            }
                        }
                            
                        break;
                    }
                case SyntaxTree.ReturnStmtNode ret:
                    {
                        // 先递归，确保内层调用的move已处理
                        Pass4_OwnershipLifetime(ret.returnExprNode);

                        // borrow-return escape check: returned borrow must be derived from `this` or a borrow parameter
                        if(lifeTimeInfo.currentFuncReturnFlag.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        {
                            CheckBorrowReturnEscape(ret, ret.returnExprNode);
                        }

                        // 如果返回的是owner，且返回变量是Identity，则将其标记为moved，避免被删除
                        string returnedName = null;
                        if(lifeTimeInfo.currentFuncReturnFlag.HasFlag(SymbolTable.RecordFlag.OwnerVar) && ret.returnExprNode is SyntaxTree.IdentityNode rid)
                        {
                            var rrec = Query(rid.FullName);
                            if(rrec != null && rrec.flags.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                returnedName = rrec.name;
                                lifeTimeInfo.currBranch.SetVarStatus(rrec.name, LifetimeInfo.VarStatus.Dead);
                            }
                        }

                        // 检查:对象成员不可以MoveOut  
                        if(lifeTimeInfo.currentFuncReturnFlag.HasFlag(SymbolTable.RecordFlag.OwnerVar) && ret.returnExprNode is SyntaxTree.ObjectMemberAccessNode)
                        {
                            throw new SemanticException(ExceptioName.OwnershipError_CanNotMoveOutClassField, ret, "returning owner field is not allowed; use replace.");
                        }

                        // 汇总当前所有活跃Owner（所有栈帧），return前删除（排除被返回者）
                        var delList = new List<(LifetimeInfo.VarStatus status, string varname)>();
                        HashSet<string> varnameSet = new();
                        foreach(var scope in lifeTimeInfo.currBranch.scopeStack)
                        {
                            foreach(var (varname, varstatus) in scope.localVariableStatusDict)
                            {
                                if(varname == returnedName)
                                    continue;
                                if(varnameSet.Contains(varname))
                                    continue;

                                if(varstatus != LifetimeInfo.VarStatus.Dead)
                                {
                                    delList.Add((varstatus, varname));
                                    varnameSet.Add(varname);
                                }
                            }
                        }
                        if(delList.Count > 0)
                            ret.attributes[AstAttr.drop_var_before_return] = delList;


                        break;
                    }
                case SyntaxTree.SingleExprStmtNode sstmt:
                    {
                        // 表达式作为语句：若new或call返回owner，语句末删除
                        Pass4_OwnershipLifetime(sstmt.exprNode);

                        var delExprs = new List<SyntaxTree.ExprNode>();
                        if(sstmt.exprNode is SyntaxTree.NewObjectNode)
                        {
                            // 视为临时所有权，需删除
                            delExprs.Add(sstmt.exprNode);
                            sstmt.exprNode.attributes[AstAttr.store_expr_result] = true;
                        }
                        else if(sstmt.exprNode is SyntaxTree.CallNode cnode)
                        {
                            SymbolTable.Record f = cnode.attributes[AstAttr.func_rec] as SymbolTable.Record;

                            if(f.flags.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                // 视为临时所有权，需删除
                                delExprs.Add(sstmt.exprNode);
                                sstmt.exprNode.attributes[AstAttr.store_expr_result] = true;
                            }
                                
                        }
                        else if(sstmt.exprNode is SyntaxTree.ReplaceNode rnode)
                        {
                            if(rnode.targetNode is SyntaxTree.ObjectMemberAccessNode targetAccess)
                            {
                                var objTypeExpr = (string)targetAccess.objectNode.attributes[AstAttr.type];
                                var objClassRec = Query(objTypeExpr);
                                var fieldRec = objClassRec?.envPtr?.Class_GetMemberRecordInChain(targetAccess.memberNode.FullName);
                                if(fieldRec != null && fieldRec.flags.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                {
                                    delExprs.Add(sstmt.exprNode);
                                }
                            }
                        }

                        if(delExprs.Count > 0)
                        {
                            sstmt.attributes[AstAttr.drop_expr_result_after_stmt] = delExprs;
                        }

                        break;
                    }
                case SyntaxTree.ReplaceNode replaceNode:
                    {
                        Pass4_OwnershipLifetime(replaceNode.targetNode);
                        Pass4_OwnershipLifetime(replaceNode.newValueNode);

                        if(replaceNode.targetNode is not SyntaxTree.ObjectMemberAccessNode targetAccess)
                            throw new SemanticException(ExceptioName.OwnershipError, replaceNode, "replace target must be a field access.");

                        var objTypeExpr = (string)targetAccess.objectNode.attributes[AstAttr.type];
                        var objClassRec = Query(objTypeExpr);
                        if(objClassRec == null || objClassRec.envPtr == null)
                            throw new SemanticException(ExceptioName.ClassSymbolTableNotFound, replaceNode, objTypeExpr);

                        var fieldRec = objClassRec.envPtr.Class_GetMemberRecordInChain(targetAccess.memberNode.FullName);
                        if(fieldRec == null)
                            throw new SemanticException(ExceptioName.MemberFieldNotFound, replaceNode, targetAccess.memberNode.FullName);

                        if(GType.Parse(fieldRec.typeExpression).IsReferenceType)
                        {
                            var lmodel = fieldRec.flags & OwnershipModelMask;
                            CheckOwnershipCompare_Core(replaceNode, lmodel, fieldRec.name, replaceNode.newValueNode, out var _);

                            if(lmodel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                CheckOwnershipCanMoveOut(replaceNode.newValueNode);

                                if(replaceNode.newValueNode is SyntaxTree.IdentityNode idrvalue)
                                {
                                    var rrec = Query(idrvalue.FullName);
                                    lifeTimeInfo.currBranch.SetVarStatus(rrec.name, LifetimeInfo.VarStatus.Dead);

                                    replaceNode.attributes[AstAttr.set_null_after_stmt] = new List<string> { rrec.name };
                                }
                            }
                        }
                        break;
                    }

                //只处理传参的所有权转移，返回值的所有权转移再SingleStmt/Assign/VarDecl节点处理  
                case SyntaxTree.CallNode callNode:  
                    {
                        // *** 先处理所有实参（递归）***
                        foreach(var a in callNode.argumantsNode.arguments)
                        {

                            if(a.overrideNode != null)
                            {
                                foreach(var c in callNode.argumantsNode.Children())
                                {
                                    GixConsole.WriteLine($"node: {c.GetType().Name}  override:  {c.overrideNode != null}");
                                }
                            }



                            Pass4_OwnershipLifetime(a);
                        }
                     

                        // *** 获取函数的符号表记录 ***  
                        SymbolTable.Record funcRec = null;
                        if(callNode.isMemberAccessFunction == false)
                        {
                            if(callNode.attributes.ContainsKey(AstAttr.mangled_name))
                                funcRec = Query((string)callNode.attributes[AstAttr.mangled_name]);
                            else if(callNode.attributes.ContainsKey(AstAttr.extern_name))
                                funcRec = Query((string)callNode.attributes[AstAttr.extern_name]);
                        }
                        else
                        {
                            if(callNode.funcNode is ObjectMemberAccessNode memberAccNode)
                            {
                                var objtype = (string)memberAccNode.objectNode.attributes[AstAttr.type];
                                var classRec = Query(objtype);
                                var memfuncRec = classRec.envPtr.Class_GetMemberRecordInChain((string)callNode.attributes[AstAttr.mangled_name]);
                                funcRec = memfuncRec;
                            }
                        }
                        
                        if(funcRec == null)
                            throw new GizboxException(ExceptioName.Undefine, $"funcrec not found.");

                        callNode.attributes[AstAttr.func_rec] = funcRec;


                        if(funcRec == null || funcRec.envPtr == null)
                        {
                            throw new GizboxException(ExceptioName.Undefine, $"func rec not exist.");
                        }


                        // *** 根据被调函数签名对实参做move/校验 ***

                        var allParams = funcRec.envPtr.GetByCategory(SymbolTable.RecordCatagory.Param) ?? new List<SymbolTable.Record>();
                        // 成员函数形参表含this，非成员不含
                        int offset = 0;
                        if(callNode.isMemberAccessFunction && allParams.Count > 0 && allParams[0].name == "this")
                            offset = 1;

                        // 实参分析  
                        for(int i = 0; i < callNode.argumantsNode.arguments.Count; ++i)
                        {
                            if(i + offset >= allParams.Count)
                                break;
                            var pr = allParams[i + offset];
                            var pflag = pr.flags & (SymbolTable.RecordFlag.OwnerVar | SymbolTable.RecordFlag.BorrowVar | SymbolTable.RecordFlag.ManualVar);
                            var arg = callNode.argumantsNode.arguments[i];
                            var type = GType.Parse(pr.typeExpression);

                            // 值类型参数不用处理所有权语义  
                            if(type.IsReferenceType == false)
                                continue;

                            // 所有权比较  
                            CheckOwnershipCompare_Param(pr, arg, out var paramModel, out var argModel);

                            // 检查own模型实参能否MoveOut
                            if(paramModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                CheckOwnershipCanMoveOut(arg);
                            }


                            // 所有权转移  
                            if(pflag.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                            {
                                if(arg is SyntaxTree.IdentityNode argIdNode && argModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                {
                                    var argRec = Query(argIdNode.FullName);
                                    lifeTimeInfo.currBranch.SetVarStatus(argRec.name, LifetimeInfo.VarStatus.Dead);


                                    // 语句结束实参赋值NULL，作为drop-flag  
                                    if(callNode.attributes.TryGetValue(AstAttr.set_null_after_call, out var v) == false || v is not List<string>)
                                    {
                                        callNode.attributes[AstAttr.set_null_after_call] = new List<string>();
                                    }
                                    var listSetNull = (List<string>)callNode.attributes[AstAttr.set_null_after_call];
                                    if(listSetNull.Contains(argRec.name) == false)
                                    {
                                        listSetNull.Add(argRec.name);
                                    }
                                }
                            }
                        }


                        break;
                    }
                case SyntaxTree.IdentityNode id:
                    {
                        // 一般作为rvalue使用：禁止使用已move的owner变量
                        var rec = Query(id.FullName);
                        if(rec != null && rec.flags.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                        {
                            for(int i = lifeTimeInfo.currBranch.scopeStack.Count - 1; i >= 0; --i)
                            {
                                var dict = lifeTimeInfo.currBranch.scopeStack.ElementAt(i).localVariableStatusDict;
                                if(dict.TryGetValue(rec.name, out var st) && st != LifetimeInfo.VarStatus.Alive)
                                    throw new SemanticException(ExceptioName.OwnershipError_CanNotUseAfterMove, id, "use after move");
                            }
                        }
                        break;
                    }

                // 其他表达式仅递归其子节点，避免遗漏
                case SyntaxTree.BinaryOpNode b:
                    Pass4_OwnershipLifetime(b.leftNode);
                    Pass4_OwnershipLifetime(b.rightNode);
                    break;
                case SyntaxTree.UnaryOpNode u:
                    Pass4_OwnershipLifetime(u.exprNode);
                    break;
                case SyntaxTree.CastNode c:
                    Pass4_OwnershipLifetime(c.factorNode);
                    break;
                case SyntaxTree.ObjectMemberAccessNode ma:
                    Pass4_OwnershipLifetime(ma.objectNode);
                    break;
                case SyntaxTree.ElementAccessNode ea:
                    Pass4_OwnershipLifetime(ea.containerNode);
                    Pass4_OwnershipLifetime(ea.indexNode);
                    break;


                case SyntaxTree.NewObjectNode _:
                case SyntaxTree.NewArrayNode _:
                case SyntaxTree.LiteralNode _:
                case SyntaxTree.ThisNode _:
                case SyntaxTree.SizeOfNode _:
                case SyntaxTree.TypeOfNode _:
                    // 无需处理
                    break;

                default:
                    // 类型/参数/实参与其它节点，对子树递归
                    foreach(var n in node.Children())
                    {
                        Pass4_OwnershipLifetime(n);
                    }
                    break;
            }
        }



        private void CollectAllUsingNamespacePrefix()
        {
            availableNamespacePrefixList.Clear();
            foreach(var usingnamespace in this.ast.rootNode.usingNamespaceNodes)
            {
                availableNamespacePrefixList.Add($"{usingnamespace.namespaceNameNode.FullName}::");
            }
        }

        private void CheckBorrowReturnEscape(SyntaxTree.ReturnStmtNode retNode, SyntaxTree.ExprNode returnExpr)
        {
            if(returnExpr == null)
                throw new SemanticException(ExceptioName.OwnershipError, retNode, "borrow return must have expression.");

            // 仅允许：1) 以 this/bor 参数为根的成员/元素访问；2) 直接返回 bor 参数（以及 this 本身）
            if(IsBorrowDerivedFromAllowedInput(returnExpr))
                return;

            throw new SemanticException(ExceptioName.OwnershipError, retNode, "borrow return must be derived from this or borrow parameter.");
        }

        private bool IsBorrowDerivedFromAllowedInput(SyntaxTree.ExprNode expr)
        {
            // 允许 return this（等价于借用调用者持有的对象）
            if(expr is SyntaxTree.ThisNode)
                return true;

            // return 标识符：必须是显式 bor 参数
            if(expr is SyntaxTree.IdentityNode id)
            {
                var rec = Query(id.FullName);
                if(rec == null)
                    return false;

                if(rec.category != SymbolTable.RecordCatagory.Param)
                    return false;

                var model = rec.flags & OwnershipModelMask;
                return model.HasFlag(SymbolTable.RecordFlag.BorrowVar);
            }

            // return 成员访问：递归检查其根，必须是 this 或 bor 参数
            if(expr is SyntaxTree.ObjectMemberAccessNode ma)
            {
                // 根为 this.* 或 borParam.* 则允许
                if(IsBorrowDerivedFromAllowedInput(ma.objectNode))
                    return true;
                return false;
            }

            // return 下标访问：仅当容器表达式派生自允许的输入
            if(expr is SyntaxTree.ElementAccessNode ea)
            {
                return IsBorrowDerivedFromAllowedInput(ea.containerNode);
            }

            // return cast：cast 不改变所有权/借用来源，只递归检查操作数
            if(expr is SyntaxTree.CastNode c)
            {
                return IsBorrowDerivedFromAllowedInput(c.factorNode);
            }

            // 其余情况都视为临时值/未知来源 => 拒绝（new/call/literal/binary/unary 等）
            return false;
        }

        /// <summary> 所有权比较 </summary>
        private void CheckOwnershipCompare_Core(SyntaxTree.Node errorNode, SymbolTable.RecordFlag lModel, string lname, SyntaxTree.ExprNode rNode, out SymbolTable.RecordFlag rModel)
        {
            // 变量右值：ID
            if(rNode is IdentityNode rvalueVarNode)
            {
                var rvalueVarRec = Query(rvalueVarNode.FullName);
                rModel = rvalueVarRec.flags & OwnershipModelMask;

                // manual <- (owner|borrow) 禁止
                if(lModel == SymbolTable.RecordFlag.ManualVar && rModel != SymbolTable.RecordFlag.ManualVar)
                {
                    if(rModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignOwnToManual, errorNode, lname);
                    if(rModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignBorrwToManual, errorNode, lname);
                }

                // owner <- (manual|borrow) 禁止
                if(lModel == SymbolTable.RecordFlag.OwnerVar && rModel != SymbolTable.RecordFlag.OwnerVar)
                {
                    if(rModel.HasFlag(SymbolTable.RecordFlag.ManualVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignManualToOwn, errorNode, lname);
                    if(rModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignBorrowToOwn, errorNode, lname);
                }

                // borrow <- manual 禁止
                if(lModel == SymbolTable.RecordFlag.BorrowVar && rModel == SymbolTable.RecordFlag.ManualVar)
                {
                    throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignManualToBorrow, errorNode, lname);
                }

                return;
            }

            // 右值：this  
            else if(rNode is ThisNode thisNode)
            {
                var classDecl = TrygetClassDeclNode(thisNode);
                var classrec = classDecl.attributes[AstAttr.class_rec] as SymbolTable.Record;
                if(classrec.flags.HasFlag(SymbolTable.RecordFlag.OwnershipClass))
                {
                    rModel = SymbolTable.RecordFlag.OwnerVar;
                }
                else
                {
                    rModel = SymbolTable.RecordFlag.ManualVar;
                }

                return;
            }

            // 右值：replace
            else if(rNode is ReplaceNode replaceNode)
            {
                if(replaceNode.targetNode is not ObjectMemberAccessNode memAccess)
                    throw new SemanticException(ExceptioName.OwnershipError, errorNode, "replace target must be a field access.");

                if(memAccess.objectNode.attributes.TryGetValue(AstAttr.type, out var objTypeObj) == false || objTypeObj is not string objTypeExpr)
                    throw new GizboxException(ExceptioName.Undefine, "member access object type not set.");

                var objClassRec = Query(objTypeExpr);
                if(objClassRec == null || objClassRec.envPtr == null)
                    throw new GizboxException(ExceptioName.Undefine, "class record not found for member access.");

                var fieldRec = objClassRec.envPtr.Class_GetMemberRecordInChain(memAccess.memberNode.FullName);
                if(fieldRec == null)
                    throw new GizboxException(ExceptioName.Undefine, "field record not found for member access.");

                rModel = fieldRec.flags & OwnershipModelMask;

                // manual <- (owner|borrow) 禁止
                if(lModel == SymbolTable.RecordFlag.ManualVar && rModel != SymbolTable.RecordFlag.ManualVar)
                {
                    if(rModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignOwnToManual, errorNode, lname);
                    if(rModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignBorrwToManual, errorNode, lname);
                }

                // owner <- (manual|borrow) 禁止
                if(lModel == SymbolTable.RecordFlag.OwnerVar && rModel != SymbolTable.RecordFlag.OwnerVar)
                {
                    if(rModel.HasFlag(SymbolTable.RecordFlag.ManualVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignManualToOwn, errorNode, lname);
                    if(rModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignBorrowToOwn, errorNode, lname);
                }

                // borrow <- manual 禁止
                if(lModel == SymbolTable.RecordFlag.BorrowVar && rModel == SymbolTable.RecordFlag.ManualVar)
                {
                    throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignManualToBorrow, errorNode, lname);
                }

                return;
            }

            // 右值：对象成员访问 obj.field
            else if(rNode is ObjectMemberAccessNode memAccess)
            {
                if(memAccess.objectNode.attributes.TryGetValue(AstAttr.type, out var objTypeObj) == false || objTypeObj is not string objTypeExpr)
                    throw new GizboxException(ExceptioName.Undefine, "member access object type not set.");

                var objClassRec = Query(objTypeExpr);
                if(objClassRec == null || objClassRec.envPtr == null)
                    throw new GizboxException(ExceptioName.Undefine, "class record not found for member access.");

                var fieldRec = objClassRec.envPtr.Class_GetMemberRecordInChain(memAccess.memberNode.FullName);
                if(fieldRec == null)
                    throw new GizboxException(ExceptioName.Undefine, "field record not found for member access.");

                rModel = fieldRec.flags & OwnershipModelMask;

                // manual <- (owner|borrow) 禁止
                if(lModel == SymbolTable.RecordFlag.ManualVar && rModel != SymbolTable.RecordFlag.ManualVar)
                {
                    if(rModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignOwnToManual, errorNode, lname);
                    if(rModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignBorrwToManual, errorNode, lname);
                }

                // owner <- (manual|borrow) 禁止
                if(lModel == SymbolTable.RecordFlag.OwnerVar && rModel != SymbolTable.RecordFlag.OwnerVar)
                {
                    if(rModel.HasFlag(SymbolTable.RecordFlag.ManualVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignManualToOwn, errorNode, lname);
                    if(rModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignBorrowToOwn, errorNode, lname);
                }

                // borrow <- manual 禁止
                if(lModel == SymbolTable.RecordFlag.BorrowVar && rModel == SymbolTable.RecordFlag.ManualVar)
                {
                    throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignManualToBorrow, errorNode, lname);
                }

                return;
            }

            // 数组元素访问  
            else if(rNode is ElementAccessNode elementAccessNode)
            {
                rModel = SymbolTable.RecordFlag.ManualVar;//数组只能存放非own类型  
            }

            // 字面量
            else if(rNode is LiteralNode litnode)
            {
                if(litnode.token.name == "null")
                {
                    rModel = SymbolTable.RecordFlag.None;

                    if(lModel.HasFlag(SymbolTable.RecordFlag.OwnerVar) || lModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError_OwnAndBorrowTypeCantBeNull, errorNode, "own/borrow cannot be null.");
                }
                else if(litnode.token.name == "LITSTRING")
                {
                    rModel = SymbolTable.RecordFlag.None;

                    if(lModel.HasFlag(SymbolTable.RecordFlag.OwnerVar) || lModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError, errorNode, "own/borrow cannot be literial string.");
                }
                else
                {
                    rModel = SymbolTable.RecordFlag.None;
                }
                return;
            }

            // 临时右值 - new
            else if(rNode is NewObjectNode newobjNode)
            {
                if(lModel == SymbolTable.RecordFlag.BorrowVar)
                    throw new SemanticException(ExceptioName.OwnershipError_BorrowCanNotFromTemp, errorNode, lname);

                var classRec = Query(newobjNode.className.FullName);
                bool isownershipClass = classRec.flags.HasFlag(SymbolTable.RecordFlag.OwnershipClass);
                rModel = isownershipClass ? SymbolTable.RecordFlag.OwnerVar : SymbolTable.RecordFlag.ManualVar;
                return;
            }

            // 临时右值 - new[]
            else if(rNode is NewArrayNode newarrNode)
            {
                if(lModel == SymbolTable.RecordFlag.BorrowVar )
                    throw new SemanticException(ExceptioName.OwnershipError_BorrowCanNotFromTemp, errorNode, lname);

                if(lModel == SymbolTable.RecordFlag.OwnerVar)
                    throw new SemanticException(ExceptioName.OwnershipError, errorNode, "array type cant be owner.");//数组类型暂时不能是own类型  

                rModel = SymbolTable.RecordFlag.ManualVar;
                return;
            }


            // 临时右值 - 调用返回
            else if(rNode is CallNode callNode)
            {
                var funcRec = callNode.attributes[AstAttr.func_rec] as SymbolTable.Record;
                rModel = funcRec.flags & OwnershipModelMask;

                if(rModel.HasFlag(SymbolTable.RecordFlag.OwnerVar) && lModel.HasFlag(SymbolTable.RecordFlag.OwnerVar) == false)
                {
                    if(lModel.HasFlag(SymbolTable.RecordFlag.ManualVar))
                        throw new SemanticException(ExceptioName.OwnershipError_CanNotAssignOwnToManual, errorNode, string.Empty);
                    else if(lModel.HasFlag(SymbolTable.RecordFlag.BorrowVar))
                        throw new SemanticException(ExceptioName.OwnershipError, errorNode, "cant assign temp own value to borrow variable.");
                }
                return;
            }

            // 临时右值 - Cast
            else if(rNode is CastNode castNode)
            {
                //对引用类型的Cast本质都是指针reinterpret，不涉及所有权转移  
                CheckOwnershipCompare_Core(errorNode, lModel, lname, castNode.factorNode, out rModel);
                return;
            }

            //临时右值 - SizeOf/TypeOf
            else if(rNode is SizeOfNode || rNode is TypeOfNode)
            {
                rModel = SymbolTable.RecordFlag.None;
                return;
            }

            // 其他临时右值
            else
            {
                if(lModel == SymbolTable.RecordFlag.BorrowVar)
                    throw new SemanticException(ExceptioName.OwnershipError_BorrowCanNotFromTemp, errorNode, lname);

                rModel = SymbolTable.RecordFlag.None;
                throw new SemanticException(ExceptioName.OwnershipError, errorNode, "undefined rvalue:" + rNode.GetType().Name);
            }
            
        }

        /// <summary> 变量定义 的 所有权和生命周期检查</summary>
        private void CheckOwnershipCompare_VarDecl(VarDeclareNode varDeclNode, SymbolTable.Record varRec, out SymbolTable.RecordFlag lModel, out SymbolTable.RecordFlag rModel)
        {
            lModel = GetOwnershipModel(varDeclNode.flags, varDeclNode.typeNode);
            var lname = varDeclNode.identifierNode.FullName;

            CheckOwnershipCompare_Core(varDeclNode, lModel, lname, varDeclNode.initializerNode, out rModel);
        }

        /// <summary> 赋值 的 所有权和生命周期检查</summary>
        private void CheckOwnershipCompare_Assign(AssignNode assignNode, SymbolTable.Record lvarRec, out SymbolTable.RecordFlag lModel, out SymbolTable.RecordFlag rModel)
        {
            if(lvarRec == null)
                throw new GizboxException(ExceptioName.Undefine, "lvalue record not found.");

            string lname;
            if(assignNode.lvalueNode is IdentityNode leftIdnode)
            {
                lname = leftIdnode.FullName;
            }
            else if(assignNode.lvalueNode is ObjectMemberAccessNode leftAccess)
            {
                lname = leftAccess.memberNode.FullName;
            }
            else
            {
                throw new GizboxException(ExceptioName.Undefine, "lvalue must be identity node or member access node.");
            }

            lModel = lvarRec.flags & OwnershipModelMask;

            CheckOwnershipCompare_Core(assignNode, lModel, lname, assignNode.rvalueNode, out rModel);
        }

        /// <summary> 参数传递 的 所有权和生命周期检查</summary>
        private void CheckOwnershipCompare_Param(SymbolTable.Record paramRec, ExprNode argNode, out SymbolTable.RecordFlag paramModel, out SymbolTable.RecordFlag argModel)
        {
            paramModel = paramRec.flags & OwnershipModelMask;
            var lname = paramRec.name;

            CheckOwnershipCompare_Core(argNode, paramModel, lname, argNode, out argModel);
        }

        /// <summary> 所有权可移出检查 </summary>
        private void CheckOwnershipCanMoveOut(SyntaxTree.ExprNode rvalNode)
        {
            if(rvalNode == null)
                return;

            if(rvalNode is SyntaxTree.ObjectMemberAccessNode || rvalNode is SyntaxTree.ElementAccessNode)
                throw new SemanticException(ExceptioName.OwnershipError_CanNotMoveOutClassField, rvalNode, "move-out from field is disabled; use replace.");

            if(rvalNode is SyntaxTree.CastNode castNode)
            {
                CheckOwnershipCanMoveOut(castNode.factorNode);
            }
        }


        /// <summary>
        /// 分析变量/参数/返回值的所有权模型  
        /// </summary>
        private SymbolTable.RecordFlag GetOwnershipModel(VarModifiers explicitModifier, SyntaxTree.TypeNode typeNode)
        {
            string typeExpr = typeNode.TypeExpression();

            GType type = GType.Parse(typeExpr);
            if(type.IsReferenceType)
            {
                SymbolTable.RecordFlag ownerModel = SymbolTable.RecordFlag.None;

                if(explicitModifier.HasFlag(VarModifiers.Own))
                {
                    ownerModel = SymbolTable.RecordFlag.OwnerVar;//显式own
                }
                else if(explicitModifier.HasFlag(VarModifiers.Bor))
                {
                    ownerModel = SymbolTable.RecordFlag.BorrowVar;//显式借用
                }
                else
                {
                    bool isOwnershipClass = false;
                    if(typeNode is ClassTypeNode classTypeNode)
                    {
                        var classRec = Query(classTypeNode.classname.FullName);

                        if(classRec.flags.HasFlag(SymbolTable.RecordFlag.OwnershipClass))
                            isOwnershipClass = true;
                    }

                    if(isOwnershipClass)
                        ownerModel = SymbolTable.RecordFlag.OwnerVar;//own class 类型
                    else
                        ownerModel = SymbolTable.RecordFlag.ManualVar;//手动释放类型
                }
                return ownerModel;
            }

            return SymbolTable.RecordFlag.None;
        }


        /// <summary>
        /// 获取表达式的类型表达式  
        /// </summary>
        private string AnalyzeTypeExpression(SyntaxTree.ExprNode exprNode)
        {
            if (exprNode == null) throw new GizboxException(ExceptioName.ExpressionNodeIsNull);
            if (exprNode.attributes == null) throw new SemanticException(ExceptioName.NodeNoInitializationPropertyList, exprNode, "");
            if (exprNode.attributes.ContainsKey(AstAttr.type)) return (string)exprNode.attributes[AstAttr.type];

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
                            result = Query_IgnoreMangle(idNode.FullName);
                        if (result == null)
                        {
                            throw new SemanticException(ExceptioName.IdentifierNotFound, idNode, "");
                        }


                        nodeTypeExprssion = result.typeExpression;
                    }
                    break;
                case SyntaxTree.LiteralNode litNode:
                    {
                        nodeTypeExprssion = TypeUtils.GetLitType(litNode.token);
                    }
                    break;
                case SyntaxTree.DefaultValueNode defaultNode:
                    {
                        nodeTypeExprssion = defaultNode.typeNode.TypeExpression();
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

                        var memberRec = classEnv.Class_GetMemberRecordInChainByRawname(accessNode.memberNode.FullName);//使用RawName以防找不到成员为函数时找不到    
                        if (memberRec == null) throw new SemanticException(ExceptioName.MemberFieldNotFound, accessNode.objectNode, accessNode.memberNode.FullName);

                        accessNode.attributes[AstAttr.klass] = className;//记录memberAccess节点的点左边类型
                        accessNode.attributes[AstAttr.member_name] = accessNode.memberNode.FullName;//记录memberAccess节点的点右边名称

                        nodeTypeExprssion = memberRec.typeExpression;
                    }
                    break;
                case SyntaxTree.ElementAccessNode eleAccessNode:
                    {
                        string containerTypeExpr = AnalyzeTypeExpression(eleAccessNode.containerNode);

                        if (containerTypeExpr.EndsWith("[]") == false)
                            throw new SemanticException(ExceptioName.Undefine, eleAccessNode, "only array can use [] operator");

                        nodeTypeExprssion = containerTypeExpr.Substring(0, containerTypeExpr.Length - 2);
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        string funcMangledName;

                        if (callNode.isMemberAccessFunction)
                        {
                            string[] explicitArgTypeArr = callNode.argumantsNode.arguments.Select(argN => AnalyzeTypeExpression(argN)).ToArray();
                            string[] explicitParamTypeArr = new string[explicitArgTypeArr.Length];


                            var funcAccess = (callNode.funcNode as SyntaxTree.ObjectMemberAccessNode);
                            string funcFullName = funcAccess.memberNode.FullName;

                            var className = AnalyzeTypeExpression(funcAccess.objectNode);


                            List<string> allArgTypeList = new List<string>() { className };
                            allArgTypeList.AddRange(callNode.argumantsNode.arguments.Select(argN => AnalyzeTypeExpression(argN)));
                            string[] allArgTypeArr = allArgTypeList.ToArray();
                            string[] allParamTypeArr = new string[allArgTypeArr.Length];



                            var classRec = Query(className);
                            if (classRec == null) throw new SemanticException(ExceptioName.ClassNameNotFound, callNode, className);

                            var classEnv = classRec.envPtr;
                            if (classEnv == null) throw new SemanticException(ExceptioName.ClassScopeNotFound, callNode, "");

                            bool anyFunc = TryQueryAndMatchFunction(funcFullName, explicitArgTypeArr, explicitParamTypeArr, true, classEnv);
                            if(anyFunc == false) throw new SemanticException(ExceptioName.FunctionMemberNotFound, callNode, funcAccess.memberNode.FullName);

                            funcMangledName = Utils.Mangle(funcFullName, explicitParamTypeArr);

                            var memberRec = classEnv.Class_GetMemberRecordInChain(funcMangledName);
                            if (memberRec == null) throw new SemanticException(ExceptioName.FunctionMemberNotFound, callNode, funcAccess.memberNode.FullName);

                            var typeExpr = memberRec.typeExpression;

                            if (typeExpr.Contains("=>") == false) throw new SemanticException(ExceptioName.ObjectMemberNotFunction, callNode, typeExpr);
                            nodeTypeExprssion = typeExpr.Split(' ').LastOrDefault();


                            callNode.attributes[AstAttr.mangled_name] = funcMangledName;
                        }
                        else
                        {
                            string[] argTypeArr = callNode.argumantsNode.arguments.Select(argN => AnalyzeTypeExpression(argN)).ToArray();
                            string[] paramTypeArr = new string[argTypeArr.Length];


                            var funcId = (callNode.funcNode as SyntaxTree.IdentityNode);

                            bool anyFunc = TryQueryAndMatchFunction(funcId.FullName, argTypeArr, paramTypeArr);
                            if(anyFunc == false) throw new SemanticException(ExceptioName.FunctionNotFound, callNode, funcId.FullName);


                            bool isExternFunc = false;
                            funcMangledName = Utils.Mangle(funcId.FullName, paramTypeArr);
                            var idRec = Query(funcMangledName);
                            if(idRec == null)
                            {
                                idRec = Query(Utils.ToExternFuncName(funcId.FullName));
                                if(idRec != null)
                                    isExternFunc = true;
                            }

                            if (idRec == null) 
                                throw new SemanticException(ExceptioName.FunctionNotFound, callNode, funcId.FullName);

                            string typeExpr = idRec.typeExpression.Split(' ').LastOrDefault();

                            nodeTypeExprssion = typeExpr;

                            if(isExternFunc)
                            {
                                callNode.attributes[AstAttr.extern_name] = Utils.ToExternFuncName(funcId.FullName);
                            }
                            else
                            {
                                callNode.attributes[AstAttr.mangled_name] = funcMangledName;
                            }
                        }
                    }
                    break;
                case SyntaxTree.ReplaceNode replaceNode:
                    {
                        if(replaceNode.targetNode is not SyntaxTree.ObjectMemberAccessNode)
                            throw new SemanticException(ExceptioName.OwnershipError, replaceNode, "replace target must be a field access.");

                        nodeTypeExprssion = AnalyzeTypeExpression(replaceNode.targetNode);
                    }
                    break;
                case SyntaxTree.NewObjectNode newObjNode:
                    {
                        string className = newObjNode.className.FullName;
                        if(Query(className) == null)
                        {
                            Console.WriteLine("AST");
                            this.ilUnit.globalScope.env.Print();
                            Console.WriteLine();
                            
                            throw new SemanticException(ExceptioName.ClassDefinitionNotFound, newObjNode.className, className);
                        }
                        

                        nodeTypeExprssion = newObjNode.typeNode?.TypeExpression() ?? className;
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
                            if (CheckType_Equal(typeL, typeR) == false) throw new SemanticException(ExceptioName.InconsistentExpressionTypesCannotCompare, binaryOp, "");

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
                case SyntaxTree.SizeOfNode sizeofNode:
                    {
                        nodeTypeExprssion = "int";
                    }
                    break;
                case SyntaxTree.TypeOfNode typeofNode:
                    {
                        if(Query("Core::Type") == null)
                            throw new SemanticException(ExceptioName.ClassDefinitionNotFound, typeofNode, "Core::Type");

                        nodeTypeExprssion = "Core::Type";
                    }
                    break;
                default:
                    throw new SemanticException(ExceptioName.CannotAnalyzeExpressionNodeType, exprNode, exprNode.GetType().Name);
            }

            exprNode.attributes[AstAttr.type] = nodeTypeExprssion;

            return nodeTypeExprssion;
        }

        /// <summary>
        /// 类型推断
        /// </summary>
        private string InferType(SyntaxTree.InferTypeNode typeNode, SyntaxTree.ExprNode exprNode)
        {
            var initializerType = AnalyzeTypeExpression(exprNode);
            if (initializerType == "null")
            {
                throw new SemanticException(ExceptioName.SemanticAnalysysError, typeNode, "");
            }
            typeNode.attributes[AstAttr.type] = initializerType;
            return initializerType;
        }

        /// <summary>
        /// 检查类型
        /// </summary>
        private bool CheckType_Equal(string typeExpr, SyntaxTree.ExprNode exprNode)
        {
            return CheckType_Equal(typeExpr, AnalyzeTypeExpression(exprNode));
        }
        private bool CheckType_Equal(SyntaxTree.ExprNode exprNode1, SyntaxTree.ExprNode exprNode2)
        {
            return CheckType_Equal(AnalyzeTypeExpression(exprNode1), AnalyzeTypeExpression(exprNode2));
        }
        private bool CheckType_Equal(string typeExpr1, string typeExpr2)
        {
            if (typeExpr1 == "null" && GType.Parse(typeExpr2).IsReferenceType)
            {
                return true;
            }
            else if (typeExpr2 == "null" && GType.Parse(typeExpr1).IsReferenceType)
            {
                return true;
            }

            return typeExpr1 == typeExpr2;
        }
        private bool CheckType_Is(string typeExpr1, string typeExpr2)
        {
            if(typeExpr1 == typeExpr2) return true;

            //有至少一个是基元类型  
            if(GType.Parse(typeExpr1).IsValuePrimitive || GType.Parse(typeExpr2).IsValuePrimitive)
            {
                return typeExpr1 == typeExpr2;
            }
            //全是非基元类型  
            else
            {
                //null可以是任何非基元类型的子类  
                if(typeExpr1 == "null")
                {
                    return true;
                }
                //两个都是类类型
                else if(GType.Parse(typeExpr1).IsClassType && GType.Parse(typeExpr2).IsClassType)
                {
                    var typeRec1 = Query(typeExpr1);
                    if(typeRec1.envPtr.Class_IsSubClassOf(typeExpr2))
                    {
                        return true;
                    }
                }
                //两个都是数组类型  
                else if(GType.Parse(typeExpr1).IsArray && GType.Parse(typeExpr2).IsArray)
                {
                    //不支持逆变和协变  
                }
            }


            return false;
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
                            if(CheckReturnStmt(stmt, returnType))
                            {
                                anyReturnStmt = true;//不break，确保所有return节点都被检查  
                            }
                        }
                        return anyReturnStmt;
                    }
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        bool anyReturnStmt = false;
                        for(int i = stmtsNode.statements.Count - 1; i > -1; --i)
                        {
                            var stmt = stmtsNode.statements[i];
                            if(CheckReturnStmt(stmt, returnType))
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
                            if(CheckReturnStmt(conditionClause.thenNode, returnType) == false)
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
                        bool typeValid = CheckType_Equal(returnType, retNode.returnExprNode);
                        if(typeValid == false)
                            throw new SemanticException(ExceptioName.ReturnTypeError, retNode, "");

                        return true;
                    }
                //其他节点  
                default:
                    return false;
            }
        }


        private SymbolTable.Record Query(string name)
        {
            //符号表链查找  
            var toList = envStack.AsList();
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

        private SymbolTable.Record Query_IgnoreMangle(string rawname)
        {
            //符号表链查找  
            var toList = envStack.AsList();
            for (int i = toList.Count - 1; i > -1; --i)
            {
                if (toList[i].ContainRecordRawName(rawname))
                {
                    return toList[i].GetRecordByRawname(rawname);
                }
            }
            //库依赖中查找  
            foreach (var lib in this.ilUnit.dependencyLibs)
            {
                var result = lib.QueryTopSymbol(rawname, ignoreMangle:true);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private SymbolTable.Record Query_OperatorOverload(string opName, string typeExpr1, string typeExpr2)
        {
            //顶层作用域查找  
            List<SymbolTable.Record> result = new();
            ilUnit.globalScope.env.GetAllRecordByFlag(SymbolTable.RecordFlag.OperatorOverloadFunc, result);
            //库依赖中查找  
            foreach(var lib in this.ilUnit.dependencyLibs)
            {
                lib.globalScope.env.GetAllRecordByFlag(SymbolTable.RecordFlag.OperatorOverloadFunc, result);
            }

            //筛选
            string mangledName = Utils.Mangle(opName, typeExpr1, typeExpr2);
            foreach(var opOverload in result)
            {
                if(opOverload.name.EndsWith(mangledName))
                {
                    return opOverload;
                }
            }

            return null;
        }

        private void QueryAll_IgnoreMangle(string rawname, List<SymbolTable.Record> result)
        {
            //符号表链查找  
            var asList = envStack.AsList();
            for(int i = asList.Count - 1; i > -1; --i)
            {
                asList[i].GetAllRecordByRawname(rawname, result);
            }
            //库依赖中查找  
            foreach(var lib in this.ilUnit.dependencyLibs)
            {
                lib.QueryAndFillTopSymbolsToContainer(rawname, result, ignoreMangle: true);
            }
        }
        private bool TryQueryIgnoreMangle(string name)
        {
            //符号表链查找  
            var asList = envStack.AsList();
            for (int i = asList.Count - 1; i > -1; --i)
            {
                if(asList[i].ContainRecordRawName(name))
                    return true;
            }
            //库依赖中查找  
            foreach (var lib in this.ilUnit.dependencyLibs)
            {
                if(lib.QueryTopSymbol(name, ignoreMangle: true) != null)
                {
                    return true;
                }
            }

            if (Compiler.enableLogParser)
                Log("TryQuery  库中未找到:" + name);
            
            return false;
        }

        private bool HasTemplate(string name)
        {
            if(ilUnit.templateClasses.Contains(name))
                return true;
            if(ilUnit.templateFunctions.Contains(name))
                return true;


            //库依赖中查找  
            foreach(var lib in this.ilUnit.dependencyLibs)
            {
                if(lib.templateClasses.Contains(name))
                    return true;
                if(lib.templateFunctions.Contains(name))
                    return true;
            }

            return false;
        }

        private SymbolTable TryGetClassEnv()
        {
            //符号表链查找  
            var toList = envStack.AsList();
            for(int i = toList.Count - 1; i > -1; --i)
            {
                if(toList[i].tableCatagory == SymbolTable.TableCatagory.ClassScope)
                {
                    return toList[i];
                }
            }
            return null;
        }
        private SyntaxTree.ClassDeclareNode TrygetClassDeclNode(SyntaxTree.Node curr)
        {
            SyntaxTree.Node node = curr;
            while(node != null)
            {
                if(node is SyntaxTree.ClassDeclareNode classDeclNode)
                {
                    return classDeclNode;
                }
                node = node.Parent;
            }
            return null;
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
                string name = idNode.token.attribute;
                if(name.Contains('^'))
                {
                    name = name.Substring(0, name.IndexOf('^'));
                    string nameToQuery = namespaceUsing.namespaceNameNode.token.attribute + "::" + name;
                    if(HasTemplate(nameToQuery))
                    {
                        if(found == true)
                        {
                            throw new SemanticException(ExceptioName.IdentifierAmbiguousBetweenNamespaces, idNode, nameToQuery + " vs " + namevalid);
                        }
                        found = true;
                        idNode.SetPrefix(namespaceUsing.namespaceNameNode.token.attribute);
                    }
                }
                else
                {
                    string nameToQuery = namespaceUsing.namespaceNameNode.token.attribute + "::" + name;
                    if(TryQueryIgnoreMangle(nameToQuery))
                    {
                        if(found == true)
                        {
                            throw new SemanticException(ExceptioName.IdentifierAmbiguousBetweenNamespaces, idNode, nameToQuery + " vs " + namevalid);
                        }
                        found = true;
                        idNode.SetPrefix(namespaceUsing.namespaceNameNode.token.attribute);
                    }
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
            if(typeNode.attributes.ContainsKey(AstAttr.name_completed))
                return;

            switch (typeNode)
            {
                case SyntaxTree.PrimitiveTypeNode primitiveTypeNode:
                    break;
                case SyntaxTree.ClassTypeNode classTypeNode:
                    {
                        TryCompleteIdenfier(classTypeNode.classname);
                    }
                    break;
                case SyntaxTree.ArrayTypeNode arrayTypeNpde:
                    {
                        TryCompleteType(arrayTypeNpde.elemtentType);
                    }
                    break;
            }
            typeNode.attributes[AstAttr.name_completed] = true;
        }

        private bool TryQueryAndMatchFunction(string funcRawName, string[] argTypes, string[] outParamTypes, bool isMethod = false, SymbolTable classEnvOfMethod = null)
        {
            List<SymbolTable.Record> allFunctions = new List<SymbolTable.Record>();
            if(classEnvOfMethod != null)
                classEnvOfMethod.Class_GetAllMemberRecordInChainByRawname(funcRawName, allFunctions);
            else
                QueryAll_IgnoreMangle(funcRawName, allFunctions);

            //未找到函数名  
            if(allFunctions.Count == 0)
            {
                return false;
            }

            //实参形参类型匹配  
            SymbolTable.Record targetFunc = null;
            List<SymbolTable.Record> targetFuncParams = null;
            foreach(var funcRec in allFunctions)
            {
                List<SymbolTable.Record> paramRecs;
                if(isMethod)
                {
                    paramRecs = funcRec.envPtr.GetByCategory(SymbolTable.RecordCatagory.Param).Where(r => r.name != "this").ToList();
                }
                else
                {
                    paramRecs = funcRec.envPtr.GetByCategory(SymbolTable.RecordCatagory.Param);
                }
                
                //无参函数  
                if(paramRecs == null)
                {
                    //0个实参  
                    if(argTypes.Length == 0)
                    {
                        targetFunc = funcRec;
                        targetFuncParams = paramRecs;
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    if(paramRecs.Count != argTypes.Length)
                    {
                        continue;//参数个数不匹配  
                    }
                        


                    bool allMatch = true;
                    for(int i = 0; i < argTypes.Length; ++i)
                    {
                        var argT = argTypes[i];
                        var parmT = paramRecs[i].typeExpression;
                        if(CheckType_Is(argT, parmT) == false)
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if(allMatch)
                    {
                        targetFunc = funcRec;
                        targetFuncParams = paramRecs;
                        break;
                    }
                }
            }


            //Result  
            if(targetFunc != null)
            {
                if(targetFuncParams != null)
                {
                    for(int i = 0; i < argTypes.Length; ++i)
                    {
                        outParamTypes[i] = targetFuncParams[i].typeExpression;
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private LiteralNode GenDefaultLitNode(SyntaxTree.TypeNode typenode)
        {
            string strLit = TypeUtils.GenDefaultValue(typenode.TypeExpression());
            string tokenname = TypeUtils.GetLitTokenName(typenode.TypeExpression());
            var start = typenode.StartToken();
            LiteralNode litnode = new LiteralNode()
            {
                token = new Token(tokenname, PatternType.Literal, strLit, start.line, start.start, start.length),
                attributes = new Dictionary<AstAttr, object>(),
            };
            return litnode;
        }


        private bool CheckIntrinsicCall(SyntaxTree.CallNode callNode, out SyntaxTree.Node replace)
        {
            if(callNode.isMemberAccessFunction == false
                && callNode.funcNode is SyntaxTree.IdentityNode funcId)
            {
                if(funcId.token?.attribute == "replace")
                {
                    if(callNode.argumantsNode.arguments.Count != 2)
                        throw new SemanticException(ExceptioName.SemanticAnalysysError, callNode, "replace expects 2 arguments.");

                    var replaceNode = new SyntaxTree.ReplaceNode()
                    {
                        targetNode = callNode.argumantsNode.arguments[0],
                        newValueNode = callNode.argumantsNode.arguments[1],
                        attributes = callNode.attributes,
                    };
                    replaceNode.Parent = callNode.Parent;
                    callNode.overrideNode = replaceNode;

                    replace = replaceNode;
                    return true;
                }
                else if(funcId.token?.attribute == "hashof")
                {
                    if(callNode.argumantsNode.arguments.Count != 1)
                        throw new SemanticException(ExceptioName.SemanticAnalysysError, callNode, "hashof expects 1 argument.");

                    //基元类型的hashof
                    if(GType.Parse(AnalyzeTypeExpression(callNode.argumantsNode.arguments[0])).IsPrimitive)
                    {
                        var replaceNode = new SyntaxTree.CallNode()
                        {
                            isMemberAccessFunction = false,
                            funcNode = new SyntaxTree.IdentityNode()
                            {
                                token = new Token("ID", PatternType.Id, "Core::Hash::GetHashCode", funcId.token.line, funcId.token.start, funcId.token.length),
                                identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                            },
                            argumantsNode = new SyntaxTree.ArgumentListNode(),
                        };
                        replaceNode.argumantsNode.arguments.Add(callNode.argumantsNode.arguments[0]);
                        replaceNode.Parent = callNode.Parent;
                        callNode.overrideNode = replaceNode;

                        replace = replaceNode;
                        return true;
                    }
                    //自定义引用类型的hashof
                    else
                    {
                        var replaceNode = new SyntaxTree.CallNode()
                        {
                            isMemberAccessFunction = true,
                            funcNode = new SyntaxTree.ObjectMemberAccessNode()
                            {
                                objectNode = callNode.argumantsNode.arguments[0],
                                memberNode = new SyntaxTree.IdentityNode()
                                {
                                    token = new Token("ID", PatternType.Id, "GetHashCode", funcId.token.line, funcId.token.start, funcId.token.length),
                                    identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod,
                                }
                            },
                            argumantsNode = new SyntaxTree.ArgumentListNode(),
                        };
                        replaceNode.Parent = callNode.Parent;
                        callNode.overrideNode = replaceNode;

                        replace = replaceNode;
                        return true;
                    }
                }
            }



            replace = null;
            return false;
        }

        private void GenClassLayoutInfo(SymbolTable.Record classRec)
        {
            var classEnv = classRec.envPtr;
            List<SymbolTable.Record> fieldRecs = new();
            foreach(var (memberName, memberRec) in classEnv.records)
            {
                if(memberRec.category != SymbolTable.RecordCatagory.Variable)
                    continue;
                fieldRecs.Add(memberRec);
            }
            (int size, int align)[] fieldSizeAndAlignArr = new (int size, int align)[fieldRecs.Count];
            //对象头是虚函数表指针  
            for(int i = 0; i < fieldRecs.Count; i++)
            {
                string typeExpress = fieldRecs[i].typeExpression;
                var size = MemUtility.GetGizboxTypeSize(typeExpress);
                var align = MemUtility.GetGizboxTypeSize(typeExpress);
                fieldRecs[i].size = size;
                fieldSizeAndAlignArr[i] = (size, align);
            }
            long classSize = MemUtility.ClassLayout(8, fieldSizeAndAlignArr, out var allocAddrs);
            for(int i = 0; i < fieldRecs.Count; i++)
            {
                fieldRecs[i].addr = allocAddrs[i];
            }

            classRec.size = classSize;
        }

        public static void Log(object content)
        {
            if (!Compiler.enableLogSemanticAnalyzer) return;
            GixConsole.WriteLine("SematicAnalyzer >>>>" + content);
        }
    }
}






