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

    // 永久化记忆
    private List<KillerMemory> killerHallOfFame = new List<KillerMemory>();
    private HashSet<Vector2i> permanentCrackTriggers = new HashSet<Vector2i>();

    // 临时击杀记录
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

        // 1. 清理已销毁对象
        for (int i = activeTraps.Count - 1; i >= 0; i--)
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);

        // 2. 检测永久裂地触发点
        if (targetPlayer.mOnGround)
        {
            Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
            Vector2i currentTile = map.GetMapTileAtPoint(feetPos);

            if (permanentCrackTriggers.Contains(currentTile))
            {
                // 只有当地面还是实心的时候才触发（防止重复触发）
                if (map.GetTile(currentTile.x, currentTile.y) == TileType.Block)
                {
                    Debug.Log("<color=red>Director: 触发永久地形杀！</color>");
                    TriggerGroundCrackComboAt(currentTile);
                    return;
                }
            }
        }

        // 3. 随机普通陷阱
        if (Time.time > lastTrapTime + cooldown)
        {
            if (targetPlayer.mSpeed.magnitude > 5f || Random.value < 0.05f)
            {
                AttemptLethalTrap();
            }
        }

        // 4. 随机地形变动
        if (Time.time > lastTerrainTime + terrainCooldown)
        {
            // 只要在地上，就有小概率触发
            if (targetPlayer.mOnGround && Random.value < 0.05f)
            {
                TriggerGroundCrackCombo();
            }
        }
    }

    // ----------------------------------------------------
    //  地形连招系统 (Improved Logic)
    // ----------------------------------------------------

    void TriggerGroundCrackCombo()
    {
        if (map == null) return;
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);

        // 确保脚下有地
        if (map.GetTile(playerTile.x, playerTile.y) == TileType.Block)
        {
            TriggerGroundCrackComboAt(playerTile);
        }
    }

    // 在指定位置触发裂地 + 深渊刺
    void TriggerGroundCrackComboAt(Vector2i centerTile)
    {
        lastCrackTile = centerTile;
        lastCrackTime = Time.time;

        // 目标：制造一个 gap，让 centerTile 处的砖块飞走
        // 左半块：包含 centerTile，向左飞
        // 右半块：从 centerTile+1 开始，向右飞

        int leftWidth = 3;
        int rightWidth = 3;
        int depth = 5; // 深度，确保挖穿

        // 计算左半块的中心点
        // 范围 [centerTile.x - 2, centerTile.x]
        // 中心 x = centerTile.x - 1
        Vector2i leftCenter = new Vector2i(centerTile.x - 1, centerTile.y - depth / 2 + 1);

        map.ConvertRegionToDynamic(
            leftCenter,
            leftWidth, depth,
            TerrainMotion.SplitHorizontal, -180f // 向左
        );

        // 计算右半块的中心点
        // 范围 [centerTile.x + 1, centerTile.x + 3]
        // 中心 x = centerTile.x + 2
        Vector2i rightCenter = new Vector2i(centerTile.x + 2, centerTile.y - depth / 2 + 1);

        map.ConvertRegionToDynamic(
            rightCenter,
            rightWidth, depth,
            TerrainMotion.SplitHorizontal, 180f // 向右
        );

        lastTerrainTime = Time.time;

        // [连携] 在坑底生成刺
        StartCoroutine(SpawnAbyssSpikes(centerTile, depth));
    }

    // 在深渊底部生成一排静态刺
    IEnumerator SpawnAbyssSpikes(Vector2i centerTile, int depth)
    {
        yield return new WaitForSeconds(0.15f); // 稍微等地板滑开

        if (map.spikePrefab != null)
        {
            // 在坑底覆盖一排刺
            int yPos = centerTile.y - depth; // 坑的最底下

            // 覆盖 x-1 到 x+1 的范围
            for (int x = centerTile.x - 1; x <= centerTile.x + 1; x++)
            {
                Vector2 spawnPos = map.GetMapTilePosition(x, yPos);

                GameObject spikeObj = Instantiate(map.spikePrefab, new Vector3(spawnPos.x, spawnPos.y, -5f), Quaternion.identity);
                activeTraps.Add(spikeObj); // 加入列表以便清理

                SmartTrap trap = spikeObj.GetComponent<SmartTrap>();
                if (trap == null) trap = spikeObj.AddComponent<SmartTrap>();

                // 设置为静态深渊刺
                trap.behaviorType = TrapBehaviorType.Static;
                trap.configName = "AbyssSpike"; // 特殊标记

                // 激活
                trap.ActivateTrap(Vector2.zero, 0, targetPlayer);
            }
        }
    }

    // ----------------------------------------------------
    //  记录与重生系统
    // ----------------------------------------------------

    public void RecordKillerTrap(SmartTrap trap)
    {
        KillerMemory memory = new KillerMemory
        {
            configName = trap.configName,
            spawnPos = trap.initialSpawnPosition,
            targetPos = targetPlayer.mPosition,
            isEvent = false
        };
        currentFrameKiller = memory;
    }

    public void OnPlayerDeath()
    {
        bool killerFound = false;

        // 1. 实体陷阱击杀
        if (currentFrameKiller.HasValue)
        {
            killerHallOfFame.Add(currentFrameKiller.Value);
            killerFound = true;
            Debug.Log($"<color=yellow>Director: 陷阱 [{currentFrameKiller.Value.configName}] 已晋升！</color>");
        }
        // 2. 地形杀 (坠落)
        else if (Time.time - lastCrackTime < 2.5f)
        {
            KillerMemory eventMem = new KillerMemory { isEvent = true, eventTile = lastCrackTile };
            killerHallOfFame.Add(eventMem);
            killerFound = true;
            Debug.Log($"<color=yellow>Director: 地形裂变 [{lastCrackTile}] 已晋升！</color>");
        }

        currentFrameKiller = null;
        ClearTraps();
    }

    public void RespawnPermanentThreats()
    {
        foreach (var mem in killerHallOfFame)
        {
            if (mem.isEvent)
            {
                permanentCrackTriggers.Add(mem.eventTile);
            }
            else
            {
                // 如果是深渊刺，不在这里重生，而是由裂地事件触发
                if (mem.configName == "AbyssSpike") continue;

                // 普通陷阱重生
                TrapConfig config = trapLibrary.Find(x => x.name == mem.configName);
                if (config.prefab == null && trapLibrary.Count > 0) config = trapLibrary[0];

                if (config.prefab != null)
                {
                    SpawnTrap(config, mem.spawnPos, mem.targetPos, true);
                }
            }
        }
        Debug.Log($"Director: 重生了 {permanentCrackTriggers.Count} 个必死地形点。");
    }

    // ----------------------------------------------------
    //  常规辅助方法
    // ----------------------------------------------------

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
        if (trap != null)
        {
            trap.configName = config.name;
            trap.ActivateTrap(targetZone, predictionWindow, targetPlayer);
        }
    }

    public void ClearTraps()
    {
        foreach (var t in activeTraps) if (t != null) Destroy(t);
        activeTraps.Clear();
    }

    // 辅助计算方法
    TrapConfig GetRandomTrapConfig()
    {
        float totalWeight = 0f; foreach (var c in trapLibrary) totalWeight += c.weight;
        float r = Random.Range(0, totalWeight); float cur = 0f;
        foreach (var c in trapLibrary) { cur += c.weight; if (r <= cur) return c; }
        return trapLibrary.Count > 0 ? trapLibrary[0] : new TrapConfig();
    }

    Vector2? CalculateLethalSpawnPosition(TrapConfig c, List<Vector2> p)
    {
        Vector2 k = p[p.Count - 1];
        if (c.strategy == TrapStrategy.DropFromAbove) return new Vector2(k.x, k.y + 150f);
        if (c.strategy == TrapStrategy.RiseFromBelow) return k + Vector2.down * 50f;
        // 狙击逻辑略
        return k + Vector2.left * 200f;
    }

    List<Vector2> SimulatePlayerPath(float t)
    {
        List<Vector2> path = new List<Vector2>();
        Vector2 p = targetPlayer.mPosition; Vector2 v = targetPlayer.mSpeed;
        for (int i = 0; i < 30; i++)
        {
            if (!targetPlayer.mOnGround) v.y += Constants.cGravity * 0.02f;
            p += v * 0.02f; path.Add(p);
        }
        return path;
    }
}