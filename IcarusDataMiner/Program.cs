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

namespace IcarusDataMiner
{
	internal class Program
	{
#nullable disable warnings
		private static Config sConfig;
		private static Logger sLogger;
#nullable restore warnings

		private const string UsageText =
			"Usage: IcarusDataMiner [content dir] [output dir] [[miners]]\n" +
			"\n" +
			"  content dir   Path the the game's Content directory (Icarus/Content)\n" +
			"\n" +
			"  output dir    Path to directory where mined data will be output\n" +
			"\n" +
			"  miners        (Optional) Comma separated list of miners to run. If not\n" +
			"                specified, all default miners will run. Specify 'all' to force\n" +
			"                all miners to run.";

		/// <summary>
		/// Program entry point
		/// </summary>
		private static int Main(string[] args)
		{
			sLogger = new ConsoleLogger();

			if (args.Length < 2)
			{
				sLogger.Log(LogLevel.Important, UsageText);
				return 0;
			}

			int argIndex = 0;

			if (!LoadConfig(args, ref argIndex)) return 1;

			bool success;
			using (MineRunner runner = new MineRunner(sConfig, sLogger))
			{
				if (!runner.Initialize(getMiners(args, ref argIndex))) return 1;
				success = runner.Run();
			}

			sLogger.Log(LogLevel.Important, "Done.");

			if (!success)
			{
				sLogger.Log(LogLevel.Warning, "\nOne or more miners failed. See above for details.");
			}

			// Pause if debugger attached
			if (System.Diagnostics.Debugger.IsAttached) Console.ReadKey(true);

			return 0;
		}

		private static bool LoadConfig(IReadOnlyList<string> args, ref int argIndex)
		{
			string gameContentDir = Path.GetFullPath(args[argIndex]);
			if (!Directory.Exists(gameContentDir))
			{
				sLogger.Log(LogLevel.Fatal, $"Cannot access game content directory {args[argIndex]}");
				return false;
			}
			++argIndex;

			string outDir = Path.GetFullPath(args[argIndex]);
			try
			{
				Directory.CreateDirectory(outDir);
			}
			catch (Exception ex)
			{
				sLogger.Log(LogLevel.Fatal, $"Cannot access/create output directoy {args[argIndex]}. [{ex.GetType().FullName}] {ex.Message}");
				return false;
			}
			++argIndex;

			sConfig = new Config()
			{
				GameContentDirectory = gameContentDir,
				OutputDirectory = outDir
			};

			return true;
		}

		private static IEnumerable<string>? getMiners(IReadOnlyList<string> args, ref int argIndex)
		{
			if (argIndex >= args.Count) return null;

			return args[argIndex++].Split(',').Select(m => m.Trim());
		}
	}
}