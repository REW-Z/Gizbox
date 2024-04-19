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

            //语句
            "statements",
            "statementblock",
            "stmt",

            //子句  
            "elifclauselist",
            "elifclause",
            "elseclause",
            
            //复合语句表达式  
            "stmtexpr",
            "assign",
            "call",
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
            "memberaccess",

            //函数过程  
            "params",
            "args",
        };

        public List<string> productionExpressions = new List<string>() {
            "S -> statements",

            "statements -> stmt",
            "statements -> statements stmt",

            "statementblock -> { statements }",

            "stmt -> statementblock",
            "stmt -> type ID = expr ;",
            "stmt -> stmtexpr ;",
            "stmt -> type ID ( params ) { statements }",
            "stmt -> break ;",
            "stmt -> return expr ;",
            "stmt -> while ( expr ) stmt",
            "stmt -> for ( stmt bexpr ; stmtexpr ) stmt",
            "stmt -> if ( expr ) stmt elifclauselist elseclause",

            "elifclauselist -> ε",
            "elifclauselist -> elifclauselist elifclause",
            "elifclause -> else if ( expr ) stmt",
            "elseclause -> ε",
            "elseclause -> else stmt",


            "stmtexpr -> assign",
            "stmtexpr -> call",
            "stmtexpr -> incdec",

            "assign -> ID = expr",
            "assign -> ID += expr",
            "assign -> ID -= expr",
            "assign -> ID *= expr",
            "assign -> ID /= expr",
            "assign -> ID %= expr",



            "type -> primitive",
            "primitive -> void",
            "primitive -> bool",
            "primitive -> int",
            "primitive -> float",
            "primitive -> string",

            "expr -> assign",
            "expr -> nexpr",

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
            "primary -> lit",

            "incdec -> ++ ID",
            "incdec -> -- ID",
            "incdec -> ID ++",
            "incdec -> ID --",
            "call -> ID ( args )",
            "call -> memberaccess ( args )",
            "cast -> ( type ) factor",
            "memberaccess -> primary . ID",

            "lit -> LITINT",
            "lit -> LITFLOAT",
            "lit -> LITSTRING",
            "lit -> LITBOOL",



            "params -> ε",
            "params -> type ID",
            "params -> type ID , params",

            "args -> ε",
            "args -> expr",
            "args -> expr , args",
        };
    }
}
