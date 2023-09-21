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

using CUE4Parse.UE4.Assets;
using System.Diagnostics;
using System.Reflection;

namespace IcarusDataMiner
{
	/// <summary>
	/// Locates, instantiates and runs data miners
	/// </summary>
	internal class MineRunner : IDisposable
	{
		private bool mIsDisposed;

		private readonly Config mConfig;

		private readonly Logger mLogger;

		private readonly ProviderManager mProviderManager;

		private readonly List<IDataMiner> mMiners;

		static MineRunner()
		{
			ObjectTypeRegistry.RegisterClass("GameplayTexture", typeof(UGameplayTexture));
		}

		public MineRunner(Config config, Logger logger)
		{
			mConfig = config;
			mLogger = logger;
			mProviderManager = new ProviderManager(config);
			mMiners = new List<IDataMiner>();
		}

		public bool Initialize(IEnumerable<string>? minersToInclude = null)
		{
			if (!mProviderManager.Initialize(mLogger))
			{
				return false;
			}
			CreateMiners(minersToInclude);
			return true;
		}

		#region Dispose
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~MineRunner()
		{
			Dispose(false);
		}

		private void Dispose(bool disposing)
		{
			if (!mIsDisposed)
			{
				if (disposing)
				{
					// Dispose managed objects

					foreach (IDataMiner miner in mMiners)
					{
						if (miner is IDisposable disposable)
						{
							disposable.Dispose();
						}
					}
					mMiners.Clear();

					mProviderManager.Dispose();
				}

				// Free unmanaged resources

				mIsDisposed = true;
			}
		}
		#endregion

		public bool Run()
		{
			bool success = true;
			foreach (IDataMiner miner in mMiners)
			{
				mLogger.Log(LogLevel.Important, $"Running data miner [{miner.Name}]...");
				Stopwatch timer = new Stopwatch();
				timer.Start();
				if (Debugger.IsAttached)
				{
					// Allow exceptions to escape for easier debugging
					success &= miner.Run(mProviderManager, mConfig, mLogger);
				}
				else
				{
					try
					{
						success &= miner.Run(mProviderManager, mConfig, mLogger);
					}
					catch (Exception ex)
					{
						mLogger.Log(LogLevel.Error, $"Data miner [{miner.Name}] failed! [{ex.GetType().FullName}] {ex.Message}");
						success = false;
					}
				}
				timer.Stop();
				mLogger.Log(LogLevel.Information, $"[{miner.Name}] completed in {((double)timer.ElapsedTicks / (double)Stopwatch.Frequency * 1000.0):0.##}ms");
			}
			return success;
		}

		private void CreateMiners(IEnumerable<string>? minersToInclude)
		{
			HashSet<string>? includeMiners = minersToInclude == null ? null : new HashSet<string>(minersToInclude.Select(m => m.ToLowerInvariant()));
			bool forceInclude = includeMiners?.Contains("all", StringComparer.OrdinalIgnoreCase) ?? false;

			Type minerInterface = typeof(IDataMiner);

			Assembly assembly = Assembly.GetExecutingAssembly();
			foreach (Type type in assembly.GetTypes())
			{
				if (!type.IsAbstract && minerInterface.IsAssignableFrom(type))
				{
					if (includeMiners == null)
					{
						DefaultEnabledAttribute? defaultEnabledAttribute = type.GetCustomAttribute<DefaultEnabledAttribute>();
						if (!(defaultEnabledAttribute?.IsEnabled ?? true))
						{
							continue;
						}
					}

					IDataMiner? miner;
					try
					{
						miner = (IDataMiner?)Activator.CreateInstance(type);
						if (miner == null)
						{
							mLogger.Log(LogLevel.Error, $"Could not create an instance of {type.Name}. Ensure it has a parameterless constructor. This miner will not run.");
							continue;
						}
					}
					catch (Exception ex)
					{
						mLogger.Log(LogLevel.Error, $"Could not create an instance of {type.Name}. This miner will not run. [{ex.GetType().FullName}] {ex.Message}");
						continue;
					}
					string name = miner.Name.ToLowerInvariant();
					if (forceInclude || (includeMiners?.Contains(name) ?? true))
					{
						includeMiners?.Remove(name);
						mMiners.Add(miner);
					}
					else if (miner is IDisposable disposable)
					{
						disposable.Dispose();
					}
				}
			}

			includeMiners?.RemoveWhere(n => n.Equals("all", StringComparison.OrdinalIgnoreCase));

			if (includeMiners?.Count > 0)
			{
				mLogger.Log(LogLevel.Warning, $"The following miners specified in the filter could not be located: {string.Join(',', includeMiners)}");
			}
			if (mMiners.Count == 0)
			{
				mLogger.Log(LogLevel.Error, "No data miners were found which match the passed in filter");
			}
			else
			{
				mLogger.Log(LogLevel.Information, $"The following miners will be run: {string.Join(',', mMiners.Select(m => m.Name))}");
			}
		}
	}
}
