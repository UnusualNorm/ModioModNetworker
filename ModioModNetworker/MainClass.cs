﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Web.WebPages;
using LabFusion.Network;
using LabFusion.Representation;
using LabFusion.SDK.Modules;
using LabFusion.Utilities;
using MelonLoader;
using MelonLoader.ICSharpCode.SharpZipLib.Zip;
using ModioModNetworker.Data;
using SLZ.Marrow.SceneStreaming;
using SLZ.Marrow.Warehouse;
using UnityEngine;
using ZipFile = System.IO.Compression.ZipFile;

namespace ModioModNetworker
{
    public class MainClass : MelonMod
    {

        private static string MODIO_MODNETWORKER_DIRECTORY = MelonUtils.GameDirectory + "/ModIoModNetworker";
        private static string MODIO_AUTH_TXT_DIRECTORY = MODIO_MODNETWORKER_DIRECTORY+"/auth.txt";

        public static List<string> subscribedModIoIds = new List<string>();
        private static List<string> toRemoveSubscribedModIoIds = new List<string>();
        public static List<ModInfo> subscribedMods = new List<ModInfo>();
        public static List<ModInfo> installedMods = new List<ModInfo>();
        public static List<InstalledModInfo> InstalledModInfos = new List<InstalledModInfo>();

        public static bool warehouseReloadRequested = false;
        public static List<string> warehousePalletReloadTargets = new List<string>();
        public static List<string> warehouseReloadFolders = new List<string>();
        
        public static bool subsChanged = false;
        public static bool refreshInstalledModsRequested = false;
        public static bool menuRefreshRequested = false;

        public static string subscriptionThreadString = "";
        public static bool subsRefreshing = false;
        private static int desiredSubs = 0;

        private bool addedCallback = false;
        
        private MelonPreferences_Category melonPreferencesCategory;
        private MelonPreferences_Entry<string> modsDirectory;
        
        private static int subsShown = 0;
        private static int subTotal = 0;
        
        public bool palletLock = false;

        public override void OnInitializeMelon()
        {
            melonPreferencesCategory = MelonPreferences.CreateCategory("ModioModNetworker");
            melonPreferencesCategory.SetFilePath(MelonUtils.UserDataDirectory+"/ModioModNetworker.cfg");
            modsDirectory =
                melonPreferencesCategory.CreateEntry<string>("ModDirectoryPath",
                    Application.persistentDataPath + "/Mods");
            ModFileManager.MOD_FOLDER_PATH = modsDirectory.Value;
            PrepareModFiles();
            string auth = ReadAuthKey();
            if (auth.IsEmpty())
            {
                MelonLogger.Error("---------------- IMPORTANT ERROR ----------------");
                MelonLogger.Error("AUTH KEY NOT FOUND IN auth.txt.");
                MelonLogger.Error("MODIONETWORKER WILL NOT RUN! PLEASE FOLLOW THE INSTRUCTIONS LOCATED IN auth.txt!");
                MelonLogger.Error("You can find the auth.txt file in the ModIoModNetworker folder in your game directory.");
                MelonLogger.Error("-------------------------------------------------");
                return;
            }
            
            ModFileManager.OAUTH_KEY = auth;
            MelonLogger.Msg("Registered on mod.io with auth key!");

            MelonLogger.Msg("Loading internal module...");
            ModuleHandler.LoadModule(System.Reflection.Assembly.GetExecutingAssembly());
            ModFileManager.Initialize();
            ModlistMenu.Initialize();
            MultiplayerHooking.OnPlayerJoin += OnPlayerJoin;
            MultiplayerHooking.OnDisconnect += OnDisconnect;
            MultiplayerHooking.OnStartServer += OnStartServer;
            MelonLogger.Msg("Populating currently installed mods via this mod.");
            installedMods.Clear();
            InstalledModInfos.Clear();
            PopulateInstalledMods(ModFileManager.MOD_FOLDER_PATH);
            MelonLogger.Msg("Checking mod.io account subscriptions");
            PopulateSubscriptions();
            
            melonPreferencesCategory.SaveToFile();
        }

        public override void OnUpdate()
        {
            if (ModFileManager.activeDownloadAction != null)
            {
                if (ModFileManager.activeDownloadAction.Check())
                {
                    ModFileManager.activeDownloadAction.Handle();
                    ModFileManager.activeDownloadAction = null;
                }
            }

            if (!addedCallback)
            {
                if (AssetWarehouse.Instance != null)
                {
                    AssetWarehouse.Instance.OnCrateAdded += new Action<string>((s =>
                    {
                        palletLock = false;
                        foreach (var playerRep in PlayerRepManager.PlayerReps)
                        {
                            // Use reflection to get the _isAvatarDirty bool from the playerRep
                            // This is so we can force the playerRep to update the avatar, just incase they had an avatar that we didnt previously have.
                            var isAvatarDirty = playerRep.GetType().GetField("_isAvatarDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            isAvatarDirty.SetValue(playerRep, true);
                        }
                    }));
                    addedCallback = true;
                }
            }

            if (subsRefreshing)
            {
                if (subscribedModIoIds.Count >= desiredSubs)
                {
                    // Remove all invalid modids from the list
                    foreach (var modioId in toRemoveSubscribedModIoIds)
                    {
                        subscribedModIoIds.Remove(modioId);
                    }
                    toRemoveSubscribedModIoIds.Clear();
                    ModlistMenu.Refresh(true);
                    subsRefreshing = false;
                    FusionNotifier.Send(new FusionNotification()
                    {
                        title = "Mod.io Subscriptions Refreshed!",
                        showTitleOnPopup = true,
                        popupLength = 1f,
                        isMenuItem = false,
                        isPopup = true,
                    });
                    MelonLogger.Msg("Finished refreshing mod.io subscriptions!");
                }
            }

            if (warehouseReloadRequested && AssetWarehouse.Instance != null && AssetWarehouse.Instance._initialLoaded && !palletLock)
            {
                bool update = false;
                if (warehousePalletReloadTargets.Count > 0)
                {
                    AssetWarehouse.Instance.ReloadPallet(warehousePalletReloadTargets[0]);
                    warehousePalletReloadTargets.RemoveAt(0);
                    update = true;
                }

                if (warehouseReloadFolders.Count > 0)
                {
                    AssetWarehouse.Instance.LoadPalletFromFolderAsync(warehouseReloadFolders[0], true);
                    // Remove the first element from the list
                    warehouseReloadFolders.RemoveAt(0);
                    palletLock = true;
                }

                if (warehouseReloadFolders.Count == 0 && warehousePalletReloadTargets.Count == 0)
                {
                    string reference = "Downloaded!";
                    string subtitle = "This mod has been loaded into the game.";
                    if (update)
                    {
                        reference = "Updated!";
                        subtitle = "This mod has been updated and reloaded.";
                    }

                    FusionNotifier.Send(new FusionNotification()
                    {
                        title = $"{ModlistMenu.activeDownloadModInfo.modId} {reference}",
                        showTitleOnPopup = true,
                        message = subtitle,
                        popupLength = 3f,
                        isMenuItem = false,
                        isPopup = true,
                    });
                
                    palletLock = false;
                    warehouseReloadRequested = false;
                    ModFileManager.isDownloading = false;
                    ModlistMenu.activeDownloadModInfo = null;
                }
            }
            
            ModInfo.HandleQueue();
            ModFileManager.CheckQueue();

            if (subsChanged)
            {
                subsChanged = false;
                if (NetworkInfo.HasServer && NetworkInfo.IsServer)
                {
                    SendAllMods();
                }
            }

            if (menuRefreshRequested)
            {
                ModlistMenu.Refresh(true);
                menuRefreshRequested = false;
            }

            if (refreshInstalledModsRequested)
            {
                installedMods.Clear();
                InstalledModInfos.Clear();
                ModlistMenu.installPage = 0;
                PopulateInstalledMods(ModFileManager.MOD_FOLDER_PATH);
                ModlistMenu.Refresh(true);
                refreshInstalledModsRequested = false;
            }

            if (!subscriptionThreadString.IsEmpty())
            {
                InternalPopulateSubscriptions();
            }
        }

        public static void ReceiveSubModInfo(ModInfo modInfo)
        {
            if (!modInfo.isValidMod)
            {
                toRemoveSubscribedModIoIds.Add(modInfo.modId);
            }

            ModFileManager.AddToQueue(modInfo);
            subscribedModIoIds.Add(modInfo.modId);
            subscribedMods.Add(modInfo);
        }

        public static void PopulateSubscriptions()
        {
            subscribedMods.Clear();
            subscribedModIoIds.Clear();
            subTotal = 0;
            subsShown = 0;
            desiredSubs = 0;
            ModFileManager.QueueSubscriptions(subsShown);
        }

        private static void InternalPopulateSubscriptions()
        {
            string json = subscriptionThreadString;
            subscriptionThreadString = "";
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            var subs = serializer.Deserialize<dynamic>(json);
            int count = 0;
            int resultTotal = subs["result_total"];
            if (subTotal == 0)
            {
                MelonLogger.Msg("Total subscriptions: " + resultTotal);
                subTotal = resultTotal;
                desiredSubs = 0;
            }

            int resultCount = subs["result_count"];
            if (resultCount == 0)
            {
                MelonLogger.Msg("No subscriptions found!");
                return;
            }
            
            foreach (var sub in subs["data"])
            {
                if (sub["game_id"] == 3809)
                {
                    count++;
                }
            }
            
            desiredSubs += count;

            while (ModInfo.modInfoThreadRequests.TryDequeue(out var useless))
            {
            }

            ModInfo.requestSize = count;
            foreach (var sub in subs["data"])
            {
                // Make sure the sub is a mod from Bonelab
                if (sub["game_id"] == 3809)
                {
                    string modUrl = sub["profile_url"];
                    string[] split = modUrl.Split('/');
                    string name = split[split.Length - 1];
                    bool valid = true;
                    int windowsId = 0;
                    int androidId = 0;
                    foreach (var platform in sub["platforms"])
                    {
                        if (platform["platform"] == "windows")
                        {
                            int desired = platform["modfile_live"];
                            windowsId = desired;
                            break;
                        }
                    }

                    foreach (var platform in sub["platforms"])
                    {
                        if (platform["platform"] == "android")
                        {
                            int desired = platform["modfile_live"];
                            androidId = desired;
                            break;
                        }
                    }

                    if (windowsId != 0)
                    {
                        if (androidId != 0)
                        {
                            if (windowsId == androidId)
                            {
                                valid = false;
                            }
                        }
                    }

                    ModInfo modInfo = ModInfo.MakeFromDynamic(sub["modfile"], name);
                    modInfo.isValidMod = false;

                    if (valid)
                    {
                        modInfo.directDownloadLink = $"https://api.mod.io/v1/games/3809/mods/{sub["id"]}/files/{windowsId}/download";
                        modInfo.isValidMod = true;
                    }
                    
                    ReceiveSubModInfo(modInfo);
                }
            }
            
            subsShown += resultCount;

            if (subTotal - subsShown > 0)
            {
                ModFileManager.QueueSubscriptions(subsShown);
            }

            if (subsShown >= subTotal)
            {
                subsRefreshing = true;
            }
        }

        public void PopulateInstalledMods(string directory)
        {
            foreach (var subDirectory in Directory.GetDirectories(directory))
            {
                PopulateInstalledMods(subDirectory);
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string palletId = "";
            bool validMod = false;
            foreach (var file in Directory.GetFiles(directory))
            {
                if (file.EndsWith("pallet.json"))
                {
                    string fileContents = File.ReadAllText(file);
                    var jsonData = serializer.Deserialize<dynamic>(fileContents);
                    try
                    {
                        palletId = jsonData["objects"]["o:1"]["barcode"];
                        validMod = true;
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Warning($"Failed to parse pallet.json for mod {directory}: {e}");
                    }
                }
            }

            if (validMod)
            {
                foreach (var file in Directory.GetFiles(directory))
                {
                    if (file.EndsWith("modinfo.json"))
                    {
                        var modInfo = serializer.Deserialize<ModInfo>(File.ReadAllText(file));
                        foreach (var alreadyInstalled in installedMods)
                        {
                            if (alreadyInstalled.modId == modInfo.modId)
                            {
                                return;
                            }
                        }

                        installedMods.Add(modInfo);
                        InstalledModInfos.Add(new InstalledModInfo(){palletJsonPath = file, modId = modInfo.modId, palletBarcode = palletId});
                    }
                }
            }
        }

        public void OnStartServer()
        {
            ModlistMenu.Refresh(false);
        }

        public void OnDisconnect()
        {
            ModlistMenu.Clear();
        }

        public void OnPlayerJoin(PlayerId playerId)
        {
            if (NetworkInfo.HasServer && NetworkInfo.IsServer)
            {
                SendAllMods();
            }
        }

        private void SendAllMods()
        {
            int index = 0;
            foreach (ModInfo id in subscribedMods)
            {
                bool final = index == subscribedMods.Count - 1;
                ModlistData modListData = ModlistData.Create(final, id);
                using (var writer = FusionWriter.Create()) {
                    using (var data = modListData) {
                        writer.Write(data);
                        using (var message = FusionMessage.ModuleCreate<ModlistMessage>(writer))
                        {
                            MessageSender.BroadcastMessageExceptSelf(NetworkChannel.Reliable, message);
                        }
                    }
                }
                index++;
            }
        }

        private void PrepareModFiles()
        {
            if (!Directory.Exists(MODIO_MODNETWORKER_DIRECTORY))
            {
                Directory.CreateDirectory(MODIO_MODNETWORKER_DIRECTORY);
            }

            if (!File.Exists(MODIO_AUTH_TXT_DIRECTORY))
            {
                CreateDefaultAuthText(MODIO_AUTH_TXT_DIRECTORY);
            }
        }

        private void CreateDefaultAuthText(string directory)
        {
            using (StreamWriter sw = File.CreateText(directory))    
            {    
                sw.WriteLine("#                       ----- WELCOME TO THE MOD.IO AUTH TXT! -----");
                sw.WriteLine("#");
                sw.WriteLine("# Put your mod.io OAuth key in this file, and it will be used to download mods from the mod.io network.");
                sw.WriteLine("# Your OAuth key can be found here: https://mod.io/me/access");
                sw.WriteLine("# At the bottom, you should see a section called 'OAuth Access'");
                sw.WriteLine("# Create a key, call it whatever you'd like, this doesnt matter.");
                sw.WriteLine("# Then create a token, call it whatever you'd like, this doesnt matter.");
                sw.WriteLine("# The token is pretty long, so make sure you copy the entire thing. Make sure you're copying the token, not the key.");
                sw.WriteLine("# Once you've copied the token, paste it in this file, replacing the text labeled REPLACE_THIS_TEXT_WITH_YOUR_TOKEN.");
                sw.WriteLine("AuthToken=REPLACE_THIS_TEXT_WITH_YOUR_TOKEN");
            }   
        }

        private string ReadAuthKey()
        {
            // Read the file and get the AuthToken= line
            string[] lines = File.ReadAllLines(MODIO_AUTH_TXT_DIRECTORY);
            string builtString = "";
            foreach (string line in lines)
            {
                if (!line.StartsWith("#"))
                {
                    builtString += line;
                }
            }
            
            return builtString.Replace("AuthToken=", "").Replace("REPLACE_THIS_TEXT_WITH_YOUR_TOKEN", "").Trim();
        }
    }
}