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
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using System.Text;

namespace IcarusDataMiner
{
	/// <summary>
	/// Utlity for dumping the data associated with a blueprint asset to text files
	/// </summary>
	internal static class BlueprintDumper
	{
		private const string SectionDivider = "================================================================================";

		/// <summary>
		/// Dumps blueprint data to a directory
		/// </summary>
		/// <param name="assetPath">The path to the asset</param>
		/// <param name="provider">The provider from which to read the asset</param>
		/// <param name="outDir">The output directory for the dump</param>
		/// <param name="logger">For logging any issues</param>
		public static void DumpBlueprintData(string assetPath, IFileProvider provider, string outDir, Logger logger)
		{
			string assetName = Path.GetFileNameWithoutExtension(assetPath);

			outDir = Path.Combine(outDir, assetName);
			Directory.CreateDirectory(outDir);

			provider.ReadScriptData = true;

			GameFile file = provider.Files[assetPath];
			Package package = (Package)provider.LoadPackage(file);

			foreach (FObjectExport? export in package.ExportMap)
			{
				if (export is null) continue;

				UObject exportObject = export.ExportObject.Value;
				if (exportObject is UFunction function)
				{
					DumpBlueprintFunction(function, assetName, package, outDir, logger);
				}
				else if (exportObject is UBlueprintGeneratedClass genClass)
				{
					DumpBlueprintClass(genClass, outDir, logger);
				}
			}

			provider.ReadScriptData = false;
		}

		private static void DumpBlueprintClass(UBlueprintGeneratedClass clss, string outDir, Logger logger)
		{
			List<FFieldInfo> classProperties = new(clss.ChildProperties.Select(p => new FFieldInfo(p)));
			List<KeyValuePair<string, string?>> superOverrides = new();

			UObject? defaults = clss.ClassDefaultObject.ResolvedObject?.Object?.Value;
			if (defaults is not null)
			{
				Dictionary<string, FFieldInfo> classPropertyMap = classProperties.ToDictionary(p => p.Name, p => p);
				foreach (FPropertyTag prop in defaults.Properties)
				{
					string? valueStr = GetDefaultValueString(prop);
					if (classPropertyMap.TryGetValue(prop.Name.Text, out FFieldInfo? propInfo))
					{
						propInfo.DefaultValue = valueStr;
					}
					else
					{
						superOverrides.Add(new(prop.Name.Text, valueStr));
					}
				}
			}

			for (int i = classProperties.Count - 1; i >= 0; --i)
			{
				if (classProperties[i].Type.Equals("PointerToUberGraphFrame"))
				{
					classProperties.RemoveAt(i);
				}
			}

			string outPath = Path.Combine(outDir, $"Class_{clss.Name[..^2]}.txt");
			using (FileStream file = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(file))
			{
				writer.WriteLine($"Class: {clss.Name}");
				writer.WriteLine($"Parent: {clss.SuperStruct.Name}");
				writer.WriteLine($"Flags: {(EClassFlags)clss.ClassFlags}");
				writer.WriteLine($"Config: {clss.ClassConfigName.Text}");

				WriteFields(writer, "Properties", classProperties);

				WriteHeader(writer, "Parent property overrides");
				foreach (var pair in superOverrides)
				{
					writer.WriteLine($"{pair.Key} = {pair.Value}");
				}
			}
		}

		private static void DumpBlueprintFunction(UFunction function, string assetName, Package package, string outDir, Logger logger)
		{
			List<FFieldInfo> inParams = new();
			List<FFieldInfo> outParams = new();
			List<FFieldInfo> localVars = new();
			for (int i = 0; i < function.ChildProperties.Length; ++i)
			{
				FFieldInfo param = new(function.ChildProperties[i]);

				if ((param.PropertyFlags & EPropertyFlags.OutParm) != EPropertyFlags.None)
				{
					outParams.Add(param);
				}
				else if ((param.PropertyFlags & EPropertyFlags.Parm) != EPropertyFlags.None)
				{
					inParams.Add(param);
				}
				else
				{
					localVars.Add(param);
				}
			}

			DisassembledFunction script = UFunctionDisassembler.Process(package, function);

			string funcName = function.Name;
			string funcType = "Function";
			if ((script.FunctionFlags & EFunctionFlags.Delegate) != EFunctionFlags.None)
			{
				funcName = funcName[0..funcName.LastIndexOf("__")];
				funcType = "Delegate";
			}
			if ((script.FunctionFlags & EFunctionFlags.UbergraphFunction) != EFunctionFlags.None)
			{
				funcName = funcName.Replace($"_{assetName}", string.Empty);
				funcType = "Graph";
			}

			string outPath = Path.Combine(outDir, $"{funcType}_{funcName}.txt");

			using (FileStream file = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(file))
			{
				writer.WriteLine($"Function: {function.Name}");
				writer.WriteLine($"Flags: {script.FunctionFlags}");

				WriteFields(writer, "Inputs", inParams);
				WriteFields(writer, "Outputs", outParams);
				WriteFields(writer, "Locals", localVars);

				WriteHeader(writer, "Code");
				writer.WriteLine(script.Assembly);
			}
		}

		private static void WriteHeader(TextWriter writer, string header)
		{
			writer.WriteLine();
			writer.WriteLine(SectionDivider);
			writer.WriteLine(header);
			writer.WriteLine(SectionDivider);
		}

		private static void WriteFields(TextWriter writer, string header, IReadOnlyList<FFieldInfo> fields)
		{
			WriteHeader(writer, header);

			for (int i = 0; i < fields.Count; ++i)
			{
				WriteField(writer, fields[i]);
				if (i < fields.Count - 1)
				{
					writer.WriteLine();
				}
			}
		}

		private static void WriteField(TextWriter writer, FFieldInfo field)
		{
			writer.WriteLine($"Name: {field.Name}");
			writer.WriteLine($"Type: {field.Type}");
			if (field.DefaultValue is not null)
			{
				writer.WriteLine($"Default: {field.DefaultValue}");
			}
			if (field.PropertyFlags != EPropertyFlags.None)
			{
				writer.WriteLine($"Flags: {field.PropertyFlags}");
			}
		}

		private static string? GetDefaultValueString(FPropertyTag prop)
		{
			return GetDefaultValueString(prop.Tag);
		}

		private static string? GetDefaultValueString(FPropertyTagType? prop, string indent = "")
		{
			object? value = prop?.GenericValue;
			string nextIndent = indent + "  ";
			if (value is UScriptStruct usc)
			{
				if (usc.StructType is FStructFallback sfb)
				{
					if (sfb.Properties.Count == 0)
					{
						return "{ }";
					}

					StringBuilder builder = new($"{{{Environment.NewLine}");
					for (int i = 0; i < sfb.Properties.Count; ++i)
					{
						builder.Append($"{nextIndent}{GetDefaultValueString(sfb.Properties[i].Tag, nextIndent)}");
						if (i < sfb.Properties.Count - 1)
						{
							builder.Append(",");
						}
						builder.AppendLine();
					}
					builder.Append($"{indent}}}");
					return builder.ToString();
				}
				return $"{indent}{{ {usc.StructType} }}";
			}
			else if (value is UScriptArray || value is UScriptSet)
			{
				List<FPropertyTagType> properties = value is UScriptArray arr ? arr.Properties : ((UScriptSet)value).Properties;

				if (properties.Count == 0)
				{
					return "{ }";
				}

				StringBuilder builder = new($"{{{Environment.NewLine}");
				for (int i = 0; i < properties.Count; ++i)
				{
					builder.Append($"{nextIndent}{GetDefaultValueString(properties[i], nextIndent)}");
					if (i < properties.Count - 1)
					{
						builder.Append(",");
					}
					builder.AppendLine();
				}
				builder.Append($"{indent}}}");
				return builder.ToString();
			}
			else if (value is UScriptMap map)
			{
				if (map.Properties.Count == 0)
				{
					return "{ }";
				}

				StringBuilder builder = new($"{{{Environment.NewLine}");
				int i = 0;
				foreach (var pair in map.Properties)
				{
					builder.Append($"{nextIndent}{GetDefaultValueString(pair.Key, nextIndent)} = {GetDefaultValueString(pair.Value, nextIndent)}");
					if (i < map.Properties.Count - 1)
					{
						builder.Append(",");
					}
					builder.AppendLine();
					++i;
				}
				builder.Append($"{indent}}}");
				return builder.ToString();
			}
			return value?.ToString();
		}

		private class FFieldInfo
		{
			public string Name { get; }

			public string Type { get; }

			public EPropertyFlags Flags { get; }

			public EPropertyFlags PropertyFlags { get; }

			public string? DefaultValue { get; set; }

			public FFieldInfo(FField field)
			{
				Name = field.Name.Text;
				Flags = (EPropertyFlags)field.Flags;

				if (field is FProperty prop)
				{
					Type = GetPropertyType(prop);
					PropertyFlags = (EPropertyFlags)prop.PropertyFlags;
				}
				else
				{
					Type = GetUnknownFieldType(field);
					PropertyFlags = EPropertyFlags.None;
				}
			}

			private static string GetUnknownFieldType(FField field)
			{
				string typeName = field.GetType().Name;
				int suffixIndex = typeName.IndexOf("Property");
				if (suffixIndex < 0)
				{
					return typeName;
				}
				return typeName[1..suffixIndex];
			}

			private static string GetPropertyType(FProperty? property)
			{
				if (property is null) return "None";

				if (property is FArrayProperty array)
				{
					string itemType = GetPropertyType(array.Inner);
					return $"Array<{itemType}>";
				}
				else if (property is FByteProperty bt)
				{
					return bt.Enum.ResolvedObject?.Name.Text ?? "Byte";
				}
				else if (property is FDelegateProperty dlgt)
				{
					return $"{dlgt.SignatureFunction.Name} (Delegate)";
				}
				else if (property is FEnumProperty enm)
				{
					return enm.Enum.Name;
				}
				else if (property is FFieldPathProperty fieldPath)
				{
					return $"{fieldPath.PropertyClass.Text} field path";
				}
				else if (property is FInterfaceProperty intrfc)
				{
					return $"{intrfc.InterfaceClass.Name} interface";
				}
				else if (property is FMapProperty map)
				{
					string keyType = GetPropertyType(map.KeyProp);
					string valueType = GetPropertyType(map.ValueProp);
					return $"Map<{keyType}, {valueType}>";
				}
				else if (property is FMulticastDelegateProperty mdlgt)
				{
					return $"{mdlgt.SignatureFunction.Name} (Multicast Delegate)";
				}
				else if (property is FMulticastInlineDelegateProperty midlgt)
				{
					return $"{midlgt.SignatureFunction.Name} (Multicast Inline Delegate)";
				}
				else if (property is FObjectProperty objct)
				{
					if (property is FClassProperty clss)
					{
						return $"{clss.MetaClass.Name} Class";
					}
					else if (property is FSoftClassProperty softClass)
					{
						return $"{softClass.MetaClass.Name} Class (soft)";
					}
					else
					{
						return objct.PropertyClass.Name;
					}
				}
				else if (property is FSetProperty set)
				{
					string itemType = GetPropertyType(set.ElementProp);
					return $"Set<{itemType}>";
				}
				else if (property is FStructProperty strct)
				{
					return strct.Struct.ResolvedObject?.Name.Text ?? "Struct";
				}

				return GetUnknownFieldType(property);
			}
		}
	}

	// The enums below were taken from Unreal Engine source code

	[Flags]
	internal enum EPropertyFlags : ulong
	{
		None = 0,

		Edit = 0x0000000000000001,  ///< Property is user-settable in the editor.
		ConstParm = 0x0000000000000002, ///< This is a constant function parameter
		BlueprintVisible = 0x0000000000000004,  ///< This property can be read by blueprint code
		ExportObject = 0x0000000000000008,  ///< Object can be exported with actor.
		BlueprintReadOnly = 0x0000000000000010, ///< This property cannot be modified by blueprint code
		Net = 0x0000000000000020,   ///< Property is relevant to network replication.
		EditFixedSize = 0x0000000000000040, ///< Indicates that elements of an array can be modified, but its size cannot be changed.
		Parm = 0x0000000000000080,  ///< Function/When call parameter.
		OutParm = 0x0000000000000100,   ///< Value is copied out after function call.
		ZeroConstructor = 0x0000000000000200,   ///< memset is fine for construction
		ReturnParm = 0x0000000000000400,    ///< Return value.
		DisableEditOnTemplate = 0x0000000000000800, ///< Disable editing of this property on an archetype/sub-blueprint
		//	    						= 0x0000000000001000,	///< 
		Transient = 0x0000000000002000, ///< Property is transient: shouldn't be saved or loaded, except for Blueprint CDOs.
		Config = 0x0000000000004000,    ///< Property should be loaded/saved as permanent profile.
		//								= 0x0000000000008000,	///< 
		DisableEditOnInstance = 0x0000000000010000, ///< Disable editing on an instance of this class
		EditConst = 0x0000000000020000, ///< Property is uneditable in the editor.
		GlobalConfig = 0x0000000000040000,  ///< Load config from base class, not subclass.
		InstancedReference = 0x0000000000080000,    ///< Property is a component references.
		//								= 0x0000000000100000,	///<
		DuplicateTransient = 0x0000000000200000,    ///< Property should always be reset to the default value during any type of duplication (copy/paste, binary duplication, etc.)
		//								= 0x0000000000400000,	///< 
		//    							= 0x0000000000800000,	///< 
		SaveGame = 0x0000000001000000,  ///< Property should be serialized for save games, this is only checked for game-specific archives with ArIsSaveGame
		NoClear = 0x0000000002000000,   ///< Hide clear (and browse) button.
		//  							= 0x0000000004000000,	///<
		ReferenceParm = 0x0000000008000000, ///< Value is passed by reference; CPF_OutParam and CPF_Param should also be set.
		BlueprintAssignable = 0x0000000010000000,   ///< MC Delegates only.  Property should be exposed for assigning in blueprint code
		Deprecated = 0x0000000020000000,    ///< Property is deprecated.  Read it from an archive, but don't save it.
		IsPlainOldData = 0x0000000040000000,    ///< If this is set, then the property can be memcopied instead of CopyCompleteValue / CopySingleValue
		RepSkip = 0x0000000080000000,   ///< Not replicated. For non replicated properties in replicated structs 
		RepNotify = 0x0000000100000000, ///< Notify actors when a property is replicated
		Interp = 0x0000000200000000,    ///< interpolatable property for use with matinee
		NonTransactional = 0x0000000400000000,  ///< Property isn't transacted
		EditorOnly = 0x0000000800000000,    ///< Property should only be loaded in the editor
		NoDestructor = 0x0000001000000000,  ///< No destructor
		//								= 0x0000002000000000,	///<
		AutoWeak = 0x0000004000000000,  ///< Only used for weak pointers, means the export type is autoweak
		ContainsInstancedReference = 0x0000008000000000,    ///< Property contains component references.
		AssetRegistrySearchable = 0x0000010000000000,   ///< asset instances will add properties with this flag to the asset registry automatically
		SimpleDisplay = 0x0000020000000000, ///< The property is visible by default in the editor details view
		AdvancedDisplay = 0x0000040000000000,   ///< The property is advanced and not visible by default in the editor details view
		Protected = 0x0000080000000000, ///< property is protected from the perspective of script
		BlueprintCallable = 0x0000100000000000, ///< MC Delegates only.  Property should be exposed for calling in blueprint code
		BlueprintAuthorityOnly = 0x0000200000000000,    ///< MC Delegates only.  This delegate accepts (only in blueprint) only events with BlueprintAuthorityOnly.
		TextExportTransient = 0x0000400000000000,   ///< Property shouldn't be exported to text format (e.g. copy/paste)
		NonPIEDuplicateTransient = 0x0000800000000000,  ///< Property should only be copied in PIE
		ExposeOnSpawn = 0x0001000000000000, ///< Property is exposed on spawn
		PersistentInstance = 0x0002000000000000,    ///< A object referenced by the property is duplicated like a component. (Each actor should have an own instance.)
		UObjectWrapper = 0x0004000000000000,    ///< Property was parsed as a wrapper class like TSubclassOf<T>, FScriptInterface etc., rather than a USomething*
		HasGetValueTypeHash = 0x0008000000000000,   ///< This property can generate a meaningful hash value.
		NativeAccessSpecifierPublic = 0x0010000000000000,   ///< Public native access specifier
		NativeAccessSpecifierProtected = 0x0020000000000000,    ///< Protected native access specifier
		NativeAccessSpecifierPrivate = 0x0040000000000000,  ///< Private native access specifier
		SkipSerialization = 0x0080000000000000, ///< Property shouldn't be serialized, can still be exported to text
	};

	[Flags]
	internal enum EClassFlags : uint
	{
		/** No Flags */
		None = 0x00000000u,
		/** Class is abstract and can't be instantiated directly. */
		Abstract = 0x00000001u,
		/** Save object configuration only to Default INIs, never to local INIs. Must be combined with Config */
		DefaultConfig = 0x00000002u,
		/** Load object configuration at construction time. */
		Config = 0x00000004u,
		/** This object type can't be saved; null it out at save time. */
		Transient = 0x00000008u,
		/** Successfully parsed. */
		Parsed = 0x00000010u,
		/** */
		MatchedSerializers = 0x00000020u,
		/** Indicates that the config settings for this class will be saved to Project/User*.ini (similar to GlobalUserConfig) */
		ProjectUserConfig = 0x00000040u,
		/** Class is a native class - native interfaces will have Native set, but not RF_MarkAsNative */
		Native = 0x00000080u,
		/** Don't export to C++ header. */
		NoExport = 0x00000100u,
		/** Do not allow users to create in the editor. */
		NotPlaceable = 0x00000200u,
		/** Handle object configuration on a per-object basis, rather than per-class. */
		PerObjectConfig = 0x00000400u,

		/** Whether SetUpRuntimeReplicationData still needs to be called for this class */
		ReplicationDataIsSetUp = 0x00000800u,

		/** Class can be constructed from editinline New button. */
		EditInlineNew = 0x00001000u,
		/** Display properties in the editor without using categories. */
		CollapseCategories = 0x00002000u,
		/** Class is an interface **/
		Interface = 0x00004000u,
		/**  Do not export a constructor for this class, assuming it is in the cpptext **/
		CustomConstructor = 0x00008000u,
		/** all properties and functions in this class are const and should be exported as const */
		Const = 0x00010000u,

		/** Class flag indicating the class is having its layout changed, and therefore is not ready for a CDO to be created */
		LayoutChanging = 0x00020000u,

		/** Indicates that the class was created from blueprint source material */
		CompiledFromBlueprint = 0x00040000u,

		/** Indicates that only the bare minimum bits of this class should be DLL exported/imported */
		MinimalAPI = 0x00080000u,

		/** Indicates this class must be DLL exported/imported (along with all of it's members) */
		RequiredAPI = 0x00100000u,

		/** Indicates that references to this class default to instanced. Used to be subclasses of UComponent, but now can be any UObject */
		DefaultToInstanced = 0x00200000u,

		/** Indicates that the parent token stream has been merged with ours. */
		TokenStreamAssembled = 0x00400000u,
		/** Class has component properties. */
		HasInstancedReference = 0x00800000u,
		/** Don't show this class in the editor class browser or edit inline new menus. */
		Hidden = 0x01000000u,
		/** Don't save objects of this class when serializing */
		Deprecated = 0x02000000u,
		/** Class not shown in editor drop down for class selection */
		HideDropDown = 0x04000000u,
		/** Class settings are saved to <AppData>/..../Blah.ini (as opposed to DefaultConfig) */
		GlobalUserConfig = 0x08000000u,
		/** Class was declared directly in C++ and has no boilerplate generated by UnrealHeaderTool */
		Intrinsic = 0x10000000u,
		/** Class has already been constructed (maybe in a previous DLL version before hot-reload). */
		Constructed = 0x20000000u,
		/** Indicates that object configuration will not check against ini base/defaults when serialized */
		ConfigDoNotCheckDefaults = 0x40000000u,
		/** Class has been consigned to oblivion as part of a blueprint recompile, and a newer version currently exists. */
		NewerVersionExists = 0x80000000u,
	};
}
