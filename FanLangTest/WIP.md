
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


（DONE）符号表填充。    

（DONE）类型检查。  


# 函数中间代码生成  


### 参考的汇编流程    

入口处理的汇编：
1. 函数名标号定义。    
2. 分配栈帧。  
3. 参数和返回值。    

4. 函数体

出口处理代码：  
5. 返回值传送至专用于返回结果的寄存器。    
6. 恢复寄存器和栈帧。  
7. return指令。（JUMP到返回地址）    
8.汇编语言需要的声明一个函数结束的伪指令。    


### 示例  

```C  
int AddOne(int input){ int result = input + 1; return result; } int main(){ int a = 2; int b = AddOne(a); }
编译为：
AddOne:
    t1 = input + 1
    result = t1
    return result

main:
    a = 2
    param a
    call AddOne, 1
    b = RET
```



# 类的中间代码生成    

。。。


# 错误处理    


语法、语义错误定位所在行数。    