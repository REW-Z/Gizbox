﻿
## 进度  

（DONE）添加常量池    

（DONE）identifier的token属性改为词素  

（DONE）词法单元中token和语法分析中的terminal对应起来。（给pattern类加一个参数tokenname）    

（DONE）强类型语言 - 完善基本类型  

（DONE）生成抽象语法树（实际可以是一个DAG，用于公共表达式优化）        

（DONE）复制Stack中的attributes到所有的Node中（特别是newElement）  

（DONE）引入字符串常量、浮点数常量等。(常用做法是整数和浮点数不同的token、以及不同的终结符)    

（DONE）ε产生式的项集出错。（已解决：把ε产生式的body改为0个就行，之前是1个null）（TerminalSet中依然用null表示ε）  

（DONE）引入函数定义和函数调用。    

（DONE）引入逻辑运算。    

（DONE）引入条件语句。    

（DONE）引入while循环和for循环。    

            "factor -> call",
            "factor -> assign",
            "stmt -> assign ;"
            "stmt -> call ;"

            C#：只有assign、call、decrease、new 语句可用作表达式

（DONE）引入+=\-=\*=\/=\%=语句。    
（DONE）引入++\--语句。    

（DONE）引入逻辑非和减号（！）(一元运算符的低优先级)（可以作用于非bool值，即重载）    

（DONE）引入成员访问。    

（DONE）引入类声明。    

（DONE）可空语句块。    

（DONE）memberAccess完善（ID.ID或者this.ID）  

（DONE）自定义类作为Type。    

静态函数调用。    

## 脚本模式  

（DONE）脚本引擎    

（DONE）算术运算    

（DONE）函数调用     

对象创建和访问（成员变量赋值取值，成员函数调用）      
    声明成员变量时不分配内存，仅加入符号表？  
    成员的值存储在object中还是内存中？  

外部代理对象    

## 编译模式    

符号表和常量表的填充。    

类型检查。    
