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


namespace Gizbox.SemanticRule;

public partial class SemanticAnalyzer
{

    private enum NamedTypeTemplateKind
    {
        Class,
        Struct,
    }

    private class NamedTypeTemplateDefinition
    {
        public NamedTypeTemplateKind kind;
        public SyntaxTree.DeclareNode templateNode;
        public IdentityNode nameNode;
    }

    //模板命名类型实例信息
    private class NamedTypeTemplateInstance
    {
        public NamedTypeTemplateKind kind;
        public string templateName;
        public string mangledName;
        public List<SyntaxTree.TypeNode> typeArguments;
    }

    //模板函数实例信息
    private class FunctionTemplateInstance
    {
        public string templateName;
        public string mangledBaseName;
        public List<SyntaxTree.TypeNode> typeArguments;
    }

    //标识符可能全名列表的映射(ShortName -> [FullName])  
    private Dictionary<string, HashSet<string>> templateFullNameDict = new();





    /// <summary>模板命名类型特化（AST层面）：统一处理类模板和结构体模板。</summary>
    private bool SpecializeNamedTypeTemplates()
    {
        if(ast?.rootNode == null)
            return false;

        EnsureTemplateSpecializationDependenciesLoaded();

        templateFullNameDict.Clear();

        //收集当前模块的命名类型模板定义
        var templatesLocal = new Dictionary<string, NamedTypeTemplateDefinition>();
        CollectNamedTypeTemplates(ast.rootNode, templatesLocal);

        var templatesDeps = new Dictionary<string, NamedTypeTemplateDefinition>();
        foreach(var dep in ilUnit.dependencyLibs)
        {
            dep.EnsureAst();
            if(dep.ast?.rootNode == null)
                continue;

            CollectNamedTypeTemplates(dep.ast.rootNode, templatesDeps);
        }

        //记录所有命名类型模板实例化信息（类型引用和new表达式）
        BuildTemplateFullNameDict(templatesLocal.Values.Concat(templatesDeps.Values));

        var instances = new Dictionary<string, NamedTypeTemplateInstance>();
        CollectNamedTypeTemplateInstantiations(ast.rootNode, instances, templatesLocal, templatesDeps, inTemplate: false);

        var newSpecializations = new List<SyntaxTree.DeclareNode>();


        foreach(var inst in instances.Values)
        {
            if(IsNamedTypeSpecializationAvailable(inst.kind, inst.mangledName))
                continue;

            var templateNode = FindNamedTypeTemplate(inst.templateName, templatesLocal, templatesDeps);
            if(templateNode == null)
            {
                continue;
            }

            var specialized = (SyntaxTree.DeclareNode)templateNode.templateNode.DeepClone();
            ApplyNamedTypeTemplateSpecialization(templateNode, specialized, inst.mangledName, inst.typeArguments);
            newSpecializations.Add(specialized);
        }

        if(newSpecializations.Count > 0)
        {
            for(int i = newSpecializations.Count - 1; i >= 0; --i)
            {
                ast.rootNode.statementsNode.statements.Insert(0, newSpecializations[i]);
            }
        }

        return newSpecializations.Count > 0;
    }

    /// <summary>收集命名类型模板定义（类/结构体）。</summary>
    private void CollectNamedTypeTemplates(SyntaxTree.Node node, Dictionary<string, NamedTypeTemplateDefinition> templates)
    {
        if(node == null)
            return;

        if(node is SyntaxTree.ClassDeclareNode classDecl && classDecl.isTemplateClass)
        {
            TemplateCompleteDefineFullName(classDecl.classNameNode);
            templates[classDecl.classNameNode.FullName] = new NamedTypeTemplateDefinition()
            {
                kind = NamedTypeTemplateKind.Class,
                templateNode = classDecl,
                nameNode = classDecl.classNameNode,
            };
            return;
        }

        if(node is SyntaxTree.StructDeclareNode structDecl && structDecl.isTemplateStruct)
        {
            TemplateCompleteDefineFullName(structDecl.structNameNode);
            templates[structDecl.structNameNode.FullName] = new NamedTypeTemplateDefinition()
            {
                kind = NamedTypeTemplateKind.Struct,
                templateNode = structDecl,
                nameNode = structDecl.structNameNode,
            };
            return;
        }

        foreach(var child in node.Children())
        {
            CollectNamedTypeTemplates(child, templates);
        }
    }

    /// <summary>构建模板短名到全名的映射，供命名空间匹配使用。</summary>
    private void BuildTemplateFullNameDict(IEnumerable<NamedTypeTemplateDefinition> templates)
    {
        foreach(var template in templates)
        {
            var shortname = template.nameNode.token.attribute;
            var fullname = template.nameNode.FullName;
            if(templateFullNameDict.TryGetValue(shortname, out var list) == false)
            {
                list = new();
                templateFullNameDict[shortname] = list;
            }
            list.Add(fullname);
        }
    }

    /// <summary>收集命名类型模板实例化信息（统一处理类模板和结构体模板）。</summary>
    private void CollectNamedTypeTemplateInstantiations(SyntaxTree.Node node,
        Dictionary<string, NamedTypeTemplateInstance> instances,
        Dictionary<string, NamedTypeTemplateDefinition> templatesLocal,
        Dictionary<string, NamedTypeTemplateDefinition> templatesDeps,
        bool inTemplate)
    {
        if(node == null)
            return;

        if((node is SyntaxTree.ClassDeclareNode classDecl && classDecl.isTemplateClass)
            || (node is SyntaxTree.StructDeclareNode structDecl && structDecl.isTemplateStruct)
            || (node is SyntaxTree.FuncDeclareNode funcDecl && funcDecl.isTemplateFunction))
        {
            inTemplate = true;
        }

        if(!inTemplate)
        {
            if(node is SyntaxTree.NamedTypeNode classType && classType.tempGenericArguments.Count > 0)
            {
                //记录类型引用处的模板实例
                TemplateTryMatchNamespace(classType.classname);
                var templateDef = FindNamedTypeTemplate(classType.classname.FullName, templatesLocal, templatesDeps);
                if(templateDef != null)
                {
                    RegisterNamedTypeTemplateInstance(classType, templateDef, instances);
                    NormalizeGenericUsage(classType);
                }
            }

            if(node is SyntaxTree.NewObjectNode newObjNode && newObjNode.typeNode is SyntaxTree.NamedTypeNode newObjTypeNode && newObjTypeNode.tempGenericArguments.Count > 0)
            {
                //记录new处的模板实例并规范化名称
                TemplateTryMatchNamespace(newObjTypeNode.classname);
                var templateDef = FindNamedTypeTemplate(newObjTypeNode.classname.FullName, templatesLocal, templatesDeps);
                if(templateDef != null && templateDef.kind == NamedTypeTemplateKind.Class)
                {
                    RegisterNamedTypeTemplateInstance(newObjTypeNode, templateDef, instances);
                    NormalizeGenericUsage(newObjNode, newObjTypeNode);
                }
            }
        }

        foreach(var child in node.Children())
        {
            CollectNamedTypeTemplateInstantiations(child, instances, templatesLocal, templatesDeps, inTemplate);
        }

        if(node is SyntaxTree.CallNode callNode)
        {
            foreach(var arg in callNode.tempGenericArguments)
            {
                CollectNamedTypeTemplateInstantiations(arg, instances, templatesLocal, templatesDeps, inTemplate);
            }
        }

        if(node is SyntaxTree.NamedTypeNode classTypeWithArgs)
        {
            foreach(var arg in classTypeWithArgs.tempGenericArguments)
            {
                CollectNamedTypeTemplateInstantiations(arg, instances, templatesLocal, templatesDeps, inTemplate);
            }
        }
    }

    //特化函数模板：收集模板定义、实例化并生成专用函数
    private bool SpecializeFunctionTemplates()
    {
        EnsureTemplateSpecializationDependenciesLoaded();

        //收集函数模板定义  
        var templatesLocal = new Dictionary<string, SyntaxTree.FuncDeclareNode>();
        CollectTemplateFunctions(ast.rootNode, templatesLocal);

        var templatesDeps = new Dictionary<string, SyntaxTree.FuncDeclareNode>();
        foreach(var dep in ilUnit.dependencyLibs)
        {
            dep.EnsureAst();
            if(dep.ast?.rootNode == null)
                continue;

            CollectTemplateFunctions(dep.ast.rootNode, templatesDeps);
        }

        //生成ShortName ->  [FullName]映射（辅助命名空间匹配）  
        foreach(var t in templatesLocal)
        {
            var shortname = t.Value.identifierNode.token.attribute;
            var fullname = t.Value.identifierNode.FullName;
            if(templateFullNameDict.TryGetValue(shortname, out var list) == false)
            {
                list = new();
                templateFullNameDict[shortname] = list;
            }
            list.Add(fullname);
        }
        foreach(var t in templatesDeps)
        {
            var shortname = t.Value.identifierNode.token.attribute;
            var fullname = t.Value.identifierNode.FullName;
            if(templateFullNameDict.TryGetValue(shortname, out var list) == false)
            {
                list = new();
                templateFullNameDict[shortname] = list;
            }
            list.Add(fullname);
        }



        //记录所有函数模板特化信息  
        var instances = new Dictionary<string, FunctionTemplateInstance>();
        CollectFunctionTemplateInstantiations(ast.rootNode, instances, templatesLocal, templatesDeps, inTemplate: false);

        var newSpecializations = new List<SyntaxTree.FuncDeclareNode>();
        foreach(var inst in instances.Values)
        {
            if(IsFunctionSpecializationAvailable(inst.mangledBaseName))
                continue;

            var templateNode = FindTemplateFunction(inst.templateName, templatesLocal, templatesDeps);
            if(templateNode == null)
                continue;

            var specialized = (SyntaxTree.FuncDeclareNode)templateNode.DeepClone();
            ApplyFunctionTemplateSpecialization(templateNode, specialized, inst.mangledBaseName, inst.typeArguments);
            newSpecializations.Add(specialized);
        }

        if(newSpecializations.Count > 0)
        {
            for(int i = newSpecializations.Count - 1; i >= 0; --i)
            {
                ast.rootNode.statementsNode.statements.Insert(0, newSpecializations[i]);
            }
        }

        return newSpecializations.Count > 0;
    }

    //收集模板函数定义
    private void CollectTemplateFunctions(SyntaxTree.Node node, Dictionary<string, SyntaxTree.FuncDeclareNode> templates)
    {
        if(node == null)
            return;

        if(node is SyntaxTree.FuncDeclareNode funcDecl && funcDecl.isTemplateFunction)
        {
            if(funcDecl.Parent is not SyntaxTree.ClassDeclareNode)
            {
                TemplateCompleteDefineFullName(funcDecl.identifierNode);
                templates[funcDecl.identifierNode.FullName] = funcDecl;
            }
            return;
        }

        foreach(var child in node.Children())
        {
            CollectTemplateFunctions(child, templates);
        }
    }



    //收集模板函数实例化信息
    private void CollectFunctionTemplateInstantiations(SyntaxTree.Node node,
        Dictionary<string, FunctionTemplateInstance> instances,
        Dictionary<string, SyntaxTree.FuncDeclareNode> templatesLocal,
        Dictionary<string, SyntaxTree.FuncDeclareNode> templatesDeps,
        bool inTemplate)
    {
        if(node == null)
            return;

        if(node is SyntaxTree.FuncDeclareNode funcDecl && funcDecl.isTemplateFunction)
        {
            inTemplate = true;
        }

        if(!inTemplate && node is SyntaxTree.CallNode callNode && callNode.isMemberAccessFunction == false && callNode.tempGenericArguments.Count > 0)
        {
            if(callNode.funcNode is SyntaxTree.IdentityNode funcId)
            {
                //记录函数调用处的模板实例
                TemplateTryMatchNamespace(funcId);
                if(templatesLocal.ContainsKey(funcId.FullName) || templatesDeps.ContainsKey(funcId.FullName))
                {
                    RegisterFunctionTemplateInstance(funcId, callNode, instances);
                }
            }
        }

        foreach(var child in node.Children())
        {
            CollectFunctionTemplateInstantiations(child, instances, templatesLocal, templatesDeps, inTemplate);
        }
    }

    //注册函数模板实例
    private void RegisterFunctionTemplateInstance(SyntaxTree.IdentityNode funcId, SyntaxTree.CallNode callNode, Dictionary<string, FunctionTemplateInstance> instances)
    {
        var mangledBaseName = BuildTemplateInstanceName(funcId.FullName, callNode.tempGenericArguments);
        if(instances.ContainsKey(mangledBaseName))
        {
            // 已记录实例，仍需规范化调用表达式
            NormalizeFunctionGenericUsage(callNode, mangledBaseName);
            return;
        }

        instances[mangledBaseName] = new FunctionTemplateInstance
        {
            templateName = funcId.FullName,
            mangledBaseName = mangledBaseName,
            typeArguments = callNode.tempGenericArguments.ToList(),
        };

        NormalizeFunctionGenericUsage(callNode, mangledBaseName);
    }

    //规范化函数模板实例调用（清理泛型参数并替换函数名）
    private void NormalizeFunctionGenericUsage(SyntaxTree.CallNode callNode, string mangledBaseName)
    {
        callNode.tempGenericArguments.Clear();

        if(callNode.funcNode is SyntaxTree.IdentityNode funcId)
        {
            funcId.SetPrefix(null);
            funcId.token.attribute = mangledBaseName;
        }
    }

    private SyntaxTree.FuncDeclareNode FindTemplateFunction(string name,
        Dictionary<string, SyntaxTree.FuncDeclareNode> localTemplates,
        Dictionary<string, SyntaxTree.FuncDeclareNode> depTemplates)
    {
        if(localTemplates.TryGetValue(name, out var local))
            return local;
        if(depTemplates.TryGetValue(name, out var dep))
            return dep;
        return null;
    }

    private bool IsFunctionSpecializationAvailable(string mangledBaseName)
    {
        if(ast.rootNode?.statementsNode?.statements != null)
        {
            foreach(var stmt in ast.rootNode.statementsNode.statements)
            {
                if(stmt is SyntaxTree.FuncDeclareNode funcDecl && !funcDecl.isTemplateFunction && funcDecl.identifierNode.FullName == mangledBaseName)
                    return true;
            }
        }

        foreach(var dep in ilUnit.dependencyLibs)
        {
            var rec = dep.QueryTopSymbol(mangledBaseName);
            if(rec != null && rec.category == SymbolTable.RecordCatagory.Function)
                return true;
        }

        return false;
    }

    /// <summary>模板特化前校验依赖库是否已经按名称完整加载。</summary>
    private void EnsureTemplateSpecializationDependenciesLoaded()
    {
        ilUnit.ValidateDependencyLibraries();
    }

    private void ApplyFunctionTemplateSpecialization(SyntaxTree.FuncDeclareNode template,
        SyntaxTree.FuncDeclareNode specialized,
        string mangledBaseName,
        List<SyntaxTree.TypeNode> typeArguments)
    {
        var paramList = template.templateParameters;
        if(paramList.Count != typeArguments.Count)
            throw new SemanticException(ExceptioName.SemanticAnalysysError, template, "template argument count mismatch");

        specialized.isTemplateFunction = false;
        specialized.templateParameters.Clear();

        specialized.identifierNode.SetPrefix(null);
        specialized.identifierNode.token.attribute = mangledBaseName;
        specialized.identifierNode.identiferType = SyntaxTree.IdentityNode.IdType.FunctionOrMethod;

        var typeMap = new Dictionary<string, SyntaxTree.TypeNode>();
        for(int i = 0; i < paramList.Count; ++i)
        {
            typeMap[paramList[i].FullName] = typeArguments[i];
            typeMap[paramList[i].token.attribute] = typeArguments[i];
        }

        ApplyTemplateTypeReplacement(specialized, typeMap);
    }

    /// <summary>注册命名类型模板实例。</summary>
    private void RegisterNamedTypeTemplateInstance(SyntaxTree.NamedTypeNode namedType, NamedTypeTemplateDefinition templateDef, Dictionary<string, NamedTypeTemplateInstance> instances)
    {
        var mangledName = BuildTemplateInstanceName(namedType.classname.FullName, namedType.tempGenericArguments);
        if(instances.ContainsKey(mangledName))
            return;

        instances[mangledName] = new NamedTypeTemplateInstance
        {
            kind = templateDef.kind,
            templateName = namedType.classname.FullName,
            mangledName = mangledName,
            typeArguments = namedType.tempGenericArguments.ToList(),
        };
    }

    //规范化模板类类型引用（清理泛型参数并替换类型名）
    private void NormalizeGenericUsage(SyntaxTree.NamedTypeNode classType)
    {
        var mangledName = BuildTemplateInstanceName(classType.classname.FullName, classType.tempGenericArguments);
        classType.tempGenericArguments.Clear();
        classType.classname.SetPrefix(null);
        classType.classname.token.attribute = mangledName;
    }

    //规范化new语句中的模板类型引用
    private void NormalizeGenericUsage(SyntaxTree.NewObjectNode newObjNode, SyntaxTree.NamedTypeNode classType)
    {
        var mangledName = BuildTemplateInstanceName(classType.classname.FullName, classType.tempGenericArguments);
        classType.tempGenericArguments.Clear();
        classType.classname.SetPrefix(null);
        classType.classname.token.attribute = mangledName;

        newObjNode.className.SetPrefix(null);
        newObjNode.className.token.attribute = mangledName;
    }

    /// <summary>在模板阶段根据语法类型节点构造稳定的模板实例名。</summary>
    private string BuildTemplateInstanceName(string baseName, IEnumerable<SyntaxTree.TypeNode> typeArguments)
    {
        return Utils.MangleTemplateInstanceName(baseName, typeArguments?.Select(BuildTemplateTypeNameFromSyntax));
    }

    /// <summary>在模板阶段从语法类型节点构造类型名，不依赖语义补全后的 TypeExpression。</summary>
    private string BuildTemplateTypeNameFromSyntax(SyntaxTree.TypeNode typeNode)
    {
        if(typeNode == null)
            return string.Empty;

        string ownershipPrefix = typeNode.ownershipModifier switch
        {
            VarModifiers.Own => "own_",
            VarModifiers.Bor => "bor_",
            _ => string.Empty,
        };

        switch(typeNode)
        {
            case SyntaxTree.PrimitiveTypeNode primitiveTypeNode:
                return ownershipPrefix + primitiveTypeNode.token.name;
            case SyntaxTree.InferTypeNode:
                return ownershipPrefix + "var";
            case SyntaxTree.NamedTypeNode namedTypeNode:
                {
                    var baseTypeName = ownershipPrefix + namedTypeNode.classname.FullName;
                    if(namedTypeNode.tempGenericArguments.Count == 0)
                        return baseTypeName;

                    return BuildTemplateInstanceName(baseTypeName, namedTypeNode.tempGenericArguments);
                }
            case SyntaxTree.ArrayTypeNode arrayTypeNode:
                return ownershipPrefix + BuildTemplateTypeNameFromSyntax(arrayTypeNode.elemtentType) + "[]";
            case SyntaxTree.RefTypeNode refTypeNode:
                return ownershipPrefix + "ref_" + BuildTemplateTypeNameFromSyntax(refTypeNode.targetType);
            case SyntaxTree.FuncPtrTypeNode funcPtrTypeNode:
                return ownershipPrefix + "fn_" + string.Join("_", funcPtrTypeNode.typeArguments.Select(BuildTemplateTypeNameFromSyntax));
            default:
                return ownershipPrefix + typeNode.GetType().Name;
        }
    }

    /// <summary>查找命名类型模板定义。</summary>
    private NamedTypeTemplateDefinition FindNamedTypeTemplate(string name,
        Dictionary<string, NamedTypeTemplateDefinition> localTemplates,
        Dictionary<string, NamedTypeTemplateDefinition> depTemplates)
    {
        if(localTemplates.TryGetValue(name, out var local))
            return local;
        if(depTemplates.TryGetValue(name, out var dep))
            return dep;
        return null;
    }

    /// <summary>判断命名类型模板特化是否已存在。</summary>
    private bool IsNamedTypeSpecializationAvailable(NamedTypeTemplateKind kind, string mangledName)
    {
        if(ast.rootNode?.statementsNode?.statements != null)
        {
            foreach(var stmt in ast.rootNode.statementsNode.statements)
            {
                if(kind == NamedTypeTemplateKind.Class
                    && stmt is SyntaxTree.ClassDeclareNode classDecl
                    && !classDecl.isTemplateClass
                    && classDecl.classNameNode.FullName == mangledName)
                {
                    return true;
                }

                if(kind == NamedTypeTemplateKind.Struct
                    && stmt is SyntaxTree.StructDeclareNode structDecl
                    && !structDecl.isTemplateStruct
                    && structDecl.structNameNode.FullName == mangledName)
                {
                    return true;
                }
            }
        }

        foreach(var dep in ilUnit.dependencyLibs)
        {
            var rec = dep.QueryTopSymbol(mangledName);
            if(rec == null)
                continue;

            if(kind == NamedTypeTemplateKind.Class && rec.category == SymbolTable.RecordCatagory.Class)
                return true;
            if(kind == NamedTypeTemplateKind.Struct && rec.category == SymbolTable.RecordCatagory.Struct)
                return true;
        }

        return false;
    }

    /// <summary>应用命名类型模板特化并替换模板参数。</summary>
    private void ApplyNamedTypeTemplateSpecialization(NamedTypeTemplateDefinition templateDef,
        SyntaxTree.DeclareNode specialized,
        string mangledName,
        List<SyntaxTree.TypeNode> typeArguments)
    {
        List<IdentityNode> paramList;
        if(templateDef.kind == NamedTypeTemplateKind.Class)
        {
            paramList = ((SyntaxTree.ClassDeclareNode)templateDef.templateNode).templateParameters;
        }
        else
        {
            paramList = ((SyntaxTree.StructDeclareNode)templateDef.templateNode).templateParameters;
        }

        if(paramList.Count != typeArguments.Count)
            throw new SemanticException(ExceptioName.SemanticAnalysysError, templateDef.templateNode, "template argument count mismatch");

        if(templateDef.kind == NamedTypeTemplateKind.Class)
        {
            var specializedClass = (SyntaxTree.ClassDeclareNode)specialized;
            specializedClass.isTemplateClass = false;
            specializedClass.templateParameters.Clear();
            specializedClass.classNameNode.SetPrefix(null);
            specializedClass.classNameNode.token.attribute = mangledName;
            specializedClass.classNameNode.identiferType = SyntaxTree.IdentityNode.IdType.TypeName;
        }
        else
        {
            var specializedStruct = (SyntaxTree.StructDeclareNode)specialized;
            specializedStruct.isTemplateStruct = false;
            specializedStruct.templateParameters.Clear();
            specializedStruct.structNameNode.SetPrefix(null);
            specializedStruct.structNameNode.token.attribute = mangledName;
            specializedStruct.structNameNode.identiferType = SyntaxTree.IdentityNode.IdType.TypeName;
        }

        var typeMap = new Dictionary<string, SyntaxTree.TypeNode>();
        for(int i = 0; i < paramList.Count; ++i)
        {
            typeMap[paramList[i].FullName] = typeArguments[i];
            typeMap[paramList[i].token.attribute] = typeArguments[i];
        }

        ApplyTemplateTypeReplacement(specialized, typeMap);
    }

    private void ApplyTemplateTypeReplacement(SyntaxTree.Node node, Dictionary<string, SyntaxTree.TypeNode> typeMap)
    {
        if(node == null)
            return;

        switch(node)
        {
            case SyntaxTree.ConstantDeclareNode constDecl:
                constDecl.typeNode = ReplaceTypeNode(constDecl.typeNode, typeMap, constDecl);
                break;
            case SyntaxTree.VarDeclareNode varDecl:
                varDecl.typeNode = ReplaceTypeNode(varDecl.typeNode, typeMap, varDecl);
                break;
            case SyntaxTree.ParameterNode param:
                param.typeNode = ReplaceTypeNode(param.typeNode, typeMap, param);
                break;
            case SyntaxTree.FuncDeclareNode funcDecl:
                funcDecl.returnTypeNode = ReplaceTypeNode(funcDecl.returnTypeNode, typeMap, funcDecl);
                break;
            case SyntaxTree.ExternFuncDeclareNode externDecl:
                externDecl.returnTypeNode = ReplaceTypeNode(externDecl.returnTypeNode, typeMap, externDecl);
                break;
            case SyntaxTree.OwnershipCaptureStmtNode captureNode:
                captureNode.typeNode = ReplaceTypeNode(captureNode.typeNode, typeMap, captureNode);
                break;
            case SyntaxTree.OwnershipLeakStmtNode leakNode:
                leakNode.typeNode = ReplaceTypeNode(leakNode.typeNode, typeMap, leakNode);
                break;
            case SyntaxTree.DefaultValueNode defaultNode:
                defaultNode.typeNode = ReplaceTypeNode(defaultNode.typeNode, typeMap, defaultNode);
                break;
            case SyntaxTree.CastNode castNode:
                castNode.typeNode = ReplaceTypeNode(castNode.typeNode, typeMap, castNode);
                break;
            case SyntaxTree.NewArrayNode newArrayNode:
                newArrayNode.typeNode = ReplaceTypeNode(newArrayNode.typeNode, typeMap, newArrayNode);
                break;
            case SyntaxTree.NewObjectNode newObjNode:
                if(newObjNode.className.FullName.Contains("BBB"))
                    throw new Exception();

                if(newObjNode.typeNode != null)
                    newObjNode.typeNode = ReplaceTypeNode(newObjNode.typeNode, typeMap, newObjNode);
                if(newObjNode.className != null && typeMap.TryGetValue(newObjNode.className.FullName, out var replType) && replType is SyntaxTree.NamedTypeNode replClass)
                {
                    newObjNode.className = (SyntaxTree.IdentityNode)replClass.classname.DeepClone();
                    newObjNode.className.Parent = newObjNode;
                }
                break;
            case SyntaxTree.CallNode callNode:
                for(int i = 0; i < callNode.tempGenericArguments.Count; ++i)
                {
                    var replacedArg = ReplaceTypeNode(callNode.tempGenericArguments[i], typeMap, callNode);
                    callNode.tempGenericArguments[i] = replacedArg;

                    if(i < callNode.genericArguments.Count)
                    {
                        callNode.genericArguments[i] = replacedArg;
                    }
                }

                for(int i = callNode.tempGenericArguments.Count; i < callNode.genericArguments.Count; ++i)
                {
                    callNode.genericArguments[i] = ReplaceTypeNode(callNode.genericArguments[i], typeMap, callNode);
                }
                break;
            case SyntaxTree.NamedTypeNode classTypeNode:
                for(int i = 0; i < classTypeNode.tempGenericArguments.Count; ++i)
                {
                    var replacedArg = ReplaceTypeNode(classTypeNode.tempGenericArguments[i], typeMap, classTypeNode);
                    classTypeNode.tempGenericArguments[i] = replacedArg;

                    if(i < classTypeNode.genericArguments.Count)
                    {
                        classTypeNode.genericArguments[i] = replacedArg;
                    }
                }

                for(int i = classTypeNode.tempGenericArguments.Count; i < classTypeNode.genericArguments.Count; ++i)
                {
                    classTypeNode.genericArguments[i] = ReplaceTypeNode(classTypeNode.genericArguments[i], typeMap, classTypeNode);
                }
                break;
        }

        foreach(var child in node.Children())
        {
            ApplyTemplateTypeReplacement(child, typeMap);
        }
    }

    private SyntaxTree.TypeNode ReplaceTypeNode(SyntaxTree.TypeNode typeNode, Dictionary<string, SyntaxTree.TypeNode> typeMap, SyntaxTree.Node parent)
    {
        if(typeNode == null)
            return null;

        switch(typeNode)
        {
            case SyntaxTree.NamedTypeNode classTypeNode:
                if(typeMap.TryGetValue(classTypeNode.classname.FullName, out var replacement) && classTypeNode.tempGenericArguments.Count == 0)
                {
                    var cloned = (SyntaxTree.TypeNode)replacement.DeepClone();
                    cloned.Parent = parent;
                    return cloned;
                }

                for(int i = 0; i < classTypeNode.tempGenericArguments.Count; ++i)
                {
                    var replacedArg = ReplaceTypeNode(classTypeNode.tempGenericArguments[i], typeMap, classTypeNode);
                    classTypeNode.tempGenericArguments[i] = replacedArg;

                    if(i < classTypeNode.genericArguments.Count)
                    {
                        classTypeNode.genericArguments[i] = replacedArg;
                    }
                }

                for(int i = classTypeNode.tempGenericArguments.Count; i < classTypeNode.genericArguments.Count; ++i)
                {
                    classTypeNode.genericArguments[i] = ReplaceTypeNode(classTypeNode.genericArguments[i], typeMap, classTypeNode);
                }

                classTypeNode.Parent = parent;
                return classTypeNode;
            case SyntaxTree.ArrayTypeNode arrayTypeNode:
                arrayTypeNode.elemtentType = ReplaceTypeNode(arrayTypeNode.elemtentType, typeMap, arrayTypeNode);
                arrayTypeNode.Parent = parent;
                return arrayTypeNode;
            case SyntaxTree.FuncPtrTypeNode funcPtrTypeNode:
                for(int i = 0; i < funcPtrTypeNode.typeArguments.Count; ++i)
                {
                    funcPtrTypeNode.typeArguments[i] = ReplaceTypeNode(funcPtrTypeNode.typeArguments[i], typeMap, funcPtrTypeNode);
                }
                funcPtrTypeNode.Parent = parent;
                return funcPtrTypeNode;
            default:
                typeNode.Parent = parent;
                return typeNode;
        }
    }

    //检查模板定义的命名空间前缀
    private void TemplateCompleteDefineFullName(SyntaxTree.IdentityNode idNode)
    {
        SyntaxTree.Node curr = idNode;
        for(int i = 0; i < 99; ++i)
        {
            curr = curr.Parent;
            if(curr == null)
            {
                return;
            }
            if(curr is SyntaxTree.NamespaceNode namespaceNode)
            {
                idNode.SetPrefix(namespaceNode.namepsaceNode.FullName);
                return;
            }
        }
    }

    //检查补全模板实例处的命名空间前缀  
    private void TemplateTryMatchNamespace(SyntaxTree.IdentityNode templateNameNode)
    {
        if(templateNameNode.token.attribute.Contains("::"))
            return;


        //不存在模板  
        if(templateFullNameDict.TryGetValue(templateNameNode.token.attribute, out var fullnameList) == false)
        {
            throw new SemanticException(ExceptioName.SemanticAnalysysError, templateNameNode, $"template not exist:{templateNameNode.FullName}");
        }

        //优先使用不带名称空间的模板  
        if(fullnameList.Contains(templateNameNode.token.attribute))
        {
            templateNameNode.SetPrefix(null);
        }
        //匹配命名空间  
        else
        {
            int match = 0;
            string matchNamespace = null;
            foreach(var prefix in availableNamespacePrefixList)
            {
                string possibleFullName = prefix + templateNameNode.token.attribute;
                if(fullnameList.Contains(possibleFullName))
                {
                    match++;
                    matchNamespace = prefix.Substring(0, prefix.Length - 2);//去掉`::`
                }
                if(match > 1)
                {
                    //命名空间歧义  
                    throw new SemanticException(ExceptioName.IdentifierAmbiguousBetweenNamespaces, templateNameNode, string.Empty);
                }
            }


            if(match == 0)
            {
                //需要引用命名空间  
                throw new SemanticException(ExceptioName.SemanticAnalysysError, templateNameNode, "need using namespace.");
            }
            else
            {
                templateNameNode.SetPrefix(matchNamespace);
            }
        }
    }

}
