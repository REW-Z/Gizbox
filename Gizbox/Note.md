
# Note  



# Optimize  

局部临时变量的栈内存分配。不同live区间的temp共用同一块栈内存。  
具名的局部变量可以不做这个优化，用于debug模式下访问每个变量的值。    


# TODO  


### 顶级语句    

当有显式定义的main函数，顶级语句生成top函数。  

### 所有权-临时变量    

编译期检查。  
作用域drop、语句末drop。  
临时借用-表达式中借用临时own类型（函数返回值）    


a.语义分析阶段进行所有权分析。IR代码生成阶段不分析所有权，而是仅仅负责分析结果生成delete语句。  

b.临时对象怎么处理，单表达式语句(Call和New)怎么处理:    
```
own AAA Generete()
{
    own AAA ret = new AAA();
    return ret;
}

new AAA();   //临时值没有名称，怎么指名delete它？    
Generate();   //返回对象没有一个对象来承载，怎么指名delete它？    
```  

单表达式返回的临时对象可以用一个临时变量承接，然后语句末delete该临时变量。    

这个做法和Rust一致，•	Rust 编译器在降低到 MIR 时会为表达式结果引入临时局部（temporary local），并在drop elaboration阶段为这些临时的生命周期尽头插入drop指令。    
临时局部虽然源代码里没有名字，但是MIR里面有。    


### 所有权-容器    

原始数组只能存放manual类型的元素。  
own类型的容器用类封装，实际存储用原始数组，需要加`adopt`操作符和`disown`操作符。定义get、set、add、remove等方法。      



### 分支和循环    

循环处理：  

已解决  

分支处理：  

AI：
```
不需要单独区分 Released；delete 后就是 Dead。
分支合并的 join 规则：
•	Alive + Alive = Alive
•	Dead + Dead = Dead
•	Alive + Dead = MaybeDead
•	X + MaybeDead = MaybeDead (MaybeDead 与任何非同质状态合并继续保持 MaybeDead)

需要设置一个drop flag，离开作用域时根据flag进行条件delete。    
```

替代drop-flag的方法：所有权移动后，右值变量置null。用null作为drop-flag。    

doing:在Pass4处理SingleStmt和CallNode。    

IR生成器插入语句。    
