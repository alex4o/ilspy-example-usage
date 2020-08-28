using System;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler.IL.Transforms;
using ICSharpCode.Decompiler.IL.ControlFlow;
using System.Collections.Generic;
using ICSharpCode.Decompiler.CSharp;
using System.Linq;
using System.Text;

namespace ICSharpCode
{
    class Program
    {
        static DecompilerSettings settings = new DecompilerSettings(LanguageVersion.CSharp4)
        {
            ThrowOnAssemblyResolveErrors = false,
            RemoveDeadCode = true,
            RemoveDeadStores = true
        };

        static List<IILTransform> ilTransforms = new List<IILTransform> {
                new ControlFlowSimplification(),
                new SplitVariables(),
                new ILInlining(),
            };
        static void Main(string[] args)
        {
            var file = new PEFile("./break_continue.exe");
            var assemblyResolver = new UniversalAssemblyResolver("./break_continue.exe", false, file.Reader.DetectTargetFrameworkId());
            var typeSystem = new DecompilerTypeSystem(file, assemblyResolver, settings);

            var ilReader = new ILReader(typeSystem.MainModule)
            {
                UseDebugSymbols = settings.UseDebugSymbols,
                DebugInfo = null
            };
            var decompiler = new Decompiler.CSharp.CSharpDecompiler(typeSystem, settings);

            foreach (var typeDefHandle in typeSystem.MainModule.TypeDefinitions)
            {

                foreach (var method in typeDefHandle.GetMethods())
                {
                    if (!method.Name.Contains("Main")) continue;

                    var methodDef = file.Metadata.GetMethodDefinition((MethodDefinitionHandle)method.MetadataToken);
                    var methodBody = file.Reader.GetMethodBody(methodDef.RelativeVirtualAddress);
                    ILFunction il = ilReader.ReadIL((MethodDefinitionHandle)method.MetadataToken, methodBody, kind: ILFunctionKind.TopLevelFunction);

                    ILTransformContext context = decompiler.CreateILTransformContext(il);

                    il.RunTransforms(new IILTransform[] {
                        new ControlFlowSimplification(),
                        new ILInlining(),
                        new SplitVariables(),
                        new BlockILTransform {
                            PostOrderTransforms = {
                                new LoopDetection(),
                            }
                        }, new BlockILTransform {
                            PostOrderTransforms = {
                                new ConditionDetection(),
                                new StatementTransform(
                                    new ILInlining(),
                                    new ExpressionTransforms())
                            }
                        },
                        new AssignVariableNames(),

                    }, context);

                    // Console.WriteLine(il);
                    // continue;

                    Console.WriteLine("#include<stdint.h>\n#include<stdbool.h>\n#include<stdio.h>\n\n\nvoid {0}() {{", method.Name.ToLowerInvariant());



                    foreach (var variable in il.Variables)
                    {
                        if (variable.Type.ToPrimitiveType() == PrimitiveType.I4)
                        {
                            Console.WriteLine("{0} {1};", "int32_t", variable.Name);
                        }
                        else if (variable.Type.Name == "Boolean")
                        {
                            Console.WriteLine("{0} {1};", "bool", variable.Name);
                        }
                        else
                        {
                            Console.WriteLine("Error: Variable type {0} {1}", variable.Type, variable.Type.ToPrimitiveType());
                        }
                    }

                    Console.WriteLine(GetStatement(il.Body));

                    Console.WriteLine("}");
                }
            }
        }

        static String GetStatement(ILInstruction instruction)
        {
            if (instruction.MatchStLoc(out var variable, out var value))
            {
                return String.Format("{0} = {1};", variable.Name, GetExpression(value));
            }
            else if (instruction.MatchBranch(out var block))
            {
                return String.Format("continue;");

                // return String.Format("{0} {1}", instruction, block);
            }
            else if (instruction.MatchIfInstruction(out var cond, out var trueCase, out var falseCase))
            {
                if (falseCase.MatchNop())
                {

                    return String.Format("if({0}) {{\n {1} \n}}\n", GetExpression(cond), GetStatement(trueCase));

                }
                else
                {
                    return String.Format("if({0}) {{\n {1} \n}} else {{\n {2} \n}}\n", GetExpression(cond), GetStatement(trueCase), GetStatement(falseCase));

                }

            }
            else if (instruction.OpCode == OpCode.Call)
            {
                var call = instruction as Call;

                return String.Format("printf(\"%d\\n\", {0});", GetExpression(call.Arguments[0]));
            }
            else if (instruction.OpCode == OpCode.BlockContainer)
            {
                var blockContainer = instruction as BlockContainer;
                var builder = new StringBuilder();
                if (blockContainer.Kind == ContainerKind.Loop)
                {
                    builder.AppendLine("while(1){");
                    foreach (var b in blockContainer.Blocks)
                    {
                        builder.AppendLine(GetBlock(b));
                    }
                    builder.AppendLine("}");
                    return builder.ToString();
                }
                else if (blockContainer.Kind == ContainerKind.Normal)
                {
                    builder.AppendLine("{");
                    foreach (var b in blockContainer.Blocks)
                    {
                        builder.AppendLine(GetBlock(b));
                    }
                    builder.AppendLine("}");
                    return builder.ToString();
                }
                else
                {
                    return String.Format("Error: GetBlockContainer {0}", blockContainer.Kind);
                };
            }
            else if (instruction is Block)
            {
                return GetBlock(instruction as Block);
            }
            else if (instruction.MatchLeave(out var container, out var leave_value))
            {
                if (container.Kind == ContainerKind.Loop)
                {
                    return String.Format("break;");
                }
                else if (container.Kind == ContainerKind.Normal)
                {
                    return String.Format("return;");
                }
                else
                {
                    return String.Format("Error leave: {0} {1}", container.Kind, leave_value);
                }
            }
            else
            {
                return String.Format("Error: GetStatement {0}", instruction.GetType());
            }
        }


        static String GetExpression(ILInstruction instruction)
        {
            if (instruction.MatchLdcI4(out var value))
            {
                return value.ToString();
            }
            else if (instruction is Comp)
            {
                var comp = instruction as Comp;
                if (comp.Kind == ComparisonKind.GreaterThanOrEqual)
                {
                    return String.Format("{0} {1} {2}", GetExpression(comp.Left), ">=", GetExpression(comp.Right));

                }
                else if (comp.Kind == ComparisonKind.GreaterThan)
                {
                    return String.Format("{0} {1} {2}", GetExpression(comp.Left), ">", GetExpression(comp.Right));

                }
                else if (comp.Kind == ComparisonKind.LessThan)
                {
                    return String.Format("{0} {1} {2}", GetExpression(comp.Left), "<", GetExpression(comp.Right));

                }
                else if (comp.Kind == ComparisonKind.Equality)
                {
                    return String.Format("{0} {1} {2}", GetExpression(comp.Left), "==", GetExpression(comp.Right));

                }
                else
                {
                    return String.Format("Error comp: {0}", comp.Kind);
                }
            }
            else if (instruction.MatchLdLoc(out var variable))
            {
                return variable.Name;
            }
            else if (instruction.MatchBinaryNumericInstruction(out var op, out var left, out var right))
            {
                if (op == BinaryNumericOperator.Add)
                {
                    return String.Format("{0} + {1}", GetExpression(left), GetExpression(right));
                }
                else
                {
                    return String.Format("Error: BinaryNumericOperator {0}", op);
                }
            }
            else
            {
                return String.Format("Error: GetExpression {0}", instruction.GetType());
            }
        }
        static String GetBlock(Block block)
        {
            // Console.WriteLine(block);
            if (block.Kind == BlockKind.ControlFlow)
            {
                return String.Join("\n", block.Instructions.Select((instruction) => GetStatement(instruction)));
            }
            else
            {
                return String.Format("Error: GetBlock {0}", block.Kind);
            }
        }
    }
}
