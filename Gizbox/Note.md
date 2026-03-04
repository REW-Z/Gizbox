
# Optimize  

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



# 近期更新

哈希、泛型模板、RTTI    


# TODO  

## Struct 落地进度（Todo/Done）

### Done
- 词法：新增 `struct` 关键字。  
- 语法：新增 `declstmt -> struct TYPE_NAME { declstatements }`。  
- AST：新增 `StructDeclareNode`。  
- 语义：Pass1/Pass2/Pass3/Pass4 接入 struct；限制 struct 成员仅字段声明；禁止 `own/bor` 字段与 ownership class 字段。  
- 布局：新增 `GenStructLayoutInfo`，计算字段偏移与整体大小，并回写 struct 类型表达式 `("struct:size")Name`。  
- 类型系统：`GType` 新增 `Struct` Kind，支持解析 `(struct:size)`、`(primitive)`、`(class)`、`(own class)`、`(bor class)` 前缀。  
- IR：结构体成员访问改为 `obj.field` 形式生成。  
- 后端：接入结构体成员访问表达式解析与寻址（局部/参数基于 `rbp+offset`，全局基于 `rel+disp`）。  
- 调用点 lowering：`Size > 8` 的 struct 实参在调用前复制到调用方栈临时区，并按地址传递。  
- 返回值 lowering：`Size > 8` 的 struct 采用隐藏返回缓冲区（sret）路径，CALL/PCALL/MCALL 与 RETURN 已接入。  
- 构建：当前工作区编译通过。  

### TODO
- 单元测试：补充 struct 字段访问、嵌套 struct、作为函数参数/返回值、大 struct 按值调用等用例。  


### 函数指针  

函数指针  
闭包  


### 显式析构函数    


### 结构体  