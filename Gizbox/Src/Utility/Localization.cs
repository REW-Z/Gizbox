using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Gizbox
{
    public enum SystemLanguage
    {
        EN_US,
        ZH_CN,
    }
    public class Localization
    {
        public static SystemLanguage systemLanguage = SystemLanguage.EN_US;

        private static Dictionary<string, string> EN = new Dictionary<string, string>();
        private static Dictionary<string, string> ZH = new Dictionary<string, string>();

        private static bool isInitialized = false;
        
        private static void Init()
        {
            if (isInitialized) return;

            HardCodeKeyValue(ExceptioName.LibraryLoadPathNotSet.ToString(), null, "没有设置库加载目录");
            HardCodeKeyValue(ExceptioName.LibraryFileNotFound.ToString(), null, "找不到库文件");
            HardCodeKeyValue(ExceptioName.LibraryDependencyCannotBeEmpty.ToString(), null, "依赖的库不能为空");
            HardCodeKeyValue(ExceptioName.LibraryFileNameMismatch.ToString(), null, "库文件名和库名不匹配");
            HardCodeKeyValue(ExceptioName.LabelNotFound.ToString(), null, "标号未找到");
            HardCodeKeyValue(ExceptioName.TypeNotFound.ToString(), null, "查询不到类型");
            HardCodeKeyValue(ExceptioName.OperationTypeError.ToString(), null, "运算类型错误");
            HardCodeKeyValue(ExceptioName.ExpressionNodeIsNull.ToString(), null, "表达式节点为空");
            HardCodeKeyValue(ExceptioName.ProductionNotFound.ToString(), null, "找不到产生式");
            HardCodeKeyValue(ExceptioName.TypeConversionError.ToString(), null, "类型转换错误");
            HardCodeKeyValue(ExceptioName.AssignmentTypeError.ToString(), null, "赋值类型错误");
            HardCodeKeyValue(ExceptioName.CannotAssignToConstant.ToString(), null, "不能对常数赋值");
            HardCodeKeyValue(ExceptioName.CannotAssignToLiteral.ToString(), null, "不能对字面量赋值");
            HardCodeKeyValue(ExceptioName.TacError.ToString(), null, "三地址码错误");
            HardCodeKeyValue(ExceptioName.UnknownConstant.ToString(), null, "未知常量");
            HardCodeKeyValue(ExceptioName.UnknownLiteral.ToString(), null, "未知字面量");
            HardCodeKeyValue(ExceptioName.NotPointerToConstant.ToString(), null, "不是指向常量的指针");
            HardCodeKeyValue(ExceptioName.InvalidHeapWrite.ToString(), null, "堆写入无效");
            HardCodeKeyValue(ExceptioName.NoInstructionsToExecute.ToString(), null, "没有指令要执行");
            HardCodeKeyValue(ExceptioName.ArgumentError.ToString(), null, "实参错误");
            HardCodeKeyValue(ExceptioName.AccessError.ToString(), null, "错误的访问");

            HardCodeKeyValue(ExceptioName.LexicalAnalysisError.ToString(), null, "词法分析错误");

            HardCodeKeyValue(ExceptioName.SyntaxAnalysisError.ToString(), null, "语法分析错误");

            HardCodeKeyValue(ExceptioName.SemanticAnalysysError.ToString(), null, "语义分析错误");
            HardCodeKeyValue(ExceptioName.SubExpressionNoReturnVariable.ToString(), null, "子表达式无返回变量");
            HardCodeKeyValue(ExceptioName.TypeNotSet.ToString(), null, "type属性未设置");
            HardCodeKeyValue(ExceptioName.FunctionObfuscationNameNotSet.ToString(), null, "函数未设置混淆名");
            HardCodeKeyValue(ExceptioName.EmptyImportNode.ToString(), null, "空的导入节点");
            HardCodeKeyValue(ExceptioName.ExternFunctionGlobalOrNamespaceOnly.ToString(), null, "extern函数只能定义在全局作用域");
            HardCodeKeyValue(ExceptioName.ConstantGlobalOrNamespaceOnly.ToString(), null, "常量只能定义在全局作用域");
            HardCodeKeyValue(ExceptioName.ClassNameCannotBeObject.ToString(), null, "类名不能定义为最终基类名Object");
            HardCodeKeyValue(ExceptioName.ClassDefinitionGlobalOrNamespaceOnly.ToString(), null, "类定义只能在全局或者命名空间顶层");
            HardCodeKeyValue(ExceptioName.NamespaceTopLevelNonMemberFunctionOnly.ToString(), null, "命名空间顶层只能定义非类成员函数");
            HardCodeKeyValue(ExceptioName.BaseClassNotFound.ToString(), null, "未找到基类");
            HardCodeKeyValue(ExceptioName.ConstantTypeDeclarationError.ToString(), null, "常量类型声明错误");
            HardCodeKeyValue(ExceptioName.VariableTypeDeclarationError.ToString(), null, "变量类型声明错误");
            HardCodeKeyValue(ExceptioName.MissingReturnStatement.ToString(), null, "没有return语句");
            HardCodeKeyValue(ExceptioName.ReturnTypeError.ToString(), null, "返回值类型错误");
            HardCodeKeyValue(ExceptioName.InvalidDeleteStatement.ToString(), null, "错误的delete语句");
            HardCodeKeyValue(ExceptioName.ClassSymbolTableNotFound.ToString(), null, "类符号表不存在");
            HardCodeKeyValue(ExceptioName.NodeNoInitializationPropertyList.ToString(), null, "节点没有初始化属性列表");
            HardCodeKeyValue(ExceptioName.ClassMemberFunctionThisKeywordMissing.ToString(), null, "在类成员函数中的类成员标识符没有加this关键字");
            HardCodeKeyValue(ExceptioName.IdentifierNotFound.ToString(), null, "找不到标识符");
            HardCodeKeyValue(ExceptioName.LiteralTypeUnknown.ToString(), null, "未知的Literal类型");
            HardCodeKeyValue(ExceptioName.MissingThisPtrInSymbolTable.ToString(), null, "符号表中找不到this指针");
            HardCodeKeyValue(ExceptioName.ClassNameNotFound.ToString(), null, "找不到类名");
            HardCodeKeyValue(ExceptioName.ClassScopeNotFound.ToString(), null, "类作用域不存在");
            HardCodeKeyValue(ExceptioName.FunctionMemberNotFound.ToString(), null, "函数成员不存在");
            HardCodeKeyValue(ExceptioName.ObjectMemberNotFunction.ToString(), null, "对象成员不是函数");
            HardCodeKeyValue(ExceptioName.FunctionNotFound.ToString(), null, "函数未找到");
            HardCodeKeyValue(ExceptioName.ClassDefinitionNotFound.ToString(), null, "找不到类定义");
            HardCodeKeyValue(ExceptioName.InconsistentExpressionTypesCannotCompare.ToString(), null, "类型不一致的表达式不能比较");
            HardCodeKeyValue(ExceptioName.BinaryOperationTypeMismatch.ToString(), null, "二元运算两边类型不一致");
            HardCodeKeyValue(ExceptioName.CannotAnalyzeExpressionNodeType.ToString(), null, "无法分析表达式节点类型");
            HardCodeKeyValue(ExceptioName.MemberFieldNotFound.ToString(), null, "成员字段不存在");
            HardCodeKeyValue(ExceptioName.OwnershipError.ToString(), null, "所有权错误");


            HardCodeKeyValue(ExceptioName.ScriptRuntimeError.ToString(), null, "脚本运行时错误");
            HardCodeKeyValue(ExceptioName.ScriptRuntimeUndefineBehaviour.ToString(), null, "未定义行为");
            HardCodeKeyValue(ExceptioName.StackOverflow.ToString(), null, "堆栈溢出");
            HardCodeKeyValue(ExceptioName.StackOverflow.ToString(), null, "脚本运行时错误");
            HardCodeKeyValue(ExceptioName.OnlyHeapObjectsCanBeFreed.ToString(), null, "只能对堆中的对象进行释放");
            HardCodeKeyValue(ExceptioName.AccessedObjectNotFound.ToString(), null, "找不到要访问的对象");
            HardCodeKeyValue(ExceptioName.ObjectTypeError.ToString(), null, "对象类型错误");
            HardCodeKeyValue(ExceptioName.ObjectFieldNotInitialized.ToString(), null, "对象字段未初始化");
            HardCodeKeyValue(ExceptioName.ParameterListParameterNotFound.ToString(), null, "形参列表未找到形参");
            HardCodeKeyValue(ExceptioName.LocalVariableNotInitialized.ToString(), null, "局部变量未初始化");
            HardCodeKeyValue(ExceptioName.GlobalVariableNotInitialized.ToString(), null, "全局变量未初始化");
            HardCodeKeyValue(ExceptioName.NotParameterOrLocalVariable.ToString(), null, "不是参数也不是局部变量");
            HardCodeKeyValue(ExceptioName.StackFrameAndGlobalSymbolTableNameNotFound.ToString(), null, "栈帧和全局符号表都不包含名称");
            HardCodeKeyValue(ExceptioName.OnlyBooleanValuesCanBeNegated.ToString(), null, "只有布尔值能取非");
            HardCodeKeyValue(ExceptioName.TypeCannotBeNegative.ToString(), null, "该类型不能取负数");



            isInitialized = true;
        }

        private static void HardCodeKeyValue(string key, string enValue, string zhValue)
        {
            if(string.IsNullOrEmpty(enValue) == false)
            {
                EN[key] = enValue;
            }
            else
            {
                EN[key] = key;
            }

            if (string.IsNullOrEmpty(zhValue) == false)
            {
                ZH[key] = zhValue;
            }
            else
            {
                ZH[key] = key;
            }
        }

        public static string GetString(string key)
        {
            if (isInitialized == false) Init();

            switch(systemLanguage)
            {
                case SystemLanguage.EN_US:
                    {
                        if (EN.ContainsKey(key)) 
                            return EN[key];
                        else 
                            return "";
                    }
                case SystemLanguage.ZH_CN:
                    {
                        if (EN.ContainsKey(key))
                            return EN[key];
                        else
                            return "";
                    }
                default:
                    {
                        if (EN.ContainsKey(key)) 
                            return EN[key];
                        else 
                            return "";
                    }
            }
        }
    }
}

