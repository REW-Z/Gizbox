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
        switch_clause_list,
        enum_member_list,

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
        func_ptr_ownsig,
        class_rec,
        enum_rec,
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

        private class FunctionOwnershipSignature
        {
            public SymbolTable.RecordFlag ReturnModel;
            public List<SymbolTable.RecordFlag> ParamModels = new List<SymbolTable.RecordFlag>();

            public FunctionOwnershipSignature Clone()
            {
                return new FunctionOwnershipSignature()
                {
                    ReturnModel = this.ReturnModel,
                    ParamModels = new List<SymbolTable.RecordFlag>(this.ParamModels),
                };
            }
        }


        //temp  
        private int blockCounter = 0;//Block自增  
        private int ifCounter = 0;//if语句标号自增  
        private int whileCounter = 0;//while语句标号自增  
        private int forCounter = 0;//for语句标号自增  
        private int switchCounter = 0;//switch语句标号自增

        //temp  
        private string currentNamespace = "";
        private List<string> namespaceUsings = new List<string>();

        //temp  
        private LifetimeInfo lifeTimeInfo = new();

        //函数指针 -> 所有权签名（仅前端语义分析阶段使用）
        private readonly Dictionary<SymbolTable.Record, FunctionOwnershipSignature> funcPtrOwnershipSignatures = new();


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
            RewriteAllKnownStructTypeExpressions();

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
                                constDeclNode.typeNode.TypeExpression()
                                //暂不写入initValue,需要Pass3常量表达式求值  
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
                            if(funcDeclNode.isExport)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "export function cannot be generic.");

                            //附加命名空间名  
                            if(isGlobalOrTopNamespace == false)
                            {
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "template function can only declare at global env.");
                            }

                            funcDeclNode.identifierNode.SetPrefix(currentNamespace);
                            ilUnit.templateFunctions.Add(funcDeclNode.identifierNode.FullName);
                            break;
                        }
                            


                        if (isGlobalOrTopNamespace)
                        {
                            if(funcDeclNode.isExport && isTopLevelAtNamespace)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "export function can only be declared at global scope.");

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
                                typeExpr += paramNode.typeNode.TypeExpression();
                            }
                            typeExpr += (" => " + funcDeclNode.returnTypeNode.TypeExpression());


                            //函数修饰名称  
                            var paramTypeArr = funcDeclNode.parametersNode.parameterNodes.Select(n => n.typeNode.TypeExpression()).ToArray();
                            var funcMangledName = funcDeclNode.identifierNode.FullName;
                            if(funcMangledName != "main" && funcDeclNode.isExport == false)
                            {
                                funcMangledName = Utils.Mangle(funcDeclNode.identifierNode.FullName, paramTypeArr);
                            }
                            

                            funcDeclNode.attributes[AstAttr.mangled_name] = funcMangledName;

                            //新的作用域  
                            string envName = isMethod ? envStack.Peek().name + "::" + funcMangledName : funcMangledName;

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
                            if(funcDeclNode.isExport)
                            {
                                newRec.flags |= SymbolTable.RecordFlag.ExportFunc;
                            }
                        }
                        else if(funcDeclNode.isExport)
                        {
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "export function can only be declared at global scope.");
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
                            typeExpr += (" => " + (externFuncDeclNode.returnTypeNode.TypeExpression()));

                            //函数修饰名称  
                            var paramTypeArr = externFuncDeclNode.parametersNode.parameterNodes.Select(n => (n.typeNode.TypeExpression())).ToArray();
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
                case SyntaxTree.StructDeclareNode structDeclNode:
                    {
                        if(isGlobalOrTopNamespace)
                        {
                            if(isTopLevelAtNamespace)
                                structDeclNode.structNameNode.SetPrefix(currentNamespace);

                            var newEnv = new SymbolTable(structDeclNode.structNameNode.FullName, SymbolTable.TableCatagory.ClassScope, envStack.Peek());
                            structDeclNode.attributes[AstAttr.env] = newEnv;

                            var newRec = envStack.Peek().NewRecord(
                                structDeclNode.structNameNode.FullName,
                                SymbolTable.RecordCatagory.Struct,
                                "",
                                newEnv
                            );
                            structDeclNode.attributes[AstAttr.class_rec] = newRec;
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.ClassDefinitionGlobalOrNamespaceOnly, structDeclNode, "");
                        }
                    }
                    break;
                case SyntaxTree.EnumDeclareNode enumDeclNode:
                    {
                        if(isGlobalOrTopNamespace)
                        {
                            if(isTopLevelAtNamespace)
                                enumDeclNode.enumNameNode.SetPrefix(currentNamespace);
                            var newEnv = new SymbolTable(enumDeclNode.enumNameNode.FullName, SymbolTable.TableCatagory.ClassScope, envStack.Peek());
                            enumDeclNode.attributes[AstAttr.env] = newEnv;

                            var newRec = envStack.Peek().NewRecord(
                                enumDeclNode.enumNameNode.FullName,
                                SymbolTable.RecordCatagory.Enum,
                                "(enum)" + enumDeclNode.enumNameNode.FullName,
                                newEnv
                            );
                            enumDeclNode.attributes[AstAttr.enum_rec] = newRec;
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.ClassDefinitionGlobalOrNamespaceOnly, enumDeclNode, "");
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

                        if(funcDeclNode.isExport)
                        {
                            if(isTopLevelAtNamespace)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "export function can only be declared at global scope.");
                            if(isGlobalOrTopAtNamespace == false)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "export function can only be declared at global scope.");
                        }

                        //Id at env
                        funcDeclNode.identifierNode.attributes[AstAttr.def_at_env] = envStack.Peek();


                        //是否是实例成员函数  
                        bool isMethod = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;
                        string className = null;
                        if (isMethod) className = envStack.Peek().name;
                        if (isMethod && isGlobalOrTopAtNamespace) throw new SemanticException(ExceptioName.NamespaceTopLevelNonMemberFunctionOnly, funcDeclNode, "");

                        if(isMethod == false && (IsCtorIdentifier(funcDeclNode.identifierNode) || IsDtorIdentifier(funcDeclNode.identifierNode)))
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "constructor/destructor can only be declared in class scope.");


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

                            //函数修饰名称（成员函数）
                            bool isCtor = IsCtorIdentifier(funcDeclNode.identifierNode);
                            bool isDtor = IsDtorIdentifier(funcDeclNode.identifierNode);

                            if(isCtor && funcDeclNode.attributes.TryGetValue(AstAttr.member_name, out var ctorDeclNameObj) && ctorDeclNameObj is string ctorDeclName)
                            {
                                var classShortName = GetClassShortName(className);
                                if(ctorDeclName != classShortName)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "constructor name must match class name.");
                            }

                            string funcMangledName;

                            if(isCtor)
                            {
                                funcMangledName = BuildCtorFunctionFullName(className, paramTypeArr);
                            }
                            else if(isDtor)
                            {
                                if(paramTypeArr.Length != 0)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode, "destructor can not have parameters.");

                                funcMangledName = BuildDtorFunctionFullName(className);
                            }
                            else
                            {
                                funcMangledName = Utils.Mangle(funcDeclNode.identifierNode.FullName, paramTypeArr);
                            }

                            funcDeclNode.attributes[AstAttr.mangled_name] = funcMangledName;


                            //新的作用域（成员函数）  
                            string envName = (isMethod && isCtor == false && isDtor == false) 
                                ? envStack.Peek().name + "::" + funcMangledName 
                                : funcMangledName;

                            var newEnv = new SymbolTable(envName, SymbolTable.TableCatagory.FuncScope, envStack.Peek());
                            funcDeclNode.attributes[AstAttr.env] = newEnv;

                            //类符号表同名方法去重（成员函数）    
                            if (envStack.Peek().ContainRecordName(funcMangledName))
                            {
                                envStack.Peek().records.Remove(funcMangledName);
                            }

                            //添加到虚函数表（成员函数）
                            if(isCtor == false && isDtor == false)
                            {
                                this.ilUnit.vtables[className].NewRecord(funcMangledName, className);
                            }

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

                            if(isCtor)
                                newRec.flags |= SymbolTable.RecordFlag.Ctor;
                            if(isDtor)
                                newRec.flags |= SymbolTable.RecordFlag.Dtor;

                            funcDeclNode.attributes[AstAttr.func_rec] = newRec;
                        }





                        {
                            SymbolTable funcEnv = (SymbolTable)funcDeclNode.attributes[AstAttr.env];

                            //进入函数作用域  
                            envStack.Push(funcEnv);



                            //隐藏的this参数加入符号表    
                            if(isMethod)
                            {
                                var classRec = funcDeclNode.Parent.attributes[AstAttr.class_rec] as SymbolTable.Record;
                                bool isOwnershipClass = classRec.flags.HasFlag(SymbolTable.RecordFlag.OwnershipClass);
                                funcEnv.NewRecord("this", SymbolTable.RecordCatagory.Param, $"{ (isOwnershipClass ? "(own-class)" : "(class)")}{className}");
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

                        bool hasExplicitCtor = false;
                        bool hasExplicitDtor = false;
                        foreach(var decl in classDeclNode.memberDelareNodes)
                        {
                            if(decl is not SyntaxTree.FuncDeclareNode fn)
                                continue;

                            if(IsCtorIdentifier(fn.identifierNode))
                                hasExplicitCtor = true;
                            if(IsDtorIdentifier(fn.identifierNode))
                                hasExplicitDtor = true;
                        }

                        //构造函数
                        if(hasExplicitCtor == false)
                        {
                            var ctorName = BuildCtorFunctionFullName(classDeclNode.classNameNode.FullName, Array.Empty<string>());
                            var ctorEnv = new SymbolTable(ctorName, SymbolTable.TableCatagory.FuncScope, newEnv);
                            ctorEnv.NewRecord("this", SymbolTable.RecordCatagory.Param, $"{(classDeclNode.flags.HasFlag(TypeModifiers.Own) ? "(own-class)" : "(class)")}{classDeclNode.classNameNode.FullName}");
                            var ctorRec = newEnv.NewRecord(
                                ctorName,
                                SymbolTable.RecordCatagory.Function,
                                GType.GenFuncType(GType.Parse("void"), GType.Parse(classDeclNode.classNameNode.FullName)).ToString(),
                                ctorEnv
                            );
                            ctorRec.rawname = "ctor";
                            ctorRec.flags |= SymbolTable.RecordFlag.Ctor;
                        }

                        //析构函数
                        if(hasExplicitDtor == false)
                        {
                            var dtorName = BuildDtorFunctionFullName(classDeclNode.classNameNode.FullName);
                            var dtorEnv = new SymbolTable(dtorName, SymbolTable.TableCatagory.FuncScope, newEnv);
                            dtorEnv.NewRecord("this", SymbolTable.RecordCatagory.Param, $"{ (classDeclNode.flags.HasFlag(TypeModifiers.Own) ? "(own-class)" : "(class)") }{classDeclNode.classNameNode.FullName}");
                            var dtorRec = newEnv.NewRecord(
                                dtorName,
                                SymbolTable.RecordCatagory.Function,
                                GType.GenFuncType(GType.Parse("void"), GType.Parse(classDeclNode.classNameNode.FullName)).ToString(),
                                dtorEnv
                            );
                            dtorRec.rawname = "dtor";
                            dtorRec.flags |= SymbolTable.RecordFlag.Dtor;
                        }

                        //生成类的内存布局  
                        GenClassLayoutInfo(classDeclNode.attributes[AstAttr.class_rec] as SymbolTable.Record);


                        //离开类作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.StructDeclareNode structDeclNode:
                    {
                        structDeclNode.structNameNode.attributes[AstAttr.def_at_env] = envStack.Peek();

                        var newEnv = (SymbolTable)structDeclNode.attributes[AstAttr.env];
                        envStack.Push(newEnv);

                        foreach(var declNode in structDeclNode.memberDelareNodes)
                        {
                            if(declNode is not SyntaxTree.VarDeclareNode)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, declNode, "struct only allows field variable declarations.");

                            Pass2_CollectOtherSymbols(declNode);
                        }

                        // 生成结构体内存布局
                        GenStructLayoutInfo(structDeclNode.attributes[AstAttr.class_rec] as SymbolTable.Record);

                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.EnumDeclareNode enumDeclNode:
                    {
                        envStack.Push(enumDeclNode.attributes[AstAttr.env] as SymbolTable);
                        string enumTypeExpr = "(enum)" + enumDeclNode.enumNameNode.FullName;
                        foreach(var memberNode in enumDeclNode.memberNodes)
                        {
                            memberNode.identifierNode.SetPrefix(null);

                            var newRec = envStack.Peek().NewRecord(
                                memberNode.identifierNode.FullName,
                                SymbolTable.RecordCatagory.Constant,
                                enumTypeExpr,
                                initValue: memberNode.valueNode.token.attribute
                            );
                            memberNode.attributes[AstAttr.const_rec] = newRec;
                        }

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
                case SyntaxTree.SwitchStmtNode switchNode:
                    {
                        switchNode.attributes[AstAttr.uid] = switchCounter++;

                        foreach(var caseNode in switchNode.caseNodes)
                        {
                            Pass2_CollectOtherSymbols(caseNode.statementsNode);
                        }
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
                        if(!valid)
                            throw new SemanticException(ExceptioName.ConstantTypeDeclarationError, constDeclNode, "type:" + constDeclNode.typeNode.TypeExpression() + "  value type:" + AnalyzeTypeExpression(constDeclNode.litValNode));

                        //写入initValue值    
                        var rec = constDeclNode.attributes[AstAttr.const_rec] as SymbolTable.Record;
                        bool isConstExpr = TryGetLiteralConstant(constDeclNode.litValNode, out string typeExpr, out object val);
                        if(isConstExpr == false)
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, constDeclNode.litValNode, "constant initializer must be a constant expression.");
                        rec.initValue = FormatLiteralAttribute(GType.Parse(typeExpr), val);
                    }
                    break;
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        bool isBraceInitializer = varDeclNode.initializerNode is SyntaxTree.BraceInitializerNode;

                        //分析初始化表达式  
                        if(isBraceInitializer == false)
                        {
                            Pass3_AnalysisNode(varDeclNode.initializerNode);
                        }

                        TryCompleteType(varDeclNode.typeNode);

                        //类型推断  
                        if(varDeclNode.typeNode is InferTypeNode typeNode)
                        {
                            if(isBraceInitializer)
                                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, varDeclNode, "brace initializer does not support var inference.");

                            var typeExpr = InferType(typeNode, varDeclNode.initializerNode);
                            if(GType.Parse(typeExpr).Category == GType.Kind.Function)
                            {
                                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, varDeclNode, "function pointer type does not support var inference, use TR(...) explicitly.");
                            }
                            var record = envStack.Peek().GetRecord(varDeclNode.identifierNode.FullName);
                            record.typeExpression = typeExpr;
                        }
                        //类型检查（初始值）  
                        else
                        {
                            ValidateByRefType(varDeclNode.typeNode, varDeclNode);

                            var declaredTypeExpr = varDeclNode.typeNode.TypeExpression();
                            if(GType.Parse(declaredTypeExpr).IsRefType)
                            {
                                ValidateRefBinding(varDeclNode, declaredTypeExpr, varDeclNode.initializerNode);
                            }
                            else if(varDeclNode.initializerNode is SyntaxTree.BraceInitializerNode braceInitializerNode)
                            {
                                AnalyzeBraceInitializerType(braceInitializerNode, declaredTypeExpr, varDeclNode);
                            }
                            else
                            {
                                bool valid = CheckType_Equal(declaredTypeExpr, varDeclNode.initializerNode);
                                if(!valid)
                                {
                                    throw new SemanticException(ExceptioName.VariableTypeDeclarationError, varDeclNode, "type:" + declaredTypeExpr + "  intializer type:" + AnalyzeTypeExpression(varDeclNode.initializerNode));
                                }
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
                            ValidateByRefType(captureNode.typeNode, captureNode);

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
                        if(gtype.IsRawReferenceType == false)
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
                            ValidateByRefType(leakNode.typeNode, leakNode);

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
                        if(gtype.IsRawReferenceType == false)
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

                        ValidateByRefType(funcDeclNode.returnTypeNode, funcDeclNode.returnTypeNode);

                        if(IsDeclaredByRef(funcDeclNode.returnTypeNode))
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, funcDeclNode.returnTypeNode, "ref return type is not supported.");

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

                        ValidateByRefType(externFuncDeclNode.returnTypeNode, externFuncDeclNode.returnTypeNode);

                        if(IsDeclaredByRef(externFuncDeclNode.returnTypeNode))
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, externFuncDeclNode.returnTypeNode, "ref return type is not supported.");


                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.ParameterNode paramNode:
                    {
                        ValidateByRefType(paramNode.typeNode, paramNode);
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

                        // 当前语法不支持在构造函数中显式指定 base(...)
                        // 简化规则：若基类没有默认构造（通常表示基类显式构造集合），
                        // 则派生类必须显式定义并且其构造签名集合与基类一致。
                        if(classdeclNode.baseClassNameNode != null)
                        {
                            string baseClassName = classdeclNode.baseClassNameNode.FullName;
                            var baseClassRec = Query(baseClassName);
                            if(baseClassRec == null || baseClassRec.envPtr == null)
                                throw new SemanticException(ExceptioName.BaseClassNotFound, classdeclNode.baseClassNameNode, baseClassName);

                            var baseCtorSigs = GetCtorSignatureSet(baseClassRec.envPtr);
                            bool baseHasDefaultCtor = baseCtorSigs.Contains(string.Empty);
                            if(baseHasDefaultCtor == false)
                            {
                                var derivedCtorSigs = new HashSet<string>();
                                foreach(var decl in classdeclNode.memberDelareNodes)
                                {
                                    if(decl is not FuncDeclareNode fn)
                                        continue;
                                    if(IsCtorIdentifier(fn.identifierNode) == false)
                                        continue;

                                    derivedCtorSigs.Add(
                                        BuildCtorSignatureKeyByParamTypes(fn.parametersNode.parameterNodes.Select(p => p.typeNode.TypeExpression()))
                                    );
                                }

                                if(derivedCtorSigs.Count == 0)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, classdeclNode, "base class has no default constructor; derived class must define matching constructors.");

                                if(baseCtorSigs.SetEquals(derivedCtorSigs) == false)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, classdeclNode, "derived constructor signatures must match base constructor signatures.");
                            }
                        }

                        //离开作用域  
                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.StructDeclareNode structDeclNode:
                    {
                        envStack.Push(structDeclNode.attributes[AstAttr.env] as SymbolTable);

                        foreach(var declNode in structDeclNode.memberDelareNodes)
                        {
                            if(declNode is not SyntaxTree.VarDeclareNode)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, declNode, "struct only allows field variable declarations.");

                            Pass3_AnalysisNode(declNode);
                        }

                        envStack.Pop();
                    }
                    break;
                case SyntaxTree.EnumDeclareNode enumDeclNode:
                    {
                        envStack.Push(enumDeclNode.attributes[AstAttr.env] as SymbolTable);
                        HashSet<int> values = new();
                        foreach(var memberNode in enumDeclNode.memberNodes)
                        {
                            int val = ParseEnumMemberValue(memberNode);
                            if(values.Add(val) == false)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, memberNode, "duplicate enum member value.");

                            var rec = memberNode.attributes[AstAttr.const_rec] as SymbolTable.Record;
                            rec.initValue = val.ToString();
                        }

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
                            //检查条件节点  
                            Pass3_AnalysisNode(clause.conditionNode);
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
                case SyntaxTree.SwitchStmtNode switchNode:
                    {
                        Pass3_AnalysisNode(switchNode.conditionNode);

                        var switchTypeExpr = AnalyzeTypeExpression(switchNode.conditionNode);
                        if(IsSupportedSwitchType(switchTypeExpr) == false)
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, switchNode, "switch only supports bool/char/integer primitive types.");

                        int defaultCount = 0;
                        var caseValueSet = new HashSet<string>();

                        foreach(var caseNode in switchNode.caseNodes)
                        {
                            if(caseNode.isDefault)
                            {
                                defaultCount++;
                                if(defaultCount > 1)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, caseNode, "switch can only contain one default clause.");
                            }
                            else
                            {
                                Pass3_AnalysisNode(caseNode.valueNode);

                                var caseTypeExpr = AnalyzeTypeExpression(caseNode.valueNode);
                                if(TryGetSwitchCaseConstantKey(caseNode.valueNode, out var caseKey) == false)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, caseNode, "switch case must be a compile-time literal.");

                                if(CheckType_Equal(switchTypeExpr, caseTypeExpr) == false)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, caseNode, $"switch case type mismatch: switch={switchTypeExpr}, case={caseTypeExpr}");

                                if(caseValueSet.Add(caseKey) == false)
                                    throw new SemanticException(ExceptioName.SemanticAnalysysError, caseNode, "duplicate switch case value.");
                            }

                            Pass3_AnalysisNode(caseNode.statementsNode);
                        }
                    }
                    break;
                case SyntaxTree.ReturnStmtNode retNode:
                    {
                        //检查返回值  
                        if(retNode.returnExprNode != null)
                        { Pass3_AnalysisNode(retNode.returnExprNode); }
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
                        TryCompleteType(defaultValNode.typeNode);

                        var typeExpr = defaultValNode.typeNode.TypeExpression();
                        var gtype = GType.Parse(typeExpr);

                        if(gtype.IsStructType)
                        {
                            var initNode = GenDefaultBraceInitializerNode(defaultValNode.typeNode);
                            defaultValNode.overrideNode = initNode;
                            defaultValNode.overrideNode.Parent = defaultValNode.Parent;

                            AnalyzeBraceInitializerType(initNode, typeExpr, defaultValNode);
                        }
                        else
                        {
                            var litnode = GenDefaultLitNode(defaultValNode.typeNode);
                            defaultValNode.overrideNode = litnode;
                            defaultValNode.overrideNode.Parent = defaultValNode.Parent;

                            AnalyzeTypeExpression(litnode);
                        }
                    }
                    break;
                case SyntaxTree.BraceInitializerNode braceInitializerNode:
                    {
                        if(braceInitializerNode.attributes != null
                            && braceInitializerNode.attributes.TryGetValue(AstAttr.type, out var targetTypeObj)
                            && targetTypeObj is string targetTypeExpr
                            && string.IsNullOrWhiteSpace(targetTypeExpr) == false)
                        {
                            AnalyzeBraceInitializerType(braceInitializerNode, targetTypeExpr, braceInitializerNode);
                            break;
                        }

                        throw new SemanticException(ExceptioName.SemanticAnalysysError, braceInitializerNode, "brace initializer requires a known target type.");
                    }
                    break;
                case SyntaxTree.TernaryConditionNode ternaryNode:
                    {
                        Pass3_AnalysisNode(ternaryNode.conditionNode);
                        Pass3_AnalysisNode(ternaryNode.trueNode);
                        Pass3_AnalysisNode(ternaryNode.falseNode);

                        if(CheckType_Equal("bool", ternaryNode.conditionNode) == false)
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, ternaryNode, "ternary condition must be bool.");

                        if(CheckType_Equal(ternaryNode.trueNode, ternaryNode.falseNode) == false)
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, ternaryNode, "ternary branch type mismatch.");

                        AnalyzeTypeExpression(ternaryNode);
                    }
                    break;
                case SyntaxTree.UnaryOpNode unaryNode:
                    {
                        Pass3_AnalysisNode(unaryNode.exprNode);

                        if(TryFoldUnaryConstant(unaryNode, out var foldedUnaryNode))
                        {
                            unaryNode.overrideNode = foldedUnaryNode;
                            unaryNode.overrideNode.Parent = unaryNode.Parent;
                            Pass3_AnalysisNode(foldedUnaryNode);
                            break;
                        }

                        AnalyzeTypeExpression(unaryNode);
                    }
                    break;
                case SyntaxTree.BinaryOpNode binaryNode:
                    {
                        Pass3_AnalysisNode(binaryNode.leftNode);
                        Pass3_AnalysisNode(binaryNode.rightNode);

                        var effectiveLeftNode = GetEffectiveExprNode(binaryNode.leftNode);
                        var effectiveRightNode = GetEffectiveExprNode(binaryNode.rightNode);

                        string typeExprL = AnalyzeTypeExpression(effectiveLeftNode);
                        string typeExprR = AnalyzeTypeExpression(effectiveRightNode);
                        var typeL = GType.Parse(typeExprL);
                        var typeR = GType.Parse(typeExprR);

                        // !! 操作符重载
                        if(typeL.CanOverrideOperator || typeR.CanOverrideOperator)
                        {
                            var operatorRec = Query_OperatorOverload(binaryNode.GetOpName(), typeExprL, typeExprR);
                            if(operatorRec != null)
                            {
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

                                break;
                            }
                        }

                        if(TryFoldBinaryConstant(binaryNode, out var foldedBinaryNode))
                        {
                            binaryNode.overrideNode = foldedBinaryNode;
                            binaryNode.overrideNode.Parent = binaryNode.Parent;
                            Pass3_AnalysisNode(foldedBinaryNode);
                            break;
                        }

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
                        // !!特殊的转换需要重写为函数调用
                        TryCompleteType(castNode.typeNode);
                        Pass3_AnalysisNode(castNode.factorNode);
                        var effectiveFactorNode = GetEffectiveExprNode(castNode.factorNode);
                        AnalyzeTypeExpression(effectiveFactorNode);
                        AnalyzeTypeExpression(castNode);

                        var srcType = GType.Parse((string)effectiveFactorNode.attributes[AstAttr.type]);
                        var targetType = GType.Parse(castNode.typeNode.TypeExpression());

                        //非string类型 -> string类型
                        if(targetType.Category == GType.Kind.String && srcType.Category != GType.Kind.String)
                        {
                            //基元类型转string   
                            if(srcType.IsPrimitive)
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

                            //类对象转string -> 调用ToString成员函数  
                            else if(srcType.Category == GType.Kind.Object)
                            {
                                var overrideNode = new SyntaxTree.CallNode()
                                {
                                    isMemberAccessFunction = true,
                                    funcNode = new SyntaxTree.ObjectMemberAccessNode()
                                    {
                                        attributes = new Dictionary<AstAttr, object>(),
                                        memberNode = new IdentityNode()
                                        {
                                            token = new Token("ID", PatternType.Id, "ToString", default, default, default),
                                            attributes = new Dictionary<AstAttr, object>(),
                                            identiferType = IdentityNode.IdType.VariableOrField,
                                        },
                                        objectNode = castNode.factorNode,
                                    },
                                    argumantsNode = new SyntaxTree.ArgumentListNode()
                                    { },
                                    attributes = new(),
                                };
                                castNode.overrideNode = overrideNode;
                                castNode.overrideNode.Parent = castNode.Parent;
                                castNode.Parent.ReplaceChild(castNode, castNode.overrideNode);
                                Pass3_AnalysisNode(castNode.overrideNode);
                                break;
                            }
                            //结构体或者枚举 -> string （暂不支持）
                            else if(srcType.IsStructType || srcType.IsEnumType)
                            {
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, castNode, "cast struct or enum to string is not supported.");
                            }
                        }
                        //string类型 -> string类型
                        else if(targetType.Category == GType.Kind.String && srcType.Category == GType.Kind.String)
                        {
                            castNode.overrideNode = castNode.factorNode;
                            castNode.overrideNode.Parent = castNode.Parent;
                            castNode.Parent.ReplaceChild(castNode, castNode.overrideNode);
                            Pass3_AnalysisNode(castNode.overrideNode);
                        }
                        //不支持的转换  
                        else if(targetType.Category == GType.Kind.Array)
                        {
                            throw new SemanticException(ExceptioName.SemanticAnalysysError, castNode, "cast to array not support.");
                        }

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

                        SymbolTable.Record resolvedFuncRec = null;
                        if(callNode.attributes.TryGetValue(AstAttr.func_rec, out var funcRecObj) && funcRecObj is SymbolTable.Record funcRec && funcRec.envPtr != null)
                        {
                            resolvedFuncRec = funcRec;
                        }
                        else if(callNode.isMemberAccessFunction)
                        {
                            if(callNode.funcNode is ObjectMemberAccessNode memberAccess
                                && callNode.attributes.TryGetValue(AstAttr.mangled_name, out var mangledObj)
                                && mangledObj is string mangledName)
                            {
                                var objTypeExpr = GetValueTypeExpression((string)memberAccess.objectNode.attributes[AstAttr.type]);
                                var classRec = Query(GType.Normalize(objTypeExpr));
                                resolvedFuncRec = classRec?.envPtr?.Class_GetMemberRecordInChain(mangledName);
                            }
                        }
                        else if(callNode.attributes.TryGetValue(AstAttr.mangled_name, out var directMangledObj) && directMangledObj is string directMangledName)
                        {
                            resolvedFuncRec = Query(directMangledName);
                        }
                        else if(callNode.attributes.TryGetValue(AstAttr.extern_name, out var externObj) && externObj is string externName)
                        {
                            resolvedFuncRec = Query(externName);
                        }

                        if(resolvedFuncRec != null && resolvedFuncRec.envPtr != null)
                        {
                            callNode.attributes[AstAttr.func_rec] = resolvedFuncRec;

                            var paramTypeExprs = (resolvedFuncRec.envPtr.GetByCategory(SymbolTable.RecordCatagory.Param) ?? new List<SymbolTable.Record>())
                                .Where(p => p.name != "this")
                                .Select(p => p.typeExpression)
                                .ToList();
                            ValidateCallByRefArguments(callNode, paramTypeExprs);
                        }
                        else if(callNode.isMemberAccessFunction == false)
                        {
                            var calleeType = GType.Parse((string)callNode.funcNode.attributes[AstAttr.type]);
                            if(calleeType.IsFunction)
                            {
                                ValidateCallByRefArguments(callNode, calleeType.FunctionParamTypes.Select(p => p.ToString()).ToList());
                            }
                        }

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
                        //禁止枚举成员赋值 
                        if(assignNode.lvalueNode is SyntaxTree.EnumAccessNode)
                            throw new SemanticException(ExceptioName.AssignmentTypeError, assignNode, "enum member is not assignable.");

                        //!!setter属性替换  
                        if(assignNode.lvalueNode is SyntaxTree.ObjectMemberAccessNode)
                        {
                            var memberAccess = assignNode.lvalueNode as SyntaxTree.ObjectMemberAccessNode;

                            var className = GType.Normalize(AnalyzeTypeExpression(memberAccess.objectNode));

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

                        var objectTypeExpr = GetValueTypeExpression(AnalyzeTypeExpression(objMemberAccessNode.objectNode));
                        var objectType = GType.Parse(objectTypeExpr);

                        var className = objectType.NormTypeName;
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
                case SyntaxTree.EnumAccessNode enumAccessNode:
                    {
                        TryCompleteIdenfier(enumAccessNode.enumTypeNode);
                        AnalyzeTypeExpression(enumAccessNode);
                    }
                    break;
                case SyntaxTree.ThisNode thisObjNode:
                    {
                        AnalyzeTypeExpression(thisObjNode);
                    }
                    break;
                case SyntaxTree.SizeOfNode sizeofNode:
                    {
                        TryCompleteType(sizeofNode.typeNode);
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

                        foreach(var arg in newobjNode.argumantsNode.arguments)
                        {
                            Pass3_AnalysisNode(arg);
                        }

                        string className = newobjNode.className.FullName;
                        var classRec = Query(className);
                        if(classRec == null || classRec.envPtr == null)
                            throw new SemanticException(ExceptioName.ClassDefinitionNotFound, newobjNode.className, className);

                        var argTypes = newobjNode.argumantsNode.arguments.Select(a => AnalyzeTypeExpression(a)).ToArray();
                        var matchedParamTypes = new string[argTypes.Length];
                        bool ctorMatched = TryQueryAndMatchFunction("ctor", argTypes, matchedParamTypes, true, classRec.envPtr);
                        if(ctorMatched == false)
                            throw new SemanticException(ExceptioName.FunctionMemberNotFound, newobjNode, "ctor");

                        newobjNode.attributes[AstAttr.mangled_name] = BuildCtorFunctionFullName(className, matchedParamTypes);
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
                case SyntaxTree.SwitchStmtNode switchNode:
                    {
                        Pass4_OwnershipLifetime(switchNode.conditionNode);

                        foreach(var caseNode in switchNode.caseNodes)
                        {
                            if(caseNode.isDefault == false)
                                Pass4_OwnershipLifetime(caseNode.valueNode);
                        }

                        var saved = lifeTimeInfo.currBranch;
                        List<LifetimeInfo.Branch> branches = new();

                        for(int i = 0; i < switchNode.caseNodes.Count; ++i)
                        {
                            var branch = lifeTimeInfo.NewBranch(saved);
                            lifeTimeInfo.currBranch = branch;

                            for(int j = i; j < switchNode.caseNodes.Count; ++j)
                            {
                                Pass4_OwnershipLifetime(switchNode.caseNodes[j].statementsNode);

                                if(SwitchCaseFallsThrough(switchNode.caseNodes[j]) == false)
                                    break;
                            }

                            branches.Add(branch);
                        }

                        if(switchNode.caseNodes.Any(c => c.isDefault) == false)
                        {
                            branches.Add(lifeTimeInfo.NewBranch(saved));
                        }

                        lifeTimeInfo.currBranch = saved;
                        if(branches.Count > 0)
                        {
                            lifeTimeInfo.MergeBranchesTo(saved, branches);
                        }

                        break;
                    }
                case SyntaxTree.TernaryConditionNode ternaryNode:
                    {
                        Pass4_OwnershipLifetime(ternaryNode.conditionNode);

                        var saved = lifeTimeInfo.currBranch;
                        List<LifetimeInfo.Branch> branches = new();

                        var trueBranch = lifeTimeInfo.NewBranch(saved);
                        lifeTimeInfo.currBranch = trueBranch;
                        Pass4_OwnershipLifetime(ternaryNode.trueNode);
                        branches.Add(trueBranch);

                        var falseBranch = lifeTimeInfo.NewBranch(saved);
                        lifeTimeInfo.currBranch = falseBranch;
                        Pass4_OwnershipLifetime(ternaryNode.falseNode);
                        branches.Add(falseBranch);

                        lifeTimeInfo.currBranch = saved;
                        lifeTimeInfo.MergeBranchesTo(saved, branches);
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
                case SyntaxTree.StructDeclareNode structDecl:
                    {
                        envStack.Push(structDecl.attributes[AstAttr.env] as SymbolTable);

                        foreach(var decl in structDecl.memberDelareNodes)
                        {
                            if(decl is not VarDeclareNode fvar)
                                throw new SemanticException(ExceptioName.SemanticAnalysysError, decl, "struct only allows field variable declarations.");

                            if(fvar.flags.HasFlag(VarModifiers.Own) || fvar.flags.HasFlag(VarModifiers.Bor))
                                throw new SemanticException(ExceptioName.OwnershipError, fvar, "struct field cannot use own/bor.");

                            var ftype = GType.Parse(fvar.typeNode.TypeExpression());
                            if(ftype.IsClassType)
                            {
                                var classRec = Query(ftype.ObjectTypeName);
                                if(classRec != null && classRec.flags.HasFlag(SymbolTable.RecordFlag.OwnershipClass))
                                    throw new SemanticException(ExceptioName.OwnershipError, fvar, "struct field cannot be ownership class type.");
                            }
                        }

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
                        var recType = GType.Parse(rec.typeExpression);
                        if(recType.IsRawReferenceType == false)
                            break;

                        // 函数指针不用处理所有权  
                        if(recType.IsFunction)
                        {
                            TryBindFunctionPointerOwnership(rec, varDecl.initializerNode);
                            break;
                        }


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
                            var lrecType = GType.Parse(lrec.typeExpression);
                            if(lrecType.IsRawReferenceType == false)
                                break;
                            // 函数指针不用处理所有权
                            if(lrecType.IsFunction)
                            {
                                TryBindFunctionPointerOwnership(lrec, assignNode.rvalueNode);
                                break;
                            }


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
                            var lrecType = GType.Parse(lrec.typeExpression);
                            if(lrecType.IsRawReferenceType == false)
                                break;
                            // 函数指针不用处理所有权
                            if(lrecType.IsFunction)
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
                            var callRetModel = GetCallReturnOwnershipModel(cnode);

                            if(callRetModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
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

                        if(GType.Parse(fieldRec.typeExpression).IsRawReferenceType)
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
                     

                        // *** 获取函数的符号表记录（普通调用）或函数指针签名（指针调用） ***
                        SymbolTable.Record funcRec = null;
                        FunctionOwnershipSignature funcPtrSig = null;

                        if(callNode.isMemberAccessFunction == false)
                        {
                            if(callNode.attributes.ContainsKey(AstAttr.mangled_name))
                            {
                                funcRec = Query((string)callNode.attributes[AstAttr.mangled_name]);
                            }
                            else if(callNode.attributes.ContainsKey(AstAttr.extern_name))
                            {
                                funcRec = Query((string)callNode.attributes[AstAttr.extern_name]);
                            }
                            else
                            {
                                funcPtrSig = ResolveFunctionOwnershipSignatureFromExpr(callNode.funcNode);
                                if(funcPtrSig == null)
                                {
                                    var funcTypeExpr = AnalyzeTypeExpression(callNode.funcNode);
                                    funcPtrSig = BuildConservativeFunctionOwnershipSignatureFromType(funcTypeExpr);
                                }

                                if(funcPtrSig == null)
                                    throw new GizboxException(ExceptioName.Undefine, "function pointer ownership signature not found.");

                                callNode.attributes[AstAttr.func_ptr_ownsig] = funcPtrSig;
                            }
                        }
                        else
                        {
                            if(callNode.funcNode is ObjectMemberAccessNode memberAccNode)
                            {
                                var objtype = GType.Normalize((string)memberAccNode.objectNode.attributes[AstAttr.type]);
                                var classRec = Query(objtype);
                                var memfuncRec = classRec.envPtr.Class_GetMemberRecordInChain((string)callNode.attributes[AstAttr.mangled_name]);
                                funcRec = memfuncRec;
                            }
                        }

                        if(funcRec != null)
                        {
                            callNode.attributes[AstAttr.func_rec] = funcRec;

                            if(funcRec.envPtr == null)
                                throw new GizboxException(ExceptioName.Undefine, "func rec not exist.");

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
                                var pflag = pr.flags & OwnershipModelMask;
                                var arg = callNode.argumantsNode.arguments[i];
                                var type = GType.Parse(pr.typeExpression);

                                // 值类型参数不用处理所有权语义
                                if(type.IsRawReferenceType == false)
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
                        }
                        else
                        {
                            if(funcPtrSig == null)
                                throw new GizboxException(ExceptioName.Undefine, "funcrec not found.");

                            for(int i = 0; i < callNode.argumantsNode.arguments.Count; ++i)
                            {
                                if(i >= funcPtrSig.ParamModels.Count)
                                    break;

                                var arg = callNode.argumantsNode.arguments[i];
                                var argTypeExpr = AnalyzeTypeExpression(arg);
                                if(GType.Parse(argTypeExpr).IsRawReferenceType == false)
                                    continue;

                                var paramModel = funcPtrSig.ParamModels[i] & OwnershipModelMask;
                                CheckOwnershipCompare_Core(arg, paramModel, $"arg{i}", arg, out var argModel);

                                if(paramModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                {
                                    CheckOwnershipCanMoveOut(arg);

                                    if(arg is SyntaxTree.IdentityNode argIdNode && argModel.HasFlag(SymbolTable.RecordFlag.OwnerVar))
                                    {
                                        var argRec = Query(argIdNode.FullName);
                                        lifeTimeInfo.currBranch.SetVarStatus(argRec.name, LifetimeInfo.VarStatus.Dead);

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



        private bool IsSupportedSwitchType(string typeExpr)
        {
            //switch-case支持枚举值  
            if(GType.Parse(typeExpr).IsEnumType)
                return true;

            //switch-case支持整型   
            switch(NormalizeTypeExprForTypeCheck(typeExpr))
            {
                case "bool":
                case "byte":
                case "char":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                    return true;
                default:
                    return false;
            }
        }


        private bool TryGetSwitchCaseConstantKey(SyntaxTree.ExprNode exprNode, out string caseKey)
        {
            if(exprNode == null)
            { 
                caseKey = null;
                return false;
            }
            if(exprNode.overrideNode is SyntaxTree.ExprNode overrideExpr)
                return TryGetSwitchCaseConstantKey(overrideExpr, out caseKey);

            if(exprNode is SyntaxTree.LiteralNode literalNode)
            {
                caseKey = NormalizeTypeExprForTypeCheck(AnalyzeTypeExpression(literalNode)) + ":" + literalNode.token.attribute;
                return true;
            }

            //枚举的switch-case-key  
            if(exprNode is SyntaxTree.EnumAccessNode accessNode
                && accessNode.attributes.TryGetValue(AstAttr.enum_rec, out var enumRecObj)
                && enumRecObj is SymbolTable.Record enumRec
                && enumRec.envPtr != null)
            {
                var memberRec = enumRec.envPtr.records.Values.FirstOrDefault(r => r.rawname == accessNode.memberNode.FullName);
                if(memberRec != null)
                {
                    caseKey = NormalizeTypeExprForTypeCheck(AnalyzeTypeExpression(accessNode)) + ":" + memberRec.initValue;
                    return true;
                }
            }

            caseKey = null;
            return false;
        }

        private bool SwitchCaseFallsThrough(SyntaxTree.SwitchCaseNode caseNode)
        {
            if(caseNode?.statementsNode == null || caseNode.statementsNode.statements.Count == 0)
                return true;

            return caseNode.statementsNode.statements[caseNode.statementsNode.statements.Count - 1] is not SyntaxTree.BreakStmtNode;
        }

        private void CollectAllUsingNamespacePrefix()
        {
            availableNamespacePrefixList.Clear();
            foreach(var usingnamespace in this.ast.rootNode.usingNamespaceNodes)
            {
                availableNamespacePrefixList.Add($"{usingnamespace.namespaceNameNode.FullName}::");
            }
        }

        private static bool IsCtorIdentifier(SyntaxTree.IdentityNode id)
        {
            return id?.token?.attribute == "ctor";
        }

        private static bool IsDtorIdentifier(SyntaxTree.IdentityNode id)
        {
            return id?.token?.attribute == "dtor";
        }

        private static string GetClassShortName(string classFullName)
        {
            if(string.IsNullOrEmpty(classFullName))
                return classFullName;

            int p = classFullName.LastIndexOf("::", StringComparison.Ordinal);
            string shortName = p >= 0 ? classFullName.Substring(p + 2) : classFullName;
            int g = shortName.IndexOf('^');
            if(g >= 0)
                shortName = shortName.Substring(0, g);
            return shortName;
        }

        private static string BuildCtorFunctionFullName(string classFullName, params string[] paramTypes)
        {
            return Utils.Mangle(classFullName + "::ctor", paramTypes ?? Array.Empty<string>());
        }

        private static string BuildDtorFunctionFullName(string classFullName)
        {
            return classFullName + "::dtor";
        }

        private static string BuildCtorSignatureKeyByParamTypes(IEnumerable<string> paramTypes)
        {
            return string.Join(",", paramTypes ?? Array.Empty<string>());
        }

        private static string BuildCtorSignatureKeyFromRecord(SymbolTable.Record ctorRec)
        {
            if(ctorRec?.envPtr == null)
                return string.Empty;

            var ps = ctorRec.envPtr.GetByCategory(SymbolTable.RecordCatagory.Param)
                ?.Where(p => p.name != "this")
                .Select(p => p.typeExpression)
                .ToArray()
                ?? Array.Empty<string>();

            return BuildCtorSignatureKeyByParamTypes(ps);
        }

        private static HashSet<string> GetCtorSignatureSet(SymbolTable classEnv)
        {
            var set = new HashSet<string>();
            if(classEnv == null)
                return set;

            foreach(var kv in classEnv.records)
            {
                var rec = kv.Value;
                if(rec.category != SymbolTable.RecordCatagory.Function)
                    continue;
                if(rec.flags.HasFlag(SymbolTable.RecordFlag.Ctor) == false)
                    continue;

                set.Add(BuildCtorSignatureKeyFromRecord(rec));
            }

            return set;
        }

        private SymbolTable.RecordFlag GetCallReturnOwnershipModel(SyntaxTree.CallNode callNode)
        {
            if(callNode.attributes.TryGetValue(AstAttr.func_rec, out var funcRecObj) && funcRecObj is SymbolTable.Record funcRec)
            {
                return funcRec.flags & OwnershipModelMask;
            }

            if(callNode.attributes.TryGetValue(AstAttr.func_ptr_ownsig, out var sigObj) && sigObj is FunctionOwnershipSignature sig)
            {
                return sig.ReturnModel & OwnershipModelMask;
            }

            return SymbolTable.RecordFlag.None;
        }

        private FunctionOwnershipSignature BuildFunctionOwnershipSignatureFromFunctionRecord(SymbolTable.Record funcRec)
        {
            if(funcRec == null || funcRec.envPtr == null)
                return null;

            var sig = new FunctionOwnershipSignature();
            sig.ReturnModel = funcRec.flags & OwnershipModelMask;

            var allParams = funcRec.envPtr.GetByCategory(SymbolTable.RecordCatagory.Param) ?? new List<SymbolTable.Record>();
            foreach(var p in allParams)
            {
                if(p.name == "this")
                    continue;

                var model = p.flags & OwnershipModelMask;
                if(model == SymbolTable.RecordFlag.None)
                {
                    var pType = GType.Parse(p.typeExpression);
                    if(pType.IsClassType)
                    {
                        model = SymbolTable.RecordFlag.ManualVar;
                    }
                }

                sig.ParamModels.Add(model);
            }

            return sig;
        }

        private FunctionOwnershipSignature BuildConservativeFunctionOwnershipSignatureFromType(string funcTypeExpr)
        {
            var ftype = GType.Parse(funcTypeExpr);
            if(ftype.Category != GType.Kind.Function)
                return null;

            var sig = new FunctionOwnershipSignature();
            if(ftype.FunctionReturnType.IsClassType)
            {
                sig.ReturnModel = ftype.FunctionReturnType.OwnershipHint switch
                {
                    GType.OwnershipHintKind.Own => SymbolTable.RecordFlag.OwnerVar,
                    GType.OwnershipHintKind.Borrow => SymbolTable.RecordFlag.BorrowVar,
                    _ => SymbolTable.RecordFlag.ManualVar,
                };
            }
            else
            {
                sig.ReturnModel = SymbolTable.RecordFlag.None;
            }

            var paramTypes = ftype.FunctionParamTypes ?? new List<GType>();
            foreach(var p in paramTypes)
            {
                if(p.IsClassType)
                {
                    sig.ParamModels.Add(p.OwnershipHint switch
                    {
                        GType.OwnershipHintKind.Own => SymbolTable.RecordFlag.OwnerVar,
                        GType.OwnershipHintKind.Borrow => SymbolTable.RecordFlag.BorrowVar,
                        _ => SymbolTable.RecordFlag.ManualVar,
                    });
                }
                else
                {
                    sig.ParamModels.Add(SymbolTable.RecordFlag.None);
                }
            }

            return sig;
        }

        private FunctionOwnershipSignature ResolveFunctionOwnershipSignatureFromExpr(SyntaxTree.ExprNode expr)
        {
            if(expr == null)
                return null;

            if(expr is SyntaxTree.CastNode cast)
            {
                return ResolveFunctionOwnershipSignatureFromExpr(cast.factorNode);
            }

            if(expr is SyntaxTree.IdentityNode id)
            {
                var rec = Query(id.FullName) ?? Query_IgnoreMangle(id.FullName);
                if(rec == null)
                    return null;

                if(rec.category == SymbolTable.RecordCatagory.Function)
                {
                    return BuildFunctionOwnershipSignatureFromFunctionRecord(rec);
                }

                if(GType.Parse(rec.typeExpression).Category == GType.Kind.Function)
                {
                    if(funcPtrOwnershipSignatures.TryGetValue(rec, out var saved))
                        return saved.Clone();

                    return BuildConservativeFunctionOwnershipSignatureFromType(rec.typeExpression);
                }
            }

            return null;
        }

        private void TryBindFunctionPointerOwnership(SymbolTable.Record targetFuncPtr, SyntaxTree.ExprNode source)
        {
            if(targetFuncPtr == null)
                return;

            if(GType.Parse(targetFuncPtr.typeExpression).Category != GType.Kind.Function)
                return;

            var sig = ResolveFunctionOwnershipSignatureFromExpr(source)
                ?? BuildConservativeFunctionOwnershipSignatureFromType(targetFuncPtr.typeExpression);

            if(sig == null)
            {
                funcPtrOwnershipSignatures.Remove(targetFuncPtr);
            }
            else
            {
                funcPtrOwnershipSignatures[targetFuncPtr] = sig.Clone();
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
                rModel = GetCallReturnOwnershipModel(callNode);

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

            else if(rNode is TernaryConditionNode ternaryNode)
            {
                CheckOwnershipCompare_Core(errorNode, lModel, lname, ternaryNode.trueNode, out var trueModel);
                CheckOwnershipCompare_Core(errorNode, lModel, lname, ternaryNode.falseNode, out var falseModel);

                if(trueModel != falseModel)
                    throw new SemanticException(ExceptioName.OwnershipError, errorNode, "ternary branch ownership mismatch.");

                rModel = trueModel;
                return;
            }

            //临时右值 - Binary/Unary表达式
            else if(rNode is BinaryOpNode || rNode is UnaryOpNode)
            {
                //Pass阶段所有二元一元运算目前都只针对基元值类型。其他类型的运算都被重载为函数调用。    
                rModel = SymbolTable.RecordFlag.None;
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
                return;
            }

            if(rvalNode is SyntaxTree.TernaryConditionNode ternaryNode)
            {
                CheckOwnershipCanMoveOut(ternaryNode.trueNode);
                CheckOwnershipCanMoveOut(ternaryNode.falseNode);
            }
        }

        /// <summary> 判断表达式是否可作为引用绑定目标（必须是可寻址左值）</summary>
        private bool IsRefBindable(SyntaxTree.ExprNode exprNode)
        {
            return exprNode is IdentityNode
                || exprNode is ObjectMemberAccessNode
                || exprNode is ElementAccessNode;
        }

        /// <summary> 将引用绑定目标对应的基对象标记为地址暴露  </summary>
        private void MarkRefBindAddressable(SyntaxTree.ExprNode exprNode)
        {
            switch(exprNode)
            {
                case IdentityNode idNode:
                    {
                        var rec = Query(idNode.FullName);
                        if(rec != null && (rec.category == SymbolTable.RecordCatagory.Variable || rec.category == SymbolTable.RecordCatagory.Param))
                        {
                            var recType = GType.Parse(rec.typeExpression);
                            if(recType.IsRefType == false)
                                rec.flags |= SymbolTable.RecordFlag.Addressable;
                        }
                    }
                    break;
                case ObjectMemberAccessNode accessNode:
                    MarkRefBindAddressable(accessNode.objectNode);
                    break;
                case ElementAccessNode elementAccessNode:
                    MarkRefBindAddressable(elementAccessNode.containerNode);
                    break;
            }
        }


        /// <summary> ByRef声明类型 </summary>
        private bool IsDeclaredByRef(SyntaxTree.TypeNode typeNode)
        {
            var type = GType.Parse((typeNode).TypeExpression());
            return type.IsRefType;
        }

        /// <summary> 检查ByRef类型的合法性 </summary>
        private void ValidateByRefType(SyntaxTree.TypeNode typeNode, SyntaxTree.Node errorNode)
        {
            if(typeNode == null)
                return;

            var type = GType.Parse(typeNode.TypeExpression());
            if(type.IsRefType && type.RefTargetType.IsClassType)
                throw new SemanticException(ExceptioName.SemanticAnalysysError, errorNode, "class type does not support by-reference declaration.");
        }


        /// <summary> 校验引用绑定 </summary>
        private void ValidateRefBinding(SyntaxTree.Node errorNode, string declaredTypeExpr, SyntaxTree.ExprNode exprNode)
        {
            var declaredType = GType.Parse(declaredTypeExpr);
            if(declaredType.IsRefType == false)
                return;

            if(IsRefBindable(exprNode) == false)
                throw new SemanticException(ExceptioName.SemanticAnalysysError, errorNode, "reference binding requires an lvalue argument.");

            string valueTypeExpr = AnalyzeTypeExpression(exprNode);
            if(CheckType_Equal(declaredType.RefTargetType.ToString(), valueTypeExpr) == false)
                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, errorNode, $"reference target type:{declaredType.RefTargetType}  value type:{valueTypeExpr}");

            MarkRefBindAddressable(exprNode);
        }

        private List<SymbolTable.Record> GetStructFieldRecordsInOrder(string structTypeExpr)
        {
            var structRec = Query(structTypeExpr);
            if(structRec == null || structRec.category != SymbolTable.RecordCatagory.Struct || structRec.envPtr == null)
                throw new GizboxException(ExceptioName.SemanticAnalysysError, $"struct definition not found: {structTypeExpr}");

            return structRec.envPtr.records.Values
                .Where(r => r.category == SymbolTable.RecordCatagory.Variable)
                .OrderBy(r => r.addr)
                .ToList();
        }

        private SyntaxTree.StructDeclareNode FindStructDeclareNode(string structTypeExpr)
        {
            var structName = GType.Normalize(structTypeExpr);

            SyntaxTree.StructDeclareNode FindInNode(SyntaxTree.Node node)
            {
                if(node == null)
                    return null;

                if(node is SyntaxTree.StructDeclareNode structDeclNode
                    && structDeclNode.structNameNode?.FullName == structName)
                {
                    return structDeclNode;
                }

                foreach(var child in node.Children())
                {
                    var found = FindInNode(child);
                    if(found != null)
                        return found;
                }

                return null;
            }

            var currentUnitStruct = FindInNode(ast?.rootNode);
            if(currentUnitStruct != null)
                return currentUnitStruct;

            foreach(var lib in ilUnit.dependencyLibs)
            {
                var depStruct = FindInNode(lib?.astRoot);
                if(depStruct != null)
                    return depStruct;
            }

            return null;
        }

        /// <summary> 查找并返回指定结构体在AST中按声明顺序的字段声明节点列表 </summary>
        private List<SyntaxTree.VarDeclareNode> GetStructFieldDeclsInOrder(string structTypeExpr)
        {
            var structDeclNode = FindStructDeclareNode(structTypeExpr);
            if(structDeclNode == null)
                throw new GizboxException(ExceptioName.SemanticAnalysysError, $"struct AST definition not found: {structTypeExpr}");

            var result = new List<SyntaxTree.VarDeclareNode>();
            foreach(var declNode in structDeclNode.memberDelareNodes)
            {
                if(declNode is SyntaxTree.VarDeclareNode fieldDecl)
                    result.Add(fieldDecl);
            }

            return result;
        }

        /// <summary> 克隆结构体字段声明中指定的初始化表达式 </summary>
        private SyntaxTree.ExprNode CloneStructFieldInitializer(SyntaxTree.VarDeclareNode fieldDecl, SyntaxTree.Node parentNode)
        {
            if(fieldDecl?.initializerNode == null)
                throw new GizboxException(ExceptioName.SemanticAnalysysError, $"struct field initializer not found: {fieldDecl?.identifierNode?.FullName ?? "?"}");

            if(fieldDecl.initializerNode.DeepClone() is not SyntaxTree.ExprNode clonedExpr)
                throw new GizboxException(ExceptioName.SemanticAnalysysError, $"struct field initializer clone failed: {fieldDecl.identifierNode?.FullName ?? "?"}");

            clonedExpr.Parent = parentNode;
            return clonedExpr;
        }

        //分析并补全结构体的初始化表达式
        private void AnalyzeBraceInitializerType(SyntaxTree.BraceInitializerNode initNode, string expectedTypeExpr, SyntaxTree.Node errorNode)
        {
            var expectedType = GType.Parse(expectedTypeExpr);
            if(expectedType.IsStructType == false)
                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, errorNode, "brace initializer can only be used with struct type.");

            var fieldRecs = GetStructFieldRecordsInOrder(expectedType.ObjectTypeName);
            var fieldDecls = GetStructFieldDeclsInOrder(expectedType.ObjectTypeName);

            if(initNode.fieldExprNodes.Count > fieldRecs.Count)
                throw new SemanticException(ExceptioName.VariableTypeDeclarationError, errorNode, "too many struct initializer elements.");

            if(fieldDecls.Count != fieldRecs.Count)
                throw new GizboxException(ExceptioName.SemanticAnalysysError, $"struct field declaration count mismatch: {expectedTypeExpr}");

            for(int i = initNode.fieldExprNodes.Count; i < fieldDecls.Count; ++i)
            {
                var clonedInitExpr = CloneStructFieldInitializer(fieldDecls[i], initNode);
                initNode.fieldExprNodes.Add(clonedInitExpr);
            }

            //对每个字段的初始化表达式进行分析与类型校验
            for(int i = 0; i < initNode.fieldExprNodes.Count; ++i)
            {
                var fieldExpr = initNode.fieldExprNodes[i];
                var fieldRec = fieldRecs[i];

                if(fieldExpr is SyntaxTree.BraceInitializerNode nestedInit)
                {
                    AnalyzeBraceInitializerType(nestedInit, fieldRec.typeExpression, errorNode);
                }
                else
                {
                    Pass3_AnalysisNode(fieldExpr);

                    if(CheckType_Equal(fieldRec.typeExpression, fieldExpr) == false)
                    {
                        throw new SemanticException(ExceptioName.VariableTypeDeclarationError, errorNode, $"struct field initializer type mismatch: field={fieldRec.name}, type={fieldRec.typeExpression}, value type={AnalyzeTypeExpression(fieldExpr)}");
                    }
                }
            }

            initNode.attributes[AstAttr.type] = expectedTypeExpr;
        }

        /// <summary>
        /// 按被调函数形参列表批量校验所有byref实参绑定。
        /// </summary>
        private void ValidateCallByRefArguments(SyntaxTree.CallNode callNode, IList<string> parameterTypeExprs)
        {
            if(parameterTypeExprs == null)
                return;

            int count = Math.Min(callNode.argumantsNode.arguments.Count, parameterTypeExprs.Count);
            for(int i = 0; i < count; ++i)
            {
                var paramType = GType.Parse(parameterTypeExprs[i]);
                if(paramType.IsRefType == false)
                    continue;

                ValidateRefBinding(callNode, parameterTypeExprs[i], callNode.argumantsNode.arguments[i]);
            }
        }


        /// <summary>
        /// 获取表达式的值类型。
        /// 若类型为byref，则递归剥离byref（返回其最终指向的值类型表达式）
        /// </summary>
        private string GetValueTypeExpression(string typeExpr)
        {
            if(string.IsNullOrWhiteSpace(typeExpr))
                return typeExpr;

            var type = GType.Parse(typeExpr);
            if(type.IsRefType)
                return GetValueTypeExpression(type.RefTargetType.ToString());

            return typeExpr;
        }


        /// <summary>
        /// 分析变量/参数/返回值的所有权模型  
        /// </summary>
        private SymbolTable.RecordFlag GetOwnershipModel(VarModifiers explicitModifier, SyntaxTree.TypeNode typeNode)
        {
            string typeExpr = typeNode.TypeExpression();

            GType type = GType.Parse(typeExpr);
            if(type.IsRefType)
            {
                if(explicitModifier != VarModifiers.None || typeNode.ownershipModifier != VarModifiers.None)
                    throw new SemanticException(ExceptioName.OwnershipError, typeNode, "ref type can not use own/bor modifiers.");

                return SymbolTable.RecordFlag.None;
            }

            if(type.IsRawReferenceType)
            {
                SymbolTable.RecordFlag ownerModel = SymbolTable.RecordFlag.None;

                var effectiveModifier = explicitModifier != VarModifiers.None
                    ? explicitModifier
                    : typeNode.ownershipModifier;

                if(effectiveModifier == VarModifiers.None && type.IsClassType)
                {
                    effectiveModifier = type.OwnershipHint switch
                    {
                        GType.OwnershipHintKind.Own => VarModifiers.Own,
                        GType.OwnershipHintKind.Borrow => VarModifiers.Bor,
                        _ => VarModifiers.None,
                    };
                }

                // own/bor 仅允许用于 class 引用类型；string/array/function 等不支持所有权语义
                if((effectiveModifier.HasFlag(VarModifiers.Own) || effectiveModifier.HasFlag(VarModifiers.Bor))
                    && type.IsClassType == false)
                {
                    throw new SemanticException(ExceptioName.OwnershipError, typeNode, "own/bor only supports class reference types.");
                }

                if(effectiveModifier.HasFlag(VarModifiers.Own))
                {
                    ownerModel = SymbolTable.RecordFlag.OwnerVar;//显式own
                }
                else if(effectiveModifier.HasFlag(VarModifiers.Bor))
                {
                    ownerModel = SymbolTable.RecordFlag.BorrowVar;//显式借用
                }
                else
                {
                    bool isOwnershipClass = false;
                    if(typeNode is NamedTypeNode classTypeNode)
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


                        nodeTypeExprssion = GetValueTypeExpression(result.typeExpression);
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
                        var classTypeExpression = GetValueTypeExpression(AnalyzeTypeExpression(accessNode.objectNode));
                        GType classType = GType.Parse(classTypeExpression);
                        if(classType.IsEnumType)
                        {
                            var enumRec = Query(classType.ObjectTypeName);
                            if(enumRec == null || enumRec.envPtr == null)
                                throw new SemanticException(ExceptioName.ClassNameNotFound, accessNode.objectNode, classType.ObjectTypeName);

                            var memberRec = enumRec.envPtr.records.Values.FirstOrDefault(r => r.rawname == accessNode.memberNode.FullName);
                            if(memberRec == null)
                                throw new SemanticException(ExceptioName.MemberFieldNotFound, accessNode.objectNode, accessNode.memberNode.FullName);

                            accessNode.attributes[AstAttr.enum_rec] = enumRec;
                            accessNode.attributes[AstAttr.klass] = classTypeExpression;
                            accessNode.attributes[AstAttr.member_name] = accessNode.memberNode.FullName;

                            nodeTypeExprssion = memberRec.typeExpression;
                            break;
                        }

                        var classRec = Query(classType.ObjectTypeName);
                        if(classRec == null)
                            throw new SemanticException(ExceptioName.ClassNameNotFound, accessNode.objectNode, classType.ObjectTypeName);

                        var classEnv = classRec.envPtr;
                        if(classEnv == null)
                            throw new SemanticException(ExceptioName.ClassScopeNotFound, accessNode.objectNode, "");

                        var memberRec2 = classEnv.Class_GetMemberRecordInChainByRawname(accessNode.memberNode.FullName);
                        if(memberRec2 == null)
                            throw new SemanticException(ExceptioName.MemberFieldNotFound, accessNode.objectNode, accessNode.memberNode.FullName);

                        accessNode.attributes[AstAttr.klass] = classTypeExpression;
                        accessNode.attributes[AstAttr.member_name] = accessNode.memberNode.FullName;

                        nodeTypeExprssion = memberRec2.typeExpression;
                    }
                    break;
                case SyntaxTree.EnumAccessNode enumValueAccessNode:
                    {
                        nodeTypeExprssion = $"(enum){enumValueAccessNode.enumTypeNode.FullName}";
                    }
                    break;
                case SyntaxTree.ElementAccessNode eleAccessNode:
                    {
                        string containerTypeExpr = GetValueTypeExpression(AnalyzeTypeExpression(eleAccessNode.containerNode));

                        if (containerTypeExpr.EndsWith("[]") == false)
                            throw new SemanticException(ExceptioName.Undefine, eleAccessNode, "only array can use [] operator");

                        nodeTypeExprssion = containerTypeExpr.Substring(0, containerTypeExpr.Length - 2);
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        // 函数指针调用（callee 不是函数符号本身，而是一个函数类型值）
                        if(callNode.isMemberAccessFunction == false)
                        {
                            SymbolTable.Record calleeRec = null;
                            GType calleeType = null;

                            if(callNode.funcNode is SyntaxTree.IdentityNode fid)
                            {
                                calleeRec = Query(fid.FullName);
                                if(calleeRec == null)
                                    calleeRec = Query_IgnoreMangle(fid.FullName);

                                // 仅当标识符不是函数符号时，才按“函数指针值”处理
                                if(calleeRec != null && calleeRec.category != SymbolTable.RecordCatagory.Function)
                                {
                                    calleeType = GType.Parse(calleeRec.typeExpression);
                                }
                            }
                            else
                            {
                                calleeType = GType.Parse(AnalyzeTypeExpression(callNode.funcNode));
                            }

                            if(calleeType != null && calleeType.Category == GType.Kind.Function)
                            {
                                var paramTypes = calleeType.FunctionParamTypes ?? new List<GType>();
                                if(paramTypes.Count != callNode.argumantsNode.arguments.Count)
                                {
                                    throw new SemanticException(ExceptioName.FunctionNotFound, callNode, "function pointer argument count mismatch");
                                }

                                for(int i = 0; i < paramTypes.Count; ++i)
                                {
                                    var argType = AnalyzeTypeExpression(callNode.argumantsNode.arguments[i]);
                                    if(CheckType_Is(argType, paramTypes[i].ToString()) == false)
                                    {
                                        throw new SemanticException(ExceptioName.FunctionNotFound, callNode, "function pointer argument type mismatch");
                                    }
                                }

                                nodeTypeExprssion = calleeType.FunctionReturnType.ToString();
                                break;
                            }
                        }

                        string funcMangledName;

                        if (callNode.isMemberAccessFunction)
                        {
                            string[] explicitArgTypeArr = callNode.argumantsNode.arguments.Select(argN => AnalyzeTypeExpression(argN)).ToArray();
                            string[] explicitParamTypeArr = new string[explicitArgTypeArr.Length];


                            var funcAccess = (callNode.funcNode as SyntaxTree.ObjectMemberAccessNode);
                            string funcFullName = funcAccess.memberNode.FullName;

                            var className = GType.Normalize(AnalyzeTypeExpression(funcAccess.objectNode));


                            List<string> allArgTypeList = new List<string>() { className };
                            allArgTypeList.AddRange(callNode.argumantsNode.arguments.Select(argN => AnalyzeTypeExpression(argN)));
                            string[] allArgTypeArr = allArgTypeList.ToArray();
                            string[] allParamTypeArr = new string[allArgTypeArr.Length];



                            var classRec = Query(className);
                            if (classRec == null) throw new SemanticException(ExceptioName.ClassNameNotFound, callNode, className);

                            var classEnv = classRec.envPtr;
                            if (classEnv == null) throw new SemanticException(ExceptioName.ClassScopeNotFound, callNode, "");

                            bool anyFunc = TryQueryAndMatchFunction(funcFullName, explicitArgTypeArr, explicitParamTypeArr, out var matchedMemberRec, true, classEnv);
                            if(anyFunc == false) throw new SemanticException(ExceptioName.FunctionMemberNotFound, callNode, funcAccess.memberNode.FullName);

                            funcMangledName = matchedMemberRec?.name ?? Utils.Mangle(funcFullName, explicitParamTypeArr);

                            var memberRec = matchedMemberRec ?? classEnv.Class_GetMemberRecordInChain(funcMangledName);
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

                            bool anyFunc = TryQueryAndMatchFunction(funcId.FullName, argTypeArr, paramTypeArr, out var matchedFuncRec);
                            if(anyFunc == false) throw new SemanticException(ExceptioName.FunctionNotFound, callNode, funcId.FullName);


                            bool isExternFunc = matchedFuncRec != null && matchedFuncRec.flags.HasFlag(SymbolTable.RecordFlag.ExternFunc);
                            funcMangledName = matchedFuncRec?.name ?? Utils.Mangle(funcId.FullName, paramTypeArr);
                            var idRec = matchedFuncRec ?? Query(funcMangledName);
                            if(idRec == null)
                                idRec = Query(Utils.ToExternFuncName(funcId.FullName));

                            if (idRec == null) 
                                throw new SemanticException(ExceptioName.FunctionNotFound, callNode, funcId.FullName);

                            string typeExpr = idRec.typeExpression.Split(' ').LastOrDefault();

                            nodeTypeExprssion = typeExpr;

                            if(isExternFunc)
                            {
                                callNode.attributes[AstAttr.extern_name] = idRec.name;
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
                            throw new SemanticException(ExceptioName.ClassDefinitionNotFound, newObjNode.className, className);
                        }

                        TryCompleteType(newObjNode.typeNode);
                        nodeTypeExprssion = newObjNode.typeNode?.TypeExpression() ?? $"(class){className}";
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

                            nodeTypeExprssion = "(primitive)bool";
                        }
                        //移位运算符（仅允许用于int/uint/long/ulong）
                        else if(op == "<<" || op == ">>")
                        {
                            if (GType.Parse(typeR).IsInteger == false) throw new SemanticException(ExceptioName.BinaryOperationTypeMismatch, binaryOp, "shift operator requires integer type");
                            if (GType.Parse(typeL).IsInteger == false) throw new SemanticException(ExceptioName.BinaryOperationTypeMismatch, binaryOp, "shift operator requires integer type");
                            nodeTypeExprssion = typeL;
                        }
                        //其他普通运算符  
                        else
                        {
                            if (CheckType_Equal(typeL, typeR) == false) throw new SemanticException(ExceptioName.BinaryOperationTypeMismatch, binaryOp, "");

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
                case SyntaxTree.TernaryConditionNode ternaryNode:
                    {
                        nodeTypeExprssion = AnalyzeTypeExpression(ternaryNode.trueNode);
                    }
                    break;
                case SyntaxTree.CastNode castNode:
                    {
                        nodeTypeExprssion = castNode.typeNode.TypeExpression();
                    }
                    break;
                case SyntaxTree.SizeOfNode sizeofNode:
                    {
                        nodeTypeExprssion = "(primitive)int";
                    }
                    break;
                case SyntaxTree.TypeOfNode typeofNode:
                    {
                        if(Query("Core::Type") == null)
                            throw new SemanticException(ExceptioName.ClassDefinitionNotFound, typeofNode, "Core::Type");

                        nodeTypeExprssion = "(class)Core::Type";
                    }
                    break;
                default:
                    throw new SemanticException(ExceptioName.CannotAnalyzeExpressionNodeType, exprNode, exprNode.GetType().Name);
            }

            string sss = nodeTypeExprssion;
            nodeTypeExprssion = CanonicalizeTypeExpression(nodeTypeExprssion);
            exprNode.attributes[AstAttr.type] = nodeTypeExprssion;

            if(nodeTypeExprssion.Contains("(class)class)"))
            {
                throw new Exception();
            }
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
            typeExpr1 = NormalizeTypeExprForTypeCheck(typeExpr1);
            typeExpr2 = NormalizeTypeExprForTypeCheck(typeExpr2);

            if (typeExpr1 == "null" && GType.Parse(typeExpr2).IsRawReferenceType)
            {
                return true;
            }
            else if (typeExpr2 == "null" && GType.Parse(typeExpr1).IsRawReferenceType)
            {
                return true;
            }

            return typeExpr1 == typeExpr2;
        }
        private bool CheckType_Is(string typeExpr1, string typeExpr2)
        {
            typeExpr1 = NormalizeTypeExprForTypeCheck(typeExpr1);
            typeExpr2 = NormalizeTypeExprForTypeCheck(typeExpr2);

            if(typeExpr1 == typeExpr2) return true;

            //有至少一个是基元类型  
            if(GType.Parse(typeExpr1).IsValuePrimitive || GType.Parse(typeExpr2).IsValuePrimitive)
            {
                return GType.Normalize(typeExpr1) == GType.Normalize(typeExpr2);
            }
            //全是非基元类型  
            else
            {
                //null可以是任何非基元类型的子类  
                if(GType.Normalize(typeExpr1) == "null")
                {
                    return true;
                }
                //两个都是类类型
                else if(GType.Parse(typeExpr1).IsClassType && GType.Parse(typeExpr2).IsClassType)
                {
                    var typeRec1 = Query(GType.Normalize(typeExpr1));
                    if(typeRec1.envPtr.Class_IsSubClassOf(GType.Normalize(typeExpr2)))
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

        private string NormalizeTypeExprForTypeCheck(string typeExpr)
        {
            if(string.IsNullOrWhiteSpace(typeExpr))
                return typeExpr;

            var t = GType.Parse(typeExpr);
            if(t.IsRefType)
                return NormalizeTypeExprForTypeCheck(t.RefTargetType.ToString());

            if(t.IsClassType || t.IsStructType)
                return t.ObjectTypeName;

            if(t.IsEnumType)
                return "(enum)" + t.ObjectTypeName;

            if(t.IsArray)
                return NormalizeTypeExprForTypeCheck(t.ArrayElementType.ToString()) + "[]";

            if(t.IsFunction)
            {
                var paramPart = string.Join(",", t.FunctionParamTypes.Select(p => NormalizeTypeExprForTypeCheck(p.ToString())));
                var retPart = NormalizeTypeExprForTypeCheck(t.FunctionReturnType.ToString());
                return paramPart + " => " + retPart;
            }

            return t.Category switch
            {
                GType.Kind.Void => "void",
                GType.Kind.Bool => "bool",
                GType.Kind.Byte => "byte",
                GType.Kind.Char => "char",
                GType.Kind.Int => "int",
                GType.Kind.UInt => "uint",
                GType.Kind.Long => "long",
                GType.Kind.ULong => "ulong",
                GType.Kind.Float => "float",
                GType.Kind.Double => "double",
                GType.Kind.String => "string",
                _ => typeExpr,
            };
        }

        private string CanonicalizeTypeExpression(string typeExpr)
        {
            if(string.IsNullOrWhiteSpace(typeExpr))
                return typeExpr;

            var t = GType.Parse(typeExpr);

            if(t.IsRefType)
                return "(ref)" + CanonicalizeTypeExpression(t.RefTargetType.ToString());

            if(t.IsArray)
                return CanonicalizeTypeExpression(t.ArrayElementType.ToString()) + "[]";

            if(t.Category == GType.Kind.Function)
            {
                var p = t.FunctionParamTypes?.Select(x => CanonicalizeTypeExpression(x.ToString())) ?? Enumerable.Empty<string>();
                var r = CanonicalizeTypeExpression(t.FunctionReturnType.ToString());
                return $"{string.Join(",", p)} => {r}";
            }

            if(t.IsStructType)
            {
                var n = t.ObjectTypeName;
                var size = t.Size;
                return $"(struct:{size}){n}";
            }

            if(t.IsEnumType)
            {
                return $"(enum){t.ObjectTypeName}";
            }

            if(t.IsClassType)
            {
                var n = t.ObjectTypeName;
                if(t.OwnershipHint == GType.OwnershipHintKind.Own)
                    return $"(own-class){n}";
                if(t.OwnershipHint == GType.OwnershipHintKind.Borrow)
                    return $"(bor-class){n}";
                return $"(class){n}";
            }

            return t.Category switch
            {
                GType.Kind.Void => "(primitive)void",
                GType.Kind.Bool => "(primitive)bool",
                GType.Kind.Byte => "(primitive)byte",
                GType.Kind.Char => "(primitive)char",
                GType.Kind.Int => "(primitive)int",
                GType.Kind.UInt => "(primitive)uint",
                GType.Kind.Long => "(primitive)long",
                GType.Kind.ULong => "(primitive)ulong",
                GType.Kind.Float => "(primitive)float",
                GType.Kind.Double => "(primitive)double",
                GType.Kind.String => "(primitive)string",
                _ => typeExpr,
            };
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
                case SyntaxTree.SwitchStmtNode switchNode:
                    {
                        bool hasDefault = false;
                        bool allPathValid = true;

                        foreach(var caseNode in switchNode.caseNodes)
                        {
                            if(caseNode.isDefault)
                                hasDefault = true;

                            if(CheckReturnStmt(caseNode.statementsNode, returnType) == false)
                            {
                                allPathValid = false;
                                break;
                            }
                        }

                        return hasDefault && allPathValid;
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
            name = GType.Normalize(name);

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
            rawname = GType.Normalize(rawname);

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
                case SyntaxTree.NamedTypeNode namedTypeNode:
                    {
                        TryCompleteIdenfier(namedTypeNode.classname);

                        var typeName = namedTypeNode.classname.FullName;
                        var rec = Query(typeName);
                        if(rec == null)
                            throw new GizboxException(ExceptioName.Undefine, $"namedType:{typeName} not found!");

                        if(rec.category == SymbolTable.RecordCatagory.Struct)
                        {
                            namedTypeNode.Complate(true, (int)rec.size);
                        }
                        else if(rec.category == SymbolTable.RecordCatagory.Enum)
                        {
                            namedTypeNode.Complate(false, 0, true);
                        }
                        else
                        {
                            namedTypeNode.Complate(false);
                        }
                    }
                    break;
                case SyntaxTree.ArrayTypeNode arrayTypeNpde:
                    {
                        TryCompleteType(arrayTypeNpde.elemtentType);
                    }
                    break;
                case SyntaxTree.RefTypeNode refTypeNode:
                    {
                        TryCompleteType(refTypeNode.targetType);
                    }
                    break;
            }
            typeNode.attributes[AstAttr.name_completed] = true;
        }

        private bool TryQueryAndMatchFunction(string funcRawName, string[] argTypes, string[] outParamTypes, bool isMethod = false, SymbolTable classEnvOfMethod = null)
            => TryQueryAndMatchFunction(funcRawName, argTypes, outParamTypes, out _, isMethod, classEnvOfMethod);

        private bool TryQueryAndMatchFunction(string funcRawName, string[] argTypes, string[] outParamTypes, out SymbolTable.Record matchedFuncRec, bool isMethod = false, SymbolTable classEnvOfMethod = null)
        {
            matchedFuncRec = null;
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
                matchedFuncRec = targetFunc;
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
        private BraceInitializerNode GenDefaultBraceInitializerNode(SyntaxTree.TypeNode typenode)
        {
            var initNode = new SyntaxTree.BraceInitializerNode()
            {
                attributes = new Dictionary<AstAttr, object>(),
            };

            var start = typenode?.StartToken();
            var end = typenode?.EndToken();

            if(start != null)
                initNode.attributes[AstAttr.start] = start;
            if(end != null)
                initNode.attributes[AstAttr.end] = end;

            initNode.attributes[AstAttr.type] = typenode.TypeExpression();

            return initNode;
        }
        private SyntaxTree.TypeNode BuildTypeNodeFromTypeExpression(string typeExpr, Token refToken)
        {
            refToken ??= new Token("ID", PatternType.Id, typeExpr, 0, 0, 0);

            var parsedType = GType.Parse(typeExpr);
            if(parsedType.IsRefType)
            {
                var targetTypeNode = BuildTypeNodeFromTypeExpression(parsedType.RefTargetType.ToString(), refToken);
                var refTypeNode = new SyntaxTree.RefTypeNode()
                {
                    targetType = targetTypeNode,
                    attributes = new Dictionary<AstAttr, object>(),
                };
                targetTypeNode.Parent = refTypeNode;
                return refTypeNode;
            }

            if(typeExpr.EndsWith("[]"))
            {
                var elemTypeExpr = typeExpr.Substring(0, typeExpr.Length - 2);
                var elemTypeNode = BuildTypeNodeFromTypeExpression(elemTypeExpr, refToken);
                var arrTypeNode = new SyntaxTree.ArrayTypeNode()
                {
                    elemtentType = elemTypeNode,
                    attributes = new Dictionary<AstAttr, object>(),
                };
                elemTypeNode.Parent = arrTypeNode;
                return arrTypeNode;
            }

            bool isPrimitive = typeExpr == "void"
                || typeExpr == "bool"
                || typeExpr == "char"
                || typeExpr == "byte"
                || typeExpr == "int"
                || typeExpr == "uint"
                || typeExpr == "long"
                || typeExpr == "ulong"
                || typeExpr == "float"
                || typeExpr == "double"
                || typeExpr == "string";

            if(isPrimitive)
            {
                return new SyntaxTree.PrimitiveTypeNode()
                {
                    token = new Token(GType.Normalize(typeExpr), PatternType.Keyword, typeExpr, refToken.line, refToken.start, refToken.length),
                    attributes = new Dictionary<AstAttr, object>(),
                };
            }

            var idNode = new SyntaxTree.IdentityNode()
            {
                token = new Token("ID", PatternType.Id, GType.Normalize(typeExpr), refToken.line, refToken.start, refToken.length),
                identiferType = SyntaxTree.IdentityNode.IdType.Class,
                attributes = new Dictionary<AstAttr, object>(),
            };
            var classTypeNode = new SyntaxTree.NamedTypeNode()
            {
                classname = idNode,
                attributes = new Dictionary<AstAttr, object>(),
            };
            idNode.Parent = classTypeNode;
            return classTypeNode;
        }

        //解析枚举成员的整型常量值  
        private int ParseEnumMemberValue(SyntaxTree.EnumMemberNode memberNode)
        {
            string DealNumStr(string num)
            {
                StringBuilder strb = new StringBuilder();
                foreach(char c in num)
                {
                    if(char.IsDigit(c))
                        strb.Append(c);
                    else if(c == '.')
                        strb.Append(c);
                }
                return strb.ToString();
            }

            var token = memberNode?.valueNode?.token;
            if(token == null)
                throw new SemanticException(ExceptioName.SemanticAnalysysError, memberNode, "enum member value is missing.");
            try
            {
                checked
                {
                    switch(token.name)
                    {
                        case "LITINT":
                            return int.Parse(token.attribute);

                        case "LITUINT":
                            {
                                uint v = uint.Parse(DealNumStr(token.attribute));
                                return (int)v;
                            }

                        case "LITLONG":
                            {
                                long v = long.Parse(DealNumStr(token.attribute));
                                return (int)v;
                            }

                        case "LITULONG":
                            {
                                string raw = DealNumStr(token.attribute);
                                ulong v = ulong.Parse(raw);
                                return (int)v;
                            }

                        case "LITCHAR":
                            {
                                string raw = token.attribute;
                                if(raw.Length >= 3 && raw[0] == '\'' && raw[raw.Length - 1] == '\'')
                                {
                                    string content = raw.Substring(1, raw.Length - 2);
                                    if(content.Length == 1)
                                        return content[0];

                                    if(content.Length == 2 && content[0] == '\\')
                                    {
                                        return content[1] switch
                                        {
                                            '0' => '\0',
                                            'n' => '\n',
                                            'r' => '\r',
                                            't' => '\t',
                                            '\\' => '\\',
                                            '\'' => '\'',
                                            '"' => '"',
                                            _ => content[1],
                                        };
                                    }
                                }
                                break;
                            }
                    }
                }
            }
            catch(OverflowException)
            {
                throw new SemanticException(ExceptioName.SemanticAnalysysError, memberNode, "enum member value overflow.");
            }

            throw new SemanticException(ExceptioName.SemanticAnalysysError, memberNode, "enum member only supports int-compatible literal values.");
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
                else if(funcId.token?.attribute == "gettype")
                {
                    if(callNode.argumantsNode.arguments.Count != 1)
                        throw new SemanticException(ExceptioName.SemanticAnalysysError, callNode, "gettype expects 1 argument.");

                    SymbolTable.Record varRec = null;
                    if(callNode.argumantsNode.arguments[0] is SyntaxTree.IdentityNode varId)
                    {
                        varRec = Query(varId.FullName);
                        if(varRec == null)
                            throw new SemanticException(ExceptioName.IdentifierNotFound, varId, varId.FullName);
                    }
                    else if(callNode.argumantsNode.arguments[0] is SyntaxTree.ThisNode thisNode)
                    {
                        varRec = Query("this");
                        if(varRec == null)
                            throw new SemanticException(ExceptioName.IdentifierNotFound, thisNode, "this");
                    }

                    if(varRec == null)
                        throw new SemanticException(ExceptioName.SemanticAnalysysError, callNode, "gettype expects a variable identifier.");


                    if(varRec.category != SymbolTable.RecordCatagory.Variable
                        && varRec.category != SymbolTable.RecordCatagory.Param)
                    {
                        throw new SemanticException(ExceptioName.SemanticAnalysysError, callNode, "gettype argument must be variable or parameter.");
                    }

                    var typeNode = BuildTypeNodeFromTypeExpression(varRec.typeExpression, funcId.token);
                    var replaceNode = new SyntaxTree.TypeOfNode()
                    {
                        typeNode = typeNode,
                        attributes = callNode.attributes,
                    };
                    typeNode.Parent = replaceNode;
                    replaceNode.Parent = callNode.Parent;
                    callNode.overrideNode = replaceNode;

                    replace = replaceNode;
                    return true;
                }
            }



            replace = null;
            return false;
        }

        private SyntaxTree.ExprNode GetEffectiveExprNode(SyntaxTree.ExprNode exprNode)
        {
            while(exprNode?.overrideNode is SyntaxTree.ExprNode overrideExpr)
            {
                exprNode = overrideExpr;
            }

            return exprNode;
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

        private void GenStructLayoutInfo(SymbolTable.Record structRec)
        {
            if(structRec == null || structRec.envPtr == null)
                throw new GizboxException(ExceptioName.SemanticAnalysysError, "invalid struct record.");

            var structEnv = structRec.envPtr;
            List<SymbolTable.Record> fieldRecs = new();
            foreach(var (_, memberRec) in structEnv.records)
            {
                if(memberRec.category != SymbolTable.RecordCatagory.Variable)
                    continue;
                fieldRecs.Add(memberRec);
            }

            (int size, int align)[] fieldSizeAndAlignArr = new (int size, int align)[fieldRecs.Count];
            for(int i = 0; i < fieldRecs.Count; i++)
            {
                var ftype = GType.Parse(fieldRecs[i].typeExpression);
                var size = ftype.Size;
                var align = ftype.Align;

                fieldRecs[i].size = size;
                fieldSizeAndAlignArr[i] = (size, align);
            }

            long structSize = MemUtility.ClassLayout(0, fieldSizeAndAlignArr, out var allocAddrs);
            for(int i = 0; i < fieldRecs.Count; i++)
            {
                fieldRecs[i].addr = allocAddrs[i];
            }

            structRec.size = structSize;
            structRec.typeExpression = $"(struct:{structSize}){structRec.name}";

            RewriteAllTypeExpressionsForStruct(structRec.name, structRec.typeExpression);
        }

        private void RewriteAllTypeExpressionsForStruct(string structName, string structTypeExpr)
        {
            foreach(var (_, rec) in ilUnit.globalScope.env.GetRecordsRecursive())
            {
                if(string.IsNullOrWhiteSpace(rec.typeExpression))
                    continue;

                rec.typeExpression = RewriteTypeExpressionForStruct(rec.typeExpression, structName, structTypeExpr);
            }
        }

        private void RewriteAllKnownStructTypeExpressions()
        {
            foreach(var (_, rec) in ilUnit.globalScope.env.records)
            {
                if(rec.category != SymbolTable.RecordCatagory.Struct)
                    continue;
                if(string.IsNullOrWhiteSpace(rec.typeExpression))
                    continue;

                RewriteAllTypeExpressionsForStruct(rec.name, rec.typeExpression);
            }
        }

        private string RewriteTypeExpressionForStruct(string typeExpr, string structName, string structTypeExpr)
        {
            if(string.IsNullOrWhiteSpace(typeExpr))
                return typeExpr;

            GType t;
            try
            {
                t = GType.Parse(typeExpr);
            }
            catch
            {
                return typeExpr;
            }

            if(t.IsArray)
            {
                return RewriteTypeExpressionForStruct(t.ArrayElementType.ToString(), structName, structTypeExpr) + "[]";
            }

            if(t.Category == GType.Kind.Function)
            {
                var p = t.FunctionParamTypes?.Select(x => RewriteTypeExpressionForStruct(x.ToString(), structName, structTypeExpr))
                    ?? Enumerable.Empty<string>();
                var r = RewriteTypeExpressionForStruct(t.FunctionReturnType.ToString(), structName, structTypeExpr);
                return $"{string.Join(",", p)} => {r}";
            }

            if(t.IsRefType)
            {
                return "(ref)" + RewriteTypeExpressionForStruct(t.RefTargetType.ToString(), structName, structTypeExpr);
            }

            if(t.IsClassType && t.ObjectTypeName == structName)
                return structTypeExpr;

            if(t.IsStructType && t.ObjectTypeName == structName)
                return structTypeExpr;

            return typeExpr;
        }

        public static void Log(object content)
        {
            if (!Compiler.enableLogSemanticAnalyzer) return;
            GixConsole.WriteLine("SematicAnalyzer >>>>" + content);
        }
    }
}






