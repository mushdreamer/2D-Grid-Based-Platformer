using UnityEngine;
using System.Collections.Generic;

public class AdversarialDirector : MonoBehaviour
{
    public Bot targetPlayer;
    public Map map;
    public GameObject trapPrefab; // 拖入陷阱 Prefab (红色方块)

    [Header("Director Brain")]
    public float observationWindow = 1.0f;
    public float predictionHorizon = 0.4f; // 预测未来 0.4 秒
    public float cooldown = 3.0f; // 陷阱冷却时间

    private float lastTrapTime = 0f;
    private List<GameObject> activeTraps = new List<GameObject>();
    private Queue<Vector2> velocityHistory = new Queue<Vector2>();

    void Update()
    {
        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        // 1. 简单的碰撞检测 (代替 OnTriggerEnter)
        CheckTrapCollision();

        // 2. 收集数据 (Feature Extraction)
        velocityHistory.Enqueue(targetPlayer.mSpeed);
        if (velocityHistory.Count > 60) velocityHistory.Dequeue();

        // 3. 决策逻辑 (Inference)
        if (Time.time > lastTrapTime + cooldown)
        {
            // 只有当玩家在全速移动时才预判 (防止坑杀挂机玩家)
            if (targetPlayer.mSpeed.magnitude > 50f)
            {
                if (ShouldSpawnTrap())
                {
                    Vector2 predictedPos = PredictPlayerPos();
                    SpawnTrap(predictedPos);
                    lastTrapTime = Time.time;
                }
            }
        }
    }

    // 决策模型：这里用 Heuristic 模拟神经网络的输出
    bool ShouldSpawnTrap()
    {
        // 计算玩家的"跳跃决心" (Jump Commitment)
        // 如果玩家垂直速度很大，说明他刚起跳，无法改变轨迹 -> 此时放陷阱命中率最高
        bool committedJump = targetPlayer.mSpeed.y > 150f;

        // 计算"节奏点"：如果玩家处于最高点附近 (速度接近0)
        bool atApex = Mathf.Abs(targetPlayer.mSpeed.y) < 50f && !targetPlayer.mOnGround;

        return committedJump || atApex;
    }

    // 预测模型：简单的物理外推
    Vector2 PredictPlayerPos()
    {
        Vector2 futurePos = targetPlayer.mPosition;
        Vector2 futureVel = targetPlayer.mSpeed;
        float dt = 0.02f;
        int steps = Mathf.CeilToInt(predictionHorizon / dt);

        for (int i = 0; i < steps; i++)
        {
            futureVel.y += Constants.cGravity * dt;
            futurePos += futureVel * dt;
        }
        return futurePos;
    }

    void SpawnTrap(Vector2 pos)
    {
        Vector2i tile = map.GetMapTileAtPoint(pos);

        // 只有目标点是空气时才生成
        if (!map.IsObstacle(tile.x, tile.y))
        {
            // 在预判位置生成一个 Trap
            // 为了视觉效果，我们生成在那个格子的中心
            Vector2 worldPos = map.GetMapTilePosition(tile);

            // 确保生成在 Z=-5，显示在角色前面
            GameObject trap = Instantiate(trapPrefab, new Vector3(worldPos.x, worldPos.y, -5f), Quaternion.identity);
            trap.transform.localScale = Vector3.one * 0.8f; // 稍微小一点
            activeTraps.Add(trap);

            Debug.Log($"<color=red>Director: Predicted you at {tile}. Trap set!</color>");
        }
    }

    void CheckTrapCollision()
    {
        // 遍历所有陷阱，检查是否碰到玩家 (简单的距离检测)
        for (int i = activeTraps.Count - 1; i >= 0; i--)
        {
            if (activeTraps[i] == null) { activeTraps.RemoveAt(i); continue; }

            float dist = Vector2.Distance(targetPlayer.mPosition, activeTraps[i].transform.position);
            // 简单的距离判定：如果距离小于半个格子
            if (dist < Map.cTileSize * 0.8f)
            {
                Debug.Log("Director: Gotcha!");
                targetPlayer.Die();
                map.GameOver();

                // 清除所有陷阱
                ClearTraps();
                return;
            }
        }
    }

    public void ClearTraps()
    {
        foreach (var t in activeTraps)
        {
            if (t != null) Destroy(t);
        }
        activeTraps.Clear();
    }
}