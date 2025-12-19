using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 确保这两个定义在类外面
public enum TrapStrategy { DropFromAbove, RiseFromBelow, SniperIntercept, FakeBlockSurprise }
[System.Serializable]
public struct TrapConfig { public string name; public GameObject prefab; public TrapStrategy strategy; [Range(0f, 1f)] public float weight; }

public class AdversarialDirector : MonoBehaviour
{
    public Bot targetPlayer;
    public Map map;

    [Header("Difficulty Brain")]
    public float predictionWindow = 0.6f;
    public float cooldown = 1.5f;

    [Header("Terrain Trap Settings")]
    public float terrainCooldown = 4.0f; // 地形变动冷却 (稍微缩短一点，让您更容易遇到)
    private float lastTerrainTime = 0f;

    [Header("The Arsenal")]
    public List<TrapConfig> trapLibrary = new List<TrapConfig>();

    private float lastTrapTime = 0f;
    private bool isRunning = true;
    private List<GameObject> activeTraps = new List<GameObject>();

    public void SetRunning(bool state)
    {
        isRunning = state;
        if (!isRunning) ClearTraps();
    }

    void Update()
    {
        // 强制测试按键：按 T 键直接触发裂地
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log(">>> 手动触发：大地裂变！");
            TriggerGroundCrackCombo();
        }

        if (!isRunning || targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        // 清理失效对象
        for (int i = activeTraps.Count - 1; i >= 0; i--)
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);

        // --- 1. 普通陷阱逻辑 ---
        if (Time.time > lastTrapTime + cooldown)
        {
            // 只要玩家动了，或者极小概率随机，就丢个普通陷阱
            if (targetPlayer.mSpeed.magnitude > 5f || Random.value < 0.05f)
            {
                AttemptLethalTrap();
            }
        }

        // --- 2. 地形变动逻辑 (自动触发) ---
        if (Time.time > lastTerrainTime + terrainCooldown)
        {
            // [修正] 移除速度限制！只要在地上，就有可能触发
            // 增加一个随机性：每帧有 2% 的概率触发 (相当于 1秒内 60帧 * 2% ≈ 必定触发)
            // 这样不会冷却一好就立刻触发，稍微自然一点点
            if (targetPlayer.mOnGround && Random.value < 0.05f)
            {
                TriggerGroundCrackCombo();
                // 注意：lastTerrainTime 会在 TriggerGroundCrackCombo 内部更新
            }
        }
    }

    // --- 连招：大地裂变 (局部版) ---
    void TriggerGroundCrackCombo()
    {
        if (map == null) return;

        // 1. 获取玩家脚下的网格坐标
        Vector2 feetPos = targetPlayer.mPosition + Vector2.down * (targetPlayer.mAABB.HalfSizeY + 2.0f);
        Vector2i playerTile = map.GetMapTileAtPoint(feetPos);

        // 2. 检查脚下是否是实心砖块
        // 如果脚下是空的（比如刚跳起来，或者站在边缘），或者脚下是单向板（OneWay），就不触发裂地
        if (map.GetTile(playerTile.x, playerTile.y) != TileType.Block)
        {
            return;
        }

        // [新增] 高度保护：如果在非常高的地方（比如生成的空中平台），可能也不要裂开比较好？
        // 不过 IWBTG 只要是砖块都能裂，这里暂时不限制高度。

        Debug.Log("<color=red>Director: 自动触发 -> 局部大地裂变！</color>");

        // 3. 计算裂变范围：只裂开脚下左右各几格
        int halfWidth = 3; // 左右各 3 格，总宽 6 格
        int depth = 4;     // 向下挖 4 格深 (保证挖穿地板)

        // 左边的块：向左滑
        // 区域：[玩家X - halfWidth, 玩家Y] -> 宽度 halfWidth
        map.ConvertRegionToDynamic(
            new Vector2i(playerTile.x - halfWidth / 2 - 1, playerTile.y - depth / 2 + 1),
            halfWidth, depth,
            TerrainMotion.SplitHorizontal, -150f // 向左飞
        );

        // 右边的块：向右滑
        // 区域：[玩家X + 1, 玩家Y] -> 宽度 halfWidth
        map.ConvertRegionToDynamic(
            new Vector2i(playerTile.x + halfWidth / 2 + 1, playerTile.y - depth / 2 + 1),
            halfWidth, depth,
            TerrainMotion.SplitHorizontal, 150f // 向右飞
        );

        // 更新冷却时间
        lastTerrainTime = Time.time;

        // 4. [连携] 延迟生成地刺
        StartCoroutine(SpawnTrapDelay(playerTile, 0.2f));
    }

    IEnumerator SpawnTrapDelay(Vector2i centerTile, float delay)
    {
        yield return new WaitForSeconds(delay);

        // 尝试寻找地刺配置
        TrapConfig spikeConfig = trapLibrary.Find(x => x.strategy == TrapStrategy.RiseFromBelow);

        // 保底：没找到地刺就用列表第一个
        if (spikeConfig.prefab == null && trapLibrary.Count > 0) spikeConfig = trapLibrary[0];

        if (spikeConfig.prefab != null)
        {
            // 在裂缝下方生成
            Vector2 spawnPos = map.GetMapTilePosition(centerTile.x, centerTile.y - 5);

            GameObject trap = Instantiate(spikeConfig.prefab, new Vector3(spawnPos.x, spawnPos.y, -5f), Quaternion.identity);
            activeTraps.Add(trap);

            SmartTrap st = trap.GetComponent<SmartTrap>();
            if (st != null)
            {
                st.behaviorType = TrapBehaviorType.Rising;
                st.speed = 350f;
                st.activeDelay = 0f;
                // 不需要预测位置，直接给个空参数，因为是垂直突刺
                st.ActivateTrap(Vector2.zero, 0, targetPlayer);
            }
        }
    }

    // --- 普通陷阱逻辑 ---
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
            SpawnTrap(config, spawnPos.Value, killZone);
            lastTrapTime = Time.time;
        }
    }

    TrapConfig GetRandomTrapConfig()
    {
        float totalWeight = 0f;
        foreach (var c in trapLibrary) totalWeight += c.weight;
        float r = Random.Range(0, totalWeight);
        float current = 0f;
        foreach (var c in trapLibrary) { current += c.weight; if (r <= current) return c; }
        return trapLibrary[0];
    }

    Vector2? CalculateLethalSpawnPosition(TrapConfig config, List<Vector2> path)
    {
        Vector2 killZone = path[path.Count - 1];
        float timeToImpact = predictionWindow;

        switch (config.strategy)
        {
            case TrapStrategy.DropFromAbove:
                float gravityDist = 0.5f * Constants.cGravity * timeToImpact * timeToImpact;
                Vector2 dropOrigin = new Vector2(killZone.x, killZone.y - gravityDist);
                if (IsPositionValid(dropOrigin)) return dropOrigin;
                break;
            case TrapStrategy.RiseFromBelow:
                foreach (Vector2 point in path)
                {
                    Vector2 groundCheck = point + Vector2.down * (Constants.cHalfSizes[0] + 16f);
                    Vector2i tile = map.GetMapTileAtPoint(groundCheck);
                    if (map.IsObstacle(tile.x, tile.y)) return map.GetMapTilePosition(tile);
                }
                break;
            case TrapStrategy.SniperIntercept:
                float side = Random.value > 0.5f ? -1f : 1f;
                Vector2 sniperPos = killZone + new Vector2(side * 300f, 100f);
                if (!HasWallBetween(sniperPos, killZone)) return sniperPos;
                break;
            case TrapStrategy.FakeBlockSurprise:
                foreach (Vector2 point in path)
                {
                    Vector2 footPos = point + Vector2.down * (Constants.cHalfSizes[0] + 2f);
                    Vector2i tile = map.GetMapTileAtPoint(footPos);
                    if (map.IsObstacle(tile.x, tile.y)) return map.GetMapTilePosition(tile);
                }
                break;
        }
        return null;
    }

    List<Vector2> SimulatePlayerPath(float time)
    {
        List<Vector2> path = new List<Vector2>();
        Vector2 pos = targetPlayer.mPosition;
        Vector2 vel = targetPlayer.mSpeed;
        float dt = 0.02f;
        int steps = Mathf.CeilToInt(time / dt);
        for (int i = 0; i < steps; i++)
        {
            if (!targetPlayer.mOnGround) vel.y += Constants.cGravity * dt;
            pos += vel * dt;
            path.Add(pos);
            Vector2i tile = map.GetMapTileAtPoint(pos);
            if (map.IsObstacle(tile.x, tile.y)) break;
        }
        return path;
    }

    void SpawnTrap(TrapConfig config, Vector2 pos, Vector2 predictedKillZone)
    {
        GameObject obj = Instantiate(config.prefab, new Vector3(pos.x, pos.y, -5f), Quaternion.identity);
        activeTraps.Add(obj);
        SmartTrap trap = obj.GetComponent<SmartTrap>();
        if (trap != null) trap.ActivateTrap(predictedKillZone, predictionWindow, targetPlayer);
    }

    bool IsPositionValid(Vector2 pos)
    {
        if (pos.y > map.position.y + map.mHeight * Map.cTileSize + 200f) return false;
        Vector2i tile = map.GetMapTileAtPoint(pos);
        if (map.IsObstacle(tile.x, tile.y)) return false;
        return true;
    }

    bool HasWallBetween(Vector2 start, Vector2 end)
    {
        Vector2 dir = (end - start).normalized;
        float dist = Vector2.Distance(start, end);
        for (float d = 0; d < dist; d += Map.cTileSize * 0.5f)
        {
            Vector2 check = start + dir * d;
            Vector2i tile = map.GetMapTileAtPoint(check);
            if (map.IsObstacle(tile.x, tile.y)) return true;
        }
        return false;
    }

    public void ClearTraps()
    {
        foreach (var t in activeTraps) if (t != null) Destroy(t);
        activeTraps.Clear();
    }
}