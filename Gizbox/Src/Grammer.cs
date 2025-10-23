using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox
{
    /// <summary>
    /// 生成器输入  
    /// </summary>
    public class Grammer
    {
        public List<string> terminalNames;

        public List<string> nonterminalNames = new List<string>() {

            //起始符  
            "S",

            //导入  
            "importations",
            "importation",
            
            //命名空间    
            "namespaceusings",
            "namespaceusing",


            //语句容器  
            "statements",
            "namespaceblock",
            "statementblock",
            "declstatements",
            
            //语句
            "stmt",
            "declstmt",

            //类型修饰符  
            "tmodf",

            //子句  
            "elifclauselist",
            "elifclause",
            "elseclause",
            
            //复合语句表达式  
            "stmtexpr",//特殊的表达式（不属于expr）(仅作为几种特殊表达式的集合)
            "assign",
            "call",
            "indexaccess",
            "newobj",
            "newarr",
            "incdec",//++\--

            //类型  
            "type",
            "stype",
            "arrtype",
            "primitive",

            //表达式
            "expr",
            "nexpr",//非赋值表达式（包含布尔和普通表达式）  
            "bexpr",//布尔表达式
            "aexpr",//普通表达式
            "term",
            "factor",
            "primary",
            "lit",
            "cast",
            "lvalue",
            "memberaccess",

            //函数过程
            "param", //new
            "params",
            "args",

            
            //其他辅助符号
            "stypeBracket",
            "idBracket",
            "primitiveBracket",
            "optidx",

            "inherit",
        };

        public List<string> productionExpressions = new List<string>() {
            "S -> importations namespaceusings statements",

            "importations -> importations importation",
            "importations -> importation",
            "importations -> ε",
            "importation -> import < LITSTRING >",

            "namespaceusings -> namespaceusings namespaceusing",
            "namespaceusings -> namespaceusing",
            "namespaceusings -> ε",

            "namespaceusing -> using ID ;",





            "statements -> statements stmt",
            "statements -> stmt",
            "statements -> ε",

            "namespaceblock -> namespace ID { statements }",

            "statementblock -> { statements }",

            "declstatements -> declstatements declstmt",
            "declstatements -> declstmt",
            "declstatements -> ε",

            "stmt -> namespaceblock",
            "stmt -> statementblock",
            "stmt -> declstmt",
            "stmt -> stmtexpr ;",
            "stmt -> break ;",
            "stmt -> return expr ;",
            "stmt -> return ;",
            "stmt -> delete expr ;",
            "stmt -> while ( expr ) stmt",
            "stmt -> for ( stmt bexpr ; stmtexpr ) stmt",
            "stmt -> if ( expr ) stmt elifclauselist elseclause",

            "declstmt -> type ID = expr ;",
            "declstmt -> tmodf type ID = expr ;",
            "declstmt -> const type ID = lit ;",

            "declstmt -> type ID ( params ) { statements }",
            "declstmt -> tmodf type ID ( params ) { statements }",
            "declstmt -> type operator ID ( params ) { statements }",
            "declstmt -> tmodf type operator ID ( params ) { statements }",
            "declstmt -> extern type ID ( params ) ;",

            "declstmt -> class ID inherit { declstatements }",
            "declstmt -> class own ID inherit { declstatements }",

            "tmodf -> own",
            "tmodf -> bor",

            "elifclauselist -> ε",
            "elifclauselist -> elifclauselist elifclause",
            "elifclause -> else if ( expr ) stmt",
            "elseclause -> ε",
            "elseclause -> else stmt",

            "assign -> lvalue = expr",
            "assign -> lvalue += expr",
            "assign -> lvalue -= expr",
            "assign -> lvalue *= expr",
            "assign -> lvalue /= expr",
            "assign -> lvalue %= expr",

            "lvalue -> ID",
            "lvalue -> memberaccess",
            "lvalue -> indexaccess",


            "type -> arrtype",
            "type -> stype",
            "type -> var",
            "arrtype -> stypeBracket",
            "stype -> primitive",
            "stype -> ID",
            "primitive -> void",
            "primitive -> bool",
            "primitive -> int",
            "primitive -> long",
            "primitive -> float",
            "primitive -> double",
            "primitive -> char",
            "primitive -> string",


            "expr -> assign",
            "expr -> nexpr",

            "stmtexpr -> assign",
            "stmtexpr -> call",
            "stmtexpr -> incdec",
            "stmtexpr -> newobj",

            "nexpr -> bexpr",
            "nexpr -> aexpr",

            "bexpr -> bexpr && bexpr",
            "bexpr -> bexpr || bexpr",

            "bexpr -> aexpr > aexpr",
            "bexpr -> aexpr < aexpr",
            "bexpr -> aexpr >= aexpr",
            "bexpr -> aexpr <= aexpr",
            "bexpr -> aexpr == aexpr",
            "bexpr -> aexpr != aexpr",
            "bexpr -> factor",

            "aexpr -> aexpr + term",
            "aexpr -> aexpr - term",
            "aexpr -> term",

            "term -> term * factor",
            "term -> term / factor",
            "term -> term % factor",
            "term -> factor",
            
            "factor -> incdec",
            "factor -> ! factor",
            "factor -> - factor",
            "factor -> cast",
            "factor -> primary",

            
            "primary -> ( expr )",
            "primary -> ID",
            "primary -> this",
            "primary -> memberaccess",
            "primary -> indexaccess",
            "primary -> call",
            "primary -> newobj",
            "primary -> newarr",
            "primary -> lit",

            "incdec -> ++ ID",
            "incdec -> -- ID",
            "incdec -> ID ++",
            "incdec -> ID --",

            "call -> ID ( args )",
            "call -> memberaccess ( args )",

            "indexaccess -> idBracket",
            "indexaccess -> memberaccess [ aexpr ]",

            "newobj -> new ID ( )",
            "newarr -> new stypeBracket",

            "cast -> ( type ) factor",

            "memberaccess -> primary . ID",

            "lit -> LITBOOL",
            "lit -> LITINT",
            "lit -> LITLONG",
            "lit -> LITFLOAT",
            "lit -> LITDOUBLE",
            "lit -> LITCHAR",
            "lit -> LITSTRING",
            "lit -> null",


            "param -> tmodf type ID",
            "param -> type ID",
            "params -> params , param",
            "params -> param",
            "params -> ε",
            
            "args -> ε",
            "args -> expr",
            "args -> args , expr",


            "stypeBracket -> idBracket",
            "stypeBracket -> primitiveBracket",
            "idBracket -> ID [ optidx ]",
            "primitiveBracket -> primitive [ optidx ]",
            "optidx -> aexpr",
            "optidx -> ε",


            "inherit -> : ID",
            "inherit -> ε",
        };
    }
}



///注意事项：
///1. 注意避免"错误"/"提前"归约。
///  （lookahead符号的限制有用但是作用有限，不能跨越一个以上符号限制归约动作）        
///  （如果当前状态有两个规约项，它们lookahead是一样的，就会发生归约冲突）    
///  （REW：所以应该尽量减少同一终结符在多个产生式的出现）    
///  （REW：例如如果不加限制，数组访问在分析到“id [”状态时，“id“可能被提前归约类名，就错误变成了类数组定义的前半部分）    
///  （REW：ε最好只用在明确的终结符之前(;、}、)等)）      
///  （REW：左因子化，有公共前缀时，把后缀分歧部分用一个非终结符替代，延迟分支选择）  
///  （REW：右因子化，提取公共后缀在LALR(1)语法中，可以避免reduce/reduce 冲突、以及大量规则共享相同“尾巴”导致项集膨胀。  

///  （AI： LALR 的状态合并会丢失“前缀上下文”。即便 FIRST(tmodf) 与 FIRST(type) 完全不相交，合并后仍可能在同一状态里同时出现两条可归约项，从而在同一前瞻上触发 r/r。）  


///2. 添加语法  
///  （添加终结符和非终结符）
///  （添加产生式）
///  （添加ast节点类型和语义分析动作）
///  （添加语义分析和类型检查）  
///  （添加中间代码生成）  