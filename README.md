# NTDLS.ExpressionParser
Expression parser for .net. Supports expression nesting, custom variables, custom functions as well as a ton of built in functions.

Be sure to check out the NuGet pacakge: https://www.nuget.org/packages/NTDLS.ExpressionParser/

>**Simple example:**
>
>In this example we will create an expression that uses two built in functions "Ceil" and "Sum", a custom function called "DoStuff" and one variable called "extra".
> You can also pass a configuration parameter to set max memory size, cache scavange rate and partition count.
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
```

## License
[Apache-2.0](https://choosealicense.com/licenses/apache-2.0/)
