// Copyright 2024 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using System.Text;

namespace IcarusDataMiner
{
	/// <summary>
	/// Disassembles a compiled and serialized UFunction. Must set ReadScriptData=true on the <see cref="IFileProvider" /> used to load the UFunction.
	/// </summary>
	/// <remarks>
	/// This class is only half baked, but it meets the needs of this program.
	/// </remarks>
	internal class UFunctionDisassembler : IDisposable
	{
		private const string BlockSeparator = "--------------------------------------------------------------------------------";

		private readonly Package mPackage;

		private readonly MemoryStream mStream;

		private readonly BinaryReader mReader;

		private readonly StringWriter mWriter;

		private int mIndentLevel;

		private UFunctionDisassembler(Package package, UFunction function)
		{
			mPackage = package;

			mStream = new MemoryStream(function.Script!, false);
			mReader = new BinaryReader(mStream);
			mWriter = new StringWriter();

			mIndentLevel = -1;
		}

		public void Dispose()
		{
			mWriter.Dispose();
			mReader.Dispose();
			mStream.Dispose();
		}

		public static DisassembledFunction Process(Package package, UFunction function)
		{
			if (package == null) throw new ArgumentNullException(nameof(package));
			if (function == null) throw new ArgumentNullException(nameof(function));
			if (function.Script == null) throw new ArgumentException($"Function script is not loaded. Set {nameof(IFileProvider.ReadScriptData)}=true on the {nameof(IFileProvider)} used to load the function.", nameof(function));

			EFunctionFlags functionFlags = (EFunctionFlags)function.FunctionFlags;

			using UFunctionDisassembler instance = new(package, function);
			List<Operation> operations = new();
			string assembly = instance.Process(operations);

			return new DisassembledFunction(function.Name, functionFlags, operations, assembly);
		}

		private string Process(IList<Operation> operations)
		{
			int offset = 0;
			while (mStream.Position < mStream.Length)
			{
				ProcessExpr(operations, ref offset);
			}

			return mWriter.ToString();
		}

		// Based on ProcessCommon from ScriptDisassembler.cpp. Adapted to read serialized data.
		// Offset calculation is based on deserialized data. Sizes referenced from ScriptSerialization.h
		// which is inlined into UStruct::Serialize in Class.cpp.
		private EExprToken ProcessExpr(IList<Operation> operations, ref int offset)
		{
			int opOffset = offset;
			EExprToken opcode = (EExprToken)ReadByte(ref offset);
			++mIndentLevel;

			switch (opcode)
			{
				case EExprToken.PrimitiveCast:
					{
						// A type conversion.
						ECastToken conversionType = (ECastToken)ReadByte(ref offset);
						Log(opOffset, opcode, $"Type {conversionType}");

						List<Operation> childOperations = new();

						Log("Argument:");
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation<ECastToken>(opcode, conversionType, childOperations));
						break;
					}
				case EExprToken.SetSet:
					{
						Log(opOffset, opcode);

						List<Operation> childOperations = new();

						ProcessExpr(childOperations, ref offset);
						ReadInt32(ref offset);
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndSet)
						{
							// Set contents
						}

						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.SetConst:
					{
						FieldReference? innerProp = ReadField(ref offset);

						int count = ReadInt32(ref offset);
						Log(opOffset, opcode, $"Element count: {count}, inner property: {innerProp}");

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndSetConst)
						{
							// Set contents
						}

						operations.Add(new Operation<int>(opcode, count, childOperations));
						break;
					}
				case EExprToken.SetMap:
					{
						List<Operation> keyOperations = new(1);
						ProcessExpr(keyOperations, ref offset);

						int count = ReadInt32(ref offset);
						Log(opOffset, opcode, $"Element count: {count}");

						List<Operation> valueOperations = new();
						while (ProcessExpr(valueOperations, ref offset) != EExprToken.EndMap)
						{
							// Map contents
						}

						operations.Add(new Operation<Operation>(opcode, keyOperations[0], valueOperations));
						break;
					}
				case EExprToken.MapConst:
					{
						FieldReference? keyProp = ReadField(ref offset);
						FieldReference? valueProp = ReadField(ref offset);

						int count = ReadInt32(ref offset);
						Log(opOffset, opcode, $"Element count: {count}, key property: {keyProp}, value property: {valueProp}");

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndMapConst)
						{
							// Map contents
						}

						operations.Add(new Operation<MapOperand>(opcode, new MapOperand(keyProp, valueProp), childOperations));
						break;
					}
				case EExprToken.ObjToInterfaceCast:
				case EExprToken.CrossInterfaceCast:
				case EExprToken.InterfaceToObjCast:
					{
						// A conversion from an object variable to a native interface variable.
						// We use a different bytecode to avoid the branching each time we process a cast token

						// the interface class to convert to
						string? interfaceClassName = ReadObject(ref offset).ObjectName.Text;

						Log(opOffset, opcode, $"Cast to {interfaceClassName}");

						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation<string?>(opcode, interfaceClassName, childOperations));
						break;
					}
				case EExprToken.Let:
					{
						Log(opOffset, opcode, "Variable = Expression");

						ReadField(ref offset);

						++mIndentLevel;

						List<Operation> childOperations = new(2);

						// Variable expr.
						Log("Variable:");
						ProcessExpr(childOperations, ref offset);

						// Assignment expr.
						Log("Expression:");
						ProcessExpr(childOperations, ref offset);

						--mIndentLevel;

						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.LetObj:
				case EExprToken.LetWeakObjPtr:
				case EExprToken.LetBool:
				case EExprToken.LetDelegate:
				case EExprToken.LetMulticastDelegate:
					{
						Log(opOffset, opcode, "Variable = Expression");

						List<Operation> childOperations = new(2);

						++mIndentLevel;

						// Variable expr.
						Log("Variable:");
						ProcessExpr(childOperations, ref offset);

						// Assignment expr.
						Log("Expression:");
						ProcessExpr(childOperations, ref offset);

						--mIndentLevel;

						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.LetValueOnPersistentFrame:
					{
						Log(opOffset, opcode);

						++mIndentLevel;

						FieldReference? prop = ReadField(ref offset);
						Log($"Destination variable: {prop}");

						List<Operation> childOperations = new(1);

						Log("Expression:");
						ProcessExpr(childOperations, ref offset);

						--mIndentLevel;

						operations.Add(new Operation<FieldReference?>(opcode, prop, childOperations));
						break;
					}
				case EExprToken.StructMemberContext:
					{
						Log(opOffset, opcode);

						++mIndentLevel;

						FieldReference? prop = ReadField(ref offset);
						Log($"Member name: {prop}");

						List<Operation> childOperations = new(1);

						Log("Expression to struct:");
						ProcessExpr(childOperations, ref offset);

						--mIndentLevel;

						operations.Add(new Operation<FieldReference?>(opcode, prop, childOperations));
						break;
					}
				case EExprToken.LocalVirtualFunction:
				case EExprToken.VirtualFunction:
					{
						string? functionName = ReadName(ref offset);
						Log(opOffset, opcode, functionName);

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndFunctionParms)
						{
						}
						operations.Add(new Operation<string?>(opcode, functionName, childOperations));
						break;
					}
				case EExprToken.LocalFinalFunction:
				case EExprToken.FinalFunction:
				case EExprToken.CallMath:
					{
						FObjectResource stackNode = ReadObject(ref offset);
						string functionName = $"{stackNode.OuterIndex.Name}::{stackNode.ObjectName.Text}";
						Log(opOffset, opcode, functionName);

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndFunctionParms)
						{
							// Params
						}
						operations.Add(new Operation<string?>(opcode, functionName, childOperations));
						break;
					}
				case EExprToken.ComputedJump:
					{
						Log(opOffset, opcode, "Offset specified by expression:");

						++mIndentLevel;

						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);

						--mIndentLevel;

						if (mIndentLevel == 0)
						{
							mWriter.WriteLine(BlockSeparator);
						}

						operations.Add(new Operation(opcode, childOperations));
						break;
					}

				case EExprToken.Jump:
					{
						int skipCount = ReadInt32(ref offset);
						Log(opOffset, opcode, $"Offset = 0x{skipCount:X}");
						if (mIndentLevel == 0)
						{
							mWriter.WriteLine(BlockSeparator);
						}
						break;
					}
				case EExprToken.LocalVariable:
				case EExprToken.DefaultVariable:
				case EExprToken.InstanceVariable:
				case EExprToken.LocalOutVariable:
				case EExprToken.ClassSparseDataVariable:
					{
						FieldReference? property = ReadField(ref offset);
						Log(opOffset, opcode, property?.ToString());

						operations.Add(new Operation<FieldReference?>(opcode, property));
						break;
					}
				case EExprToken.InterfaceContext:
				case EExprToken.Return:
					{
						Log(opOffset, opcode);
						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);
						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.DeprecatedOp4A:
					{
						Log(opOffset, opcode, "This opcode has been removed and does nothing.");
						operations.Add(new Operation(opcode));
						break;
					}
				case EExprToken.Nothing:
				case EExprToken.EndOfScript:
				case EExprToken.IntZero:
				case EExprToken.IntOne:
				case EExprToken.True:
				case EExprToken.False:
				case EExprToken.NoObject:
				case EExprToken.NoInterface:
				case EExprToken.Self:
				case EExprToken.EndParmValue:
					{
						Log(opOffset, opcode);
						operations.Add(new Operation(opcode));
						break;
					}
				case EExprToken.EndArray:
				case EExprToken.EndArrayConst:
				case EExprToken.EndFunctionParms:
				case EExprToken.EndMap:
				case EExprToken.EndMapConst:
				case EExprToken.EndSet:
				case EExprToken.EndSetConst:
				case EExprToken.EndStructConst:
					{
						// Don't log these because they are implied
						operations.Add(new Operation(opcode));
						break;
					}
				case EExprToken.CallMulticastDelegate:
					{
						FObjectResource stackNode = ReadObject(ref offset);
						string functionName = $"{stackNode.OuterIndex.Name}::{stackNode.ObjectName.Text}";
						Log(opOffset, opcode, functionName);

						List<Operation> targetOperation = new(1);
						ProcessExpr(targetOperation, ref offset);

						++mIndentLevel;

						Log("Params:");

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndFunctionParms)
						{
							// Params
						}
						--mIndentLevel;

						operations.Add(new Operation<Operation>(opcode, targetOperation[0], childOperations));
						break;
					}
				case EExprToken.ClassContext:
				case EExprToken.Context:
				case EExprToken.Context_FailSilent:
					{
						Log(opOffset, opcode);

						++mIndentLevel;

						// Object expression.
						Log("ObjectExpression:");
						List<Operation> objectExpression = new(1);
						ProcessExpr(objectExpression, ref offset);

						bool canFailSilently = opcode == EExprToken.Context_FailSilent;
						if (canFailSilently)
						{
							Log("Can fail silently on access none");
						}

						// Code offset for NULL expressions.
						int skipCount = ReadInt32(ref offset);
						Log($"Skip {skipCount} bytes");

						// Property corresponding to the r-value data, in case the l-value needs to be mem-zero'd
						FieldReference? field = ReadField(ref offset);
						Log($"R-Value Property: {field}");

						// Context expression.
						Log("ContextExpression:");
						List<Operation> contextExpression = new(1);
						ProcessExpr(contextExpression, ref offset);

						--mIndentLevel;

						operations.Add(new Operation<ContextOperand>(opcode, new ContextOperand(objectExpression[0], contextExpression[0], canFailSilently, skipCount, field)));
						break;
					}
				case EExprToken.IntConst:
					{
						int constValue = ReadInt32(ref offset);
						Log(opOffset, opcode, constValue.ToString());
						operations.Add(new Operation<int>(opcode, constValue));
						break;
					}
				case EExprToken.Int64Const:
					{
						long constValue = ReadInt64(ref offset);
						Log(opOffset, opcode, constValue.ToString());
						operations.Add(new Operation<long>(opcode, constValue));
						break;
					}
				case EExprToken.UInt64Const:
					{
						ulong constValue = ReadUInt64(ref offset);
						Log(opOffset, opcode, constValue.ToString());
						operations.Add(new Operation<ulong>(opcode, constValue));
						break;
					}
				case EExprToken.SkipOffsetConst:
					{
						int constValue = ReadInt32(ref offset);
						Log(opOffset, opcode, $"0x{constValue:X}");
						operations.Add(new Operation<int>(opcode, constValue));
						break;
					}
				case EExprToken.FloatConst:
					{
						float constValue = ReadSingle(ref offset);
						Log(opOffset, opcode, constValue.ToString("0.0###"));
						operations.Add(new Operation<float>(opcode, constValue));
						break;
					}
				case EExprToken.StringConst:
					{
						string constValue = ReadAsciiString(ref offset);
						Log(opOffset, opcode, constValue);
						operations.Add(new Operation<string>(opcode, constValue));
						break;
					}
				case EExprToken.UnicodeStringConst:
					{
						string constValue = ReadUnicodeString(ref offset);
						Log(opOffset, opcode, constValue);
						operations.Add(new Operation<string>(opcode, constValue));
						break;
					}
				case EExprToken.TextConst:
					{
						// What kind of text are we dealing with?
						EBlueprintTextLiteralType textLiteralType = (EBlueprintTextLiteralType)ReadByte(ref offset);

						List<string> values = new();
						switch (textLiteralType)
						{
							case EBlueprintTextLiteralType.Empty:
								{
									Log(opOffset, opcode, "Empty");
								}
								break;

							case EBlueprintTextLiteralType.LocalizedText:
								{
									string sourceString = ReadString(ref offset); values.Add(sourceString);
									string keyString = ReadString(ref offset); values.Add(keyString);
									string namespaceString = ReadString(ref offset); values.Add(namespaceString);
									Log(opOffset, opcode, $"Localized: {{ namespace: \"{namespaceString}\", key: \"{keyString}\", source: \"{sourceString}\" }}");
								}
								break;

							case EBlueprintTextLiteralType.InvariantText:
								{
									string sourceString = ReadString(ref offset); values.Add(sourceString);
									Log(opOffset, opcode, $"Invariant: \"{sourceString}\"");
								}
								break;

							case EBlueprintTextLiteralType.LiteralString:
								{
									string sourceString = ReadString(ref offset); values.Add(sourceString);
									Log(opOffset, opcode, $"Literal: \"{sourceString}\"");
								}
								break;

							case EBlueprintTextLiteralType.StringTableEntry:
								{
									ReadObject(ref offset); // String Table asset (if any)
									string tableIdString = ReadString(ref offset); values.Add(tableIdString);
									string keyString = ReadString(ref offset); values.Add(keyString);
									Log(opOffset, opcode, $"String table entry: {{ tableid: \"{tableIdString}\", key: \"{keyString}\" }}");
								}
								break;

							default:
								throw new FormatException($"Unexpected EBlueprintTextLiteralType value {textLiteralType}");
						}

						operations.Add(new Operation<TextOperand>(opcode, new TextOperand(textLiteralType, values)));
						break;
					}
				case EExprToken.PropertyConst:
					{
						FieldReference? field = ReadField(ref offset);
						Log(opOffset, opcode, field?.ToString() ?? "null");
						operations.Add(new Operation<FieldReference?>(opcode, field));
						break;
					}
				case EExprToken.ObjectConst:
					{
						FObjectResource obj = ReadObject(ref offset);
						Log(opOffset, opcode, obj.ObjectName.Text);
						operations.Add(new Operation<FObjectResource>(opcode, obj));
						break;
					}
				case EExprToken.SoftObjectConst:
				case EExprToken.FieldPathConst:
					{
						Log(opOffset, opcode);
						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);
						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.NameConst:
					{
						string? constValue = ReadName(ref offset);
						Log(opOffset, opcode, constValue);
						operations.Add(new Operation<string?>(opcode, constValue));
						break;
					}
				case EExprToken.RotationConst:
					{
						float pitch = ReadSingle(ref offset);
						float yaw = ReadSingle(ref offset);
						float roll = ReadSingle(ref offset);

						Log(opOffset, opcode, $"Pitch={pitch:0.0###}, Yaw={yaw:0.0###}, Roll={roll:0.0###}");
						operations.Add(new Operation<FRotator>(opcode, new FRotator(pitch, yaw, roll)));
						break;
					}
				case EExprToken.VectorConst:
					{
						float x = ReadSingle(ref offset);
						float y = ReadSingle(ref offset);
						float z = ReadSingle(ref offset);

						Log(opOffset, opcode, $"X={x:0.0###}, Y={y:0.0###}, Z={z:0.0###}");
						operations.Add(new Operation<FVector>(opcode, new FVector(x, y, z)));
						break;
					}
				case EExprToken.TransformConst:
					{
						ScriptMatrix matrix = new()
						{
							Rotation = new()
							{
								X = ReadSingle(ref offset),
								Y = ReadSingle(ref offset),
								Z = ReadSingle(ref offset),
								W = ReadSingle(ref offset)
							},
							Translation = new()
							{
								X = ReadSingle(ref offset),
								Y = ReadSingle(ref offset),
								Z = ReadSingle(ref offset)
							},
							Scale = new()
							{
								X = ReadSingle(ref offset),
								Y = ReadSingle(ref offset),
								Z = ReadSingle(ref offset)
							}
						};

						Log(opOffset, opcode, matrix.ToString());
						operations.Add(new Operation<ScriptMatrix>(opcode, matrix));
						break;
					}
				case EExprToken.StructConst:
					{
						FObjectResource structObj = ReadObject(ref offset);
						int serializedSize = ReadInt32(ref offset);
						Log(opOffset, opcode, $"{structObj.ObjectName.Text} (serialized size: {serializedSize})");

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndStructConst)
						{
							// struct contents
						}

						operations.Add(new Operation<StructOperand>(opcode, new StructOperand(structObj, serializedSize), childOperations));
						break;
					}
				case EExprToken.SetArray:
					{
						Log(opOffset, opcode);

						// As of VER_UE4_CHANGE_SETARRAY_BYTECODE. Prior versions have an FProperty here instead of an expression
						List<Operation> arrayExpr = new(1);
						ProcessExpr(arrayExpr, ref offset);

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndArray)
						{
							// Array contents
						}

						operations.Add(new Operation<Operation>(opcode, arrayExpr[0], childOperations));
						break;
					}
				case EExprToken.ArrayConst:
					{
						FieldReference? innerPropName = ReadField(ref offset);
						int num = ReadInt32(ref offset);

						Log(opOffset, opcode, $"Type: {innerPropName}, Count: {num}");

						List<Operation> childOperations = new();
						while (ProcessExpr(childOperations, ref offset) != EExprToken.EndArrayConst)
						{
							// Array contents
						}

						operations.Add(new Operation<ArrayOperand>(opcode, new ArrayOperand(innerPropName, num), childOperations));
						break;
					}
				case EExprToken.ByteConst:
				case EExprToken.IntConstByte:
					{
						byte constValue = ReadByte(ref offset);
						Log(opOffset, opcode, constValue.ToString());
						operations.Add(new Operation<byte?>(opcode, constValue));
						break;
					}
				case EExprToken.MetaCast:
				case EExprToken.DynamicCast:
					{
						FObjectResource classObj = ReadObject(ref offset);
						Log(opOffset, opcode, $"Cast to {classObj.ObjectName.Text} of expr:");

						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation<FObjectResource>(opcode, classObj, childOperations));
						break;
					}
				case EExprToken.JumpIfNot:
					{
						// Code offset.
						int skipCount = ReadInt32(ref offset);

						Log(opOffset, opcode, $"Offset: 0x{skipCount:X}, Condition:");

						// Boolean expr.
						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation<int>(opcode, skipCount, childOperations));
						break;
					}
				case EExprToken.Assert:
					{
						ushort lineNumber = ReadUInt16(ref offset);
						byte inDebugMode = ReadByte(ref offset);

						Log(opOffset, opcode, $"Line {lineNumber}, in debug mode = {inDebugMode != 0} with expr:");
						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset); // Assert expr.

						operations.Add(new Operation<AssertOperand>(opcode, new AssertOperand(lineNumber, inDebugMode), childOperations));
						break;
					}
				case EExprToken.Skip:
					{
						int skipCount = ReadInt32(ref offset);
						Log(opOffset, opcode, $"Possibly skip {skipCount} bytes of expr:");

						// Expression to possibly skip.
						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation<int>(opcode, skipCount, childOperations));
						break;
					}
				case EExprToken.InstanceDelegate:
					{
						// the name of the function assigned to the delegate.
						string? funcName = ReadName(ref offset);

						Log(opOffset, opcode, funcName);

						operations.Add(new Operation<string?>(opcode, funcName));
						break;
					}
				case EExprToken.AddMulticastDelegate:
				case EExprToken.RemoveMulticastDelegate:
					{
						Log(opOffset, opcode);

						List<Operation> childOperations = new(2);
						ProcessExpr(childOperations, ref offset);
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.ClearMulticastDelegate:
					{
						Log(opOffset, opcode);

						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.BindDelegate:
					{
						// the name of the function assigned to the delegate.
						string? funcName = ReadName(ref offset);

						Log(opOffset, opcode, funcName);

						++mIndentLevel;

						List<Operation> childOperations = new(2);

						Log("Delegate:");
						ProcessExpr(childOperations, ref offset);

						Log("Object:");
						ProcessExpr(childOperations, ref offset);

						--mIndentLevel;

						operations.Add(new Operation<string?>(opcode, funcName, childOperations));
						break;
					}
				case EExprToken.PushExecutionFlow:
					{
						int skipCount = ReadInt32(ref offset);
						Log(opOffset, opcode, $"FlowStack.Push(0x{skipCount:X})");
						operations.Add(new Operation<int>(opcode, skipCount));
						break;
					}
				case EExprToken.PopExecutionFlow:
					{
						Log(opOffset, opcode, "Jump to statement at FlowStack.Pop()");
						operations.Add(new Operation(opcode));
						if (mIndentLevel == 0)
						{
							mWriter.WriteLine(BlockSeparator);
						}
						break;
					}
				case EExprToken.PopExecutionFlowIfNot:
					{
						Log(opOffset, opcode, "Jump to statement at FlowStack.Pop(). Condition:");

						// Boolean expr.
						List<Operation> childOperations = new(1);
						ProcessExpr(childOperations, ref offset);

						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				case EExprToken.Breakpoint:
				case EExprToken.WireTracepoint:
				case EExprToken.Tracepoint:
					{
						Log(opOffset, opcode);
						operations.Add(new Operation(opcode));
						break;
					}
				case EExprToken.InstrumentationEvent:
					{
						EScriptInstrumentation eventType = (EScriptInstrumentation)ReadByte(ref offset);
						if (eventType == EScriptInstrumentation.InlineEvent)
						{
							offset += 12; // event name, not serialized
						}
						++offset; // There is always a byte skipped here when executing this code

						Log(opOffset, opcode, eventType.ToString());
						operations.Add(new Operation<EScriptInstrumentation>(opcode, eventType));
						break;
					}
				case EExprToken.SwitchValue:
					{
						ushort numCases = ReadUInt16(ref offset);
						int afterOffset = ReadInt32(ref offset);

						Log(opOffset, opcode, $"{numCases} cases, end at 0x{afterOffset:X}");

						++mIndentLevel;

						Log("Index:");
						List<Operation> indexOperation = new(1);
						ProcessExpr(indexOperation, ref offset);

						SwitchOperand.Case[] cases = new SwitchOperand.Case[numCases];
						for (ushort caseIndex = 0; caseIndex < numCases; ++caseIndex)
						{
							Log($"Case {caseIndex}:");
							List<Operation> caseIndexOperation = new(1);
							ProcessExpr(caseIndexOperation, ref offset); // case index value term

							++mIndentLevel;

							int offsetToNextCase = ReadInt32(ref offset);
							Log($"Offset of next case: 0x{offsetToNextCase}");
							Log("Case Term:");
							List<Operation> caseIResultOperation = new(1);
							ProcessExpr(caseIResultOperation, ref offset); // case term

							--mIndentLevel;

							cases[caseIndex] = new SwitchOperand.Case(caseIndexOperation[0], caseIResultOperation[0], offsetToNextCase);
						}

						Log($"Default term:");
						List<Operation> defaultOperation = new(1);
						ProcessExpr(defaultOperation, ref offset);

						--mIndentLevel;

						operations.Add(new Operation<SwitchOperand>(opcode, new SwitchOperand(indexOperation[0], cases, defaultOperation[0])));
						break;
					}
				case EExprToken.ArrayGetByRef:
					{
						Log(opOffset, opcode);

						++mIndentLevel;

						List<Operation> childOperations = new(2);
						ProcessExpr(childOperations, ref offset);
						ProcessExpr(childOperations, ref offset);

						--mIndentLevel;

						operations.Add(new Operation(opcode, childOperations));
						break;
					}
				default:
					throw new NotImplementedException($"Op code {opcode} not implemented in disassembler");
			}

			--mIndentLevel;
			return opcode;
		}

		private void Log(int offset, EExprToken opcode)
		{
			mWriter.WriteLine($"{offset:X8}  {new string(' ', mIndentLevel * 2)}[{(int)opcode:X2}] {opcode}");
		}

		private void Log(string? message)
		{
			mWriter.WriteLine($"          {new string(' ', mIndentLevel * 2)}{message}");
		}

		private void Log(int offset, EExprToken opcode, string? message)
		{
			mWriter.WriteLine($"{offset:X8}  {new string(' ', mIndentLevel * 2)}[{(int)opcode:X2}] {opcode}: {message}");
		}

		private byte ReadByte(ref int offset)
		{
			++offset;
			return mReader.ReadByte();
		}

		private ushort ReadUInt16(ref int offset)
		{
			offset += 2;
			return mReader.ReadUInt16();
		}

		private int ReadInt32(ref int offset)
		{
			offset += 4;
			return mReader.ReadInt32();
		}

		private long ReadInt64(ref int offset)
		{
			offset += 8;
			return mReader.ReadInt64();
		}

		private ulong ReadUInt64(ref int offset)
		{
			offset += 8;
			return mReader.ReadUInt64();
		}

		private float ReadSingle(ref int offset)
		{
			offset += 4;
			return mReader.ReadSingle();
		}

		private FieldReference? ReadField(ref int offset)
		{
			offset += 8;

			// Reference: FieldPath.cpp operator<< called from PropertyProxyArchive.h

			int pathCount = mReader.ReadInt32();
			string?[] path = new string[pathCount];
			int temp = 0;
			for (int i = 0; i < pathCount; ++i)
			{
				path[i] = ReadName(ref temp);
			}
			FObjectResource owner = ReadObjectInternal();
			return new FieldReference(path, owner);
		}

		private string? ReadName(ref int offset)
		{
			offset += 12;
			int nameIndex = mReader.ReadInt32();
			int number = mReader.ReadInt32();

			string? name = mPackage.NameMap[nameIndex].Name;

			if (number == 0) return name;
			return $"{name}_{number - 1}";
		}

		private string ReadString(ref int offset)
		{
			EExprToken opcode = (EExprToken)ReadByte(ref offset);

			switch (opcode)
			{
				case EExprToken.StringConst:
					return ReadAsciiString(ref offset);
				case EExprToken.UnicodeStringConst:
					return ReadUnicodeString(ref offset);
				default:
					throw new FormatException($"Expected op code StringConst or UnicodeStringConst, found {opcode}");
			}
		}

		private string ReadAsciiString(ref int offset)
		{
			StringBuilder builder = new();
			for (byte c = mReader.ReadByte(); c != 0; c = mReader.ReadByte())
			{
				builder.Append((char)c);
			}
			offset += builder.Length + 1;
			return builder.ToString();
		}

		private string ReadUnicodeString(ref int offset)
		{
			StringBuilder builder = new();
			for (ushort c = mReader.ReadUInt16(); c != 0; c = mReader.ReadUInt16())
			{
				builder.Append((char)c);
			}
			offset += (builder.Length + 1) * 2;
			return builder.ToString();
		}

		private FObjectResource ReadObject(ref int offset)
		{
			offset += 8;
			return ReadObjectInternal();
		}

		private FObjectResource ReadObjectInternal()
		{
			int index = mReader.ReadInt32();
			if (index < 0) return mPackage.ImportMap[~index];
			return mPackage.ExportMap[index];
		}
	}

	// From Script.h
	[Flags]
	internal enum EFunctionFlags : uint
	{
		// Function flags.
		None = 0x00000000,

		Final = 0x00000001,    // Function is final (prebindable, non-overridable function).
		RequiredAPI = 0x00000002,  // Indicates this function is DLL exported/imported.
		BlueprintAuthorityOnly = 0x00000004,   // Function will only run if the object has network authority
		BlueprintCosmetic = 0x00000008,   // Function is cosmetic in nature and should not be invoked on dedicated servers
										  // FUNC_				= 0x00000010,   // unused.
										  // FUNC_				= 0x00000020,   // unused.
		Net = 0x00000040,   // Function is network-replicated.
		NetReliable = 0x00000080,   // Function should be sent reliably on the network.
		NetRequest = 0x00000100,   // Function is sent to a net service
		Exec = 0x00000200, // Executable from command line.
		Native = 0x00000400,   // Native function.
		Event = 0x00000800,   // Event function.
		NetResponse = 0x00001000,   // Function response from a net service
		Static = 0x00002000,   // Static function.
		NetMulticast = 0x00004000, // Function is networked multicast Server -> All Clients
		UbergraphFunction = 0x00008000,   // Function is used as the merge 'ubergraph' for a blueprint, only assigned when using the persistent 'ubergraph' frame
		MulticastDelegate = 0x00010000,    // Function is a multi-cast delegate signature (also requires FUNC_Delegate to be set!)
		Public = 0x00020000,   // Function is accessible in all classes (if overridden, parameters must remain unchanged).
		Private = 0x00040000,  // Function is accessible only in the class it is defined in (cannot be overridden, but function name may be reused in subclasses.  IOW: if overridden, parameters don't need to match, and Super.Func() cannot be accessed since it's private.)
		Protected = 0x00080000,    // Function is accessible only in the class it is defined in and subclasses (if overridden, parameters much remain unchanged).
		Delegate = 0x00100000, // Function is delegate signature (either single-cast or multi-cast, depending on whether FUNC_MulticastDelegate is set.)
		NetServer = 0x00200000,    // Function is executed on servers (set by replication code if passes check)
		HasOutParms = 0x00400000,  // function has out (pass by reference) parameters
		HasDefaults = 0x00800000,  // function has structs that contain defaults
		NetClient = 0x01000000,    // function is executed on clients
		DLLImport = 0x02000000,    // function is imported from a DLL
		BlueprintCallable = 0x04000000,    // function can be called from blueprint code
		BlueprintEvent = 0x08000000,   // function can be overridden/implemented from a blueprint
		BlueprintPure = 0x10000000,    // function can be called from blueprint code, and is also pure (produces no side effects). If you set this, you should set FUNC_BlueprintCallable as well.
		EditorOnly = 0x20000000,   // function can only be called from an editor scrippt.
		Const = 0x40000000,    // function can be called from blueprint code, and only reads state (never writes state)
		NetValidate = 0x80000000,  // function must supply a _Validate implementation

		AllFlags = 0xFFFFFFFF,
	};

	// From Script.h
	internal enum EExprToken : byte
	{
		// Variable references.
		LocalVariable = 0x00,    // A local variable.
		InstanceVariable = 0x01, // An object variable.
		DefaultVariable = 0x02, // Default variable for a class context.
								//						= 0x03,
		Return = 0x04,   // Return from function.
						 //						= 0x05,
		Jump = 0x06, // Goto a local address in code.
		JumpIfNot = 0x07,    // Goto if not expression.
							 //						= 0x08,
		Assert = 0x09,   // Assertion.
						 //						= 0x0A,
		Nothing = 0x0B,  // No operation.
						 //						= 0x0C,
						 //						= 0x0D,
						 //						= 0x0E,
		Let = 0x0F,  // Assign an arbitrary size value to a variable.
					 //						= 0x10,
					 //						= 0x11,
		ClassContext = 0x12, // Class default object context.
		MetaCast = 0x13, // Metaclass cast.
		LetBool = 0x14, // Let boolean variable.
		EndParmValue = 0x15, // end of default value for optional function parameter
		EndFunctionParms = 0x16, // End of function call parameters.
		Self = 0x17, // Self object.
		Skip = 0x18, // Skippable expression.
		Context = 0x19,  // Call a function through an object context.
		Context_FailSilent = 0x1A, // Call a function through an object context (can fail silently if the context is NULL; only generated for functions that don't have output or return values).
		VirtualFunction = 0x1B,  // A function call with parameters.
		FinalFunction = 0x1C,    // A prebound function call with parameters.
		IntConst = 0x1D, // Int constant.
		FloatConst = 0x1E,   // Floating point constant.
		StringConst = 0x1F,  // String constant.
		ObjectConst = 0x20,  // An object constant.
		NameConst = 0x21,    // A name constant.
		RotationConst = 0x22,    // A rotation constant.
		VectorConst = 0x23,  // A vector constant.
		ByteConst = 0x24,    // A byte constant.
		IntZero = 0x25,  // Zero.
		IntOne = 0x26,   // One.
		True = 0x27, // Bool True.
		False = 0x28,    // Bool False.
		TextConst = 0x29, // FText constant
		NoObject = 0x2A, // NoObject.
		TransformConst = 0x2B, // A transform constant
		IntConstByte = 0x2C, // Int constant that requires 1 byte.
		NoInterface = 0x2D, // A null interface (similar to NoObject, but for interfaces)
		DynamicCast = 0x2E,  // Safe dynamic class casting.
		StructConst = 0x2F, // An arbitrary UStruct constant
		EndStructConst = 0x30, // End of UStruct constant
		SetArray = 0x31, // Set the value of arbitrary array
		EndArray = 0x32,
		PropertyConst = 0x33, // FProperty constant.
		UnicodeStringConst = 0x34, // Unicode string constant.
		Int64Const = 0x35,   // 64-bit integer constant.
		UInt64Const = 0x36,  // 64-bit unsigned integer constant.
							 //						= 0x37,
		PrimitiveCast = 0x38,    // A casting operator for primitives which reads the type as the subsequent byte
		SetSet = 0x39,
		EndSet = 0x3A,
		SetMap = 0x3B,
		EndMap = 0x3C,
		SetConst = 0x3D,
		EndSetConst = 0x3E,
		MapConst = 0x3F,
		EndMapConst = 0x40,
		//						= 0x41,
		StructMemberContext = 0x42, // Context expression to address a property within a struct
		LetMulticastDelegate = 0x43, // Assignment to a multi-cast delegate
		LetDelegate = 0x44, // Assignment to a delegate
		LocalVirtualFunction = 0x45, // Special instructions to quickly call a virtual function that we know is going to run only locally
		LocalFinalFunction = 0x46, // Special instructions to quickly call a final function that we know is going to run only locally
								   //						= 0x47, // CST_ObjectToBool
		LocalOutVariable = 0x48, // local out (pass by reference) function parameter
								 //						= 0x49, // CST_InterfaceToBool
		DeprecatedOp4A = 0x4A,
		InstanceDelegate = 0x4B, // const reference to a delegate or normal function object
		PushExecutionFlow = 0x4C, // push an address on to the execution flow stack for future execution when a PopExecutionFlow is executed.   Execution continues on normally and doesn't change to the pushed address.
		PopExecutionFlow = 0x4D, // continue execution at the last address previously pushed onto the execution flow stack.
		ComputedJump = 0x4E, // Goto a local address in code, specified by an integer value.
		PopExecutionFlowIfNot = 0x4F, // continue execution at the last address previously pushed onto the execution flow stack, if the condition is not true.
		Breakpoint = 0x50, // Breakpoint.  Only observed in the editor, otherwise it behaves like Nothing.
		InterfaceContext = 0x51, // Call a function through a native interface variable
		ObjToInterfaceCast = 0x52,   // Converting an object reference to native interface variable
		EndOfScript = 0x53, // Last byte in script code
		CrossInterfaceCast = 0x54, // Converting an interface variable reference to native interface variable
		InterfaceToObjCast = 0x55, // Converting an interface variable reference to an object
								   //						= 0x56,
								   //						= 0x57,
								   //						= 0x58,
								   //						= 0x59,
		WireTracepoint = 0x5A, // Trace point.  Only observed in the editor, otherwise it behaves like Nothing.
		SkipOffsetConst = 0x5B, // A CodeSizeSkipOffset constant
		AddMulticastDelegate = 0x5C, // Adds a delegate to a multicast delegate's targets
		ClearMulticastDelegate = 0x5D, // Clears all delegates in a multicast target
		Tracepoint = 0x5E, // Trace point.  Only observed in the editor, otherwise it behaves like Nothing.
		LetObj = 0x5F,   // assign to any object ref pointer
		LetWeakObjPtr = 0x60, // assign to a weak object pointer
		BindDelegate = 0x61, // bind object and name to delegate
		RemoveMulticastDelegate = 0x62, // Remove a delegate from a multicast delegate's targets
		CallMulticastDelegate = 0x63, // Call multicast delegate
		LetValueOnPersistentFrame = 0x64,
		ArrayConst = 0x65,
		EndArrayConst = 0x66,
		SoftObjectConst = 0x67,
		CallMath = 0x68, // static pure function from on local call space
		SwitchValue = 0x69,
		InstrumentationEvent = 0x6A, // Instrumentation event
		ArrayGetByRef = 0x6B,
		ClassSparseDataVariable = 0x6C, // Sparse data variable
		FieldPathConst = 0x6D
	};

	// From Script.h
	internal enum EBlueprintTextLiteralType : byte
	{
		/* Text is an empty string. The bytecode contains no strings, and you should use FText::GetEmpty() to initialize the FText instance. */
		Empty,
		/** Text is localized. The bytecode will contain three strings - source, key, and namespace - and should be loaded via FInternationalization */
		LocalizedText,
		/** Text is culture invariant. The bytecode will contain one string, and you should use FText::AsCultureInvariant to initialize the FText instance. */
		InvariantText,
		/** Text is a literal FString. The bytecode will contain one string, and you should use FText::FromString to initialize the FText instance. */
		LiteralString,
		/** Text is from a string table. The bytecode will contain an object pointer (not used) and two strings - the table ID, and key - and should be found via FText::FromStringTable */
		StringTableEntry,
	};

	// From Script.h
	internal enum EScriptInstrumentation : byte
	{
		Class = 0,
		ClassScope,
		Instance,
		Event,
		InlineEvent,
		ResumeEvent,
		PureNodeEntry,
		NodeDebugSite,
		NodeEntry,
		NodeExit,
		PushState,
		RestoreState,
		ResetState,
		SuspendState,
		PopState,
		TunnelEndOfThread,
		Stop
	}

	// From Script.h
	enum ECastToken
	{
		ObjectToInterface = 0x46,
		ObjectToBool = 0x47,
		InterfaceToBool = 0x49,
		Max = 0xFF,
	};

	internal class Operation
	{
		public EExprToken OpCode { get; }

		public IReadOnlyList<Operation> ChildOperations { get; }

		internal Operation(EExprToken opCode, IReadOnlyList<Operation> childOperations)
		{
			OpCode = opCode;
			ChildOperations = childOperations;
		}

		internal Operation(EExprToken opCode)
			: this(opCode, Array.Empty<Operation>())
		{
		}

		public override string ToString()
		{
			return OpCode.ToString();
		}
	}

	internal class Operation<T> : Operation
	{
		public T Operand { get; }

		internal Operation(EExprToken opCode, T operand, IReadOnlyList<Operation> childOperations)
			: base(opCode, childOperations)
		{
			Operand = operand;
		}

		internal Operation(EExprToken opCode, T operand)
			: this(opCode, operand, Array.Empty<Operation>())
		{
		}
	}

	internal class MapOperand
	{
		public FieldReference? Key { get; }

		public FieldReference? Value { get; }

		public MapOperand(FieldReference? key, FieldReference? value)
		{
			Key = key;
			Value = value;
		}
	}

	internal class ContextOperand
	{
		public Operation ObjectExpression { get; }

		public Operation ContextExpression { get; }

		public bool CanFailSilently { get; }

		public int SkipCount { get; }

		public FieldReference? RValue { get; }

		public ContextOperand(Operation objectExpression, Operation contextExpression, bool canFailSilently, int skipCount, FieldReference? rValue)
		{
			ObjectExpression = objectExpression;
			ContextExpression = contextExpression;
			CanFailSilently = canFailSilently;
			SkipCount = skipCount;
			RValue = rValue;
		}
	}

	internal class TextOperand
	{
		public EBlueprintTextLiteralType Type { get; }

		public IReadOnlyList<string> Values { get; }

		public TextOperand(EBlueprintTextLiteralType type, IReadOnlyList<string> values)
		{
			Type = type;
			Values = values;
		}
	}

	internal class StructOperand
	{
		public FObjectResource ObjectImport { get; }

		public int SerializedSize { get; }

		public StructOperand(FObjectResource objectImport, int serializedSize)
		{
			ObjectImport = objectImport;
			SerializedSize = serializedSize;
		}
	}

	internal class ArrayOperand
	{
		public FieldReference? Type { get; }

		public int Count { get; }

		public ArrayOperand(FieldReference? type, int count)
		{
			Type = type;
			Count = count;
		}
	}

	internal class AssertOperand
	{
		public ushort LineNumber { get; }

		public byte InDebugMode { get; }

		public AssertOperand(ushort lineNumber, byte inDebugMode)
		{
			LineNumber = lineNumber;
			InDebugMode = inDebugMode;
		}
	}

	internal class SwitchOperand
	{
		public Operation Index { get; }

		public IReadOnlyList<Case> Cases { get; }

		public Operation DefaultCase { get; }

		public SwitchOperand(Operation index, IReadOnlyList<Case> cases, Operation defaultCase)
		{
			Index = index;
			Cases = cases;
			DefaultCase = defaultCase;
		}

		public class Case
		{
			public Operation Index { get; }

			public Operation Result { get; }

			public int OffsetToNextCase { get; }

			public Case(Operation index, Operation result, int offsetToNextCase)
			{
				Index = index;
				Result = result;
				OffsetToNextCase = offsetToNextCase;
			}
		}
	}

	internal struct ScriptMatrix
	{
		public FQuat Rotation;

		public FVector Translation;

		public FVector Scale;

		public override string ToString()
		{
			return $"R({Rotation.X:F3},{Rotation.Y:F3},{Rotation.Z:F3},{Rotation.W:F3}) T({Translation.X:F3}{Translation.Y:F3}{Translation.Z:F3}) S({Scale.X:F3}{Scale.Y:F3}{Scale.Z:F3})";
		}
	}

	internal class FieldReference
	{
		public string? Path { get; }

		public FObjectResource Owner { get; }

		public FieldReference(IEnumerable<string?> path, FObjectResource owner)
		{
			Path = string.Join(".", path);
			Owner = owner;
		}

		public override string ToString()
		{
			if (Path == null) return Owner.ObjectName.Text;
			return Path;
		}
	}

	internal class DisassembledFunction
	{
		public string Name { get; }

		public EFunctionFlags FunctionFlags { get; }

		public IReadOnlyList<Operation> Script { get; }

		public string Assembly { get; }

		internal DisassembledFunction(string name, EFunctionFlags functionFlags, IReadOnlyList<Operation> script, string assembly)
		{
			Name = name;
			FunctionFlags = functionFlags;
			Script = script;
			Assembly = assembly;
		}

		public override string ToString()
		{
			return Assembly;
		}
	}
}
