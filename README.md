# NTDLS.ExpressionParser

📦 Be sure to check out the NuGet package: https://www.nuget.org/packages/NTDLS.ExpressionParser

ExpressionParser is a mathematics parsing engine for .NET. It supports expression nesting, custom variables, custom functions and all standard mathematical operations for integer, decimal (floating point), logic and bitwise.

🔥 Expressions are compiled once and then evaluated without any allocation. A 5 operation expression with variables evaluates in ~41 ns (≈ 24M evaluations/s, ≈ 120M operations/s per core) - about 8× faster than NCalc. See [Performance](#performance).

In addition to custom functions and variables, out of the box it supports: abs, acos, asin, atan, atan2, avg, ceil, clamp, cos, cosh, count, deg, e, exp, floor, hypot, if, log, log10, logn, max, min, modpow, not, pi, pow, prod, rad, rand, round, sign, sin, sinh, sqrt, sum, tan, tanh, trunc.

[![Regression Tests](https://github.com/NTDLS/NTDLS.ExpressionParser/actions/workflows/Regression%20Tests.yml/badge.svg)](https://github.com/NTDLS/NTDLS.ExpressionParser/actions/workflows/Regression%20Tests.yml)

👀 If you came for the C++ version you can find it at: https://github.com/NTDLS/CMathParser

## Basic usage:

>**Simple Example:**
>
>In this example we simply call the static function Expression.Evaluate to compute the string expression.
```csharp
var result = Expression.Evaluate("10 * ((1000 / 5 + (10 * 11)))");
Console.WriteLine($"{result:n2}"); //3,100.00
```

>**Simple Example (with work):**
>
>In this example we also supply an output parameter, which the parser uses to explain each operation in the order it was performed.
```csharp
var result = Expression.Evaluate("10 * ((1000 / 5 + (10 * 11)))", out var explanation);

Console.WriteLine($"{result:n2}");
Console.WriteLine(explanation);
```
```
3,100.00
{
    1000/5 = 200
    10*11 = 110
    200+110 = 310
    10*310 = 3100
} = 3100
```

>**Advanced Example:**
>
>In this example we will create an expression that uses two built in functions "Ceil" and "Sum", a custom function called "DoStuff" and one variable called "extra". Names are not case sensitive.
```csharp
var expression = new Expression("10 * ((5 + extra + DoStuff(11,55) + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10");

//Set a value for the variable called "extra".
expression.SetParameter("extra", 1000);

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

var result = expression.Evaluate();

Console.WriteLine($"{result:n2}"); //5,416,050.00
```

>**Reusing an Expression:**
>
>An expression is parsed once, when it is constructed. To evaluate it many times, keep the instance and change its parameters - this is the fastest way to use the parser.
```csharp
var expression = new Expression("price * qty * (1 - discount)");

foreach (var order in orders)
{
    expression.SetParameter("price", order.Price);
    expression.SetParameter("qty", order.Quantity);
    expression.SetParameter("discount", order.Discount);

    Console.WriteLine(expression.Evaluate());
}
```

## Operators

Listed from highest to lowest precedence. Operators on the same row are evaluated left to right. Use parentheses to override.

| Operators | Description |
|---|---|
| `-x` `+x` `!x` `~x` | Negation, unary plus, logical NOT, bitwise NOT |
| `*` `/` `%` | Multiplication, division, modulus |
| `+` `-` | Addition, subtraction |
| `<<` `>>` | Bitwise shift |
| `<` `<=` `>` `>=` | Comparison |
| `=` `==` `!=` `<>` | Equality (`=` and `==` are the same, as are `!=` and `<>`) |
| `&` | Bitwise AND |
| `^` | Bitwise XOR |
| `\|` | Bitwise OR |
| `&&` | Logical AND |
| `\|\|` | Logical OR |

- Comparison and logical operators return `1` (true) or `0` (false). Any non-zero value is true.
- Bitwise operators (`~ << >> & ^ |`) truncate their operands to 32 bit integers, e.g. `7.9 | 0` is `7`.
- `&=`, `^=` and `|=` are accepted as aliases for `&`, `^` and `|`.
- Consecutive signs are allowed, e.g. `1 - -2` is `3`.

## Functions

| Function | Description |
|---|---|
| `abs(x)` `sign(x)` | Absolute value, sign (-1, 0 or 1) |
| `ceil(x)` `floor(x)` `trunc(x)` `round(x)` `round(x, digits)` | Rounding |
| `sqrt(x)` `pow(x, y)` `exp(x)` `modpow(base, exponent, modulus)` | Powers and roots |
| `log(x)` `log10(x)` `logn(x, base)` | Logarithms |
| `sin` `cos` `tan` `asin` `acos` `atan` `sinh` `cosh` `tanh` (one parameter), `atan2(y, x)` | Trigonometry, in radians |
| `deg(radians)` `rad(degrees)` | Angle conversion |
| `min(...)` `max(...)` `sum(...)` `avg(...)` `prod(...)` `count(...)` `hypot(...)` | Aggregates of one or more values (`count` also accepts none) |
| `clamp(x, min, max)` | Limits x to the range min..max |
| `if(condition, whenTrue, whenFalse)` | Returns whenTrue if condition is non-zero, otherwise whenFalse |
| `not(x)` | Logical NOT, same as `!x` |
| `pi()` `e()` | Constants |
| `rand()` | Random number from 0 to 1 |

Custom functions are added with `AddFunction` and receive their parameters as a `double[]`. A built in function can not be replaced by a custom function of the same name.

## Nulls

The keyword `null` can be used in expressions and variables can be set to `null`.

- An operation with a `null` operand results in `null`, e.g. `null + 1` is `null`.
- A function with a `null` parameter results in `null`, and a custom function is not called.
- Set `ExpressionOptions.DefaultNullValue` to use a value in place of `null` - for null literals, variables set to null, and custom functions that return null.
- `Expression.EvaluateNotNull` returns `0` for a `null` result, and can report whether the result was `null`.

```csharp
Expression.Evaluate("null + 1");                                                      //null
Expression.Evaluate("null + 1", new ExpressionOptions { DefaultNullValue = 0 });      //1
Expression.EvaluateNotNull("null + 1", out bool wasNull);                             //0, wasNull = true
```

## Options

Pass an `ExpressionOptions` to the constructor or to the static methods.

| Option | Default | Description |
|---|---|---|
| `UseCompileCache` | `true` | Caches each compiled expression, so constructing another `Expression` with the same text skips parsing. Entries expire after 5 minutes unused. |
| `DefaultNullValue` | `null` | The value used in place of `null` (see [Nulls](#nulls)). |
| `Precision` | `17` | Significant digits used to format numbers when showing the work. Calculations always use full `double` precision. |
| `UseFastFloatingPointParser` | `true` | Uses a faster parser for number literals. It is correctly rounded, falling back to `double.Parse` for numbers it can not parse exactly. |
| `CustomHash` | `null` | A key to cache the compiled expression under, instead of its text. Only reuse a key for identical expression text. |

## Errors

Errors are raised as exceptions:

- **When constructing** an `Expression`: malformed expressions, e.g. `Syntax error: missing operand at end of expression at position 2 of '2+'.` The position refers to the expression after whitespace is removed, which the message includes.
- **When evaluating**: undefined variables or functions, the wrong number of parameters for a built in function, division or modulus by zero, and operators that produce an infinite or NaN result.

Built in functions follow `System.Math`, so for example `sqrt(-1)` returns `NaN` rather than raising an error.

## Performance

Expressions are compiled to a compact program when constructed. Evaluating it does not allocate, and:

- Parts of an expression that do not depend on variables are computed once, when compiled (`rand()` excluded). An expression without variables evaluates in a few nanoseconds.
- Variables are resolved by `SetParameter`, so `Evaluate` never looks a name up.
- Compiled expressions are cached (see `UseCompileCache`), so even the static `Expression.Evaluate(text)` only parses an expression the first time it sees it.
- `OperationCount` reports how many operations an expression performs, e.g. for calculating operations per second.

Measured with the `PerfTest` project in this repository - `10 * ((a + 1000 + ( b )) * c) * 10`, 5 operations with 3 variables, evaluated 100,000 times per run, against NCalcSync 7.2.0 on .NET 10:

| | Avg per 100,000 | Per evaluation | Operations/μs |
|---|---|---|---|
| NTDLS.ExpressionParser | 4.07 ms | ~41 ns | ~123 |
| NCalc | 32.74 - 36.04 ms | ~330 - 360 ns | ~14 - 15 |

## Thread safety

- The static methods (`Expression.Evaluate`, `Expression.EvaluateNotNull`) are thread safe.
- Separate `Expression` instances can be used on separate threads, including for the same expression text - the compile cache and the compiled expressions in it are thread safe.
- A single instance can be evaluated (`Evaluate`, including with show work) from multiple threads at once, as long as nothing modifies it at the same time.
- Modifying an instance (`SetParameter`, `RemoveParameter`, `ClearParameters`, `AddFunction`, `RemoveFunction`, `ClearFunctions`) is **not** thread safe: it must not happen at the same time as any other use of that instance.

Parameters belong to the instance, so to evaluate with different values on different threads, use one instance per thread.

## License
[MIT](https://choosealicense.com/licenses/mit/)
