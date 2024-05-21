![GizboxLogo](./GizboxLang/Documents/GizboxLogoBlack.png)

# Introduction

Gizbox is a scripting language that can be embedded in C# applications.

- Object-oriented. Can only inherit from one base class. Does not support multiple inheritance.  
- All classes directly or indirectly inherit from the Core::Object class.   
- All member functions are virtual functions.
- No implicit conversions; all types require explicit conversion.
- Field declarations must be initialized.

<br />
<br />

# Gizbox Syntax

- Hello World

```Gizbox
import <"core">

Console::Log("Hello World!");
```

- Libraries

Import library files using the `import` keyword.

```Gizbox
import <"stdlib">
```

- Namespace

Declare namespaces using the `namespace` keyword.

```Gizbox
namespace Console
{
    extern void Log(string text);
}
```

Import namespaces using the `using` keyword.

```Gizbox
using Console;

Log("Hello!"); // Equivalent to Console::Log("Hello!");
```

- Basic Types

`bool`, `int`, `float`, `double`, `char`, `string`

- Arrays

```Gizbox
int[] arr = new int[99];
```

- Loop Statements

`while(expr) {statements}`, `for(initializer ; expr ; iterator) {statements}`

- Conditional Statements

`if(expr) {statements} else if(expr) {statements} else {statements}`

- Class Definition

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

- Member Access

```Gizbox
obj.member = expr;
obj.method();
```

In member functions, all member fields and member functions must be accessed with `this.`. Otherwise, the symbol cannot be found.

- Properties

Properties: Member functions `void xxxx(type arg)` and `type xxxx()` can be used as setters and getters.    

- Interoperability

C# methods can be called through extern functions.

```Gizbox
extern void Log(string text);
```

- GC    

You can enable garbage collection (GC) by calling `GC::Enable()`, or control the object lifecycle using `delete`.    

- Notes

In the same compilation unit, the base class must be defined before the derived class. (Otherwise, the virtual function table merge will have issues)

<br />
<br />

# Compiler

- Create a compiler instance and compile the code

```C#
Gizbox.Compiler compiler = new Compiler(); // Create a compiler instance
compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory); // Set the library file search path（Requires library files for semantic analysis）
var ir = compiler.Compile(source); // Compile the source code into intermediate code
```

- Create an interpreter instance and execute the intermediate code

```C#
ScriptEngine engine = new ScriptEngine(); // Create an interpreter instance
engine.AddLibSearchDirectory(AppDomain.CurrentDomain.BaseDirectory);//Search Directory Of Libs  
engine.csharpInteropContext.ConfigExternCallClasses(typeof(GizboxLang.Examples.ExampleInterop));//Functions called externally are prioritized to be searched within these classes.
engine.Execute(ir);
```

- Configure the static class for external calls

```C#
engine.csharpInteropContext.ConfigExternCallClasses(new Type[] {
    typeof(GizboxLang.Examples.ExampleInterop),
}); // The extern functions in Gizbox will find the corresponding methods from this class
```


- Calling Gizbox Function from C#    

```C#
ScriptEngine engine = new ScriptEngine();
engine.Load(il);
var ret = engine.Call("Math::Pow", 2f, 4);
Console.WriteLine("result:" + ret); //result:16
```  

- Generate Interop Wrap code

```C#
InteropWrapGenerator generator = new InteropWrapGenerator();
generator.IncludeTypes(new Type[] {
    typeof(GizboxLang.Examples.Person),
}); // Include types (will automatically include field types, return types, and parameter types of member functions)

foreach (var t in generator.closure)
    Console.WriteLine(t.Name); // List all involved types

generator.GenerateFile("Example"); // Generate .cs file and .gix file
```
