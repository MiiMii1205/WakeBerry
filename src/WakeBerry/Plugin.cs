using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Peak;
using Peak.Afflictions;
using PEAKLib.Core;
using PEAKLib.Core.Extensions;
using Photon.Pun;
using pworld.Scripts.Extensions;
using UnityEngine;
using UnityEngine.SceneManagement;
using WakeBerry.Behaviours;
using Random = UnityEngine.Random;

namespace WakeBerry;

[BepInAutoPlugin]
[BepInDependency(CorePlugin.Id)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    public static GameObject WakeBerrySpawnPrefab { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;

        AddLocalizedTextCsv();

        this.LoadBundleWithName("wakeberry.peakbundle", LoadWakeberry);

        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (scene.name.StartsWith("Level_") || scene.name.Equals("WilIsland"))
        {
            if (MapHandler.Instance != null && 
                MapHandler.Instance.BiomeIsPresent(Biome.BiomeType.Mesa) &&
                MapHandler.Instance.gameObject.TryGetComponent<LevelGeneration>(out var gens))
            {
                Log.LogInfo("GENERATING WAKEBERRY SEEDS!!!");
                
                GenerateSeedStates(gens);

                var prevState = Random.state;

                foreach (var ms in MapHandler.Instance.segments)
                {
                    if (ms.biome is Biome.BiomeType.Mesa)
                    {
                        // Replace Dynamite spawners

                        Random.state = BiomeSeeds[ms._biome];

                        var baseBiomeObject = ms.segmentParent.transform.parent.parent;
                        var berryPositions = new List<Vector3>();
                        var singleItemSpawns = baseBiomeObject.GetComponentsInChildren<SingleItemSpawner>(true);

                        int nbOfBerries = 0;
                        
                        foreach (var sis in singleItemSpawns)
                        {
                            if (sis.prefab.gameObject.TryGetComponent<Dynamite>(out _))
                            {
                                if (IsFarEnoughFromOthers(sis.transform.position, berryPositions) && IsEligible(sis.transform) && IsLucky(0.05f))
                                {
                                    Log.LogDebug($"Placing a wakeberry spawn @ ({sis.transform.position})");
                                    
                                    var sisTransform = sis.transform;
                                    var parent = sis.transform.parent;

                                    var bush = GameObject.Instantiate(WakeBerrySpawnPrefab, parent, true);

                                    bush.transform.SetPositionAndRotation(sisTransform.position, sisTransform.rotation);

                                    Destroy(sis.gameObject);

                                    // Manage fakes items. Hopefully the index stays the same, yeah?

                                    var idx = FakeItemManager.Instance.GetAvailableIndex();
                                    var orb = bush.GetComponent<OrbThatMakesYouAwake>();

                                    if (idx >= FakeItemManager.Instance.allFakeItems.Count)
                                    {
                                        idx = FakeItemManager.Instance.allFakeItems.Count;
                                        FakeItemManager.Instance.allFakeItems.Add(orb.fakeBerry);
                                    }
                                    else
                                    {
                                        FakeItemManager.Instance.allFakeItems[idx] = orb.fakeBerry;
                                    }

                                    orb.fakeBerry.index = idx;
                                    Log.LogDebug(
                                        $"Wakeberry #{nbOfBerries + 1}'s fake item IDX is {orb.fakeBerry.index}!");

                                    berryPositions.Add(bush.transform.position);

                                    nbOfBerries++;
                                    
                                    Log.LogDebug($"Placed ({nbOfBerries}/5) wakeberries spawn");
                                }
                            }

                            if (nbOfBerries >= 5)
                            {
                                Log.LogInfo($"Placed the last possible berry bush! ({nbOfBerries} berries total)");
                                break;
                            }
                        }

                        // FakeItemManager.Instance.RefreshList();
                        BiomeSeeds[ms._biome] = Random.state;
                    }
                }

                // Reinstante the state
                Random.state = prevState;
            }
            else
            {
                Log.LogInfo($"Mesa was not active this map. No need to generate wake berries.");
            }
        }
    }

    private bool IsEligible(Transform sisTransform)
    {
        var isEligible = sisTransform.parent.parent.name is not "mineshaft";

        if (!isEligible)
        {
            Log.LogDebug($"{sisTransform.gameObject.name} is a mineshaft dynamite, can't make it a wakeberry spawn." );
        }
        
        return isEligible;
    }

    private bool IsFarEnoughFromOthers(Vector3 transformPosition, List<Vector3> berryPos)
    {
        foreach (var pos in berryPos)
        {
            var distance = Vector3.Distance(pos, transformPosition);
            
            if (distance < 10f)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsLucky(float baseChance = 0.025f)
    {
        return Random.value < baseChance;
    }

    private static readonly TimeSpan CycleLength = new(208, 0, 0, 0);
    private static readonly DateTime ConsoleUpdateReleaseDay = new DateTime(2026, 10, 22).Date;
    private static readonly Dictionary<Biome.BiomeType, Random.State> BiomeSeeds = new();
    private static readonly int FirstConsoleUpdateReleaseLevel = 79;

    public const string CreationTimeKey = "RoomCreationTime";

    private void GenerateSeedStates(LevelGeneration gens)
    {
        var prevState = Random.state;

        DateTime now;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CreationTimeKey, out var startTimeOb) &&
            startTimeOb is long o)
        {
            now = new DateTime(o, DateTimeKind.Utc).Date;

            Log.LogDebug(
                $"Retrieved {CreationTimeKey}: ({now:m})");
        }
        else
        {
            now = DateTime.UtcNow.Date;

            Log.LogDebug(
                $"No {CreationTimeKey}. Fallback to ({now:m})");
        }

        // This day predates the console release day because the cycle started at Level_79
        var firstLevelDay = ConsoleUpdateReleaseDay.AddDays(-FirstConsoleUpdateReleaseLevel);

        var nbOfDays = now.Date - firstLevelDay;

        // We might lose precisons in a couple of thousand of years
        var noOfCyclesRaw = (nbOfDays.Ticks / (float) CycleLength.Ticks);

        var nbOfCycles = Mathf.FloorToInt(noOfCyclesRaw);

        var cyclicSeed = gens.seed + nbOfCycles;

        Log.LogDebug(
            $"PEAK is on cycle #{nbOfCycles + 1}... (original seed:{gens.seed}, cycle-corrected seed: {gens.seed} + {nbOfCycles} = {cyclicSeed})");

        // Generate the master seed;
        Random.InitState(cyclicSeed);

        // Subseeds 

        foreach (Biome.BiomeType v in Enum.GetValues(typeof(Biome.BiomeType)))
        {
            var seed = Random.Range(int.MinValue, int.MaxValue);
            var mainState = Random.state;
            Random.InitState(seed);
            BiomeSeeds[v] = Random.state;
            Random.state = mainState;
        }

        Random.state = prevState;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void AddLocalizedTextCsv()
    {
        using var reader = new StreamReader(Path.Join(Path.GetDirectoryName(Info.Location),
            "WakeberryLocalizedText.csv"));

        var currentLine = 0;

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();

            if (line == null)
            {
                break;
            }

            currentLine++;

            var valList = new List<string>(CSVReader.SplitCsvLine(line));

            var locName = valList.Deque();

            var endline = valList.Pop();

            if (endline != "ENDLINE")
            {
                Log.LogError($"Invalid localization at line {currentLine}");
            }

            if (locName != "CURRENT_LANGUAGE")
            {
                LocalizedText.mainTable[locName] = valList;
                Log.LogDebug($"Added localization of {locName}");
            }
        }

        Log.LogDebug($"Added {currentLine - 1} localizations");
    }

    private void LoadWakeberry(PeakBundle b)
    {
        var wakeberry = b.LoadAsset<GameObject>("Wakeberry.prefab");

        var hcto = wakeberry.GetOrAddComponent<ItemOptimizer>();
        hcto.item = wakeberry.GetComponent<Item>();

        wakeberry.GetOrAddComponent<PhotonCleanupHelper>();

        var wakeup = wakeberry.AddComponent<Action_WakeUp>();

        wakeup.OnConsumed = true;
        wakeup.m_extraStamAmountIfAwake = 0.175f;

        wakeup.m_toGiveIfAwake = new Affliction_FasterBoi()
        {
            cachedDrowsy = 0f,
            climbDelay = 3,
            climbSpeedMod = 1.5f,
            drowsyOnEnd = 0f,
            moveSpeedMod = 0.5f,
            totalTime = 16f,
        };

        ShaderExtensions.ReplaceShaders(wakeberry);
        
        Log.LogInfo("Loaded wakeberry item!");

        var wakeberrySpawn = b.LoadAsset<GameObject>("WakeBerrySpawn.prefab");

        var obtmys = wakeberrySpawn.GetComponent<OrbThatMakesYouSleepy>();
        var obtmyh = wakeberrySpawn.AddComponent<OrbThatMakesYouAwake>();

        obtmyh.ambientParticles = obtmys.ambientParticles;
        obtmyh.orb = obtmys.orb;
        obtmyh.maxDistance = obtmys.maxDistance;
        obtmyh.minHotPerSecond = obtmys.minDrowsyPerSecond;
        obtmyh.maxHotPerSecond = obtmys.maxDrowsyPerSecond;

        obtmyh.minHotPerTick = obtmys.minDrowsyPerTick;
        obtmyh.maxHotPerTick = obtmys.maxDrowsyPerTick;

        obtmyh.particle = obtmys.particle;
        obtmyh.anim = obtmys.anim;

        obtmyh.animSpeed = obtmys.animSpeed;
        obtmyh.fakeBerry = obtmys.fakeBerry;

        DestroyImmediate(obtmys);

        ShaderExtensions.ReplaceShaders(wakeberrySpawn);

        obtmyh.fakeBerry.realItemPrefab = wakeberry.GetComponent<Item>();

        WakeBerrySpawnPrefab = wakeberrySpawn;
        
        Log.LogInfo("Loaded wakeberry spawn prefab!");

        b.Mod.RegisterContent();

        Log.LogInfo("Wakeberry is loaded!");
    }
}