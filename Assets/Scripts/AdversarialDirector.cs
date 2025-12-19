using UnityEngine;
using System.Collections.Generic;

// --- 核心定义：放在类外面，确保全局可见 ---

// 定义陷阱生成的策略类型
public enum TrapStrategy
{
    DropFromAbove,      // 天降系
    RiseFromBelow,      // 地刺系
    SniperIntercept,    // 狙击系
    FakeBlockSurprise   // 伪装系
}

[System.Serializable]
public struct TrapConfig
{
    public string name;
    public GameObject prefab;       // 必须挂载 SmartTrap
    public TrapStrategy strategy;   // 生成策略
    [Range(0f, 1f)] public float weight;   // 出现权重
}

// --- 导演类 ---

public class AdversarialDirector : MonoBehaviour
{
    public Bot targetPlayer;
    public Map map;

    [Header("Difficulty Brain")]
    public float predictionWindow = 0.6f; // 预判窗口：我们预判多远的未来？
    public float cooldown = 1.0f;         // 冷却时间

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
        if (!isRunning || targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        // 清理失效对象
        for (int i = activeTraps.Count - 1; i >= 0; i--)
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);

        if (Time.time > lastTrapTime + cooldown)
        {
            if (targetPlayer.mSpeed.magnitude > 5f || Random.value < 0.05f)
            {
                AttemptLethalTrap();
            }
        }
    }

    void AttemptLethalTrap()
    {
        if (trapLibrary.Count == 0) return;

        // 1. 随机选一种陷阱
        TrapConfig config = GetRandomTrapConfig();

        // 2. 预测玩家未来轨迹
        List<Vector2> futurePath = SimulatePlayerPath(predictionWindow);
        if (futurePath.Count == 0) return;

        // 3. 计算生成点
        Vector2? spawnPos = CalculateLethalSpawnPosition(config, futurePath);

        // 4. 生成陷阱
        if (spawnPos.HasValue)
        {
            // 目标击杀点设为预测路径的终点
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
        foreach (var c in trapLibrary)
        {
            current += c.weight;
            if (r <= current) return c;
        }
        return trapLibrary[0];
    }

    // --- 核心：逆向物理求解器 ---
    Vector2? CalculateLethalSpawnPosition(TrapConfig config, List<Vector2> path)
    {
        Vector2 killZone = path[path.Count - 1];
        // [修复] 这里使用 predictionWindow 变量
        float timeToImpact = predictionWindow;

        switch (config.strategy)
        {
            case TrapStrategy.DropFromAbove:
                // 反推 S0 = S - 0.5*g*t^2
                float gravityDist = 0.5f * Constants.cGravity * timeToImpact * timeToImpact;
                Vector2 dropOrigin = new Vector2(killZone.x, killZone.y - gravityDist);

                if (IsPositionValid(dropOrigin)) return dropOrigin;
                break;

            case TrapStrategy.RiseFromBelow:
                // 寻找落地位置
                foreach (Vector2 point in path)
                {
                    Vector2 groundCheck = point + Vector2.down * (Constants.cHalfSizes[0] + 16f);
                    Vector2i tile = map.GetMapTileAtPoint(groundCheck);

                    if (map.IsObstacle(tile.x, tile.y))
                    {
                        return map.GetMapTilePosition(tile);
                    }
                }
                break;

            case TrapStrategy.SniperIntercept:
                // 寻找侧面射击位
                float side = Random.value > 0.5f ? -1f : 1f;
                Vector2 sniperPos = killZone + new Vector2(side * 300f, 100f);

                if (!HasWallBetween(sniperPos, killZone)) return sniperPos;
                break;

            case TrapStrategy.FakeBlockSurprise:
                // 寻找踩踏块
                foreach (Vector2 point in path)
                {
                    Vector2 footPos = point + Vector2.down * (Constants.cHalfSizes[0] + 2f);
                    Vector2i tile = map.GetMapTileAtPoint(footPos);

                    if (map.IsObstacle(tile.x, tile.y))
                    {
                        return map.GetMapTilePosition(tile);
                    }
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
            if (!targetPlayer.mOnGround)
                vel.y += Constants.cGravity * dt;

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

        if (trap != null)
        {
            // [修复] 这里之前用了 predictionTime 导致报错，现在统一改为 predictionWindow
            trap.ActivateTrap(predictedKillZone, predictionWindow, targetPlayer);
        }
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