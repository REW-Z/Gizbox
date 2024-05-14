![GizboxLogo](./GizboxLang/Documents/GizboxLogoBlack.png)

# 介绍      

Gizbox是一种脚本语言，可以用来嵌入C#的应用程序。    

面向对象。只能继承一个基类。不支持多继承。    

所有成员函数都是虚函数。    

没有隐式转换，所有类型都需要显示转换。    

字段声明必须赋初始值。    

<br />
<br />

# Gizbox语法    

- Hello World    

```Gizbox  
import <"stdlib">

Console::Log("Hello World!");
```  

- 库    

通过`import`关键字引入导入库文件。    

```Gizbox  
import <"stdlib">
```

- 名称空间    

通过`namespace`关键字声明名称空间。    

```  
namespace Console
{
    extern void Log(string text);
}
```

通过`using`关键字引入名称空间。    

```Gizbox  
using Console;

Log("Hello!"); //等同于Console::Log("Hello!");
```  

- 基本类型    

`bool`,`int`,`float`,`double`,`char`,`string`    

- 数组    

`int[] arr = new int[99];`    


- 循环语句  

`while(expr) {statements}`、`for(intializer ; expr ; iterator) {statements}`  


- 条件语句

`if(expr) {statements} else if(expr) {statements} else {statements}`    


- 类定义    

```Gizbox
class Person
{
    string name;
    void Say()
    {
        Console::Log("my type is person");
    }
}
class Student : Person
{
    void Say()
    {
        Console::Log("my type is student");
    }
} 
```  

- 成员访问    

`obj.member = expr;`  
`obj.method();`  

在成员函数中，所有成员字段和成员函数的访问，必须加上`this.`。否者找不到符号。    


- 属性    

属性：成员函数`void xxxx(type arg)`和`type xxx()`可以被当作setter和getter。    


- 互操作    

可以通过extern函数调用C#的方法。    

```Gizbox  
extern void Log(string text);
```

- 注意事项    

在同一个编译单元中，基类必须先于派生类定义。（否则虚函数表合并会出问题）    




<br />
<br />

# 编译器    

- 创建编译器实例并编译代码      

```C#  
Gizbox.Compiler compiler = new Compiler();//创建编译器实例  
compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);//设置库文件搜索路径  
var ir = compiler.Compile(source);//编译源代码为中间代码  
```    

- 创建解释器实例并执行中间代码    

```C#
ScriptEngine engine = new ScriptEngine();//创建解释器实例  
engine.csharpInteropContext.ConfigExternCallClasses(typeof(GizboxLang.Examples.ExampleInterop));//
engine.Execute(ir);
```  


- 配置外部调用的静态类          

```C#  
engine.csharpInteropContext.ConfigExternCallClasses( new Type[] {
    typeof(GizboxLang.Examples.ExampleInterop),
});//Gizbox中的extern函数会从这个类中查找对应方法    
```  


- 生成互操作的Wrap代码    

```C#  
InteropWrapGenerator generator = new InteropWrapGenerator();
generator.IncludeTypes(new Type[] { 
    typeof(GizboxLang.Examples.Person),
});//包含类型（会自动包含字段类型和成员函数的返回类型以及参数类型）  

foreach (var t in generator.closure)
    Console.WriteLine(t.Name);//列出涉及的所有类型  

generator.GenerateFile("Example");//生成.cs文件和.gix文件    
```  