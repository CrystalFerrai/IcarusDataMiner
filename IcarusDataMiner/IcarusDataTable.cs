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
				FRowEnum instance = new FRowEnum();

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
									IDataTableRow targetRow = (IDataTableRow)(Activator.CreateInstance(rowType) ?? throw new MissingMethodException($"Type {rowType} has no default constructor"));
									foreach (FieldInfo fi in rowFields.Values)
									{
										fi.SetValue(targetRow, fi.GetValue(rowDefaults));
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
											field.SetValue(targetRow, prop.Value.ToObject(field.FieldType));
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
	}
}
