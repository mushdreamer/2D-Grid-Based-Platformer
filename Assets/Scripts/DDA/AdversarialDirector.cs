using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum TrapStrategy { DropFromAbove, RiseFromBelow, SniperIntercept, FakeBlockSurprise }

[System.Serializable]
public struct TrapConfig { public string name; public GameObject prefab; public TrapStrategy strategy; [Range(0f, 1f)] public float weight; }

[System.Serializable]
public struct KillerMemory
{
    public string configName;
    public Vector2 spawnPos;
    public Vector2 targetPos;
    public bool isEvent;
    public Vector2i eventTile;
}

public class AdversarialDirector : MonoBehaviour
{
    public Bot targetPlayer;
    public Map map;

    [Header("Difficulty Brain")]
    public float predictionWindow = 0.6f;
    public float cooldown = 1.5f;

    [Header("Quality of Life (体验优化)")]
    public bool ignoreIdlePlayer = true;      // 是否忽略静止的玩家
    public float spawnProtectionTime = 2.0f;  // 出生无敌保护时间（秒）
    private float runStartTime = 0f;          // 记录导演开始运行的时间

    [Header("Terrain Trap Settings")]
    public float terrainCooldown = 4.0f;
    private float lastTerrainTime = 0f;

    [Header("The Arsenal")]
    public List<TrapConfig> trapLibrary = new List<TrapConfig>();

    [Header("IWBTG Sculpting Mode")]
    public bool isIWBTGSculptingMode = false;
    public float deviationTolerance = 1.5f; // 允许偏离基准轨迹的最大距离约束
    private List<Vector3> goldenTrajectory = new List<Vector3>();

    private float lastTrapTime = 0f;
    private bool isRunning = true;
    private List<GameObject> activeTraps = new List<GameObject>();

    private List<KillerMemory> killerHallOfFame = new List<KillerMemory>();
    private HashSet<Vector2i> permanentCrackTriggers = new HashSet<Vector2i>();

    private KillerMemory? currentFrameKiller = null;
    private Vector2i lastCrackTile;
    private float lastCrackTime = -999f;

    public void SetRunning(bool state)
    {
        isRunning = state;
        if (isRunning)
        {
            runStartTime = Time.time; // 启动时记录时间，开启出生保护
            lastTrapTime = Time.time;
            lastTerrainTime = Time.time;
        }
        else
        {
            ClearTraps();
        }
    }

    public void StartIWBTGSculpting(List<Vector3> intendedPath)
    {
        goldenTrajectory = new List<Vector3>(intendedPath);
        isIWBTGSculptingMode = true;
        Debug.Log(">>> 导演已进入 IWBTG 雕刻模式，开始执行空间约束闭环。");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log(">>> 手动触发：大地裂变！");
            TriggerGroundCrackCombo();
        }

        if (!isRunning || targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        // --- 1. 出生保护期 ---
        if (Time.time < runStartTime + spawnProtectionTime)
        {
            lastTrapTime = Time.time;
            lastTerrainTime = Time.time;
            return;
        }

        // --- 2. 静止摸鱼保护 ---
        if (ignoreIdlePlayer && targetPlayer.mCurrentState != Character.CharacterState.Die && !isIWBTGSculptingMode)
        {
            bool isIdle = targetPlayer.mOnGround && Mathf.Abs(targetPlayer.mSpeed.x) < 0.1f;
            if (isIdle)
            {
                lastTrapTime = Time.time;
                lastTerrainTime = Time.time;
                return;
            }
        }

        if (isIWBTGSculptingMode && targetPlayer.mCurrentState != Character.CharacterState.Die)
        {
            CheckDeviationAndEnforceConstraint();
            return;
        }

        for (int i = activeTraps.Count - 1; i >= 0; i--)
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);

        // --- 3. 检测玩家是否在生存空间 ---
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);
        bool inSafeZone = false;
        if (map.survivalSpaceTiles != null)
        {
            inSafeZone = map.survivalSpaceTiles.Contains(playerTile);
        }

        // --- 4. 永久地形杀检测 (无视安全区) ---
        if (targetPlayer.mOnGround)
        {
            if (permanentCrackTriggers.Contains(playerTile))
            {
                if (map.GetTile(playerTile.x, playerTile.y) == TileType.Block)
                {
                    Debug.Log("<color=red>Director: 触发永久地形杀！</color>");
                    TriggerGroundCrackComboAt(playerTile);
                    return;
                }
            }
        }

        // --- 5. 安全区逻辑分支 ---
        if (inSafeZone)
        {
            lastTrapTime = Time.time;
            return;
        }

        // --- 6. 危险区逻辑 (暴走模式) ---
        float effectiveCooldown = 0.5f;

        if (Time.time > lastTrapTime + effectiveCooldown)
        {
            AttemptLethalTrap();
        }

        if (Time.time > lastTerrainTime + terrainCooldown)
        {
            if (targetPlayer.mOnGround && Random.value < 0.1f)
            {
                TriggerGroundCrackCombo();
            }
        }
    }

    void CheckDeviationAndEnforceConstraint()
    {
        if (goldenTrajectory == null || goldenTrajectory.Count == 0) return;

        float minDistance = float.MaxValue;
        foreach (Vector3 pos in goldenTrajectory)
        {
            float dist = Vector2.Distance(targetPlayer.mPosition, (Vector2)pos);
            if (dist < minDistance) minDistance = dist;
        }

        if (minDistance > deviationTolerance * Map.cTileSize)
        {
            Debug.Log($"<color=magenta>Director: 发现轨迹违规偏移 (偏差 {minDistance:F2})，执行硬约束抹杀。</color>");

            TrapConfig executionConfig = trapLibrary.Find(x => x.name == "AbyssSpike");
            if (executionConfig.prefab == null && trapLibrary.Count > 0) executionConfig = trapLibrary[0];

            if (executionConfig.prefab != null)
            {
                Vector2 killZone = targetPlayer.mPosition;
                SpawnTrap(executionConfig, killZone + Vector2.up * 50f, killZone, true);

                KillerMemory mem = new KillerMemory { configName = executionConfig.name, spawnPos = killZone + Vector2.up * 50f, targetPos = killZone, isEvent = false };
                killerHallOfFame.Add(mem);

                targetPlayer.Die();
            }
        }
    }

    void TriggerGroundCrackCombo()
    {
        if (map == null) return;
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);
        if (map.GetTile(playerTile.x, playerTile.y) == TileType.Block) TriggerGroundCrackComboAt(playerTile);
    }

    void TriggerGroundCrackComboAt(Vector2i centerTile)
    {
        lastCrackTile = centerTile;
        lastCrackTime = Time.time;
        int leftWidth = 3; int rightWidth = 3; int depth = 5;
        Vector2i leftCenter = new Vector2i(centerTile.x - 1, centerTile.y - depth / 2 + 1);
        map.ConvertRegionToDynamic(leftCenter, leftWidth, depth, TerrainMotion.SplitHorizontal, -180f);
        Vector2i rightCenter = new Vector2i(centerTile.x + 2, centerTile.y - depth / 2 + 1);
        map.ConvertRegionToDynamic(rightCenter, rightWidth, depth, TerrainMotion.SplitHorizontal, 180f);
        lastTerrainTime = Time.time;
        StartCoroutine(SpawnAbyssSpikes(centerTile, depth));
    }

    IEnumerator SpawnAbyssSpikes(Vector2i centerTile, int depth)
    {
        yield return new WaitForSeconds(0.15f);
        if (map.spikePrefab != null)
        {
            int yPos = centerTile.y - depth;
            for (int x = centerTile.x - 1; x <= centerTile.x + 1; x++)
            {
                Vector2 spawnPos = map.GetMapTilePosition(x, yPos);
                GameObject spikeObj = Instantiate(map.spikePrefab, new Vector3(spawnPos.x, spawnPos.y, -5f), Quaternion.identity);
                activeTraps.Add(spikeObj);
                SmartTrap trap = spikeObj.GetComponent<SmartTrap>();
                if (trap == null) trap = spikeObj.AddComponent<SmartTrap>();
                trap.behaviorType = TrapBehaviorType.Static;
                trap.configName = "AbyssSpike";
                trap.ActivateTrap(Vector2.zero, 0, targetPlayer);
            }
        }
    }

    public void RecordKillerTrap(SmartTrap trap)
    {
        KillerMemory memory = new KillerMemory { configName = trap.configName, spawnPos = trap.initialSpawnPosition, targetPos = targetPlayer.mPosition, isEvent = false };
        currentFrameKiller = memory;
    }

    public void OnPlayerDeath()
    {
        bool killerFound = false;
        if (currentFrameKiller.HasValue) { killerHallOfFame.Add(currentFrameKiller.Value); killerFound = true; Debug.Log($"<color=yellow>Director: 陷阱 [{currentFrameKiller.Value.configName}] 已晋升！</color>"); }
        else if (Time.time - lastCrackTime < 2.5f) { KillerMemory eventMem = new KillerMemory { isEvent = true, eventTile = lastCrackTile }; killerHallOfFame.Add(eventMem); killerFound = true; Debug.Log($"<color=yellow>Director: 地形裂变 [{lastCrackTile}] 已晋升！</color>"); }
        currentFrameKiller = null; ClearTraps();
    }

    public void RespawnPermanentThreats()
    {
        foreach (var mem in killerHallOfFame)
        {
            if (mem.isEvent) { permanentCrackTriggers.Add(mem.eventTile); }
            else
            {
                if (mem.configName == "AbyssSpike") continue;
                TrapConfig config = trapLibrary.Find(x => x.name == mem.configName);
                if (config.prefab == null && trapLibrary.Count > 0) config = trapLibrary[0];
                if (config.prefab != null) SpawnTrap(config, mem.spawnPos, mem.targetPos, true);
            }
        }
    }

    void AttemptLethalTrap()
    {
        if (trapLibrary.Count == 0) return;
        TrapConfig config = GetRandomTrapConfig();
        List<Vector2> futurePath = SimulatePlayerPath(predictionWindow);
        if (futurePath.Count == 0) return;
        Vector2? spawnPos = CalculateLethalSpawnPosition(config, futurePath);
        if (spawnPos.HasValue)
        {
            Vector2 killZone = futurePath[futurePath.Count - 1];
            SpawnTrap(config, spawnPos.Value, killZone, false);
            lastTrapTime = Time.time;
        }
    }

    void SpawnTrap(TrapConfig config, Vector2 pos, Vector2 targetZone, bool isPermanent)
    {
        GameObject obj = Instantiate(config.prefab, new Vector3(pos.x, pos.y, -5f), Quaternion.identity);
        activeTraps.Add(obj);
        SmartTrap trap = obj.GetComponent<SmartTrap>();
        if (trap != null) { trap.configName = config.name; trap.ActivateTrap(targetZone, predictionWindow, targetPlayer); }
    }

    public void ClearTraps() { foreach (var t in activeTraps) if (t != null) Destroy(t); activeTraps.Clear(); }

    TrapConfig GetRandomTrapConfig()
    {
        float totalWeight = 0f;
        foreach (var c in trapLibrary) totalWeight += c.weight;
        if (totalWeight <= 0f) return trapLibrary.Count > 0 ? trapLibrary[0] : new TrapConfig();
        float r = Random.Range(0, totalWeight);
        float cur = 0f;
        foreach (var c in trapLibrary) { cur += c.weight; if (r <= cur) return c; }
        return trapLibrary.Count > 0 ? trapLibrary[0] : new TrapConfig();
    }

    Vector2? CalculateLethalSpawnPosition(TrapConfig c, List<Vector2> p)
    {
        if (p == null || p.Count == 0) return targetPlayer.mPosition;
        Vector2 k = p[p.Count - 1];
        if (c.strategy == TrapStrategy.DropFromAbove) return new Vector2(k.x, k.y + Map.cTileSize * 15f);
        if (c.strategy == TrapStrategy.RiseFromBelow) return k + Vector2.down * Map.cTileSize * 15f;
        if (c.strategy == TrapStrategy.SniperIntercept) return targetPlayer.mPosition + new Vector2(Random.value > 0.5f ? 1 : -1, 1).normalized * Map.cTileSize * 20f;
        if (c.strategy == TrapStrategy.FakeBlockSurprise) return k;
        return k + Vector2.up * Map.cTileSize * 10f;
    }

    List<Vector2> SimulatePlayerPath(float t)
    {
        List<Vector2> path = new List<Vector2>();
        Vector2 p = targetPlayer.mPosition;
        Vector2 v = targetPlayer.mSpeed;
        float step = 0.02f;
        float timeSimulated = 0f;
        while (timeSimulated < t)
        {
            v.y += Constants.cGravity * step;
            p += v * step;
            path.Add(p);
            timeSimulated += step;
        }
        return path;
    }

    bool IsPositionValid(Vector2 p) { return true; }
    bool HasWallBetween(Vector2 s, Vector2 e) { return false; }
}