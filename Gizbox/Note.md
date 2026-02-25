
# Optimize  

优化外部函数调用效率。使用CreateDelegate？  
使用哈希表优化LALR生成算法。  

局部临时变量的栈内存分配。不同live区间的temp共用同一块栈内存。  
具名的局部变量可以不做这个优化，用于debug模式下访问每个变量的值。    



# 踩坑    

为什么函数作用域下每个作用域都需要生命周期管理？  
因为循环中涉及内存分配，如果不以更小粒度做生命周期管理就会内存泄露。  

```
void foo()
{
    while(xxx)
    {
        own AAA a = new AAA();//需要在这里释放a
    }
}
```


# Target      

**Prospects**  

操作符重载(代码生成层面 or 解释器层面)    
值类型结构体（需要脚本引擎模拟内存）      
模式匹配（Pattern Matching）  
管道操作符（Pipeline Operator）  
解构赋值（Destructuring Assignment）  
内置并发支持（Built-in Concurrency Support）  
可选链（Optional Chaining）  
函数式编程(惰性求值（Lazy Evaluation）、无副作用函数)  
函数头等公民    
元编程和宏（类似Lisp和Rust的宏）    
响应式编程原生支持    
借鉴（Haskell、LISP、Prolog、Rust）    



**多线程开发**  

无栈协程实现。  


**字符串临时值优化方法**  

1.手动释放（C语言）  
2.栈分配（自动对象）（C++）  
3.作用域内存池。  
4.编译器优化RVO。  
5.所有权（目前最优解）。    



# TODO  


### RTTI  

汇编中生成类对象常量。  
TypeOf返回类型信息对象（静态对象）。  
