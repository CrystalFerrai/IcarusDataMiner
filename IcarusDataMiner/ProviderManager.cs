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

using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using System.Text;

namespace IcarusDataMiner
{
	/// <summary>
	/// Default implementation of IProviderManager
	/// </summary>
	internal class ProviderManager : IProviderManager, IDisposable
	{
		private bool mIsDisposed;

		private readonly DefaultFileProvider mDataProvider;
		private readonly DefaultFileProvider mAssetProvider;

		private WorldDataUtil? mWorldDataUtil;
		private ProspectDataUtil? mProspectDataUtil;
		private DataTables? mDataTables;

		public IFileProvider DataProvider => mDataProvider;

		public IFileProvider AssetProvider => mAssetProvider;

		public WorldDataUtil WorldDataUtil => mWorldDataUtil ?? throw new InvalidOperationException("Manager not intialized");

		public ProspectDataUtil ProspectDataUtil => mProspectDataUtil ?? throw new InvalidOperationException("Manager not intialized");

		public DataTables DataTables => mDataTables ?? throw new InvalidOperationException("Manager not intialized");

		public ProviderManager(Config config)
		{
			mDataProvider = new DefaultFileProvider(Path.Combine(config.GameContentDirectory, "Data"), SearchOption.TopDirectoryOnly);
			mAssetProvider = new DefaultFileProvider(Path.Combine(config.GameContentDirectory, "Paks"), SearchOption.TopDirectoryOnly);
		}

		public bool Initialize(Logger logger)
		{
			InitializeProvider(mDataProvider);
			InitializeProvider(mAssetProvider);

			mWorldDataUtil = LoadWorldData();
			if (mWorldDataUtil == null)
			{
				logger.Log(LogLevel.Error, "Failed to load world data from D_WorldData.json in data.pak");
				return false;
			}

			mProspectDataUtil = ProspectDataUtil.Create(mDataProvider, logger);
			mDataTables = DataTables.Load(mDataProvider, logger);

			return true;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~ProviderManager()
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
					mDataProvider.Dispose();
					mAssetProvider.Dispose();
				}

				// Free unmanaged resources

				mIsDisposed = true;
			}
		}

		private void InitializeProvider(DefaultFileProvider provider)
		{
			provider.Initialize();

			foreach (var vfsReader in provider.UnloadedVfs)
			{
				provider.SubmitKey(vfsReader.EncryptionKeyGuid, new FAesKey(new byte[32]));
			}

			provider.LoadLocalization(ELanguage.English);
		}

		private WorldDataUtil? LoadWorldData()
		{
			GameFile file = mDataProvider.Files["World/D_WorldData.json"];
			return (WorldDataUtil?)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(file.Read()), typeof(WorldDataUtil), new FVector2DJsonConverter(), new FVectorJsonConverter());
		}
	}
}
