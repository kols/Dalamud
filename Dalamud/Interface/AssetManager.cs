using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace Dalamud.Interface
{
    internal class AssetManager
    {
        private const string ASSET_STORE_URL = "https://dalamudassets-1253720819.cos.ap-nanjing.myqcloud.com/";

        internal class AssetInfo
        {
            public int Version { get; set; }
            public List<Asset> Assets { get; set; }

            public class Asset
            {
                public string Url { get; set; }
                public string FileName { get; set; }
                public string Hash { get; set; }
            }
        }

        public static bool EnsureAssets(DirectoryInfo baseDir)
        {
            using var client = new WebClient();
            using var sha1 = SHA1.Create();

            Log.Information("[DASSET] Starting asset download");

            var (isRefreshNeeded, info) = CheckAssetRefreshNeeded(baseDir);

            if (info == null)
                return false;

            foreach (var entry in info.Assets)
            {
                entry.Url = Utility.Util.FuckGFW(entry.Url);
                var filePath = Path.Combine(baseDir.FullName, entry.FileName);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                var refreshFile = false;
                if (File.Exists(filePath) && !string.IsNullOrEmpty(entry.Hash))
                {
                    try
                    {
                        using var file = File.OpenRead(filePath);
                        var fileHash = sha1.ComputeHash(file);
                        var stringHash = BitConverter.ToString(fileHash).Replace("-", string.Empty);
                        refreshFile = stringHash != entry.Hash;
                        Log.Information("[DASSET] {0} has hash {1} when remote asset has {2}.", entry.FileName, stringHash, entry.Hash);
                        if (!refreshFile) continue;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[DASSET] Could not read asset.");
                    }
                }

                if (!File.Exists(filePath) || isRefreshNeeded || refreshFile)
                {
                    Log.Information("[DASSET] Downloading {0} to {1}...", entry.Url, entry.FileName);
                    try
                    {
                        File.WriteAllBytes(filePath, client.DownloadData(entry.Url));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[DASSET] Could not download asset.");
                        return false;
                    }
                }
            }

            if (isRefreshNeeded)
                SetLocalAssetVer(baseDir, info.Version);

            Log.Information("[DASSET] Assets OK");

            return true;
        }

        private static string GetAssetVerPath(DirectoryInfo baseDir)
        {
            return Path.Combine(baseDir.FullName, "asset.ver");
        }


        /// <summary>
        ///     Check if an asset update is needed. When this fails, just return false - the route to github
        ///     might be bad, don't wanna just bail out in that case
        /// </summary>
        /// <param name="baseDir">Base directory for assets</param>
        /// <returns>Update state</returns>
        private static (bool isRefreshNeeded, AssetInfo info) CheckAssetRefreshNeeded(DirectoryInfo baseDir)
        {
            using var client = new WebClient();

            try
            {
                var localVerFile = GetAssetVerPath(baseDir);
                var localVer = 0;

                try
                {
                    if (File.Exists(localVerFile))
                        localVer = int.Parse(File.ReadAllText(localVerFile));
                }
                catch (Exception ex)
                {
                    // This means it'll stay on 0, which will redownload all assets - good by me
                    Log.Error(ex, "[DASSET] Could not read asset.ver");
                }

                var remoteVer = JsonConvert.DeserializeObject<AssetInfo>(client.DownloadString(Utility.Util.FuckGFW(ASSET_STORE_URL + "asset.json")));

                Log.Information("[DASSET] Ver check - local:{0} remote:{1}", localVer, remoteVer.Version);

                var needsUpdate = remoteVer.Version > localVer;

                return (needsUpdate, remoteVer);
            }
            catch (Exception e)
            {
                Log.Error(e, "[DASSET] Could not check asset version");
                return (false, null);
            }
        }

        private static void SetLocalAssetVer(DirectoryInfo baseDir, int version)
        {
            try
            {
                var localVerFile = GetAssetVerPath(baseDir);
                File.WriteAllText(localVerFile, version.ToString());
            }
            catch (Exception e)
            {
                Log.Error(e, "[DASSET] Could not write local asset version");
            }
        }
    }
}
