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

    [Header("Designer Intent (由生成器动态同步)")]
    public TopologyEvaluator.DesignerIntent currentIntent;

    [Header("Base Difficulty Stats")]
    public float basePredictionWindow = 0.6f;
    public float baseCooldown = 1.5f;

    [Header("Quality of Life")]
    public bool ignoreIdlePlayer = true;
    public float spawnProtectionTime = 2.0f;
    private float runStartTime = 0f;

    [Header("Terrain Trap Settings")]
    public float terrainCooldown = 4.0f;
    private float lastTerrainTime = 0f;

    [Header("The Arsenal")]
    public List<TrapConfig> trapLibrary = new List<TrapConfig>();

    [Header("IWBTG Sculpting Mode")]
    public bool isIWBTGSculptingMode = false;
    private List<Vector3> goldenTrajectory = new List<Vector3>();

    private float lastTrapTime = 0f;
    private bool isRunning = true;
    private List<GameObject> activeTraps = new List<GameObject>();
    private List<KillerMemory> killerHallOfFame = new List<KillerMemory>();
    private HashSet<Vector2i> permanentCrackTriggers = new HashSet<Vector2i>();
    private KillerMemory? currentFrameKiller = null;
    private Vector2i lastCrackTile;
    private float lastCrackTime = -999f;

    // 同步设计师意图
    public void SyncIntent(TopologyEvaluator.DesignerIntent intent)
    {
        currentIntent = intent;
    }

    public void SetRunning(bool state)
    {
        isRunning = state;
        if (isRunning)
        {
            runStartTime = Time.time;
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
    }

    void Update()
    {
        if (!isRunning || targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        // 1. 出生保护期 (受风险紧张感影响，越高保护越短)
        float protectionBuffer = Mathf.Lerp(spawnProtectionTime, 0.5f, currentIntent.riskTension);
        if (Time.time < runStartTime + protectionBuffer) return;

        // 2. 静止保护 (在非雕刻模式下生效)
        if (ignoreIdlePlayer && targetPlayer.mCurrentState != Character.CharacterState.Die && !isIWBTGSculptingMode)
        {
            bool isIdle = targetPlayer.mOnGround && Mathf.Abs(targetPlayer.mSpeed.x) < 0.1f;
            if (isIdle) return;
        }

        // 3. 轨迹偏移硬约束
        if (isIWBTGSculptingMode && targetPlayer.mCurrentState != Character.CharacterState.Die)
        {
            CheckDeviationAndEnforceConstraint();
            return;
        }

        // 清理已销毁的陷阱引用
        for (int i = activeTraps.Count - 1; i >= 0; i--)
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);

        // 4. 生存空间检测
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);
        bool inSafeZone = map.survivalSpaceTiles != null && map.survivalSpaceTiles.Contains(playerTile);

        // 5. 永久地形杀 (无视安全区，由杀手记忆系统触发)
        if (targetPlayer.mOnGround && permanentCrackTriggers.Contains(playerTile))
        {
            if (map.GetTile(playerTile.x, playerTile.y) == TileType.Block)
            {
                TriggerGroundCrackComboAt(playerTile);
                return;
            }
        }

        if (inSafeZone)
        {
            lastTrapTime = Time.time;
            return;
        }

        // 6. 动态陷阱发射
        float effectiveCooldown = Mathf.Lerp(baseCooldown, 0.4f, currentIntent.riskTension);
        float effectivePrediction = Mathf.Lerp(basePredictionWindow, 1.2f, currentIntent.mechanicalComplexity);

        if (Time.time > lastTrapTime + effectiveCooldown)
        {
            AttemptLethalTrap(effectivePrediction);
        }

        // 7. 动态地形裂变概率
        float effectiveTerrainCooldown = Mathf.Lerp(terrainCooldown, 1.5f, currentIntent.riskTension);
        if (Time.time > lastTerrainTime + effectiveTerrainCooldown)
        {
            if (targetPlayer.mOnGround && Random.value < (0.05f + currentIntent.riskTension * 0.2f))
            {
                TriggerGroundCrackCombo();
            }
        }
    }

    private void CheckDeviationAndEnforceConstraint()
    {
        if (goldenTrajectory == null || goldenTrajectory.Count == 0) return;

        float minDistance = float.MaxValue;
        foreach (Vector3 pos in goldenTrajectory)
        {
            float dist = Vector2.Distance(targetPlayer.mPosition, (Vector2)pos);
            if (dist < minDistance) minDistance = dist;
        }

        float dynamicTolerance = Mathf.Lerp(2.5f, 0.8f, currentIntent.riskTension);
        if (minDistance > dynamicTolerance * Map.cTileSize)
        {
            targetPlayer.Die();
        }
    }

    private void AttemptLethalTrap(float prediction)
    {
        if (trapLibrary.Count == 0) return;
        TrapConfig config = GetIntentBiasedTrapConfig();
        List<Vector2> futurePath = SimulatePlayerPath(prediction);
        if (futurePath.Count == 0) return;

        Vector2? spawnPos = CalculateLethalSpawnPosition(config, futurePath);
        if (spawnPos.HasValue)
        {
            Vector2 killZone = futurePath[futurePath.Count - 1];
            SpawnTrap(config, spawnPos.Value, killZone, false, prediction);
            lastTrapTime = Time.time;
        }
    }

    private TrapConfig GetIntentBiasedTrapConfig()
    {
        float totalWeight = 0f;
        foreach (var c in trapLibrary)
        {
            float bias = 1.0f;
            if (currentIntent.mechanicalComplexity > 0.7f && c.strategy == TrapStrategy.SniperIntercept) bias = 3.0f;
            if (currentIntent.riskTension > 0.7f && c.strategy == TrapStrategy.DropFromAbove) bias = 2.0f;
            totalWeight += c.weight * bias;
        }

        float r = Random.Range(0, totalWeight);
        float cur = 0f;
        foreach (var c in trapLibrary)
        {
            float bias = 1.0f;
            if (currentIntent.mechanicalComplexity > 0.7f && c.strategy == TrapStrategy.SniperIntercept) bias = 3.0f;
            if (currentIntent.riskTension > 0.7f && c.strategy == TrapStrategy.DropFromAbove) bias = 2.0f;
            cur += c.weight * bias;
            if (r <= cur) return c;
        }
        return trapLibrary[0];
    }

    private void SpawnTrap(TrapConfig config, Vector2 pos, Vector2 targetZone, bool isPermanent, float prediction)
    {
        GameObject obj = Instantiate(config.prefab, new Vector3(pos.x, pos.y, -5f), Quaternion.identity);
        activeTraps.Add(obj);
        SmartTrap trap = obj.GetComponent<SmartTrap>();
        if (trap != null)
        {
            trap.configName = config.name;
            trap.initialSpawnPosition = pos; // 记录初始位置用于记忆
            trap.ActivateTrap(targetZone, prediction, targetPlayer);
        }
    }

    // 杀手记忆系统接口：当玩家死亡时，陷阱通过此方法自我晋升
    public void RecordKillerTrap(SmartTrap trap)
    {
        currentFrameKiller = new KillerMemory
        {
            configName = trap.configName,
            spawnPos = trap.initialSpawnPosition,
            targetPos = targetPlayer.mPosition,
            isEvent = false
        };
    }

    public void OnPlayerDeath()
    {
        // 晋升本回合最致命的陷阱或地形事件
        if (currentFrameKiller.HasValue)
        {
            killerHallOfFame.Add(currentFrameKiller.Value);
        }
        else if (Time.time - lastCrackTime < 2.5f)
        {
            killerHallOfFame.Add(new KillerMemory { isEvent = true, eventTile = lastCrackTile });
        }

        currentFrameKiller = null;
        ClearTraps();
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
                if (config.prefab != null) SpawnTrap(config, mem.spawnPos, mem.targetPos, true, basePredictionWindow);
            }
        }
    }

    public void ClearTraps() { foreach (var t in activeTraps) if (t != null) Destroy(t); activeTraps.Clear(); }

    // 地形裂变逻辑
    public void TriggerGroundCrackCombo()
    {
        if (map == null) return;
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);
        if (map.GetTile(playerTile.x, playerTile.y) == TileType.Block) TriggerGroundCrackComboAt(playerTile);
    }

    public void TriggerGroundCrackComboAt(Vector2i centerTile)
    {
        lastCrackTile = centerTile;
        lastCrackTime = Time.time;
        int leftWidth = 3, rightWidth = 3, depth = 5;
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

    private Vector2? CalculateLethalSpawnPosition(TrapConfig c, List<Vector2> p)
    {
        if (p == null || p.Count == 0) return targetPlayer.mPosition;
        Vector2 k = p[p.Count - 1];
        if (c.strategy == TrapStrategy.DropFromAbove) return new Vector2(k.x, k.y + Map.cTileSize * 15f);
        if (c.strategy == TrapStrategy.RiseFromBelow) return k + Vector2.down * Map.cTileSize * 15f;
        if (c.strategy == TrapStrategy.SniperIntercept) return targetPlayer.mPosition + new Vector2(Random.value > 0.5f ? 1 : -1, 1).normalized * Map.cTileSize * 20f;
        return k + Vector2.up * Map.cTileSize * 10f;
    }

    private List<Vector2> SimulatePlayerPath(float t)
    {
        List<Vector2> path = new List<Vector2>();
        Vector2 p = targetPlayer.mPosition;
        Vector2 v = targetPlayer.mSpeed;
        float step = 0.02f;
        float timeSimulated = 0f;
        while (timeSimulated < t)
        {
            v.y += -1600f * step;
            p += v * step;
            path.Add(p);
            timeSimulated += step;
        }
        return path;
    }
}