using System.Collections.Generic;
using BoneLib.BoneMenu;
using BoneLib.BoneMenu.Elements;
using LabFusion.Network;
using LabFusion.Utilities;
using MelonLoader;
using ModioModNetworker.Data;
using UnityEngine;

namespace ModioModNetworker
{
    public class ModlistMenu
    {
        public static MenuCategory mainCategory;
        public static MenuCategory hostModsCategory;
        public static MenuCategory installedModsCategory;
        public static MenuCategory managementCategory;
        private static string lastSelectedCategory;
        public static List<ModInfo> _modInfos = new List<ModInfo>();
        
        public static int installPage = 0;
        public static int hostPage = 0;
        
        private static int installPageCount = 0;
        private static int hostPageCount = 0;
        
        private static int modsPerPage = 4;
        
        public static ModInfo activeDownloadModInfo;

        public static void Initialize()
        {
            mainCategory = MenuManager.CreateCategory("ModIo Mod Networker", Color.cyan);
            MainClass.menuRefreshRequested = true;
        }

        public static void PopulateModInfos(List<ModInfo> modInfos)
        {
            hostPage = 0;
            _modInfos.Clear();
            _modInfos.AddRange(modInfos);
            MainClass.menuRefreshRequested = true;
        }

        public static void Clear()
        {
            _modInfos.Clear();
            Refresh(false);
        }

        public static void Refresh(bool openMenu)
        {
            installPageCount = Mathf.CeilToInt(MainClass.installedMods.Count / (float) modsPerPage);
            hostPageCount = Mathf.CeilToInt(_modInfos.Count / (float) modsPerPage);
            mainCategory.Elements.Clear();

            if (activeDownloadModInfo != null)
            {
                mainCategory.CreateSubPanel("DOWNLOADING...", Color.yellow);
                mainCategory.CreateFunctionElement("Click to Update Percentage", Color.white, () =>
                {
                    Refresh(true);
                });
                mainCategory.CreateFunctionElement(activeDownloadModInfo.modId+$" ({activeDownloadModInfo.modDownloadPercentage}%)", Color.yellow, () => { });
                mainCategory.CreateSubPanel("==============", Color.yellow);
            }
            
            mainCategory.CreateFunctionElement("Refresh Mod.Io Subscriptions", Color.cyan, () =>
            {
                MainClass.PopulateSubscriptions();
            });

            installedModsCategory = mainCategory.CreateCategory("Installed Mods", Color.white);
            CreateInstalledModsSection(installPage);
            
            if (NetworkInfo.HasServer && !NetworkInfo.IsServer)
            {
                hostModsCategory = mainCategory.CreateCategory("Host Mods", Color.white);
                CreateHostModsSection(hostPage);
            }
            
            managementCategory = mainCategory.CreateCategory("Management", Color.white);
            CreateManagementSection();

            if (openMenu)
            {
                MenuCategory category = mainCategory;
                MenuManager.SelectCategory(category);
            }
        }

        private static void CreateManagementSection()
        {
            MenuCategory downloadingCategory = managementCategory.CreateCategory("Downloading", Color.white);
            float totalSize = 0;
            int missingMods = 0;
            foreach (var modInfo in _modInfos)
            {
                bool isInstalled = false;
                foreach (var mod in MainClass.installedMods)
                {
                    if (mod.modId == modInfo.modId)
                    {
                        isInstalled = true;
                        break;
                    }
                }

                if (!isInstalled)
                {
                    missingMods++;
                    totalSize += modInfo.fileSizeKB;
                }
            }
            
            float kb = totalSize;
            float mb = kb / 1000000;
            float gb = mb / 1000;

            string display = "KB";
            float value = kb;
            if (mb > 1)
            {
                value = mb;
                display = "MB";
            }
            if (gb > 1)
            {
                value = gb;
                display = "GB";
            }
            
            // Round to 2 decimal places
            value = Mathf.Round(value * 100f) / 100f;
            downloadingCategory.CreateFunctionElement("Mods Missing: " + missingMods, Color.yellow, () => { });
            
            downloadingCategory.CreateFunctionElement($"Download All Host Mods ({value} {display})", Color.green, () =>
            {
                Refresh(true);
                foreach (var modInfo in _modInfos)
                {
                    ModFileManager.AddToQueue(modInfo);
                }
            }, "Are you sure?");
        }

        private static void CreateInstalledModsSection(int page)
        {
            installedModsCategory.CreateSubPanel("INSTALLED", Color.green);
            
            int startIndex = page * modsPerPage;
            int endIndex = Mathf.Min(startIndex + modsPerPage, MainClass.installedMods.Count);

            int index = 0;
            foreach (var info in MainClass.installedMods)
            {
                if (index >= startIndex && index < endIndex)
                {
                    MakeModInfoButton(info, installedModsCategory, true);
                }

                index++;
            }
            
            if (page > 0)
            {
                installedModsCategory.CreateFunctionElement($"Previous Page {page + 1}/{installPageCount}", Color.white, () =>
                {
                    installPage--;
                    Refresh(false);
                    MenuManager.SelectCategory(installedModsCategory);
                });
            }

            if (page < installPageCount - 1)
            {
                installedModsCategory.CreateFunctionElement($"Next Page {page + 1}/{installPageCount}", Color.white, () =>
                {
                    installPage++;
                    Refresh(false);
                    MenuManager.SelectCategory(installedModsCategory);
                });
            }
        }

        private static void CreateHostModsSection(int page)
        {
            hostModsCategory.CreateSubPanel("HOST'S MODS", Color.white);
            
            if (_modInfos.Count == 0)
            {
                hostModsCategory.CreateFunctionElement("Host either has no mods in their modlist or does not have this mod.", Color.red, ()=>{});
                return;
            }

            int startIndex = page * modsPerPage;

            int index = 0;
            int shown = 0;
            
            foreach (var info in _modInfos)
            {
                if (index >= startIndex && shown < modsPerPage)
                {
                    MakeModInfoButton(info, hostModsCategory);
                    shown++;
                }

                index++;
            }
            
            if (page > 0)
            {
                hostModsCategory.CreateFunctionElement($"Previous Page {page + 1}/{hostPageCount}", Color.white, () =>
                {
                    hostPage--;
                    Refresh(false);
                    MenuManager.SelectCategory(hostModsCategory);
                });
            }

            if (page < hostPageCount - 1)
            {
                hostModsCategory.CreateFunctionElement($"Next Page {page + 1}/{hostPageCount}", Color.white, () =>
                {
                    hostPage++;
                    Refresh(false);
                    MenuManager.SelectCategory(hostModsCategory);
                });
            }
        }

        private static ModInfo GetInstalledInfo(string modId)
        {
            foreach (var info in MainClass.installedMods)
            {
                if (info.modId == modId)
                {
                    return info;
                }
            }

            return null;
        }

        private static void MakeModInfoButton(ModInfo modInfo, MenuCategory category, bool displayUninstall = false)
        {
            if (activeDownloadModInfo != null)
            {
                if (activeDownloadModInfo.modId == modInfo.modId)
                {
                    return;
                }
            }
            
            if (!modInfo.isValidMod)
            {
                return;
            }

            Color chosenColor = Color.white;
            

            float kb = modInfo.fileSizeKB;
            float mb = kb / 1000000;
            float gb = mb / 1000;

            string display = "KB";
            float value = kb;
            if (mb > 1)
            {
                value = mb;
                display = "MB";
            }
            if (gb > 1)
            {
                value = gb;
                display = "GB";
            }
            
            value = Mathf.Round(value * 100f) / 100f;
            
            ModInfo installedInfo = GetInstalledInfo(modInfo.modId);
            bool outOfDate = false;
            bool installed = false;
            Color installedColor = Color.green;
            
            if (installedInfo != null)
            {
                installed = true;
                chosenColor = Color.green;
                if (installedInfo.version != modInfo.version)
                {
                    installedColor = Color.yellow;
                    chosenColor = Color.yellow;
                    outOfDate = true;
                }
                else
                {
                    if (!modInfo.IsSubscribed())
                    {
                        chosenColor = Color.cyan;
                    }
                }
            }

            MenuCategory modInfoButton = category.CreateCategory(modInfo.modId, chosenColor);
            if (modInfo.isValidMod)
            {
                modInfoButton.CreateSubPanel("Filename: "+modInfo.fileName, Color.yellow);
                modInfoButton.CreateFunctionElement("File Size: "+value+" "+display, Color.yellow, ()=>{});
                // We just got this from the API, so it should be up to date.
                modInfoButton.CreateFunctionElement("Latest Version: "+modInfo.version, Color.yellow, ()=>{});

                if (installed)
                {
                    modInfoButton.CreateFunctionElement("Installed Version: "+installedInfo.version, installedColor, ()=>{});
                }

                if (!ModFileManager.isDownloading)
                {
                    bool allowDownload = false;
                    string buttonText = "INSTALL";
                    Color buttonColor = Color.green;
                    if (!installed)
                    {
                        allowDownload = true;
                    }
                    else
                    {
                        if (outOfDate)
                        {
                            allowDownload = true;
                            buttonText = "UPDATE";
                            buttonColor = Color.yellow;
                        }
                    }

                    if (allowDownload)
                    {
                        modInfoButton.CreateFunctionElement(buttonText, buttonColor, () =>
                        {
                            modInfo.Download();
                            Refresh(true);
                        });
                    }
                    else
                    {
                        modInfoButton.CreateFunctionElement("UP TO DATE", Color.green, () =>
                        {
                        });
                    }

                    if (!modInfo.IsSubscribed())
                    {
                        modInfoButton.CreateFunctionElement("Subscribe", Color.cyan, () =>
                        {
                            if (ModFileManager.Subscribe(modInfo.modId))
                            {
                                MainClass.subscribedModIoIds.Add(modInfo.modId);
                                Refresh(true);
                                FusionNotifier.Send(new FusionNotification()
                                {
                                    title = "Subscribed to "+modInfo.modId,
                                    showTitleOnPopup = true,
                                    message = "This is now in your mod.io subscribed list.",
                                    popupLength = 3f,
                                    isMenuItem = false,
                                    isPopup = true,
                                });
                            }
                        });
                    }

                    if (displayUninstall)
                    {
                        string uninstallText = "UNINSTALL";
                        if (modInfo.IsSubscribed())
                        {
                            uninstallText = "UNSUBSCRIBE AND UNINSTALL";
                        }

                        modInfoButton.CreateFunctionElement(uninstallText, Color.yellow, () =>
                        {
                            ModFileManager.UninstallAndUnsubscribe(modInfo.modId);
                        });
                    }
                }
                else
                {
                    modInfoButton.CreateFunctionElement("YOU ALREADY HAVE A DOWNLOAD RUNNING.", Color.red, () =>
                    {
                    });
                }
            }
        }
    }
}