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

using CUE4Parse.UE4.Objects.Core.Math;
using SkiaSharp;

namespace IcarusDataMiner
{
	/// <summary>
	/// Helper functions for working with colors
	/// </summary>
	internal static class ColorUtil
	{
		private const float FloatToInt = 255.0f;
		private const float IntToFloat = 1.0f / FloatToInt;

		public static SKColor ToSKColor(FColor color)
		{
			return new SKColor(color.R, color.G, color.B, color.A);
		}

		public static SKColor ToSKColor(FColor color, byte overrideAlpha)
		{
			return new SKColor(color.R, color.G, color.B, overrideAlpha);
		}

		public static SKColor ToSKColor(FLinearColor linearColor)
		{
			return new SKColor(LinearToSrgb(linearColor.R), LinearToSrgb(linearColor.G), LinearToSrgb(linearColor.B), (byte)(linearColor.A * FloatToInt));
		}

		public static SKColor ToSKColor(FLinearColor linearColor, byte overrideAlpha)
		{
			return new SKColor(LinearToSrgb(linearColor.R), LinearToSrgb(linearColor.G), LinearToSrgb(linearColor.B), overrideAlpha);
		}

		public static SKColor LinearToSrgb(SKColor linearColor)
		{
			return new SKColor(LinearToSrgb(linearColor.Red * IntToFloat), LinearToSrgb(linearColor.Green * IntToFloat), LinearToSrgb(linearColor.Blue * IntToFloat), linearColor.Alpha);
		}

		public static SKColor LinearToSrgb(SKColor linearColor, byte overrideAlpha)
		{
			return new SKColor(LinearToSrgb(linearColor.Red * IntToFloat), LinearToSrgb(linearColor.Green * IntToFloat), LinearToSrgb(linearColor.Blue * IntToFloat), overrideAlpha);
		}

		public static byte LinearToSrgb(float linear)
		{
			if (linear <= 0.0f) return 0;
			if (linear <= 0.00313066844250063f) return (byte)(linear * 12.92f * FloatToInt);
			if (linear < 1) return (byte)((1.055f * Math.Pow(linear, 1.0f / 2.4f) - 0.055f) * FloatToInt);
			return 255;
		}
	}
}
