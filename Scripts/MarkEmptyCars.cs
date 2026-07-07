using HarmonyLib;
using System;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.IO;

public class MarkEmptyCarsMod : IModApi
{
    public static bool DebugMode = false;
    public static string FallbackMarkerName = "vending_machine";

    public void InitMod(Mod _modInstance)
    {
        string configPath = _modInstance.Path + "/config.txt";
        if (File.Exists(configPath))
        {
            try
            {
                string[] lines = File.ReadAllLines(configPath);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim().ToLower().Replace(" ", "");
                    if (trimmed.Contains("debug=true") && !trimmed.StartsWith("#"))
                    {
                        DebugMode = true;
                        UnityEngine.Debug.Log("[MarkEmptyCars] Debug mode ENABLED via config.txt!");
                    }
                    if (trimmed.StartsWith("fallbackmarker=") && !trimmed.StartsWith("#"))
                    {
                        string[] parts = line.Split('=');
                        if (parts.Length >= 2)
                        {
                            FallbackMarkerName = parts[1].Trim();
                            UnityEngine.Debug.Log("[MarkEmptyCars] Fallback marker set to: " + FallbackMarkerName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MarkEmptyCars] Error reading config.txt: " + ex.Message);
            }
        }

        UnityEngine.Debug.Log("[MarkEmptyCars] Loading mod by zeeCameLsnake (Deep Reflection + Minivan)...");
        var harmony = new Harmony("com.zeecamelsnake.markemptycars");
        harmony.PatchAll();
        UnityEngine.Debug.Log("[MarkEmptyCars] Harmony patches applied successfully.");
    }
}

public static class EmptyCarDataManager
{
    public static HashSet<string> EmptyCars = new HashSet<string>();
    public static bool IsDirty = false;

    private static string GetFilePath()
    {
        return GameIO.GetSaveGameDir() + "/MarkEmptyCars.txt";
    }

    public static string GetKey(Vector3 pos)
    {
        return Mathf.RoundToInt(pos.x) + "," + Mathf.RoundToInt(pos.y) + "," + Mathf.RoundToInt(pos.z);
    }

    public static void Load()
    {
        EmptyCars.Clear();
        string path = GetFilePath();
        if (File.Exists(path))
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        EmptyCars.Add(line.Trim());
                    }
                }
                UnityEngine.Debug.Log("[MarkEmptyCars] Loaded " + EmptyCars.Count + " markers from persistence.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MarkEmptyCars] Error loading persistence: " + ex.Message);
            }
        }
    }

    public static void Save()
    {
        if (!IsDirty) return;
        
        string path = GetFilePath();
        try
        {
            List<string> lines = new List<string>(EmptyCars);
            File.WriteAllLines(path, lines.ToArray());
            IsDirty = false;
            if (MarkEmptyCarsMod.DebugMode) UnityEngine.Debug.Log("[MarkEmptyCars] Saved " + EmptyCars.Count + " markers to persistence.");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.Log("[MarkEmptyCars] Error saving persistence: " + ex.Message);
        }
    }

    public static void AddCar(Vector3 pos)
    {
        if (EmptyCars.Add(GetKey(pos)))
        {
            IsDirty = true;
        }
    }

    public static void RemoveCar(Vector3 pos)
    {
        if (EmptyCars.Remove(GetKey(pos)))
        {
            IsDirty = true;
        }
    }
}

[HarmonyPatch(typeof(GameManager), "UpdateTick")]
public class Patch_GameManager_UpdateTick
{
    public static void Postfix()
    {
        try
        {
            EmptyCarScanner.Tick();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.Log("[MarkEmptyCars] Scanner Tick Error: " + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(XUiC_LootWindowGroup), "OnClose")]
public class Patch_XUiC_LootWindowGroup_OnClose
{
    public static void Postfix(XUiC_LootWindowGroup __instance)
    {
        if (GameManager.IsDedicatedServer) return;
        try
        {
            if (__instance == null) return;

            object te = null;

            // Search directly inside the LootWindowGroup fields for the TileEntity (DEEP REFLECTION)
            foreach (FieldInfo field in __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.FieldType.Name.Contains("TEFeature") || field.FieldType.Name.Contains("TileEntity") || field.FieldType.Name.Contains("Lootable"))
                {
                    te = field.GetValue(__instance);
                    if (te != null) break;
                }
            }

            // If not found, search in all child windows
            if (te == null && __instance.Children != null)
            {
                foreach (var child in __instance.Children)
                {
                    if (child == null) continue;
                    foreach (FieldInfo field in child.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (field.FieldType.Name.Contains("TEFeature") || field.FieldType.Name.Contains("TileEntity") || field.FieldType.Name.Contains("Lootable"))
                        {
                            te = field.GetValue(child);
                            if (te != null) break;
                        }
                    }
                    if (te != null) break;
                }
            }

            if (te == null) return;

            // Check if it's empty
            MethodInfo mIsEmpty = te.GetType().GetMethod("IsEmpty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mIsEmpty == null) return;

            bool isEmpty = (bool)mIsEmpty.Invoke(te, null);
            if (!isEmpty) return;

            // Find ToWorldPos
            MethodInfo mToWorldPos = te.GetType().GetMethod("ToWorldPos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object posTarget = te;
            
            if (mToWorldPos == null) 
            {
                PropertyInfo pParent = te.GetType().GetProperty("Parent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pParent != null)
                {
                    object parentTE = pParent.GetValue(te, null);
                    if (parentTE != null)
                    {
                        posTarget = parentTE;
                        mToWorldPos = parentTE.GetType().GetMethod("ToWorldPos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
            }

            if (mToWorldPos == null) return;
            
            Vector3i pos = (Vector3i)mToWorldPos.Invoke(posTarget, null);

            // Get the block and check if it's a vehicle
            World world = GameManager.Instance.World;
            if (world == null) return;

            BlockValue blockValue = world.GetBlock(pos);
            Block block = blockValue.Block;
            if (block == null) return;

            string blockName = block.GetBlockName().ToLower();
            
            // Check lootListName if available
            string lootList = "";
            FieldInfo fLootListName = te.GetType().GetField("lootListName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fLootListName != null)
            {
                lootList = fLootListName.GetValue(te) as string;
                if (lootList == null) lootList = "";
            }
            lootList = lootList.ToLower();

            // Check if it is a car/vehicle
            bool isVehicle = (blockName.Contains("car") && !blockName.Contains("parts") && !blockName.Contains("cart") && !blockName.Contains("cardboard")) || 
                             blockName.Contains("sedan") || 
                             blockName.Contains("suv") || 
                             (blockName.Contains("truck") && !blockName.Contains("tiltTruck")) || 
                             blockName.Contains("ambulance") || 
                             blockName.Contains("bus") ||
                             blockName.Contains("pickup") ||
                             blockName.Contains("minivan") ||
                             blockName.Contains("police") ||
                             blockName.Contains("taxi") ||
                             blockName.Contains("tractor") ||
                             blockName.Contains("firetruck") ||
                             blockName.Contains("delivery") ||
                             lootList.Contains("vehicle") ||
                             lootList.Contains("cars");
                            

            if (!isVehicle) return;

            // we pass the raw root block position. We can use the 'offset' property in 
            // nav_objects.xml (e.g. <property name="offset" value="0.5, 1.5, 0.5" />) to adjust it!
            Vector3 spawnPos = pos.ToVector3();

            bool exists = false;
            try
            {
                if (NavObjectManager.Instance != null && NavObjectManager.Instance.NavObjectList != null)
                {
                    foreach (NavObject nav in NavObjectManager.Instance.NavObjectList)
                    {
                        if (nav != null && nav.NavObjectClass != null)
                        {
                            string navClassName = EmptyCarScanner.GetNavClassName(nav.NavObjectClass);

                            string activeMarkerName = EmptyCarScanner.GetActiveMarkerName();
                            bool isOurMarker = false;

                            // Dynamically find the position property/field/method
                            Vector3 nposTemp = Vector3.zero;
                            bool posFoundTemp = false;

                            PropertyInfo pPosTemp = nav.GetType().GetProperty("TrackedPosition");
                            if (pPosTemp != null) { nposTemp = (Vector3)pPosTemp.GetValue(nav, null); posFoundTemp = true; }
                            else 
                            {
                                MethodInfo mGetPosTemp = nav.GetType().GetMethod("GetPosition");
                                if (mGetPosTemp != null) { nposTemp = (Vector3)mGetPosTemp.Invoke(nav, null); posFoundTemp = true; }
                                else
                                {
                                    FieldInfo fPosTemp = nav.GetType().GetField("position") ?? nav.GetType().GetField("Position") ?? nav.GetType().GetField("TrackedPosition");
                                    if (fPosTemp != null) { nposTemp = (Vector3)fPosTemp.GetValue(nav); posFoundTemp = true; }
                                }
                            }

                            if (navClassName == "empty_car_marker")
                            {
                                if (posFoundTemp && Vector3.Distance(nposTemp, spawnPos) < 1f)
                                {
                                    isOurMarker = true;
                                }
                            }
                            else if (navClassName == activeMarkerName && activeMarkerName != "empty_car_marker")
                            {
                                if (posFoundTemp)
                                {
                                    if (Vector3.Distance(nposTemp, spawnPos) < 1f)
                                    {
                                        isOurMarker = true;
                                    }
                                }
                            }

                            if (isOurMarker)
                            {
                                exists = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MarkEmptyCars] Error checking existing NavObjects: " + ex.Message);
            }

            if (!exists)
            {
                NavObject navObj = null;
                try 
                {
                    // Direct call, matching the 6-argument signature the compiler expects
                    navObj = NavObjectManager.Instance.RegisterNavObject(EmptyCarScanner.GetActiveMarkerName(), spawnPos, "", false, -1, null);
                } 
                catch (Exception e) 
                { 
                    UnityEngine.Debug.Log("[MarkEmptyCars] Direct invoke failed: " + e.Message);
                }

                if (navObj != null)
                {
                    EmptyCarDataManager.AddCar(spawnPos);
                }
                else
                {
                    UnityEngine.Debug.LogError("[MarkEmptyCars] Failed to register NavObject! NavObject was null.");
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("[MarkEmptyCars] Error in OnClose patch: " + e.Message);
        }
    }
}

public static class EmptyCarScanner
{
    private static float nextCheckTime = 0f;
    // High-performance reflection caches
    private static Dictionary<Type, MethodInfo> mGetTileEntitiesCache = new Dictionary<Type, MethodInfo>();
    private static Dictionary<Type, FieldInfo> fDictCache = new Dictionary<Type, FieldInfo>();
    private static Dictionary<Type, PropertyInfo> pDictCache = new Dictionary<Type, PropertyInfo>();
    private static Dictionary<Type, PropertyInfo> pValuesCache = new Dictionary<Type, PropertyInfo>();
    private static Dictionary<Type, FieldInfo> fListCache = new Dictionary<Type, FieldInfo>();
    private static Dictionary<Type, PropertyInfo> pListCache = new Dictionary<Type, PropertyInfo>();

    private static Dictionary<Type, MethodInfo> mToWorldPosCache = new Dictionary<Type, MethodInfo>();
    private static Dictionary<Type, MethodInfo> mIsEmptyCache = new Dictionary<Type, MethodInfo>();
    private static Dictionary<Type, FieldInfo> fLootListNameCache = new Dictionary<Type, FieldInfo>();
    private static Dictionary<Type, System.Collections.Generic.List<MemberInfo>> teEnumerableMembersCache = new Dictionary<Type, System.Collections.Generic.List<MemberInfo>>();
    private static Dictionary<Type, bool> teEnumerableMembersInitialized = new Dictionary<Type, bool>();
    private static Dictionary<Type, FieldInfo> fBTouchedCache = new Dictionary<Type, FieldInfo>();
    private static Dictionary<Type, PropertyInfo> pIsTouchedCache = new Dictionary<Type, PropertyInfo>();

    private static float nextSaveTime = 0f;
    private static bool isDataLoaded = false;
    private static bool isMarkersSpawned = false;
    private static World lastWorld = null;

    public static string ActiveMarkerName = null;

    public static string GetActiveMarkerName()
    {
        if (ActiveMarkerName != null) return ActiveMarkerName;

        ActiveMarkerName = "empty_car_marker"; 
        try
        {
            MethodInfo mGet = typeof(NavObjectClass).GetMethod("GetNavObjectClass", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (mGet != null)
            {
                object cls = mGet.Invoke(null, new object[] { "empty_car_marker" });
                if (cls == null)
                {
                    ActiveMarkerName = MarkEmptyCarsMod.FallbackMarkerName;
                    UnityEngine.Debug.LogWarning("[MarkEmptyCars] 'empty_car_marker' class not found! Falling back to '" + ActiveMarkerName + "' marker. (Client-only mode)");
                }
                else
                {
                    UnityEngine.Debug.Log("[MarkEmptyCars] 'empty_car_marker' class found! Using custom marker.");
                }
            }
            else 
            {
                FieldInfo fDict = typeof(NavObjectClass).GetField("navObjectClassDict", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ?? typeof(NavObjectClass).GetField("NavObjectClassDict", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (fDict != null)
                {
                    var dict = fDict.GetValue(null) as System.Collections.IDictionary;
                    if (dict != null && !dict.Contains("empty_car_marker"))
                    {
                        ActiveMarkerName = MarkEmptyCarsMod.FallbackMarkerName;
                        UnityEngine.Debug.LogWarning("[MarkEmptyCars] 'empty_car_marker' class not found in dict! Falling back to '" + ActiveMarkerName + "' marker. (Client-only mode)");
                    }
                    else if (dict != null)
                    {
                        UnityEngine.Debug.Log("[MarkEmptyCars] 'empty_car_marker' class found in dict! Using custom marker.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[MarkEmptyCars] Error checking NavObjectClass: " + ex.Message);
        }
        return ActiveMarkerName;
    }

    private static void SpawnPersistentMarkers()
    {
        foreach (string key in EmptyCarDataManager.EmptyCars)
        {
            string[] parts = key.Split(',');
            if (parts.Length == 3)
            {
                float x, y, z;
                if (float.TryParse(parts[0], out x) && 
                    float.TryParse(parts[1], out y) && 
                    float.TryParse(parts[2], out z))
                {
                    Vector3 pos = new Vector3(x, y, z);
                    if (!MarkerExists(pos))
                    {
                        try 
                        {
                            NavObjectManager.Instance.RegisterNavObject(GetActiveMarkerName(), pos, "", false, -1, null);
                        } 
                        catch { }
                    }
                }
            }
        }
    }

    public static string GetNavClassName(object navObjectClass)
    {
        if (navObjectClass == null) return "";
        string name = "";
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        
        PropertyInfo pName = navObjectClass.GetType().GetProperty("NavObjectName", flags) ?? navObjectClass.GetType().GetProperty("name", flags);
        if (pName != null) { name = pName.GetValue(navObjectClass, null) as string; }
        else
        {
            FieldInfo fName = navObjectClass.GetType().GetField("NavObjectName", flags) ?? navObjectClass.GetType().GetField("name", flags);
            if (fName != null) { name = fName.GetValue(navObjectClass) as string; }
            else
            {
                foreach (FieldInfo f in navObjectClass.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.Name.ToLower().Contains("name") && f.FieldType == typeof(string))
                    {
                        name = f.GetValue(navObjectClass) as string;
                        if (!string.IsNullOrEmpty(name)) break;
                    }
                }
            }
        }
        return name ?? "";
    }

    public static void Tick()
    {
        if (GameManager.IsDedicatedServer) return;
        if (UnityEngine.Time.time < nextCheckTime) return;
        nextCheckTime = UnityEngine.Time.time + 3f; // Scan every 3 seconds

        try
        {
            if (GameManager.Instance == null) return;
            World world = GameManager.Instance.World;
            
            if (world != lastWorld)
            {
                lastWorld = world;
                isDataLoaded = false;
                isMarkersSpawned = false;
            }

            if (world == null || world.ChunkCache == null) 
            {
                // We only log this occasionally if we want, but it's normal in main menu
                return;
            }

            if (!isDataLoaded)
            {
                EmptyCarDataManager.Load();
                isDataLoaded = true;
            }

            if (!isMarkersSpawned)
            {
                if (world.Players != null && world.Players.list != null && world.Players.list.Count > 0)
                {
                    SpawnPersistentMarkers();
                    isMarkersSpawned = true;
                }
            }

            if (UnityEngine.Time.time > nextSaveTime && EmptyCarDataManager.IsDirty)
            {
                nextSaveTime = UnityEngine.Time.time + 10f; // Save max every 10s
                EmptyCarDataManager.Save();
            }

            // 1. Get chunks array using GetChunkArray()
            MethodInfo mGetChunks = null;
            Type chunkCacheType = world.ChunkCache.GetType();
            
            if (!mGetTileEntitiesCache.TryGetValue(chunkCacheType, out mGetChunks)) // Reusing dict for simplicity of type tracking, actually we need a separate one, wait let's just do it directly
            {
                mGetChunks = chunkCacheType.GetMethod("GetChunkArray", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mGetChunks == null) 
                {
                    mGetChunks = chunkCacheType.GetMethod("GetChunks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                mGetTileEntitiesCache[chunkCacheType] = mGetChunks; // It's fine to store mGetChunks here just to cache it on chunkCacheType
            }

            if (mGetChunks == null) return;

            object chunksList = mGetChunks.Invoke(world.ChunkCache, null);
            if (chunksList == null) return;

            System.Collections.IEnumerable chunksEnumerable = chunksList as System.Collections.IEnumerable;
            if (chunksEnumerable == null) return;

            System.Collections.Generic.List<Vector3> emptyCarsPositions = new System.Collections.Generic.List<Vector3>();
            int processedChunks = 0;

            System.Collections.Generic.List<object> safeChunks = new System.Collections.Generic.List<object>();
            try 
            {
                foreach (var chunk in chunksEnumerable) 
                {
                    safeChunks.Add(chunk);
                }
            } 
            catch { return; }

            foreach (var chunk in safeChunks)
            {
                if (chunk == null) continue;
                processedChunks++;
                
                Type chunkType = chunk.GetType();
                MethodInfo mGetTileEntities;
                if (!mGetTileEntitiesCache.TryGetValue(chunkType, out mGetTileEntities))
                {
                    mGetTileEntities = chunkType.GetMethod("GetTileEntities", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    mGetTileEntitiesCache[chunkType] = mGetTileEntities;
                }

                if (mGetTileEntities == null) continue;
                
                object teDictList = mGetTileEntities.Invoke(chunk, null);
                if (teDictList == null) continue;

                Type dictListType = teDictList.GetType();
                System.Collections.IEnumerable valuesEnumerable = null;
                
                object dictObj = null;
                FieldInfo fDict;
                if (!fDictCache.TryGetValue(dictListType, out fDict))
                {
                    fDict = dictListType.GetField("dict", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fDict != null) fDictCache[dictListType] = fDict;
                    else fDictCache[dictListType] = null; // Cache null to avoid retrying
                }

                if (fDict != null) dictObj = fDict.GetValue(teDictList);
                else 
                {
                    PropertyInfo pDict;
                    if (!pDictCache.TryGetValue(dictListType, out pDict))
                    {
                        pDict = dictListType.GetProperty("dict", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        pDictCache[dictListType] = pDict;
                    }
                    if (pDict != null) dictObj = pDict.GetValue(teDictList, null);
                }

                if (dictObj != null)
                {
                    Type innerDictType = dictObj.GetType();
                    PropertyInfo pValues;
                    if (!pValuesCache.TryGetValue(innerDictType, out pValues))
                    {
                        pValues = innerDictType.GetProperty("Values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        pValuesCache[innerDictType] = pValues;
                    }
                    if (pValues != null)
                    {
                        valuesEnumerable = pValues.GetValue(dictObj, null) as System.Collections.IEnumerable;
                    }
                }
                
                if (valuesEnumerable == null)
                {
                    object listObj = null;
                    FieldInfo fList;
                    if (!fListCache.TryGetValue(dictListType, out fList))
                    {
                        fList = dictListType.GetField("list", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (fList != null) fListCache[dictListType] = fList;
                        else fListCache[dictListType] = null;
                    }

                    if (fList != null) listObj = fList.GetValue(teDictList);
                    else
                    {
                        PropertyInfo pList;
                        if (!pListCache.TryGetValue(dictListType, out pList))
                        {
                            pList = dictListType.GetProperty("list", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            pListCache[dictListType] = pList;
                        }
                        if (pList != null) listObj = pList.GetValue(teDictList, null);
                    }
                    valuesEnumerable = listObj as System.Collections.IEnumerable;
                }

                if (valuesEnumerable == null)
                {
                    valuesEnumerable = teDictList as System.Collections.IEnumerable;
                }

                if (valuesEnumerable == null) continue;

                foreach (var te in valuesEnumerable)
                {
                    if (te == null) continue;
                    ProcessTE(te, world, emptyCarsPositions);
                }
            }

            // 3. Cleanup old/invalid NavObjects (Anti-Ghosting)
            CleanupNavObjects(emptyCarsPositions);
        }
        catch (Exception ex)
        {
            // Print error to see why it fails silently
            UnityEngine.Debug.Log("[MarkEmptyCars] Scanner Error: " + ex.ToString());
        }
    }

    private static void ProcessTE(object te, World world, System.Collections.Generic.List<Vector3> emptyCarsPositions)
    {
        try
        {
            if (te == null) return;

            // If te is a KeyValuePair from DictionaryList iteration, unpack the Value
            if (te.GetType().IsGenericType && te.GetType().GetGenericTypeDefinition() == typeof(System.Collections.Generic.KeyValuePair<,>))
            {
                PropertyInfo valProp = te.GetType().GetProperty("Value");
                if (valProp != null)
                {
                    te = valProp.GetValue(te, null);
                    if (te == null) return;
                }
            }

            Type teType = te.GetType();

            MethodInfo mToWorldPos;
            if (!mToWorldPosCache.TryGetValue(teType, out mToWorldPos))
            {
                mToWorldPos = teType.GetMethod("ToWorldPos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mToWorldPosCache[teType] = mToWorldPos;
            }

            if (mToWorldPos == null) 
            {
                // UnityEngine.Debug.LogWarning("[MarkEmptyCars] ProcessTE: mToWorldPos is null for type " + teType.Name);
                return;
            }
            Vector3i pos = (Vector3i)mToWorldPos.Invoke(te, null);

            BlockValue blockValue = world.GetBlock(pos);
            Block block = blockValue.Block;
            if (block == null) return;
            string blockName = block.GetBlockName().ToLower();

            bool isVehicle = false;
            if (!blockName.Contains("shopping") && !blockName.Contains("tilt")) 
            {
                isVehicle = blockName.Contains("cntcar") || blockName.Contains("sedan") || blockName.Contains("truck") ||
                            blockName.Contains("suv") || blockName.Contains("minivan") || blockName.Contains("police") ||
                            blockName.Contains("ambulance") || blockName.Contains("fire") || blockName.Contains("delivery") ||
                            blockName.Contains("tractor") || blockName.Contains("bus");
            }

            // In 7D2D, wrenched cars become 'Damage1' or 'Damage2'. We want to ignore them.
            if (blockName.Contains("damage")) return;

            // Extract IsEmpty
            MethodInfo mIsEmpty;
            if (!mIsEmptyCache.TryGetValue(teType, out mIsEmpty))
            {
                mIsEmpty = teType.GetMethod("IsEmpty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mIsEmptyCache[teType] = mIsEmpty;
            }

            if (mIsEmpty == null)
            {
                System.Collections.Generic.List<System.Collections.IEnumerable> enumerables = new System.Collections.Generic.List<System.Collections.IEnumerable>();

                bool initialized;
                if (!teEnumerableMembersInitialized.TryGetValue(teType, out initialized) || !initialized)
                {
                    var members = new System.Collections.Generic.List<MemberInfo>();
                    
                    foreach (FieldInfo f in teType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(f.FieldType) && f.FieldType != typeof(string))
                            members.Add(f);
                    }

                    foreach (PropertyInfo p in teType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (p.GetIndexParameters().Length == 0 && typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string))
                            members.Add(p);
                    }

                    foreach (MethodInfo m in teType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (m.GetParameters().Length == 0 && typeof(System.Collections.IEnumerable).IsAssignableFrom(m.ReturnType) && m.ReturnType != typeof(string))
                            members.Add(m);
                    }
                    
                    teEnumerableMembersCache[teType] = members;
                    teEnumerableMembersInitialized[teType] = true;
                }

                System.Collections.Generic.List<MemberInfo> cachedMembers;
                if (teEnumerableMembersCache.TryGetValue(teType, out cachedMembers))
                {
                    foreach (MemberInfo member in cachedMembers)
                    {
                        FieldInfo f = member as FieldInfo;
                        if (f != null)
                        {
                            var e = f.GetValue(te) as System.Collections.IEnumerable;
                            if (e != null) enumerables.Add(e);
                        }
                        else
                        {
                            PropertyInfo p = member as PropertyInfo;
                            if (p != null)
                            {
                                var e = p.GetValue(te, null) as System.Collections.IEnumerable;
                                if (e != null) enumerables.Add(e);
                            }
                            else
                            {
                                MethodInfo m = member as MethodInfo;
                                if (m != null)
                                {
                                    try 
                                    {
                                        var e = m.Invoke(te, null) as System.Collections.IEnumerable;
                                        if (e != null) enumerables.Add(e);
                                    } catch { }
                                }
                            }
                        }
                    }
                }

                foreach (System.Collections.IEnumerable enumerable in enumerables)
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null) continue;

                        object featureToCheck = item;
                        if (item.GetType().IsGenericType && item.GetType().GetGenericTypeDefinition() == typeof(System.Collections.Generic.KeyValuePair<,>))
                        {
                            PropertyInfo valProp = item.GetType().GetProperty("Value");
                            if (valProp != null) featureToCheck = valProp.GetValue(item, null);
                        }

                        if (featureToCheck == null) continue;
                        Type featureType = featureToCheck.GetType();

                        MethodInfo fmIsEmpty;
                        if (!mIsEmptyCache.TryGetValue(featureType, out fmIsEmpty))
                        {
                            fmIsEmpty = featureType.GetMethod("IsEmpty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            mIsEmptyCache[featureType] = fmIsEmpty;
                        }

                        if (fmIsEmpty != null)
                        {
                            mIsEmpty = fmIsEmpty;
                            te = featureToCheck; 
                            teType = te.GetType(); // Update teType so lootList and bTouched use the feature object
                            break;
                        }
                    }
                    if (mIsEmpty != null) break;
                }
            }

            if (mIsEmpty == null) return;

            bool isTouched = true;
            FieldInfo fBTouched;
            if (!fBTouchedCache.TryGetValue(teType, out fBTouched))
            {
                fBTouched = teType.GetField("bTouched", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? teType.GetField("Touched", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                fBTouchedCache[teType] = fBTouched;
            }

            if (fBTouched != null)
            {
                isTouched = (bool)fBTouched.GetValue(te);
            }
            else 
            {
                PropertyInfo pIsTouched;
                if (!pIsTouchedCache.TryGetValue(teType, out pIsTouched))
                {
                    pIsTouched = teType.GetProperty("IsTouched", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? teType.GetProperty("bTouched", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    pIsTouchedCache[teType] = pIsTouched;
                }
                if (pIsTouched != null)
                {
                    isTouched = (bool)pIsTouched.GetValue(te, null);
                }
            }

            if (!isTouched) return; // Ignore unlooted containers

            bool isEmpty = (bool)mIsEmpty.Invoke(te, null);
            if (!isEmpty) return; // Ignore full containers

            FieldInfo fLootListName;
            if (!fLootListNameCache.TryGetValue(teType, out fLootListName))
            {
                fLootListName = teType.GetField("lootListName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                fLootListNameCache[teType] = fLootListName;
            }

            string lootList = "";
            if (fLootListName != null)
            {
                lootList = fLootListName.GetValue(te) as string;
                if (lootList == null) lootList = "";
            }
            lootList = lootList.ToLower();

            isVehicle = isVehicle || lootList.Contains("car") || lootList.Contains("vehicle");

            if (!isVehicle) return;

            Vector3 spawnPos = pos.ToVector3();
            emptyCarsPositions.Add(spawnPos);

            if (isVehicle && MarkEmptyCarsMod.DebugMode) 
            {
                UnityEngine.Debug.Log("[MarkEmptyCars] ProcessTE SUCCESS: Added empty vehicle " + blockName + " at " + spawnPos + " to active list.");
            }

            if (!MarkerExists(spawnPos))
            {
                NavObjectManager.Instance.RegisterNavObject(GetActiveMarkerName(), spawnPos, "", false, -1, null);
                EmptyCarDataManager.AddCar(spawnPos);
            }
        }
        catch (Exception ex) { 
            UnityEngine.Debug.LogError("[MarkEmptyCars] ProcessTE Exception: " + ex.Message);
        }
    }

    private static bool MarkerExists(Vector3 spawnPos)
    {
        if (NavObjectManager.Instance == null || NavObjectManager.Instance.NavObjectList == null) return false;

        foreach (NavObject nav in NavObjectManager.Instance.NavObjectList)
        {
            if (nav != null && nav.NavObjectClass != null)
            {
                string navClassName = GetNavClassName(nav.NavObjectClass);

                Vector3 npos = Vector3.zero;
                bool posFound = false;

                PropertyInfo pPos = nav.GetType().GetProperty("TrackedPosition");
                if (pPos != null) { npos = (Vector3)pPos.GetValue(nav, null); posFound = true; }
                else 
                {
                    MethodInfo mGetPos = nav.GetType().GetMethod("GetPosition");
                    if (mGetPos != null) { npos = (Vector3)mGetPos.Invoke(nav, null); posFound = true; }
                    else
                    {
                        FieldInfo fPos = nav.GetType().GetField("position") ?? nav.GetType().GetField("Position") ?? nav.GetType().GetField("TrackedPosition");
                        if (fPos != null) { npos = (Vector3)fPos.GetValue(nav); posFound = true; }
                    }
                }

                bool isOurMarker = false;
                string activeMarkerName = EmptyCarScanner.GetActiveMarkerName();

                if (navClassName == "empty_car_marker")
                {
                    isOurMarker = true;
                }
                else if (navClassName == activeMarkerName && activeMarkerName != "empty_car_marker")
                {
                    if (posFound)
                    {
                        string key = EmptyCarDataManager.GetKey(npos);
                        if (EmptyCarDataManager.EmptyCars.Contains(key))
                        {
                            isOurMarker = true;
                        }
                    }
                }

                if (isOurMarker)
                {
                    if (posFound && Vector3.Distance(npos, spawnPos) < 1f)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static void CleanupNavObjects(System.Collections.Generic.List<Vector3> activeCars)
    {
        if (NavObjectManager.Instance == null || NavObjectManager.Instance.NavObjectList == null) return;

        System.Collections.Generic.List<NavObject> toRemove = new System.Collections.Generic.List<NavObject>();

        foreach (NavObject nav in NavObjectManager.Instance.NavObjectList)
        {
            if (nav != null && nav.NavObjectClass != null)
            {
                string navClassName = GetNavClassName(nav.NavObjectClass);

                Vector3 npos = Vector3.zero;
                bool posFound = false;

                PropertyInfo pPos = nav.GetType().GetProperty("TrackedPosition");
                if (pPos != null) { npos = (Vector3)pPos.GetValue(nav, null); posFound = true; }
                else 
                {
                    MethodInfo mGetPos = nav.GetType().GetMethod("GetPosition");
                    if (mGetPos != null) { npos = (Vector3)mGetPos.Invoke(nav, null); posFound = true; }
                    else
                    {
                        FieldInfo fPos = nav.GetType().GetField("position") ?? nav.GetType().GetField("Position") ?? nav.GetType().GetField("TrackedPosition");
                        if (fPos != null) { npos = (Vector3)fPos.GetValue(nav); posFound = true; }
                    }
                }

                bool isOurMarker = false;
                string activeMarkerName = EmptyCarScanner.GetActiveMarkerName();

                if (navClassName == "empty_car_marker")
                {
                    isOurMarker = true;
                }
                else if (navClassName == activeMarkerName && activeMarkerName != "empty_car_marker")
                {
                    if (posFound)
                    {
                        string key = EmptyCarDataManager.GetKey(npos);
                        if (EmptyCarDataManager.EmptyCars.Contains(key))
                        {
                            isOurMarker = true;
                        }
                    }
                }

                if (isOurMarker)
                {
                    if (!posFound && MarkEmptyCarsMod.DebugMode)
                    {
                        UnityEngine.Debug.LogWarning("[MarkEmptyCars] DIAGNOSTIC: Could not extract position from marker!");
                    }

                    if (posFound)
                    {
                        // Check if this position is in our activeCars list
                        bool found = false;
                        foreach (Vector3 carPos in activeCars)
                        {
                            if (Vector3.Distance(npos, carPos) < 1f)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            if (MarkEmptyCarsMod.DebugMode) UnityEngine.Debug.LogWarning("[MarkEmptyCars] DIAGNOSTIC: Marker at " + npos + " is no longer an empty car. Checking if chunk is loaded...");
                            // It's not in the active empty cars list.
                            // Is the chunk loaded?
                            bool chunkLoaded = false;
                            try 
                            {
                                World world = GameManager.Instance.World;
                                // Fast path: If player is close, chunk is definitely loaded
                                EntityPlayerLocal player = world.GetPrimaryPlayer();
                                if (player != null && Vector3.Distance(player.GetPosition(), npos) < 80f)
                                {
                                    chunkLoaded = true;
                                }
                                else
                                {
                                    int cX = Mathf.FloorToInt(npos.x) >> 4;
                                    int cZ = Mathf.FloorToInt(npos.z) >> 4;

                                    MethodInfo mSync1 = world.ChunkCache.GetType().GetMethod("GetChunkSync", new Type[] { typeof(int), typeof(int) });
                                    if (mSync1 != null)
                                    {
                                        object c = mSync1.Invoke(world.ChunkCache, new object[] { cX, cZ });
                                        chunkLoaded = (c != null);
                                    }
                                    else
                                    {
                                        MethodInfo mSync2 = world.GetType().GetMethod("GetChunkFromWorldPos", new Type[] { typeof(int), typeof(int), typeof(int) });
                                        if (mSync2 != null)
                                        {
                                            object c = mSync2.Invoke(world, new object[] { Mathf.FloorToInt(npos.x), Mathf.FloorToInt(npos.y), Mathf.FloorToInt(npos.z) });
                                            chunkLoaded = (c != null);
                                        }
                                        else
                                        {
                                            // Fallback: If we can't determine, assume loaded so we don't ghost forever if close
                                            chunkLoaded = true; 
                                        }
                                    }
                                }
                            } 
                            catch (Exception ex) { 
                                chunkLoaded = false; 
                                UnityEngine.Debug.LogWarning("[MarkEmptyCars] DIAGNOSTIC: Exception checking chunk loaded: " + ex.Message);
                            }

                            if (MarkEmptyCarsMod.DebugMode) UnityEngine.Debug.LogWarning("[MarkEmptyCars] DIAGNOSTIC: Chunk loaded result for " + npos + " is " + chunkLoaded);

                            if (chunkLoaded)
                            {
                                // The chunk is loaded, but it's not an empty car anymore!
                                toRemove.Add(nav);
                                EmptyCarDataManager.RemoveCar(npos);
                            }
                        }
                    }
                }
            }
        }

        foreach (NavObject obsoleteNav in toRemove)
        {
            try
            {
                // UnRegisterNavObject is required to destroy the UI element.
                MethodInfo[] methods = NavObjectManager.Instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                bool removed = false;
                foreach (MethodInfo m in methods)
                {
                    if (m.Name == "UnRegisterNavObject" && m.GetParameters().Length == 1)
                    {
                        m.Invoke(NavObjectManager.Instance, new object[] { obsoleteNav });
                        removed = true;
                        break;
                    }
                }
                
                if (!removed)
                {
                    UnityEngine.Debug.LogWarning("[MarkEmptyCars] CRITICAL: UnRegisterNavObject method not found! Markers will ghost!");
                }
            }
            catch (Exception ex)
            {
                if (MarkEmptyCarsMod.DebugMode) UnityEngine.Debug.LogWarning("[MarkEmptyCars] Exception during marker removal: " + ex.Message);
            }
            finally
            {
                // Fallback guarantee: remove from list
                if (NavObjectManager.Instance != null && NavObjectManager.Instance.NavObjectList != null)
                {
                    NavObjectManager.Instance.NavObjectList.Remove(obsoleteNav);
                }
            }
        }
    }
}
