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

    [Header("Terrain Trap Settings")]
    public float terrainCooldown = 4.0f;
    private float lastTerrainTime = 0f;

    [Header("The Arsenal")]
    public List<TrapConfig> trapLibrary = new List<TrapConfig>();

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
        if (!isRunning) ClearTraps();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log(">>> 手动触发：大地裂变！");
            TriggerGroundCrackCombo();
        }

        if (!isRunning || targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        // 清理空对象
        for (int i = activeTraps.Count - 1; i >= 0; i--)
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);

        // --- 0. 检测玩家是否在生存空间 ---
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);
        bool inSafeZone = map.survivalSpaceTiles.Contains(playerTile);

        // --- 1. 永久地形杀检测 (无视安全区，因为这是玩家自己作死留下的遗产) ---
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

        // --- 2. 安全区逻辑分支 ---
        if (inSafeZone)
        {
            // 在安全区内：休眠，不生成新陷阱
            return;
        }

        // --- 3. 危险区逻辑 (暴走模式) ---
        // 在非安全区，我们使用更激进的冷却时间
        float effectiveCooldown = 0.5f; // 极速攻击

        // 普通陷阱
        if (Time.time > lastTrapTime + effectiveCooldown)
        {
            AttemptLethalTrap();
        }

        // 地形变动 (仍然保持一定的节奏，否则地面全裂没了)
        if (Time.time > lastTerrainTime + terrainCooldown)
        {
            if (targetPlayer.mOnGround && Random.value < 0.1f) // 提高触发概率
            {
                TriggerGroundCrackCombo();
            }
        }
    }

    // ... (以下所有方法保持不变：TriggerGroundCrackCombo, RecordKillerTrap, OnPlayerDeath 等) ...
    // ... 请保留原有的其他逻辑代码，不要删除 ...

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
    TrapConfig GetRandomTrapConfig() { float totalWeight = 0f; foreach (var c in trapLibrary) totalWeight += c.weight; float r = Random.Range(0, totalWeight); float cur = 0f; foreach (var c in trapLibrary) { cur += c.weight; if (r <= cur) return c; } return trapLibrary.Count > 0 ? trapLibrary[0] : new TrapConfig(); }
    Vector2? CalculateLethalSpawnPosition(TrapConfig c, List<Vector2> p) { Vector2 k = p[p.Count - 1]; if (c.strategy == TrapStrategy.DropFromAbove) return new Vector2(k.x, k.y + 150f); if (c.strategy == TrapStrategy.RiseFromBelow) return k + Vector2.down * 50f; return k + Vector2.left * 200f; }
    List<Vector2> SimulatePlayerPath(float t) { List<Vector2> path = new List<Vector2>(); Vector2 p = targetPlayer.mPosition; Vector2 v = targetPlayer.mSpeed; for (int i = 0; i < 30; i++) { if (!targetPlayer.mOnGround) v.y += Constants.cGravity * 0.02f; p += v * 0.02f; path.Add(p); } return path; }
    bool IsPositionValid(Vector2 p) { return true; }
    bool HasWallBetween(Vector2 s, Vector2 e) { return false; }
}