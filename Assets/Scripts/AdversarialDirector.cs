using UnityEngine;
using System.Collections.Generic;

public class AdversarialDirector : MonoBehaviour
{
    public Bot targetPlayer;
    public Map map;
    public GameObject trapPrefab;

    [Header("Director Brain")]
    public float observationWindow = 1.0f;
    public float predictionHorizon = 0.4f;
    public float cooldown = 3.0f;

    private float lastTrapTime = 0f;
    private List<GameObject> activeTraps = new List<GameObject>();
    private Queue<Vector2> velocityHistory = new Queue<Vector2>();

    // [新增] 开关控制
    private bool isRunning = true;

    public void SetRunning(bool state)
    {
        isRunning = state;
        if (!isRunning)
        {
            ClearTraps(); // 关掉时顺便清理现有陷阱
        }
    }

    void Update()
    {
        // [新增] 如果被禁用，直接返回
        if (!isRunning) return;

        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy) return;

        CheckTrapCollision();

        velocityHistory.Enqueue(targetPlayer.mSpeed);
        if (velocityHistory.Count > 60) velocityHistory.Dequeue();

        if (Time.time > lastTrapTime + cooldown)
        {
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

    bool ShouldSpawnTrap()
    {
        bool committedJump = targetPlayer.mSpeed.y > 150f;
        bool atApex = Mathf.Abs(targetPlayer.mSpeed.y) < 50f && !targetPlayer.mOnGround;
        return committedJump || atApex;
    }

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
        if (map == null) return;
        Vector2i tile = map.GetMapTileAtPoint(pos);

        if (!map.IsObstacle(tile.x, tile.y))
        {
            Vector2 worldPos = map.GetMapTilePosition(tile);
            GameObject trap = Instantiate(trapPrefab, new Vector3(worldPos.x, worldPos.y, -5f), Quaternion.identity);
            trap.transform.localScale = Vector3.one * 0.8f;
            activeTraps.Add(trap);

            Debug.Log($"<color=red>Director: Predicted you at {tile}. Trap set!</color>");
        }
    }

    void CheckTrapCollision()
    {
        for (int i = activeTraps.Count - 1; i >= 0; i--)
        {
            if (activeTraps[i] == null) { activeTraps.RemoveAt(i); continue; }

            float dist = Vector2.Distance(targetPlayer.mPosition, activeTraps[i].transform.position);
            if (dist < Map.cTileSize * 0.8f)
            {
                Debug.Log("Director: Gotcha!");
                targetPlayer.Die();
                map.GameOver();
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