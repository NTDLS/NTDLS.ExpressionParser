# NTDLS.ExpressionParser

📦 Be sure to check out the NuGet package: https://www.nuget.org/packages/NTDLS.ExpressionParser

ExpressionParser is a mathematics parsing engine for .net. It supports expression nesting, custom variables, custom functions all standard mathematical operations for integer, decimal (floating point), logic and bitwise.

It addition to the custom functions and variables, these are built in: ACOS, ASIN, ATAN, ATAN2, LDEXP, SINH, COSH, TANH, LOG, LOG10, EXP, MODPOW, SQRT, POW, FLOOR, CEIL, NOT, AVG, SUM, TAN, ATAN, SIN, COS, ABS.

If you came for the C++ version you can find it at: https://github.com/NTDLS/CMathParser

Basic example usage:

Be sure to check out the NuGet package: https://www.nuget.org/packages/NTDLS.ExpressionParser/

>**Simple Example:**
>
>In this example we simply call the static function Expression.Evaluate to compute the string expression.
```csharp

var result = Expression.Evaluate("10 * ((1000 / 5 + (10 * 11)))");
Console.WriteLine($"{result:n2}");
```

>**Simple Example (with work):**
>
>In this example we simply call the static function Expression.Evaluate to compute the string expression, and we also supply an output parameter for which the parser will use to explain the operations.
```csharp

var result = Expression.Evaluate("10 * ((1000 / 5 + (10 * 11))), out var explanation");

Console.WriteLine($"{result:n2}");
Console.WriteLine(explanation);
```

>**Advanced Example:**
>
>In this example we will create an expression that uses two built in functions "Ceil" and "Sum", a custom function called "DoStuff" and one variable called "extra".
```csharp

var expression = new Expression("10 * ((5 + extra + DoStuff(11,55) + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10");

//Add a value for the variable called "extra".
expression.AddParameter("extra", 1000);

//Handler for the custom function:
expression.AddFunction("DoStuff", (double[] parameters) =>
{
    double sum = 0;
    foreach (var parameter in parameters)
    {
        sum += parameter;
    }
	return sum;
});

var result = ExpressionParser.Evaluate(expression);

Console.WriteLine($"{result:n2}");

```

## License
[MIT](https://choosealicense.com/licenses/mit/)
