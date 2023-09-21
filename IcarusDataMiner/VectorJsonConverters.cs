// Copyright 2022 Crystal Ferrai
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

using CUE4Parse.UE4.Objects.Core.Math;
using Newtonsoft.Json;

namespace IcarusDataMiner
{
	/// <summary>
	/// When using JsonConvert.DeserializeObject on any object which may contain a FVector2D, this converter is needed because
	/// FVector2D members cannot be set after contruction.
	/// </summary>
	internal class FVector2DJsonConverter : JsonConverter<FVector2D>
	{
		public override bool CanRead => true;

		public override bool CanWrite => false;

		public static FVector2D ReadVector(JsonReader reader)
		{
			if (reader.TokenType != JsonToken.StartObject) throw new InvalidOperationException("Expected reader to be positioned on an object");

			float x = 0.0f, y = 0.0f;

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType != JsonToken.PropertyName) continue;

				string propertyName = (string)reader.Value!;
				reader.Read();

				float current = 0.0f;
				switch (reader.TokenType)
				{
					case JsonToken.Float:
						current = (float)(double)reader.Value!;
						break;
					case JsonToken.Integer:
						current = (float)(long)reader.Value!;
						break;
				}

				if (string.Equals(propertyName, "x", StringComparison.InvariantCultureIgnoreCase))
				{
					x = current;
				}
				else if (string.Equals(propertyName, "y", StringComparison.InvariantCultureIgnoreCase))
				{
					y = current;
				}
			}

			return new FVector2D(x, y);
		}

		public override FVector2D ReadJson(JsonReader reader, Type objectType, FVector2D existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			return ReadVector(reader);
		}

		public override void WriteJson(JsonWriter writer, FVector2D value, JsonSerializer serializer)
		{
			// This could be implemented, but there is currently no use case for it.
			throw new NotImplementedException();
		}
	}

	/// <summary>
	/// When using JsonConvert.DeserializeObject on any object which may contain a FVector2D, this converter is needed because
	/// FVector2D members cannot be set after contruction.
	/// </summary>
	internal class FVectorJsonConverter : JsonConverter<FVector>
	{
		public override bool CanRead => true;

		public override bool CanWrite => false;

		public static FVector ReadVector(JsonReader reader)
		{
			if (reader.TokenType != JsonToken.StartObject) throw new InvalidOperationException("Expected reader to be positioned on an object");

			float x = 0.0f, y = 0.0f, z = 0.0f;

			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType != JsonToken.PropertyName) continue;

				string propertyName = (string)reader.Value!;
				reader.Read();

				float current = 0.0f;
				switch (reader.TokenType)
				{
					case JsonToken.Float:
						current = (float)(double)reader.Value!;
						break;
					case JsonToken.Integer:
						current = (float)(long)reader.Value!;
						break;
				}

				if (string.Equals(propertyName, "x", StringComparison.InvariantCultureIgnoreCase))
				{
					x = current;
				}
				else if (string.Equals(propertyName, "y", StringComparison.InvariantCultureIgnoreCase))
				{
					y = current;
				}
				else if (string.Equals(propertyName, "z", StringComparison.InvariantCultureIgnoreCase))
				{
					z = current;
				}
			}

			return new FVector(x, y, z);
		}

		public override FVector ReadJson(JsonReader reader, Type objectType, FVector existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			return ReadVector(reader);
		}

		public override void WriteJson(JsonWriter writer, FVector value, JsonSerializer serializer)
		{
			// This could be implemented, but there is currently no use case for it.
			throw new NotImplementedException();
		}
	}
}
