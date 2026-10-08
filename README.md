# NTDLS.ExpressionParser

📦 Be sure to check out the NuGet package: https://www.nuget.org/packages/NTDLS.ExpressionParser

[![Regression Tests](https://github.com/NTDLS/NTDLS.ExpressionParser/actions/workflows/Regression%20Tests.yml/badge.svg)](https://github.com/NTDLS/NTDLS.ExpressionParser/actions/workflows/Regression%20Tests.yml)

ExpressionParser is a mathematics parsing engine for .NET. It supports expression nesting, custom variables, custom functions and all standard mathematical operations for integer, decimal (floating point), logic and bitwise.

🔥 Fast at both ends: a 5 operation expression with variables evaluates in ~44 ns without allocating (≈ 23M evaluations/s, ≈ 115M operations/s per core), and a brand new expression is parsed, compiled and evaluated in about half a microsecond. Among 9 .NET expression parsers benchmarked, it is second fastest at evaluating and #1 fastest at parsing new expressions.

It should also be noted that the performance testing of this library was not cherry-picked and the test method we used (with variables) was actually the slowest of the 3 test methods we used.
The other two test methods (without variables) were even faster, with the fastest being ~5.4 ns per evaluation (~185M evaluations/s, ~925M operations/s per core). See [Benchmarks](#benchmarks).

In addition to custom functions and variables, out of the box it supports: abs, acos, asin, atan, atan2, avg, ceil, clamp, cos, cosh, count, deg, e, exp, floor, hypot, if, log, log10, logn, max, min, modpow, not, pi, pow, prod, rad, rand, round, sign, sin, sinh, sqrt, sum, tan, tanh, trunc.

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
- Whitespace is allowed between operators and operands, but not within an operator: `a < = b` is a syntax error, not `a <= b`.
- Names (variables and functions) are not case sensitive, and whitespace is allowed between a function name and its parenthesis.

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

- **When constructing** an `Expression`: malformed expressions, e.g. `Syntax error: missing operand at end of expression at position 3 of '2 +'.` The position is the zero-based character index in the expression as written.
- **When evaluating**: undefined variables or functions, the wrong number of parameters for a built in function, division or modulus by zero, and operators that produce an infinite or NaN result.

Built in functions follow `System.Math`, so for example `sqrt(-1)` returns `NaN` rather than raising an error.

## Performance

Expressions are parsed, validated and compiled to a compact program in a single pass when constructed. Evaluating that program does not allocate, and:

- Parsing is fast: constructing an expression, setting its variables and evaluating it once takes around half a microsecond, so it suits expressions that are only evaluated once.
- No code is generated (no `Reflection.Emit` or compiled expression trees), so new expressions have no JIT compilation cost.
- Parts of an expression that do not depend on variables are computed once, when compiled (`rand()` excluded). An expression without variables evaluates in a few nanoseconds.
- Variables are resolved by `SetParameter`, so `Evaluate` never looks a name up.
- Compiled expressions are cached (see `UseCompileCache`), so even the static `Expression.Evaluate(text)` only parses an expression the first time it sees it.
- `OperationCount` reports how many operations an expression performs, e.g. for calculating operations per second.

### Benchmarks

Measured with the `PerfTest` project in this repository on .NET 10, using `10 * ((a + 1000 + ( b )) * c) * 10` (5 operations, 3 variables). Every library uses its fastest form for each scenario, and every result is verified. All numbers are from the same run.

#### Parse + evaluate: a new expression every time

Each call parses a new expression, sets its variables and evaluates it once - 1,000 calls per run. Every parse cache is disabled, and every call uses expression text that has never been seen before (the constant `1000` is replaced with a different number each time), so no library can skip parsing by recognizing an expression.

| Rank | Library | Best (ms) | Avg (ms) | Per call | Relative time |
|:---:|---|---:|---:|---:|---:|
| **1** | **NTDLS.ExpressionParser** | **0.67** | **1.40** | **~1.4 μs** | **1.00×** |
| 2 | MathEvaluator ² | 0.86 | 1.43 | ~1.4 μs | 1.02× |
| 3 | xFunc | 1.73 | 1.88 | ~1.9 μs | 1.34× |
| 4 | Mathos Parser | 4.18 | 4.43 | ~4.4 μs | 3.16× |
| 5 | NoStringEvaluating | 8.51 | 9.76 | ~9.8 μs | 6.96× |
| 6 | NCalc | 13.65 | 21.00 | ~21 μs | 15.0× |
| 7 | CalcExpr.NET | 342.39 | 374.88 | ~375 μs | 268× |
| 8 | Jace ¹ | 908.70 | 966.18 | ~966 μs | 690× |
| 9 | Flee ¹ | 2,554.05 | 2,674.68 | ~2,675 μs | 1,909× |

- **Best / Avg** are milliseconds per 1,000 calls; lower is faster.
- NTDLS.ExpressionParser and MathEvaluator are close here: PerfTest's short runs are noisy, and a steadier measurement (200,000 calls, median of 9 runs) gives ~560 ns for NTDLS.ExpressionParser and ~690 ns for MathEvaluator, with NTDLS.ExpressionParser allocating 888 bytes per call against 1,440.

¹ Generates code (IL) for each expression, which the .NET JIT must then compile to machine code before it first runs. Once compiled, evaluating is fast, but each new expression is slow: Flee spends ~2 ms per expression in the JIT (mostly inlining its variable lookups, roughly 0.7 ms per variable), and Jace ~1 ms per expression in total. Jace also reuses compiled code for repeated text even with its cache disabled, which is why every call uses new text.

² For a single evaluation MathEvaluator is used without compiling, as its interpreter is ~150× faster than compiling (~1.4 μs vs ~200 μs).

mXparser ([MathParser.org-mXparser](https://www.nuget.org/packages/MathParser.org-mXparser)) is supported by `PerfTest` but not shown, as it is disabled by default (version 5+ prints a license notice unless commercial or non-commercial use is confirmed). In earlier runs it evaluated in ~720 ns.

#### Evaluate: one expression, evaluated many times

Each library parses (or compiles) the expression once, untimed, then evaluates it 100,000 times per run.

| Rank | Library | NuGet package | Best (ms) | Avg (ms) | Per evaluation | Operations/μs | Relative time |
|:---:|---|---|---:|---:|---:|---:|---:|
| 1 | MathEvaluator ¹ | [MathEvaluator](https://www.nuget.org/packages/MathEvaluator) 3.4.0 | 1.56 | 2.08 | ~21 ns | 240.80 | 0.48× |
| **2** | **NTDLS.ExpressionParser** | [NTDLS.ExpressionParser](https://www.nuget.org/packages/NTDLS.ExpressionParser) | **3.57** | **4.35** | **~44 ns** | **114.92** | **1.00×** |
| 3 | Flee ¹ | [Flee](https://www.nuget.org/packages/Flee) 2.0.0 | 7.22 | 8.39 | ~84 ns | 59.58 | 1.93× |
| 4 | xFunc | [xFunc.Maths](https://www.nuget.org/packages/xFunc.Maths) 4.4.1 | 8.48 | 11.93 | ~119 ns | 41.90 | 2.74× |
| 5 | NoStringEvaluating | [NoStringEvaluating](https://www.nuget.org/packages/NoStringEvaluating) 2.6.1 | 11.05 | 13.05 | ~131 ns | 38.31 | 3.00× |
| 6 | Jace ¹ | [Jace](https://www.nuget.org/packages/Jace) 1.0.0 | 14.25 | 21.49 | ~215 ns | 23.26 | 4.94× |
| 7 | CalcExpr.NET | [CalcExpr.NET](https://www.nuget.org/packages/CalcExpr.NET) 1.0.1 | 17.94 | 26.71 | ~267 ns | 18.72 | 6.14× |
| 8 | NCalc | [NCalcSync](https://www.nuget.org/packages/NCalcSync) 7.2.0 | 30.13 | 36.41 | ~364 ns | 13.73 | 8.37× |
| 9 | Mathos Parser | [MathosParser](https://www.nuget.org/packages/MathosParser) 2.0.0 | 311.72 | 371.01 | ~3,710 ns | 1.35 | 85.29× |

- **Best / Avg** are milliseconds per 100,000 evaluations; lower is faster.
- **Operations/μs** counts the expression's 5 math operations, not evaluations; higher is faster.
- **Relative time** is the average compared to NTDLS.ExpressionParser; lower is faster.


## Thread safety

- The static methods (`Expression.Evaluate`, `Expression.EvaluateNotNull`) are thread safe.
- Separate `Expression` instances can be used on separate threads, including for the same expression text - the compile cache and the compiled expressions in it are thread safe.
- A single instance can be evaluated (`Evaluate`, including with show work) from multiple threads at once, as long as nothing modifies it at the same time.
- Modifying an instance (`SetParameter`, `RemoveParameter`, `ClearParameters`, `AddFunction`, `RemoveFunction`, `ClearFunctions`) is **not** thread safe: it must not happen at the same time as any other use of that instance.

Parameters belong to the instance, so to evaluate with different values on different threads, use one instance per thread.

## License
[MIT](https://choosealicense.com/licenses/mit/)
