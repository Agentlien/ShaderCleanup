# Sheader Cleanup

This is a tool for automatically simplifying shaders generated from Unity's Shader Graph. It takes Unity shader files, assumed to be generated from a Shader Graph, and applies a series of regular expressions to reduce code length and increase legibility.

The tool was originally developed by [Daniel *"Agentlien"* Kvick](https://agentlien.github.io) while working at [Thunderful Development](https://thunderfulgames.com/)

For a more detailed background and motivation for why this tool was written, check out [the blog post](https://agentlien.github.io/cleanup) released alongside this tool.

## Example

Here is a screenshot of a section from a randomly selected Shader Graph in the project I'm currently working on.

![example graph](/images/example_graph.png)

Here is the shader code generated by Unity for this portion.

``` 
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float _Property_89116ACD_Out_0 = _VertexOffsetAmount;
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float _Multiply_602F0AD4_Out_2;
Unity_Multiply_float(_Remap_A65749B9_Out_3, _Property_89116ACD_Out_0, _Multiply_602F0AD4_Out_2);
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 _Multiply_30325343_Out_2;
Unity_Multiply_float(IN.ObjectSpaceNormal, (_Multiply_602F0AD4_Out_2.xxx), _Multiply_30325343_Out_2);
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 _Add_3A54B679_Out_2;
Unity_Add_float3(IN.ObjectSpacePosition, _Multiply_30325343_Out_2, _Add_3A54B679_Out_2);
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float _Split_3764AD50_R_1 = IN.VertexColor[0];
float _Split_3764AD50_G_2 = IN.VertexColor[1];
float _Split_3764AD50_B_3 = IN.VertexColor[2];
float _Split_3764AD50_A_4 = IN.VertexColor[3];
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 _Lerp_5F9C0AF5_Out_3;
Unity_Lerp_float3(IN.ObjectSpacePosition, _Add_3A54B679_Out_2, (_Split_3764AD50_R_1.xxx), _Lerp_5F9C0AF5_Out_3);
#endif
  ```

And here is what this section becomes after running this tool: 

```
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float _Multiply_602F0AD4_Out_2 = _Remap_A65749B9_Out_3 * _VertexOffsetAmount;
float3 _Multiply_30325343_Out_2 = IN.ObjectSpaceNormal * (_Multiply_602F0AD4_Out_2.xxx);
float3 _Add_3A54B679_Out_2 = IN.ObjectSpacePosition + _Multiply_30325343_Out_2;
float3 _Lerp_5F9C0AF5_Out_3 = lerp(IN.ObjectSpacePosition, _Add_3A54B679_Out_2, (IN.VertexColor[0].xxx));
#endif 
 ```

Functionally, these are identical and will compile to the same instructions. 

## Requirements

This tool is intended to be used from within the Unity Editor. It has been tested with Unity versions ranging from 2019.4 to 2022.1.

## Usage

The script is available in the menu under *Thunderful -> Fix -> Shader Cleanup Tool*. Simply enter the path to the target script and press Run. The script itself typically takes a second or two to run, but in a large project the resulting re-imports may take a while.

## The results

First and foremost, the resulting code is a lot easier to read, which makes it better to work with and optimize. While it does shorten the code, the main improvement is that it becomes much easier to grok.

The amount of reduction depends on a lot of details but in general I've seen a 20-25% reduction in file length. 

An important point to emphasize is that the output from this tool is logically equivalent to the input and, in my tests, generated exactly the same instructions when the shader was compiled.

## What the tool actually does

The tool runs a series of regular expressions in order to clean up certain recurring patterns. Let us look at what these patterns actually are. I won't go through the exact regex here as you can find them [in the code](Source/Editor/GeneratedShaderCleanup.cs). Instead, I'll go through each of the steps of the script on a conceptual level, explaining them in the order they are done within the script. The order is actually important, since the regular expressions make assumptions of code pattern which depend on which other steps have run already. For example, some steps would miss stuff if property variables had not been removed already. Other patterns rely on strict patterns which get disrupted when the code is simplified.

For each of these I have included a brief example of how the tool improves things.
I have tried to find examples from our actual shaders which illustrate only the current step. I've also done some light editing to keep things easier to follow, hence the recurring usage of the fictitious `someFunction()`.

### Removing properties

Unity declares a number of variables which are essentially aliases for values passed to subgraphs. It also creates an alias before reading any uniform variable. All these extra variables accomplish is padding the code and making it harder to follow values - especially those being reused a lot. So, as a first step, we remove all variables that start with `_Property_` and replace their usage with whatever they were initialized to.

Before:

```
float2 _Property_894A6798_Out_0 = Vector2_C9178F4E;
sumeFunction(_Property_894A6798_Out_0);
```
After:

```
someFunction(Vector2_C9178F4E);
```

### Removing Unity functions

Unity generates functions for every node used in the graph. Most of these are really simply. The Add functions, for instance, are just a wrapper around the expression `Out = A + B`. Every time these are used, Unity declares a variable to contain the result. Then, on a separate line, it assigns this variable by calling the generated function with this variable as its last argument. This obfuscates a lot of simple arithmetic. I haven't implemented all the Unity nodes, here. Only the ones which happened to show up in the shaders I've used the tool on and I deemed simple enough that inlining makes the code easier to read.

Moreover, it's hard to pick a degree of how much we want to to simplify things. It may feel silly to create a new variable for every single addition, but trying to fold things together more than this quickly makes for some really complex and unintuitive formulas.

Before:

```
float4 _Multiply_B1E8AE9E_Out_2;
Unity_Multiply_float(Vector4_13B37695, _SampleTexture2D_44C06A44_RGBA_0, _Multiply_B1E8AE9E_Out_2);

```
After:

```

float4 _Multiply_B1E8AE9E_Out_2 = Vector4_13B37695 * _SampleTexture2D_44C06A44_RGBA_0;

```
### Removing splits

Whenever you want to use a single component of a vector in Shader Graph you accomplish this by using a Split node. Unlike most nodes, this does not generate a function call. Instead, splits are implemented by creating new variables, one for each component of the input vector. Most of these variables go unused and their names being unrelated to the input variable makes it harder to track values.

Before:

```
float _Split_84146FCF_R_1 = Vector4_D200A9F[0];
float _Split_84146FCF_G_2 = Vector4_D200A9F[1];
float _Split_84146FCF_B_3 = Vector4_D200A9F[2];
float _Split_84146FCF_A_4 = Vector4_D200A9F[3];
someFunction(_Split_84146FCF_A_4);
```

After:

```
someFunction(Vector4_D200A9F[3]);
```

### Removing faux swizzles

This is probably my favourite improvement among these. It's the original reason I added the removal of split variables and really improves legibility of the code. You often need to construct a new vector from some of the components of an existing vector. This is achieved using a split node followed by a new Vector node. With splits removed we can detect when the arguments to a new vector are different components of the same vector, and replace it with a simple swizzle.

Before:

```
float _Split_7FD1108D_R_1 = IN.WorldSpacePosition[0];
float _Split_7FD1108D_G_2 = IN.WorldSpacePosition[1];
float _Split_7FD1108D_B_3 = IN.WorldSpacePosition[2];
float _Split_7FD1108D_A_4 = 0;
float2 _Vector2_6DB86487_Out_0 = float2(_Split_7FD1108D_R_1,_Split_7FD1108D_B_3);
someFunction(_Vector2_6DB86487_Out_0);
```

After:

```
someFunction(IN.WorldSpacePosition.xz);
```

### Cleaning conditional compilation

Declarations of variables and calculations in ShaderGraph are generally guarded by conditional compilation based on for which permutations of keywords they will be performed. If you create subgraphs which do not rely on keywords the generated subgraph functions will contain a fairly limited number of these. However, declaration of data structures and code outside of subgraphs generally have every single operation guarded by its own `#ifdef` even though most of these will check for every possible permutation and most of the code will make the same checks for many consecutive statements. This clutters the code beyond belief.

Fortunately, there's a simple solution which removes most of the unnecessary checks: merge any consecutive list of `#ifdef` guards using the same argument wrapped around one line each. This, of course, relies on the fact that previous steps merge declarations followed by assignment into a declaration with initialization. 

Before:

```
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 ObjectSpaceNormal;
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 ObjectSpaceTangent;
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 ObjectSpacePosition;
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 WorldSpacePosition;
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float4 VertexColor;
#endif
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 TimeParameters;
#endif
```

After:

```
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
float3 ObjectSpaceNormal;
float3 ObjectSpaceTangent;
float3 ObjectSpacePosition;
float3 WorldSpacePosition;
float4 VertexColor;
float3 TimeParameters;
#endif
```


### Removing empty blocks

Sometimes Unity generates large blocks of nothing but empty lines. In earlier versions of my tool these tended to merge as everything between them was removed, leading to blocks sometimes consisting of nothing but dozens of empty lines. To address this I added a simple step to remove consecutive empty lines. There were some considerations here, since sometimes empty lines are used for formatting - separating different functions or structures from each other. Hence, I ended up simply identifying blocks of multiple empty lines and replacing them with a single one. One special case is that the previous step already removes any empty spaces within repeated `ifdef`s.


Before:

```
#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
output.WorldSpacePosition =          input.positionWS;
#endif




#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1) || defined(KEYWORD_PERMUTATION_2) || defined(KEYWORD_PERMUTATION_3)
output.AbsoluteWorldSpacePosition =  GetAbsolutePositionWS(input.positionWS);
#endif
```

After:

```
output.WorldSpacePosition =          input.positionWS;
output.AbsoluteWorldSpacePosition =  GetAbsolutePositionWS(input.positionWS);
```


Before:

```
ZERO_INITIALIZE(SurfaceDescriptionInputs, output);





#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1)
output.WorldSpacePosition =          input.positionWS;
```

After:

```
ZERO_INITIALIZE(SurfaceDescriptionInputs, output);

#if defined(KEYWORD_PERMUTATION_0) || defined(KEYWORD_PERMUTATION_1)
output.WorldSpacePosition =          input.positionWS;
```
## Future improvements
At the moment I have no plans to actively continue developing this tool, but I do find myself making updates to it whenever I bump into a case where it breaks or I notice something in a shader that could easily be improved. Below I have gathered a list of things which I know are missing and might implement at some point. If anyone else feels like contributing I will happily review pull requests.

* Translation of components could be moved from the faux swizzle removal to splits removal. This would replace indices like `SomeVector[3]` with components such as `SomeVector.z`.
* The type pattern lacks support for matrices. This may be easily extended, but they rarely showed up in practice beyond `mul()`. The type pattern does not contain integer or boolean types either, but these should be rare and were deliberately left out. I have also never seen a graph generate variables of type `double`.
* Conditional compilation can be further cleaned up. When declaring data structures we often get large blocks of repeated tests for certain built-in shader keywords, each wrapping the same tests for shader permutations. These aren't currently detected, but with a bit of effort they probably could be.
* Empty Bindings data structures could be removed. For each subgraph Unity declares a data structure it passes along with certain property bindings. Sometimes these are empty. When cleaning things up I've mostly removed these manually. This point is less straightforward as it requires removing the structure declaration, removing declarations of these variables, and modifying both the subgraph function itself and all call sites.
* The list of generated node functions supported is incomplete. Some have been deliberately skipped because they are wordy enough that inlining doesn't make the code easier to read. Others have simply been ignored because they didn't show up in the shaders I ran this tool on.
* The interface itself is very simple and the error checks are really basic.
