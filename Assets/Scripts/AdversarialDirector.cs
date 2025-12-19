using UnityEngine;
using System.Collections.Generic;

// 定义玩家状态，用于决策
public enum PlayerContextState
{
    Grounded,       // 在地上（适合用地刺、伪装块、天降苹果）
    Jumping,        // 上升中（适合在头顶生成砖块挡路，或侧面飞刺）
    Falling,        // 下落中（适合在落点生成地刺）
    Idle            // 呆着不动（适合用狙击陷阱）
}

// 定义陷阱配置项，用于在 Inspector 里配置库
[System.Serializable]
public struct TrapConfig
{
    public string name;
    public GameObject prefab;       // 必须挂载 SmartTrap 脚本
    public PlayerContextState targetState; // 这个陷阱专门针对哪种状态
    [Range(0f, 1f)] public float weight;   // 出现概率权重
    public Vector2 spawnOffset;     // 相对于玩家的生成位置偏移 (例如：(0, 5) 在头顶)
    public bool snapToGrid;         // 是否强制对齐网格（针对伪装块）
}

public class AdversarialDirector : MonoBehaviour
{
    public Bot targetPlayer;
    public Map map;

    [Header("Director Settings")]
    public float observationWindow = 0.5f; // 观察频率
    public float cooldown = 2.0f;          // 陷阱生成冷却

    [Header("The Arsenal (Trap Library)")]
    public List<TrapConfig> trapLibrary = new List<TrapConfig>();

    private float lastTrapTime = 0f;
    private bool isRunning = true;
    private List<GameObject> activeTraps = new List<GameObject>();

    // 状态分析变量
    private Vector2 lastVelocity;
    private float timeStandingStill = 0f;

    public void SetRunning(bool state)
    {
        isRunning = state;
        if (!isRunning) ClearTraps();
    }

    void Update()
    {
        if (!isRunning || targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        // 1. 实时清理失效陷阱
        CleanUpTraps();

        // 2. 状态分析
        PlayerContextState currentState = AnalyzePlayerState();

        // 3. 决策生成
        if (Time.time > lastTrapTime + cooldown)
        {
            // 只有当且仅当由于某种特定的“危险行为”或随机性满足时才触发
            // 这里我们简化逻辑：冷却好了就尝试根据状态“折磨”玩家
            if (ShouldSpawnTrap(currentState))
            {
                SpawnTrapStrategy(currentState);
                lastTrapTime = Time.time;
            }
        }

        lastVelocity = targetPlayer.mSpeed;
    }

    // --- 核心：状态分析 ---
    PlayerContextState AnalyzePlayerState()
    {
        if (targetPlayer.mOnGround)
        {
            if (targetPlayer.mSpeed.magnitude < 10f)
            {
                timeStandingStill += Time.deltaTime;
                return PlayerContextState.Idle; // 呆着不动最容易被坑
            }
            timeStandingStill = 0f;
            return PlayerContextState.Grounded;
        }
        else
        {
            timeStandingStill = 0f;
            if (targetPlayer.mSpeed.y > 0) return PlayerContextState.Jumping;
            else return PlayerContextState.Falling;
        }
    }

    bool ShouldSpawnTrap(PlayerContextState state)
    {
        // 自定义触发频率逻辑
        // 比如：如果玩家正在跳跃，且处于最高点附近，概率极大
        if (state == PlayerContextState.Jumping && targetPlayer.mSpeed.y < 100f) return true;

        // 如果玩家呆着不动超过 1秒，必定触发
        if (state == PlayerContextState.Idle && timeStandingStill > 1.0f) return true;

        // 其他情况随机触发，增加不可预测性
        return Random.value < 0.3f;
    }

    void SpawnTrapStrategy(PlayerContextState state)
    {
        // 1. 从库中筛选适合当前状态的陷阱
        List<TrapConfig> candidates = new List<TrapConfig>();
        float totalWeight = 0f;

        foreach (var config in trapLibrary)
        {
            // 匹配状态，或者这个陷阱是通用的 (设为 Idle 可以作为通用备选)
            if (config.targetState == state || config.weight > 0.8f)
            {
                candidates.Add(config);
                totalWeight += config.weight;
            }
        }

        if (candidates.Count == 0) return;

        // 2. 权重随机选择
        float r = Random.Range(0, totalWeight);
        float current = 0;
        TrapConfig selected = candidates[0];

        foreach (var c in candidates)
        {
            current += c.weight;
            if (r <= current)
            {
                selected = c;
                break;
            }
        }

        // 3. 计算生成位置
        Vector2 spawnPos = targetPlayer.mPosition + selected.spawnOffset;

        // 针对“下落”状态的特殊预判：不仅仅是偏移，而是预判落点
        if (state == PlayerContextState.Falling && selected.name.Contains("Spike"))
        {
            Vector2 predictedLandPos = PredictLandingPos();
            if (predictedLandPos != Vector2.zero)
            {
                spawnPos = predictedLandPos + selected.spawnOffset;
            }
        }

        // 4. 网格对齐 (针对伪装块)
        if (selected.snapToGrid && map != null)
        {
            Vector2i tile = map.GetMapTileAtPoint(spawnPos);
            Vector2 tileCenter = map.GetMapTilePosition(tile);
            spawnPos = tileCenter;
        }

        // 5. 生成并激活
        CreateTrapInstance(selected, spawnPos);
    }

    void CreateTrapInstance(TrapConfig config, Vector2 pos)
    {
        // Z轴设为 -5 确保在前景
        GameObject trapObj = Instantiate(config.prefab, new Vector3(pos.x, pos.y, -5f), Quaternion.identity);
        activeTraps.Add(trapObj);

        SmartTrap smartTrap = trapObj.GetComponent<SmartTrap>();
        if (smartTrap != null)
        {
            smartTrap.ActivateTrap();
        }

        Debug.Log($"Director: Spawning [{config.name}] because player is [{config.targetState}]");
    }

    // 简易落点预测（射线检测地面）
    Vector2 PredictLandingPos()
    {
        // 向下发射射线寻找最近的地面 Block
        Vector2 start = targetPlayer.mPosition;
        Vector2i tile = map.GetMapTileAtPoint(start);

        // 简单的垂直向下寻找
        for (int y = tile.y; y >= 0; y--)
        {
            if (map.IsObstacle(tile.x, y))
            {
                return map.GetMapTilePosition(tile.x, y + 1); // 返回地面上方一格的位置
            }
        }
        return Vector2.zero;
    }

    public void ClearTraps()
    {
        foreach (var t in activeTraps)
        {
            if (t != null) Destroy(t);
        }
        activeTraps.Clear();
    }

    void CleanUpTraps()
    {
        for (int i = activeTraps.Count - 1; i >= 0; i--)
        {
            if (activeTraps[i] == null) activeTraps.RemoveAt(i);
        }
    }
}