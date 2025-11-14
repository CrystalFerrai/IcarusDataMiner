// Copyright 2023 Crystal Ferrai
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

using CUE4Parse.UE4.Objects.Core.i18N;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace IcarusDataMiner
{
	// Various types supporting generic deserialization of Icarus's json data tables.
	// This file was taken from a work-in-progress version of IcarusSaveLib.

#pragma warning disable CS0649 // Field never assigned to

	/// <summary>
	/// Base class for data tables
	/// </summary>
	[JsonConverter(typeof(IcarusDataTableConverter))]
	internal abstract class IcarusDataTable
	{
		public string? Name;
		public string? RowStruct;
		public bool GenerateEnum;
		public JArray? Columns;

		public override string ToString()
		{
			return $"{Name ?? "Unnamed"} ({RowStruct ?? "Unknown"})";
		}
	}

	/// <summary>
	/// Represents a deserialized data table with a specific row type
	/// </summary>
	/// <typeparam name="T">The table's row type</typeparam>
	internal class IcarusDataTable<T> : IcarusDataTable, IReadOnlyList<T>, IReadOnlyDictionary<string, T> where T : IDataTableRow
	{
		private List<T> mRows;
		private Dictionary<string, T> mRowMap;
		private Dictionary<string, int> mIndexMap;

		public T? Defaults;

		public int Count => mRows.Count;

		public IEnumerable<string> Keys => mRowMap.Keys;

		public IEnumerable<T> Values => mRowMap.Values;

		public T this[int index]
		{
			get => mRows[index];
		}

		public T this[string rowName]
		{
			get => mRowMap[rowName];
		}

		public IcarusDataTable()
		{
			mRows = new();
			mRowMap = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
			mIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		}

		public static IcarusDataTable<T> DeserializeTable(string name, ReadOnlySpan<byte> data)
		{
			string json = Encoding.UTF8.GetString(data);
			return DeserializeTable(name, json);
		}

		public static IcarusDataTable<T> DeserializeTable(string name, string json)
		{
			JsonSerializerSettings settings = new JsonSerializerSettings { Context = new StreamingContext(StreamingContextStates.Other, name) };
			IcarusDataTable<T> table = JsonConvert.DeserializeObject<IcarusDataTable<T>>(json, settings) ?? throw new FormatException($"Failed to load data table {name}");

			return table;
		}

		public int IndexOf(string key)
		{
			if (mIndexMap.TryGetValue(key, out int index))
			{
				return index;
			}
			return -1;
		}

		public int IndexOf(T item)
		{
			return IndexOf(item.Name);
		}

		public bool ContainsKey(string key)
		{
			return mRowMap.ContainsKey(key);
		}

		public bool TryGetValue(string key, [MaybeNullWhen(false)] out T value)
		{
			return mRowMap.TryGetValue(key, out value);
		}

		public bool TryGetValue(int index, [MaybeNullWhen(false)] out T value)
		{
			if (index < 0 || index >= mRows.Count)
			{
				value = default;
				return false;
			}

			value = mRows[index];
			return true;
		}

		public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
		{
			return mRowMap.GetEnumerator();
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator()
		{
			return mRows.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return mRows.GetEnumerator();
		}

		public override string ToString()
		{
			return $"{base.ToString()} - {mRows.Count} Rows";
		}
	}

	[TypeConverter(typeof(FRowHandleTypeConverter))]
	internal struct FRowHandle
	{
		public string RowName;
		public string DataTableName;

		[JsonIgnore]
		public static FRowHandle Invalid;

		static FRowHandle()
		{
			Invalid = new()
			{
				RowName = "Invalid",
				DataTableName = "Invalid"
			};
		}

		public override int GetHashCode()
		{
			int hash = 17;
			hash = hash * 23 + RowName.GetHashCode();
			hash = hash * 23 + DataTableName.GetHashCode();
			return hash;
		}

		public override bool Equals([NotNullWhen(true)] object? obj)
		{
			return obj is FRowHandle other && RowName.Equals(other.RowName) && DataTableName.Equals(other.DataTableName);
		}

		public static bool operator ==(FRowHandle a, FRowHandle b)
		{
			return a.Equals(b);
		}

		public static bool operator !=(FRowHandle a, FRowHandle b)
		{
			return !a.Equals(b);
		}

		public override string ToString()
		{
			return RowName ?? "None";
		}
	}

	[TypeConverter(typeof(FRowEnumTypeConverter))]
	internal struct FRowEnum
	{
		public string Value;

		public override int GetHashCode()
		{
			return Value.GetHashCode();
		}

		public override bool Equals([NotNullWhen(true)] object? obj)
		{
			return obj is FRowEnum other && Value.Equals(other.Value);
		}

		public static bool operator ==(FRowEnum a, FRowEnum b)
		{
			return a.Equals(b);
		}

		public static bool operator !=(FRowEnum a, FRowEnum b)
		{
			return !a.Equals(b);
		}

		public override string ToString()
		{
			return Value;
		}
	}

	internal interface IDataTableRow
	{
		[JsonIgnore]
		public string Name { get; set; }

		[JsonIgnore]
		public JObject? Metadata { get; set; }
	}

	internal static class IDataTableRowExtensions
	{
		public static bool IsDeprecated(this IDataTableRow row)
		{
			if (row.Metadata != null && row.Metadata.TryGetValue("bIsDeprecated", out JToken? token))
			{
				return token.Type == JTokenType.Boolean && (bool)token;
			}
			return false;
		}
	}

#pragma warning restore CS0649

	internal class FRowHandleTypeConverter : TypeConverter
	{
		public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
		{
			return sourceType == typeof(string);
		}

		public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
		{
			return false;
		}

		public override bool GetPropertiesSupported(ITypeDescriptorContext? context)
		{
			return false;
		}

		public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context)
		{
			return false;
		}

		public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
		{
			if (value is string strValue)
			{
				FRowHandle instance = new FRowHandle();

				// (RowName=\"Foo\",DataTableName=\"D_Bar\")
				strValue = strValue.Trim('(', ')');
				string[] props = strValue.Split(',');
				foreach (string prop in props)
				{
					string[] parts = prop.Split('=');
					if (parts.Length != 2) continue;

					switch (parts[0])
					{
						case nameof(FRowHandle.RowName):
							instance.RowName = parts[1];
							break;
						case nameof(FRowHandle.DataTableName):
							instance.DataTableName = parts[1];
							break;
					}
				}

				return instance;
			}
			return null;
		}
	}

	internal class FRowEnumTypeConverter : TypeConverter
	{
		public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
		{
			return sourceType == typeof(string);
		}

		public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
		{
			return false;
		}

		public override bool GetPropertiesSupported(ITypeDescriptorContext? context)
		{
			return false;
		}

		public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context)
		{
			return false;
		}

		public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
		{
			if (value is string strValue)
			{
				FRowEnum instance = new();

				// (Value=\"Foo\")
				strValue = strValue.Trim('(', ')');
				string[] parts = strValue.Split('=');

				if (parts.Length == 2 && parts[0] == nameof(FRowEnum.Value))
				{
					instance.Value = parts[1].Trim('\"');
				}

				return instance;
			}
			return null;
		}
	}

	/// <summary>
	/// IcarusDataTable deserializer that applies proper default values for row properties that are not specified
	/// </summary>
	internal class IcarusDataTableConverter : JsonConverter
	{
		public override bool CanWrite => false;

		public override bool CanConvert(Type typeToConvert)
		{
			return typeof(IcarusDataTable<>).IsAssignableFrom(typeToConvert);
		}

		public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
		{
			if (!typeof(IcarusDataTable).IsAssignableFrom(objectType))
			{
				return null;
			}

			IcarusDataTable table = Activator.CreateInstance(objectType) as IcarusDataTable ?? throw new MissingMethodException($"Type {objectType} has no default constructor");
			if (serializer.Context.Context is string tableName)
			{
				table.Name = tableName;
			}

			Type rowType = objectType.GetGenericArguments()[0];

			const string DefaultsName = nameof(IcarusDataTable<IDataTableRow>.Defaults);
			const string RowsName = "mRows";
			const string RowMapName = "mRowMap";
			const string IndexMapName = "mIndexMap";

			FieldInfo defaultsField = objectType.GetField(DefaultsName) ?? throw new MissingFieldException($"Type {objectType} is missing field {DefaultsName}");
			FieldInfo rowsField = objectType.GetField(RowsName, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException($"Type {objectType} is missing field {RowsName}");
			FieldInfo rowMapField = objectType.GetField(RowMapName, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException($"Type {objectType} is missing field {RowMapName}");
			FieldInfo indexMapField = objectType.GetField(IndexMapName, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException($"Type {objectType} is missing field {IndexMapName}");

			int startDepth = reader.Depth;
			while (reader.Read() && reader.Depth > startDepth)
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					string propertyName = (string)reader.Value!;

					switch (propertyName)
					{
						case nameof(IcarusDataTable.RowStruct):
							table.RowStruct = reader.ReadAsString();
							break;
						case nameof(IcarusDataTable.GenerateEnum):
							bool? value = reader.ReadAsBoolean();
							table.GenerateEnum = value.HasValue ? value.Value : false;
							break;
						case nameof(IcarusDataTable.Columns):
							reader.Read();
							table.Columns = serializer.Deserialize<JArray>(reader);
							break;
						case DefaultsName:
							reader.Read();
							defaultsField.SetValue(table, serializer.Deserialize(reader, rowType));
							break;
						case "Rows":
							{
								reader.Read();

								Type rowListType = typeof(List<>).MakeGenericType(rowType);
								Type rowMapType = typeof(Dictionary<,>).MakeGenericType(typeof(string), rowType);
								Type indexMapType = typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(int));
								JArray? rows = serializer.Deserialize(reader) as JArray;
								if (rows == null) break;

								object? rowDefaults = defaultsField.GetValue(table);
								if (rowDefaults == null) break;

								Dictionary<string, FieldInfo> rowFields = rowType.GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

								IList outRows = (IList)Activator.CreateInstance(rowListType)!;
								IDictionary outRowMap = (IDictionary)Activator.CreateInstance(rowMapType, StringComparer.OrdinalIgnoreCase)!;
								IDictionary outIndexMap = (IDictionary)Activator.CreateInstance(indexMapType, StringComparer.OrdinalIgnoreCase)!;
								foreach (JObject row in rows)
								{
									object rowDefaultsCopy = Copy(rowDefaults)!;

									IDataTableRow targetRow = (IDataTableRow)(Activator.CreateInstance(rowType) ?? throw new MissingMethodException($"Type {rowType} has no default constructor"));
									foreach (FieldInfo fi in rowFields.Values)
									{
										fi.SetValue(targetRow, fi.GetValue(rowDefaultsCopy));
									}
									string? rowName = null;
									foreach (JProperty prop in row.Properties())
									{
										if (prop.Name == "Name")
										{
											rowName = prop.Value.ToString();
											targetRow.Name = rowName;
										}
										else if (prop.Name == "Metadata")
										{
											targetRow.Metadata = (JObject)prop.Value;
										}
										else if (rowFields.TryGetValue(prop.Name, out FieldInfo? field))
										{
											object? targetField = field.GetValue(targetRow);
											if (targetField is null || prop.Value.Type != JTokenType.Object)
											{
												field.SetValue(targetRow, prop.Value.ToObject(field.FieldType));
											}
											else
											{
												JsonConvert.PopulateObject(prop.Value.ToString(), targetField);
												field.SetValue(targetRow, targetField);
											}
										}
									}

									if (rowName == null) throw new InvalidDataException("Data table row has no name.");

									outIndexMap.Add(rowName, outRows.Count);

									outRows.Add(targetRow);
									outRowMap.Add(rowName, targetRow);
								}

								rowsField.SetValue(table, outRows);
								rowMapField.SetValue(table, outRowMap);
								indexMapField.SetValue(table, outIndexMap);
								break;
							}
					}
				}
			}

			return table;
		}

		public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
		{
			throw new NotSupportedException("This converter does not support writing");
		}

		private static MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(nameof(MemberwiseClone), BindingFlags.NonPublic | BindingFlags.Instance)!;

		private object? Copy(object? value)
		{
			if (value is null) return null;

			if (value is ICloneable cloneable)
			{
				return cloneable.Clone();
			}

			Type valueType = value.GetType();
			if (valueType.IsPrimitive)
			{
				return value;
			}

			{
				ConstructorInfo? copyConstructor = valueType.GetConstructor(new Type[] { valueType });
				if (copyConstructor is not null)
				{
					return Activator.CreateInstance(valueType, value);
				}
			}

			if (value is IEnumerable)
			{
				Type enumerableType;
				if (valueType.IsGenericType)
				{
					enumerableType = typeof(IEnumerable<>).MakeGenericType(valueType.GetGenericArguments());
				}
				else
				{
					enumerableType = typeof(IEnumerable);
				}

				ConstructorInfo? enumerableConstructor = valueType.GetConstructor(new Type[] { enumerableType });
				if (enumerableConstructor is not null)
				{
					return Activator.CreateInstance(valueType, value);
				}
			}

			if (valueType.IsValueType)
			{
				object newValue = MemberwiseCloneMethod.Invoke(value, null)!;

				List<FieldInfo> valueFields = valueType.GetFields(BindingFlags.Instance | BindingFlags.Public).ToList();
				foreach (FieldInfo field in valueFields)
				{
					field.SetValue(newValue, Copy(field.GetValue(value)));
				}

				return newValue;
			}

			throw new NotSupportedException($"Could not copy object of type {valueType.FullName}. The type may need to implement {nameof(ICloneable)} to be used within an {nameof(IcarusDataTable)} row.");
		}
	}

	[JsonConverter(typeof(ObjectPointerConverter))]
	[TypeConverter(typeof(ObjectPointerTypeConverter))]
	internal class ObjectPointer : IEquatable<ObjectPointer>, IComparable<ObjectPointer>, ICloneable
	{
		private readonly string? mRawText;

		private readonly string? mTypeName;
		private readonly string? mPath;

		public string? TypeName => mTypeName;

		public string? Path => mPath;

		public static ObjectPointer Null { get; }

		static ObjectPointer()
		{
			Null = new ObjectPointer(null);
		}

		public ObjectPointer()
		{
		}

		public ObjectPointer(string? rawText)
		{
			mRawText = rawText;
			if (mRawText == null) return;

			string[] parts = mRawText.Split('\'');
			switch (parts.Length)
			{
				case 1:
					// Soft object pointer
					mTypeName = null;
					mPath = parts[0];
					break;
				case 3:
					// Hard object pointer
					mTypeName = parts[0];
					mPath = parts[1];
					break;
					// Any other number of parts we don't know how to parse, so only raw text will be stored.
					// The most likely case is that the raw text is an empty string, meaning this is a pointer
					// to null. We want serialization to work regardless what is going on.
			}
		}

		public override int GetHashCode()
		{
			return mRawText?.GetHashCode() ?? 0;
		}

		public bool Equals(ObjectPointer? other)
		{
			return other is not null && (mRawText?.Equals(other.mRawText) ?? other.mRawText is null);
		}

		public override bool Equals(object? obj)
		{
			return obj is ObjectPointer other && Equals(other);
		}

		public int CompareTo(ObjectPointer? other)
		{
			if (other is null) return 1;
			if (mRawText is null) return other.mRawText is null ? 0 : -1;
			return mRawText.CompareTo(other.mRawText);
		}

		public static implicit operator string?(ObjectPointer instance)
		{
			return instance.mRawText;
		}

		public override string? ToString()
		{
			return mRawText;
		}

		public string? GetAssetPath(bool appendExtension = false)
		{
			if (mPath == null) return null;

			string assetPath = mPath;

			if (assetPath.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase))
			{
				return assetPath;
			}

			if (assetPath.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
			{
				assetPath = $"Icarus/Content/{assetPath.Substring(6)}";
			}

			int dotIndex = assetPath.LastIndexOf('.');
			if (dotIndex >= 0)
			{
				assetPath = assetPath.Substring(0, dotIndex);
			}

			if (assetPath.Equals("None", StringComparison.OrdinalIgnoreCase))
			{
				return assetPath;
			}

			if (appendExtension)
			{
				return $"{assetPath}.uasset";
			}

			return assetPath;
		}

		public object Clone()
		{
			return new ObjectPointer(mRawText);
		}

		private class ObjectPointerConverter : JsonConverter
		{
			public override bool CanConvert(Type objectType)
			{
				return typeof(FText).IsAssignableFrom(objectType);
			}

			public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
			{
				if (!typeof(ObjectPointer).IsAssignableFrom(objectType)) return null;
				if (reader.TokenType != JsonToken.String) return null;

				return new ObjectPointer((string?)reader.Value);
			}

			public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
			{
				ObjectPointer? ptr = value as ObjectPointer;
				if (ptr == null) return;

				writer.WriteValue(ptr.mRawText);
			}
		}

		private class ObjectPointerTypeConverter : TypeConverter
		{
			public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
			{
				return sourceType == typeof(string);
			}

			public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
			{
				return false;
			}

			public override bool GetPropertiesSupported(ITypeDescriptorContext? context)
			{
				return false;
			}

			public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context)
			{
				return false;
			}

			public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
			{
				if (value is string strValue)
				{
					return new ObjectPointer(strValue);
				}
				return null;
			}
		}
	}
}
