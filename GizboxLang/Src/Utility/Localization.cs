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

            HardCodeKeyValue(ExceptionType.LibraryLoadPathNotSet.ToString(), null, "没有设置库加载目录");
            HardCodeKeyValue(ExceptionType.LibraryFileNotFound.ToString(), null, "找不到库文件");
            HardCodeKeyValue(ExceptionType.LibraryDependencyCannotBeEmpty.ToString(), null, "依赖的库不能为空");
            HardCodeKeyValue(ExceptionType.LibraryFileNameMismatch.ToString(), null, "库文件名和库名不匹配");
            HardCodeKeyValue(ExceptionType.LabelNotFound.ToString(), null, "标号未找到");
            HardCodeKeyValue(ExceptionType.TypeNotFound.ToString(), null, "查询不到类型");
            HardCodeKeyValue(ExceptionType.OperationTypeError.ToString(), null, "运算类型错误");
            HardCodeKeyValue(ExceptionType.ExpressionNodeIsNull.ToString(), null, "表达式节点为空");
            HardCodeKeyValue(ExceptionType.ProductionNotFound.ToString(), null, "找不到产生式");
            HardCodeKeyValue(ExceptionType.TypeConversionError.ToString(), null, "类型转换错误");
            HardCodeKeyValue(ExceptionType.AssignmentTypeError.ToString(), null, "赋值类型错误");
            HardCodeKeyValue(ExceptionType.CannotAssignToConstant.ToString(), null, "不能对常数赋值");
            HardCodeKeyValue(ExceptionType.CannotAssignToLiteral.ToString(), null, "不能对字面量赋值");
            HardCodeKeyValue(ExceptionType.TacError.ToString(), null, "三地址码错误");
            HardCodeKeyValue(ExceptionType.UnknownConstant.ToString(), null, "未知常量");
            HardCodeKeyValue(ExceptionType.UnknownLiteral.ToString(), null, "未知字面量");
            HardCodeKeyValue(ExceptionType.NotPointerToConstant.ToString(), null, "不是指向常量的指针");
            HardCodeKeyValue(ExceptionType.InvalidHeapWrite.ToString(), null, "堆写入无效");
            HardCodeKeyValue(ExceptionType.StackOverflow.ToString(), null, "堆栈溢出");
            HardCodeKeyValue(ExceptionType.NoInstructionsToExecute.ToString(), null, "没有指令要执行");
            HardCodeKeyValue(ExceptionType.ArgumentError.ToString(), null, "实参错误");
            HardCodeKeyValue(ExceptionType.AccessError.ToString(), null, "错误的访问");
            HardCodeKeyValue(ExceptionType.LexicalAnalysisError.ToString(), null, "词法分析错误");
            HardCodeKeyValue(ExceptionType.SyntaxAnalysisError.ToString(), null, "语法分析错误");
            HardCodeKeyValue(ExceptionType.SubExpressionNoReturnVariable.ToString(), null, "子表达式无返回变量");
            HardCodeKeyValue(ExceptionType.TypeNotSet.ToString(), null, "type属性未设置");
            HardCodeKeyValue(ExceptionType.FunctionObfuscationNameNotSet.ToString(), null, "函数未设置混淆名");
            HardCodeKeyValue(ExceptionType.EmptyImportNode.ToString(), null, "空的导入节点");
            HardCodeKeyValue(ExceptionType.ExternFunctionGlobalOrNamespaceOnly.ToString(), null, "extern函数只能定义在全局或者命名空间顶层");
            HardCodeKeyValue(ExceptionType.ClassNameCannotBeObject.ToString(), null, "类名不能定义为最终基类名Object");
            HardCodeKeyValue(ExceptionType.ClassDefinitionGlobalOrNamespaceOnly.ToString(), null, "类定义只能在全局或者命名空间顶层");
            HardCodeKeyValue(ExceptionType.NamespaceTopLevelNonMemberFunctionOnly.ToString(), null, "命名空间顶层只能定义非类成员函数");
            HardCodeKeyValue(ExceptionType.BaseClassNotFound.ToString(), null, "未找到基类");
            HardCodeKeyValue(ExceptionType.VariableTypeDeclarationError.ToString(), null, "变量类型声明错误");
            HardCodeKeyValue(ExceptionType.MissingReturnStatement.ToString(), null, "没有return语句");
            HardCodeKeyValue(ExceptionType.ReturnTypeError.ToString(), null, "返回值类型错误");
            HardCodeKeyValue(ExceptionType.InvalidDeleteStatement.ToString(), null, "错误的delete语句");
            HardCodeKeyValue(ExceptionType.ClassSymbolTableNotFound.ToString(), null, "类符号表不存在");
            HardCodeKeyValue(ExceptionType.NodeNoInitializationPropertyList.ToString(), null, "节点没有初始化属性列表");
            HardCodeKeyValue(ExceptionType.ClassMemberFunctionThisKeywordMissing.ToString(), null, "在类成员函数中的类成员标识符没有加this关键字");
            HardCodeKeyValue(ExceptionType.IdentifierNotFound.ToString(), null, "找不到标识符");
            HardCodeKeyValue(ExceptionType.LiteralTypeUnknown.ToString(), null, "未知的Literal类型");
            HardCodeKeyValue(ExceptionType.MissingThisPtrInSymbolTable.ToString(), null, "符号表中找不到this指针");
            HardCodeKeyValue(ExceptionType.ClassNameNotFound.ToString(), null, "找不到类名");
            HardCodeKeyValue(ExceptionType.ClassScopeNotFound.ToString(), null, "类作用域不存在");
            HardCodeKeyValue(ExceptionType.FunctionMemberNotFound.ToString(), null, "函数成员不存在");
            HardCodeKeyValue(ExceptionType.ObjectMemberNotFunction.ToString(), null, "对象成员不是函数");
            HardCodeKeyValue(ExceptionType.FunctionNotFound.ToString(), null, "函数未找到");
            HardCodeKeyValue(ExceptionType.ClassDefinitionNotFound.ToString(), null, "找不到类定义");
            HardCodeKeyValue(ExceptionType.InconsistentExpressionTypesCannotCompare.ToString(), null, "类型不一致的表达式不能比较");
            HardCodeKeyValue(ExceptionType.BinaryOperationTypeMismatch.ToString(), null, "二元运算两边类型不一致");
            HardCodeKeyValue(ExceptionType.CannotAnalyzeExpressionNodeType.ToString(), null, "无法分析表达式节点类型");
            HardCodeKeyValue(ExceptionType.MemberFieldNotFound.ToString(), null, "成员字段不存在");

            HardCodeKeyValue(ExceptionType.ScriptRuntimeError.ToString(), null, "脚本运行时错误");
            HardCodeKeyValue(ExceptionType.OnlyHeapObjectsCanBeFreed.ToString(), null, "只能对堆中的对象进行释放");
            HardCodeKeyValue(ExceptionType.AccessedObjectNotFound.ToString(), null, "找不到要访问的对象");
            HardCodeKeyValue(ExceptionType.ObjectTypeError.ToString(), null, "对象类型错误");
            HardCodeKeyValue(ExceptionType.ObjectFieldNotInitialized.ToString(), null, "对象字段未初始化");
            HardCodeKeyValue(ExceptionType.ParameterListParameterNotFound.ToString(), null, "形参列表未找到形参");
            HardCodeKeyValue(ExceptionType.LocalVariableNotInitialized.ToString(), null, "局部变量未初始化");
            HardCodeKeyValue(ExceptionType.GlobalVariableNotInitialized.ToString(), null, "全局变量未初始化");
            HardCodeKeyValue(ExceptionType.NotParameterOrLocalVariable.ToString(), null, "不是参数也不是局部变量");
            HardCodeKeyValue(ExceptionType.StackFrameAndGlobalSymbolTableNameNotFound.ToString(), null, "栈帧和全局符号表都不包含名称");
            HardCodeKeyValue(ExceptionType.OnlyBooleanValuesCanBeNegated.ToString(), null, "只有布尔值能取非");
            HardCodeKeyValue(ExceptionType.TypeCannotBeNegative.ToString(), null, "该类型不能取负数");



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

