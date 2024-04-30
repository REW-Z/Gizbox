using System;
using System.Collections.Generic;
using System.Text;

namespace FanLang
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

            //语句容器  
            "statements",
            "statementblock",

            "declstatements",
            
            //语句
            "stmt",
            "declstmt",

            //子句  
            "elifclauselist",
            "elifclause",
            "elseclause",
            
            //复合语句表达式  
            "stmtexpr",//特殊的表达式（不属于expr）(仅作为几种特殊表达式的集合)
            "assign",
            "call",
            "newobj",
            "incdec",//++\--

            //类型  
            "type",
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
            "params",
            "args",
        };

        public List<string> productionExpressions = new List<string>() {
            "S -> importations statements",

            "importations -> importations importation",
            "importations -> importation",
            "importations -> ε",
            "importation -> import < LITSTRING >",

            "statements -> statements stmt",
            "statements -> stmt",
            "statements -> ε",

            "statementblock -> { statements }",

            "declstatements -> declstatements declstmt",
            "declstatements -> declstmt",
            "declstatements -> ε",

            "stmt -> statementblock",
            "stmt -> declstmt",
            "stmt -> stmtexpr ;",
            "stmt -> break ;",
            "stmt -> return expr ;",
            "stmt -> return ;",
            "stmt -> while ( expr ) stmt",
            "stmt -> for ( stmt bexpr ; stmtexpr ) stmt",
            "stmt -> if ( expr ) stmt elifclauselist elseclause",

            "declstmt -> type ID = expr ;",
            "declstmt -> type ID ( params ) { statements }",
            "declstmt -> extern type ID ( params ) ;",
            "declstmt -> class ID { declstatements }",

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


            "type -> primitive",
            "type -> ID",
            "primitive -> void",
            "primitive -> bool",
            "primitive -> int",
            "primitive -> float",
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
            "term -> factor",
            
            "factor -> incdec",
            "factor -> ! factor",
            "factor -> - factor",
            "factor -> cast",
            "factor -> primary",

            
            "primary -> ( expr )",
            "primary -> ID",
            "primary -> memberaccess",
            "primary -> call",
            "primary -> newobj",
            "primary -> lit",

            "incdec -> ++ ID",
            "incdec -> -- ID",
            "incdec -> ID ++",
            "incdec -> ID --",
            "call -> ID ( args )",
            "call -> memberaccess ( args )",
            "newobj -> new ID ( )",

            "cast -> ( type ) factor",

            "memberaccess -> primary . ID",
            "memberaccess -> this . ID",

            "lit -> LITINT",
            "lit -> LITFLOAT",
            "lit -> LITSTRING",
            "lit -> LITBOOL",



            "params -> ε",
            "params -> type ID",
            "params -> params , type ID",

            "args -> ε",
            "args -> expr",
            "args -> args , expr",
        };
    }
}
