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

    //模板类实例信息
    private class ClassTemplateInstance
    {
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





    //模板类特化（AST层面）：收集模板定义、实例化并生成专用类
    private void SpecializeClassTemplates()
    {
        if(ast?.rootNode == null)
            return;

        //收集当前模块的模板类定义
        var templatesLocal = new Dictionary<string, SyntaxTree.ClassDeclareNode>();
        CollectClassTemplates(ast.rootNode, templatesLocal);

        var templatesDeps = new Dictionary<string, SyntaxTree.ClassDeclareNode>();
        foreach(var dep in ilUnit.dependencyLibs)
        {
            dep.EnsureAst();
            if(dep.ast?.rootNode == null)
                continue;

            CollectClassTemplates(dep.ast.rootNode, templatesDeps);
        }

        //生成ShortName ->  [FullName]映射（辅助命名空间匹配）  
        foreach(var t in templatesLocal)
        {
            var shortname = t.Value.classNameNode.token.attribute;
            var fullname = t.Value.classNameNode.FullName;
            if(templateFullNameDict.TryGetValue(shortname, out var list) == false)
            {
                list = new();
                templateFullNameDict[shortname] = list;
            }
            list.Add(fullname);
        }
        foreach (var t in templatesDeps)
        {
            var shortname = t.Value.classNameNode.token.attribute;
            var fullname = t.Value.classNameNode.FullName;
            if (templateFullNameDict.TryGetValue(shortname, out var list) == false)
            {
                list = new();
                templateFullNameDict[shortname] = list;
            }
            list.Add(fullname);
        }



        //记录所有模板实例化信息（类型引用和new表达式）
        var instances = new Dictionary<string, ClassTemplateInstance>();
        CollectClassTemplateInstantiations(ast.rootNode, instances, inTemplate: false);

        var newSpecializations = new List<SyntaxTree.ClassDeclareNode>();


        foreach(var inst in instances.Values)
        {
            if(IsSpecializationAvailable(inst.mangledName))
                continue;

            var templateNode = FindTemplateClass(inst.templateName, templatesLocal, templatesDeps);
            if(templateNode == null)
            {
                continue;
            }

            var specialized = (SyntaxTree.ClassDeclareNode)templateNode.DeepClone();
            ApplyTemplateSpecialization(templateNode, specialized, inst.mangledName, inst.typeArguments);
            newSpecializations.Add(specialized);
        }

        if(newSpecializations.Count > 0)
        {
            for(int i = newSpecializations.Count - 1; i >= 0; --i)
            {
                ast.rootNode.statementsNode.statements.Insert(0, newSpecializations[i]);
            }
        }
    }

    //收集模板类定义
    private void CollectClassTemplates(SyntaxTree.Node node, Dictionary<string, SyntaxTree.ClassDeclareNode> templates)
    {
        //遍历语法树收集模板类定义
        if(node == null)
            return;

        if(node is SyntaxTree.ClassDeclareNode classDecl && classDecl.isTemplateClass)
        {
            TemplateCompleteDefineFullName(classDecl.classNameNode);
            templates[classDecl.classNameNode.FullName] = classDecl;
            return;
        }

        foreach(var child in node.Children())
        {
            CollectClassTemplates(child, templates);
        }
    }

    //收集模板类实例化信息
    private void CollectClassTemplateInstantiations(SyntaxTree.Node node, Dictionary<string, ClassTemplateInstance> instances, bool inTemplate)
    {
        if(node == null)
            return;

        if(node is SyntaxTree.ClassDeclareNode classDecl && classDecl.isTemplateClass)
        {
            inTemplate = true;
        }

        if(!inTemplate)
        {
            if(node is SyntaxTree.ClassTypeNode classType && classType.genericArguments.Count > 0)
            {
                //记录类型引用处的模板实例
                TemplateTryMatchNamespace(classType.classname);
                RegisterTemplateInstance(classType, instances);
                NormalizeGenericUsage(classType);
            }

            if(node is SyntaxTree.NewObjectNode newObjNode && newObjNode.typeNode is SyntaxTree.ClassTypeNode newObjTypeNode && newObjTypeNode.genericArguments.Count > 0)
            {
                //记录new处的模板实例并规范化名称
                TemplateTryMatchNamespace(newObjTypeNode.classname);
                RegisterTemplateInstance(newObjTypeNode, instances);
                NormalizeGenericUsage(newObjNode, newObjTypeNode);
            }
        }

        foreach(var child in node.Children())
        {
            CollectClassTemplateInstantiations(child, instances, inTemplate);
        }

        if(node is SyntaxTree.CallNode callNode)
        {
            foreach(var arg in callNode.genericArguments)
            {
                CollectClassTemplateInstantiations(arg, instances, inTemplate);
            }
        }

        if(node is SyntaxTree.ClassTypeNode classTypeWithArgs)
        {
            foreach(var arg in classTypeWithArgs.genericArguments)
            {
                CollectClassTemplateInstantiations(arg, instances, inTemplate);
            }
        }
    }

    //特化函数模板：收集模板定义、实例化并生成专用函数
    private void SpecializeFunctionTemplates()
    {
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
        CollectFunctionTemplateInstantiations(ast.rootNode, instances, inTemplate: false);

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
    private void CollectFunctionTemplateInstantiations(SyntaxTree.Node node, Dictionary<string, FunctionTemplateInstance> instances, bool inTemplate)
    {
        if(node == null)
            return;

        if(node is SyntaxTree.FuncDeclareNode funcDecl && funcDecl.isTemplateFunction)
        {
            inTemplate = true;
        }

        if(!inTemplate && node is SyntaxTree.CallNode callNode && callNode.isMemberAccessFunction == false && callNode.genericArguments.Count > 0)
        {
            if(callNode.funcNode is SyntaxTree.IdentityNode funcId)
            {
                //记录函数调用处的模板实例
                TemplateTryMatchNamespace(funcId);
                RegisterFunctionTemplateInstance(funcId, callNode, instances);
            }
        }

        foreach(var child in node.Children())
        {
            CollectFunctionTemplateInstantiations(child, instances, inTemplate);
        }
    }

    //注册函数模板实例
    private void RegisterFunctionTemplateInstance(SyntaxTree.IdentityNode funcId, SyntaxTree.CallNode callNode, Dictionary<string, FunctionTemplateInstance> instances)
    {
        var argTypes = callNode.genericArguments.Select(t => t.TypeExpression()).ToArray();
        var mangledBaseName = Utils.MangleTemplateInstanceName(funcId.FullName, argTypes);
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
            typeArguments = callNode.genericArguments.ToList(),
        };

        NormalizeFunctionGenericUsage(callNode, mangledBaseName);
    }

    //规范化函数模板实例调用（清理泛型参数并替换函数名）
    private void NormalizeFunctionGenericUsage(SyntaxTree.CallNode callNode, string mangledBaseName)
    {
        callNode.genericArguments.Clear();

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
            dep.EnsureAst();
            if(dep.ast?.rootNode?.statementsNode?.statements == null)
                continue;

            foreach(var stmt in dep.ast.rootNode.statementsNode.statements)
            {
                if(stmt is SyntaxTree.FuncDeclareNode funcDecl && !funcDecl.isTemplateFunction && funcDecl.identifierNode.FullName == mangledBaseName)
                    return true;
            }
        }

        return false;
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

    //注册模板类实例
    private void RegisterTemplateInstance(SyntaxTree.ClassTypeNode classType, Dictionary<string, ClassTemplateInstance> instances)
    {
        var mangledName = classType.TypeExpression();
        if(instances.ContainsKey(mangledName))
            return;

        instances[mangledName] = new ClassTemplateInstance
        {
            templateName = classType.classname.FullName,
            mangledName = mangledName,
            typeArguments = classType.genericArguments.ToList(),
        };
    }

    //规范化模板类类型引用（清理泛型参数并替换类型名）
    private void NormalizeGenericUsage(SyntaxTree.ClassTypeNode classType)
    {
        var mangledName = classType.TypeExpression();
        classType.genericArguments.Clear();
        classType.classname.SetPrefix(null);
        classType.classname.token.attribute = mangledName;
    }

    //规范化new语句中的模板类型引用
    private void NormalizeGenericUsage(SyntaxTree.NewObjectNode newObjNode, SyntaxTree.ClassTypeNode classType)
    {
        var mangledName = classType.TypeExpression();
        classType.genericArguments.Clear();
        classType.classname.SetPrefix(null);
        classType.classname.token.attribute = mangledName;

        newObjNode.className.SetPrefix(null);
        newObjNode.className.token.attribute = mangledName;
    }

    private SyntaxTree.ClassDeclareNode FindTemplateClass(string name,
        Dictionary<string, SyntaxTree.ClassDeclareNode> localTemplates,
        Dictionary<string, SyntaxTree.ClassDeclareNode> depTemplates)
    {
        if(localTemplates.TryGetValue(name, out var local))
            return local;
        if(depTemplates.TryGetValue(name, out var dep))
            return dep;
        return null;
    }

    private bool IsSpecializationAvailable(string mangledName)
    {
        if(ast.rootNode?.statementsNode?.statements != null)
        {
            foreach(var stmt in ast.rootNode.statementsNode.statements)
            {
                if(stmt is SyntaxTree.ClassDeclareNode classDecl && !classDecl.isTemplateClass && classDecl.classNameNode.FullName == mangledName)
                    return true;
            }
        }

        foreach(var dep in ilUnit.dependencyLibs)
        {
            if(dep.QueryTopSymbol(mangledName) != null)
                return true;
        }

        return false;
    }

    //应用类模板特化（替换模板参数并生成具体类）
    private void ApplyTemplateSpecialization(SyntaxTree.ClassDeclareNode template,
        SyntaxTree.ClassDeclareNode specialized,
        string mangledName,
        List<SyntaxTree.TypeNode> typeArguments)
    {
        var paramList = template.templateParameters;
        if(paramList.Count != typeArguments.Count)
            throw new SemanticException(ExceptioName.SemanticAnalysysError, template, "template argument count mismatch");

        specialized.isTemplateClass = false;
        specialized.templateParameters.Clear();

        specialized.classNameNode.SetPrefix(null);
        specialized.classNameNode.token.attribute = mangledName;
        specialized.classNameNode.SetPrefix(null);
        specialized.classNameNode.identiferType = SyntaxTree.IdentityNode.IdType.Class;

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
                if(newObjNode.typeNode != null)
                    newObjNode.typeNode = ReplaceTypeNode(newObjNode.typeNode, typeMap, newObjNode);
                if(newObjNode.className != null && typeMap.TryGetValue(newObjNode.className.FullName, out var replType) && replType is SyntaxTree.ClassTypeNode replClass)
                {
                    newObjNode.className = (SyntaxTree.IdentityNode)replClass.classname.DeepClone();
                    newObjNode.className.Parent = newObjNode;
                }
                break;
            case SyntaxTree.ClassTypeNode classTypeNode:
                for(int i = 0; i < classTypeNode.genericArguments.Count; ++i)
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
            case SyntaxTree.ClassTypeNode classTypeNode:
                if(typeMap.TryGetValue(classTypeNode.classname.FullName, out var replacement) && classTypeNode.genericArguments.Count == 0)
                {
                    var cloned = (SyntaxTree.TypeNode)replacement.DeepClone();
                    cloned.Parent = parent;
                    return cloned;
                }
                for(int i = 0; i < classTypeNode.genericArguments.Count; ++i)
                {
                    classTypeNode.genericArguments[i] = ReplaceTypeNode(classTypeNode.genericArguments[i], typeMap, classTypeNode);
                }
                classTypeNode.Parent = parent;
                return classTypeNode;
            case SyntaxTree.ArrayTypeNode arrayTypeNode:
                arrayTypeNode.elemtentType = ReplaceTypeNode(arrayTypeNode.elemtentType, typeMap, arrayTypeNode);
                arrayTypeNode.Parent = parent;
                return arrayTypeNode;
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
