using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum TrapStrategy { DropFromAbove, RiseFromBelow, SniperIntercept, FakeBlockSurprise }

[System.Serializable]
public struct TrapConfig { public string name; public GameObject prefab; public TrapStrategy strategy; [Range(0f, 1f)] public float weight; }

// [新增] 杀手记忆结构
public struct KillerMemory
{
    public string configName;   // 配置名
    public Vector2 spawnPos;    // 生成点
    public Vector2 targetPos;   // 目标点(当时玩家位置)
    public bool isEvent;        // 是否为事件
    public Vector2i eventTile;  // 事件坐标
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

    // 运行时变量
    private float lastTrapTime = 0f;
    private bool isRunning = true;
    private List<GameObject> activeTraps = new List<GameObject>();

    // --- 永久化系统变量 ---
    private List<KillerMemory> killerHallOfFame = new List<KillerMemory>(); // 永久陷阱列表
    private HashSet<Vector2i> permanentCrackTriggers = new HashSet<Vector2i>(); // 永久裂地触发点

    // 临时击杀记录 (本回合谁杀了玩家)
    private KillerMemory? currentFrameKiller = null;
    // 记录最近一次裂地事件，用于判定坠落死
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

        // 1. 清理空对象
        for (int i = activeTraps.Count - 1; i >= 0; i--)
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);

        // 2. 优先检测：永久裂地触发点
        if (targetPlayer.mOnGround)
        {
            Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
            Vector2i currentTile = map.GetMapTileAtPoint(feetPos);

            // 如果踩到了永久触发点，且该点还没被挖空(防止重复触发)
            if (permanentCrackTriggers.Contains(currentTile))
            {
                if (map.GetTile(currentTile.x, currentTile.y) == TileType.Block)
                {
                    Debug.Log("<color=red>Director: 触发永久地形杀！</color>");
                    TriggerGroundCrackComboAt(currentTile);
                    return; // 这一帧处理了永久事件就不做随机了
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
            if (targetPlayer.mOnGround && Random.value < 0.05f)
            {
                TriggerGroundCrackCombo();
            }
        }
    }

    // --- 击杀记录接口 ---

    // 陷阱杀人时调用
    public void RecordKillerTrap(SmartTrap trap)
    {
        KillerMemory memory = new KillerMemory
        {
            configName = trap.configName,
            spawnPos = trap.initialSpawnPosition,
            targetPos = targetPlayer.mPosition, // 记录当时玩家位置作为参考
            isEvent = false
        };
        currentFrameKiller = memory;
        // 注意：这里不立即加入 HallOfFame，等 OnPlayerDeath 结算，防止一尸两命导致重复
    }

    // 玩家死亡时调用 (结算)
    public void OnPlayerDeath()
    {
        bool killerFound = false;

        // 1. 优先判定实体陷阱击杀
        if (currentFrameKiller.HasValue)
        {
            killerHallOfFame.Add(currentFrameKiller.Value);
            killerFound = true;
            Debug.Log($"<color=yellow>Director: 陷阱 [{currentFrameKiller.Value.configName}] 已晋升为永久威胁！</color>");
        }
        // 2. 如果没有实体陷阱，检查是否死于地形裂变 (坠落死)
        else if (Time.time - lastCrackTime < 2.5f) // 如果死前 2.5秒内发生过裂地
        {
            // 判定为地形杀
            KillerMemory eventMem = new KillerMemory
            {
                isEvent = true,
                eventTile = lastCrackTile
            };
            killerHallOfFame.Add(eventMem);
            killerFound = true;
            Debug.Log($"<color=yellow>Director: 地形裂变 [{lastCrackTile}] 已晋升为永久威胁！</color>");
        }

        currentFrameKiller = null;

        if (!killerFound)
        {
            Debug.Log("Director: 玩家死因不明或自然死亡，无新陷阱被记录。");
        }

        // 3. 清理所有临时陷阱 (没杀人的都是垃圾)
        ClearTraps();
    }

    // 重生永久威胁 (由 Map 在重置后调用)
    public void RespawnPermanentThreats()
    {
        // 1. 注册永久事件
        foreach (var mem in killerHallOfFame)
        {
            if (mem.isEvent)
            {
                permanentCrackTriggers.Add(mem.eventTile);
            }
            else
            {
                // 2. 实例化永久陷阱
                TrapConfig config = trapLibrary.Find(x => x.name == mem.configName);
                if (config.prefab == null && trapLibrary.Count > 0) config = trapLibrary[0]; // 保底

                if (config.prefab != null)
                {
                    SpawnTrap(config, mem.spawnPos, mem.targetPos, true);
                }
            }
        }
        Debug.Log($"Director: 已重置 {permanentCrackTriggers.Count} 个地形杀和 {killerHallOfFame.Count - permanentCrackTriggers.Count} 个实体陷阱。");
    }

    // --- 陷阱生成逻辑 ---

    void TriggerGroundCrackCombo()
    {
        if (map == null) return;
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);
        TriggerGroundCrackComboAt(playerTile);
    }

    void TriggerGroundCrackComboAt(Vector2i centerTile)
    {
        if (map.GetTile(centerTile.x, centerTile.y) != TileType.Block) return;

        // 记录这次事件，以防玩家掉下去摔死
        lastCrackTile = centerTile;
        lastCrackTime = Time.time;

        int halfWidth = 3;
        int depth = 4;

        map.ConvertRegionToDynamic(
            new Vector2i(centerTile.x - halfWidth / 2 - 1, centerTile.y - depth / 2 + 1),
            halfWidth, depth, TerrainMotion.SplitHorizontal, -150f
        );

        map.ConvertRegionToDynamic(
            new Vector2i(centerTile.x + halfWidth / 2 + 1, centerTile.y - depth / 2 + 1),
            halfWidth, depth, TerrainMotion.SplitHorizontal, 150f
        );

        lastTerrainTime = Time.time;
        StartCoroutine(SpawnTrapDelay(centerTile, 0.2f));
    }

    IEnumerator SpawnTrapDelay(Vector2i centerTile, float delay)
    {
        yield return new WaitForSeconds(delay);
        TrapConfig spikeConfig = trapLibrary.Find(x => x.strategy == TrapStrategy.RiseFromBelow);
        if (spikeConfig.prefab == null && trapLibrary.Count > 0) spikeConfig = trapLibrary[0];

        if (spikeConfig.prefab != null)
        {
            Vector2 spawnPos = map.GetMapTilePosition(centerTile.x, centerTile.y - 5);
            // 这里的陷阱也是临时的，除非它杀人
            SpawnTrap(spikeConfig, spawnPos, targetPlayer.mPosition, false);
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

    // 生成陷阱通用方法
    void SpawnTrap(TrapConfig config, Vector2 pos, Vector2 targetZone, bool isPermanent)
    {
        GameObject obj = Instantiate(config.prefab, new Vector3(pos.x, pos.y, -5f), Quaternion.identity);
        activeTraps.Add(obj);
        SmartTrap trap = obj.GetComponent<SmartTrap>();
        if (trap != null)
        {
            trap.configName = config.name; // 记名
            // 如果是永久陷阱，行为稍微呆一点(固定)，如果是临时的，要有预判
            // 这里统一处理，都让它们动起来
            trap.ActivateTrap(targetZone, predictionWindow, targetPlayer);
        }
    }

    // ... (辅助方法：GetRandomTrapConfig, CalculateLethalSpawnPosition, SimulatePlayerPath, IsPositionValid, HasWallBetween, ClearTraps)
    // 请保留您原有的这些辅助方法，不做改动 ... 

    // 为了完整性，这里补充 ClearTraps
    public void ClearTraps()
    {
        foreach (var t in activeTraps) if (t != null) Destroy(t);
        activeTraps.Clear();
    }

    // 占位符：请确保下面这些辅助方法存在
    TrapConfig GetRandomTrapConfig()
    {
        float totalWeight = 0f; foreach (var c in trapLibrary) totalWeight += c.weight;
        float r = Random.Range(0, totalWeight); float cur = 0f;
        foreach (var c in trapLibrary) { cur += c.weight; if (r <= cur) return c; }
        return trapLibrary.Count > 0 ? trapLibrary[0] : new TrapConfig();
    }
    Vector2? CalculateLethalSpawnPosition(TrapConfig c, List<Vector2> p)
    {
        // ... (保持您原有的逻辑) ...
        // 简写示意：
        Vector2 k = p[p.Count - 1];
        if (c.strategy == TrapStrategy.DropFromAbove) return new Vector2(k.x, k.y + 150f);
        if (c.strategy == TrapStrategy.RiseFromBelow) return k + Vector2.down * 50f;
        return k + Vector2.left * 200f;
    }
    List<Vector2> SimulatePlayerPath(float t)
    {
        List<Vector2> path = new List<Vector2>();
        Vector2 p = targetPlayer.mPosition; Vector2 v = targetPlayer.mSpeed;
        for (int i = 0; i < 30; i++) { v.y += Constants.cGravity * 0.02f; p += v * 0.02f; path.Add(p); }
        return path;
    }
    bool IsPositionValid(Vector2 p) { return true; }
    bool HasWallBetween(Vector2 s, Vector2 e) { return false; }
}