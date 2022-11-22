using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class GeneratedShaderCleanup : UnityEditor.EditorWindow
{
	const string typePattern = "(?:float|half)[234]?";
	const string variableNamePattern = "[][()._a-zA-Z0-9]+";

	string path;
	List<string> variableCache;
	List<string> initializerCache;

	[MenuItem("Thunderful/Fix/Generated Shader Cleanup")]
	static void Init()
	{
		var window = (GeneratedShaderCleanup)EditorWindow.GetWindow(typeof(GeneratedShaderCleanup), true, "Generated Shader Cleanup");

		window.Show();
	}

	void OnGUI()
	{
		GUILayout.Label("Path");
		path = GUILayout.TextField(path);
		if(GUILayout.Button("Browse"))
		{
			path = EditorUtility.OpenFilePanel("Select Shader to clean up", "", "shader");
		}
		if(GUILayout.Button("Run"))
		{
			if(String.IsNullOrEmpty(path))
			{
				EditorUtility.DisplayDialog("Error", "Must select a file to clean up", "OK");
				return;
			}
			if(!File.Exists(path))
			{
				EditorUtility.DisplayDialog("Error", "Invalid path", "OK");
				return;
			}

			try
			{
				string fileText = File.ReadAllText(path);
				File.WriteAllText(path, Cleanup(fileText));
			} 
			catch(Exception e)
			{
				EditorUtility.DisplayDialog("Error", $"Failed to clean file:\n {e.Message}", "OK");
			}
		}
	}

	string Cleanup(string s)
	{
		variableCache = new List<string>();
		initializerCache = new List<string>();

		s = RemoveProperties(s);
		s = RemoveUnityFunctions(s);
		s = RemoveSplits(s);
		s = RemoveFauxSwizzle(s);
		s = CleanConditionalCompilation(s);
		s = RemoveEmptyBlocks(s);
		return s;
	}

	string RemoveVariableInitializations(string s, string initializationPattern)
	{
		variableCache.Clear();
		initializerCache.Clear();
		MatchEvaluator declarationEvaluator = 
			(m) => 
			{
				variableCache.Add(m.Groups[1].Value);
				initializerCache.Add(m.Groups[2].Value);
				return "";
			};

		// Finds and removes variable initializations
		return Regex.Replace(s, initializationPattern, declarationEvaluator);
	}

	string RemoveVariableAssignment(string s, string v, out string initializer)
	{
		// Find and remove assignment
		string assignmentPattern = $@"\s*{v} = (.+);";
		string initialization = "";
		MatchEvaluator assignmentEvaluator = 
			(m) => 
			{
				initialization = m.Groups[1].Value;
				return "";
			};
		s = Regex.Replace(s, assignmentPattern, assignmentEvaluator);
		initializer = initialization;
		return s;
	}

	string RemoveVariableUsage(string s, string v, string initializer)
	{
		// Replace usage with definition
		return Regex.Replace(s, v, initializer); 
	}

	// Assumes pattern is captured as 1
	string RemoveVariables(string s, string declarationPattern)
	{
		variableCache.Clear();
		MatchEvaluator declarationEvaluator = 
			(m) => 
			{
				variableCache.Add(m.Groups[1].Value);
				return "";
			};

		// Finds and removes variable declarations
		s = Regex.Replace(s, declarationPattern, declarationEvaluator);

		for(int i = 0; i < variableCache.Count; ++i)
		{
			string v = variableCache[i];
			string initializer;
			s = RemoveVariableAssignment(s, v, out initializer);
			s = RemoveVariableUsage(s, v, initializer);
		}

		return s;
	}

	string RemoveInitializedVariables(string s, string initializationPattern)
	{
		s = RemoveVariableInitializations(s, initializationPattern);

		int matchCount = variableCache.Count;
		for(int i = 0; i < matchCount; ++i)
		{
			s = RemoveVariableUsage(s, variableCache[i], initializerCache[i]);
		}

		return s;
	}

	// This won't match any functions with braces inside, but none of the supported nodes have that
	string RemoveUnityFunctionImplementation(string s, string functionName)
	{
		string declarationPattern = $@"\s*void {functionName}\(.*out {typePattern} {variableNamePattern}[^}}]*}}";
		return Regex.Replace(s, declarationPattern, "", RegexOptions.Multiline);
	}

	string RemoveUnityFunctionUsage(string s, string functionPattern, string implementation)
	{
		string initializationPattern = $@"({typePattern}) ({variableNamePattern});\s*{functionPattern};";
		s = Regex.Replace(s, initializationPattern, implementation);
		return s;
	}

	string RemoveUnityFunction(string s, string functionName, string argumentsPattern, string implementation)
	{
		string functionPattern = $@"\s*{functionName}\({argumentsPattern}\)";
		s = RemoveUnityFunctionImplementation(s, functionName);
		s = RemoveUnityFunctionUsage(s, functionPattern, implementation);
		return s;
	}

	string RemoveUnityFunctions(string s)
	{
		string numberPattern = "-?[0-9.]+";
		string vectorPattern = $@"{typePattern}\({variableNamePattern},\s*{variableNamePattern}(?:,\s*{variableNamePattern}(?:,\s*{variableNamePattern})?(?:,\s*{variableNamePattern})?)?\)";
		string functionCallPattern = $@"{variableNamePattern}\((?:{numberPattern}|{variableNamePattern}|{vectorPattern})\)";
		// This pattern solves the specific case of a vector where each component is the length of a new vector.
		// Such code gets generated when you multiply or divide by an object's scale.
		string vectorFunction = $@"{typePattern}\({functionCallPattern},\s*{functionCallPattern}(?:,\s*{functionCallPattern}(?:,\s*{functionCallPattern})?(?:,\s*{functionCallPattern})?)?\)";
		string argumentPattern = $"{variableNamePattern}|{vectorPattern}|{numberPattern}|{functionCallPattern}|{vectorFunction}";

		s = RemoveUnityFunction(s, $@"Unity_Not_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = !$3;");
		s = RemoveUnityFunction(s, $@"Unity_Negate_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = -$3;");
		s = RemoveUnityFunction(s, $@"Unity_OneMinus_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = 1.0 - $3;");
		s = RemoveUnityFunction(s, $@"Unity_Saturate_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = saturate($3);");
		s = RemoveUnityFunction(s, $@"Unity_Exponential_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = exp($3);");
		s = RemoveUnityFunction(s, $@"Unity_Absolute_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = abs($3);");
		s = RemoveUnityFunction(s, $@"Unity_Normalize_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = normalize($3);");
		s = RemoveUnityFunction(s, $@"Unity_Length_{typePattern}", $@"({argumentPattern}),\s*\2", "$1 $2 = length($3);");

		s = RemoveUnityFunction(s, $@"Unity_Modulo_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = fmod($3, $4);");
		s = RemoveUnityFunction(s, $@"Unity_Step_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = step($3, $4);");
		s = RemoveUnityFunction(s, $@"Unity_Power_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = pow($3, $4);");
		s = RemoveUnityFunction(s, $@"Unity_Minimum_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = min($3, $4);");
		s = RemoveUnityFunction(s, $@"Unity_Maximum_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = max($3, $4);");
		s = RemoveUnityFunction(s, $@"Unity_Add_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = $3 + $4;");
		s = RemoveUnityFunction(s, $@"Unity_Subtract_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = $3 - $4;");
		s = RemoveUnityFunction(s, $@"Unity_Multiply_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = $3 * $4;");
		s = RemoveUnityFunction(s, $@"Unity_Divide_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = $3 / $4;");
		s = RemoveUnityFunction(s, $@"Unity_Comparison_Greater_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = $3 > $4 ? 1.0 : 0.0);");
		s = RemoveUnityFunction(s, $@"Unity_DotProduct_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = dot($3, $4);");

		s = RemoveUnityFunction(s, $@"Unity_Clamp_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = clamp($3, $4, $5);");
		s = RemoveUnityFunction(s, $@"Unity_Lerp_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = lerp($3, $4, $5);");
		s = RemoveUnityFunction(s, $@"Unity_Smoothstep_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = smoothstep($3, $4, $5);");
		s = RemoveUnityFunction(s, $@"Unity_Branch_{typePattern}", $@"({argumentPattern}),\s*({argumentPattern}),\s*({argumentPattern}),\s*\2", "$1 $2 = $3 ? $4 : $5;");
		return s;
	}

	string RemoveProperties(string s)
	{
		string identifierPattern = "_Property_[_a-zA-Z0-9]+";
		string initializationPattern = $@"\s*{typePattern} ({identifierPattern}) = (.*);";
		return RemoveInitializedVariables(s, initializationPattern);
	}

	string RemoveSplits(string s)
	{
		string identifierPattern = "_Split_[_a-zA-Z0-9]+";
		string initializationPattern = $@"\s*{typePattern}\s*({identifierPattern}) = (.+);";
		return RemoveInitializedVariables(s, initializationPattern);
	}

	string SanitizeComponent(string component)
	{
		if(component == "0")
		{
			return "x";
		}
		else if(component == "1")
		{
			return "y";
		}
		else if(component == "2")
		{
			return "z";
		}
		else if(component == "3")
		{
			return "w";
		}

		throw new Exception($"Unknown value found for component, \"{component}\"");
	}

	string RemoveFauxSwizzle(string s)
	{
		string componentPattern = $@"(?:\.([xyzw])|\[([0-3])\])";
		string fauxSwizzlePattern = $@"{typePattern}\(({variableNamePattern}){componentPattern}, \2{componentPattern}(?:, \2{componentPattern})?(?:, \2{componentPattern})?\)";
		string fauxSwizzleDeclaration = $@"\s*{typePattern} ({variableNamePattern}) = {fauxSwizzlePattern};";

		variableCache.Clear();
		initializerCache.Clear();

		Func<Match, int, string> sanitizeComponent = 
			(Match m, int index) =>
			{
				if(m.Groups[index].Success)
				{
					return m.Groups[index].Value;
				}

				string component = m.Groups[index + 1].Value;

				return SanitizeComponent(component);
			};

		MatchEvaluator fauxSwizzleEvaluator = 
			(m) =>
			{
				string component0 = "";
				string component1 = "";
				string component2 = "";
				string component3 = "";

				// A swizzle is made from at least two components. 
				// Hence, the fist two components aren't optional and we know they matched.
				component0 = sanitizeComponent(m, 3);
				component1 = sanitizeComponent(m, 5);

				if(m.Groups[7].Success || m.Groups[8].Success)
				{
					component2 = sanitizeComponent(m, 7);
					if(m.Groups[9].Success || m.Groups[10].Success)
					{
						component3 = sanitizeComponent(m, 9);
					}
				}

				variableCache.Add(m.Groups[1].Value);
				initializerCache.Add($"{m.Groups[2].Value}.{component0}{component1}{component2}{component3}");
				return "";
			};

		s = Regex.Replace(s, fauxSwizzleDeclaration, fauxSwizzleEvaluator);

		int matchCount = variableCache.Count;
		for(int i = 0; i < matchCount; ++i)
		{
			s = RemoveVariableUsage(s, variableCache[i], initializerCache[i]);
		}
		return s;
	}

	string CleanConditionalCompilation(string s)
	{
		string emptyLinesPattern = $@"^(?:\s*\n)*";
		string endifPattern = $@"^\s*#endif";
		string emptyConditionPattern = $@"(#if.*\n){emptyLinesPattern}{endifPattern}";
		string initialConditionPattern = $@"(#if.*\n)({emptyLinesPattern}.*\n{emptyLinesPattern})({endifPattern})";
		string repeatedConditionPattern = $@"\s*\1{emptyLinesPattern}(.*\n){emptyLinesPattern}{endifPattern}";
		string conditionalCompilationPattern = $@"{initialConditionPattern}(?:{repeatedConditionPattern})+";

		MatchEvaluator conditionalsEvaluator = 
			(m) => 
			{
				string result = $"{m.Groups[1].Value}{m.Groups[2].Value}";
				foreach (Capture capture in m.Groups[4].Captures)
				{
					result += capture.Value;
				}
				result += m.Groups[3].Value;
				return result;
			};

		s = Regex.Replace(s, emptyConditionPattern, "", RegexOptions.Multiline);
		return Regex.Replace(s, conditionalCompilationPattern, conditionalsEvaluator, RegexOptions.Multiline);
	}

	string RemoveEmptyBlocks(string s)
	{
		string emptyLinePattern = $@"^\s*\n";
		string emptyBlockPattern = $@"{emptyLinePattern}({emptyLinePattern})";
		return Regex.Replace(s, emptyBlockPattern, "$1", RegexOptions.Multiline);
	}
}

