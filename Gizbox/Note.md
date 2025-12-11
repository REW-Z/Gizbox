
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


**泛型开发**  

使用类型擦除或者模板实现泛型。  

**多线程开发**  

无栈协程实现。  


**字符串临时值优化方法**  

1.手动释放（C语言）  
2.栈分配（自动对象）（C++）  
3.作用域内存池。  
4.编译器优化RVO。  
5.所有权（目前最优解）。    



# TODO  


### 所有权-容器    

原始数组只能存放manual类型的元素。  


### Delete[] 数组释放  

对应`new[]`。  


•	用 delete 释放用 new[] 得到的数组指针会导致未定义行为，常见直接后果是堆破坏（heap corruption）或“free(): invalid pointer”。根因是传给底层释放例程的地址不是那次分配的“块起始地址”。
为什么会破坏堆
•	典型布局（示意）： [分配器块头][数组cookie(元素个数或大小)][元素0][元素1]...[元素N-1]
•	new[]：
•	向分配器申请：cookie + N*sizeof(T) (+ 对齐)。
•	把“数组cookie”写在用户区前部，然后把“元素0的地址”返回给你。
•	delete[]：
•	从用户指针回退到 cookie，取出 N，逐个调用析构。
•	再回退到“块起始地址”（分配器块头之后的用户区起点），调用相应的 operator delete/分配器释放整块。
•	但标量 delete 的降级不会“回退指针”读取 cookie，它通常：
•	最多调用一次析构（若非平凡析构）。
•	直接把“用户指针（元素0地址）”传给 operator delete/free。
•	分配器的 free 期望接收的是它当初返回的“用户区起点”（在 cookie 之前）。现在它拿到的是“块内的中间地址”，于是：
•	把 cookie 当成块头去解释，得到错误的大小/标志，合并相邻空闲块时写坏内存，进而崩溃或后续随机损坏。



### 泛型实现   

手动特化。  


### 所有权-成员字段    

（DONE）禁止Move-Out  
drop_field_before_stmt实现  
隐含析构函数。析构时同时Drop所有成员字段  