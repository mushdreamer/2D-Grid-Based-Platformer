using UnityEngine;

// 定义陷阱的行为模式
public enum TrapBehaviorType
{
    Static,         // 静止（如地刺生成后不动，或者单纯的障碍）
    Falling,        // 自由落体（受重力）
    Rising,         // 向上匀速运动（如地刺突起）
    FakeBlock,      // 伪装块（平时静止，触发后下落）
    Ballistic,      // 弹道（计算抛物线）
    Sniper,         // 狙击（直线匀速）
    Homing          // 追踪（导弹）
}

public class SmartTrap : MonoBehaviour
{
    [Header("Behavior Settings")]
    public TrapBehaviorType behaviorType = TrapBehaviorType.Static;
    public float activeDelay = 0.2f; // 启动延迟（给玩家反应时间，同时也用于视觉提示）

    [Header("Motion Settings")]
    public float speed = 0f;         // 基础速度（用于 Rising/Homing）
    public float homingTurnRate = 5f;// 追踪转向率

    private bool isActive = false;
    private float timer = 0f;
    private Vector3 velocity = Vector3.zero;
    private Bot targetPlayer;
    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        // 自动添加 KillerObject 以确保碰到玩家会造成死亡
        if (GetComponent<KillerObject>() == null) gameObject.AddComponent<KillerObject>();
    }

    void Start()
    {
        // 伪装块初始化：看起来像正常砖块
        if (behaviorType == TrapBehaviorType.FakeBlock && sr != null)
        {
            sr.color = Color.white;
        }
    }

    /// <summary>
    /// 激活陷阱的核心方法
    /// </summary>
    /// <param name="predictedPos">导演计算出的“必杀点”</param>
    /// <param name="timeToReach">预计到达必杀点的时间</param>
    /// <param name="player">目标玩家引用</param>
    public void ActivateTrap(Vector2 predictedPos, float timeToReach, Bot player)
    {
        targetPlayer = player;
        isActive = true;

        switch (behaviorType)
        {
            case TrapBehaviorType.Falling:
                // 自由落体：给予一个向下的初速度增强手感
                velocity = Vector3.down * (speed > 0 ? speed : 100f);
                break;

            case TrapBehaviorType.Rising:
                // 地刺：向上冲刺
                velocity = Vector3.up * (speed > 0 ? speed : 400f);
                break;

            case TrapBehaviorType.FakeBlock:
                // 伪装块：激活瞬间静止，等待 Delay 后掉落
                velocity = Vector3.zero;
                break;

            case TrapBehaviorType.Ballistic:
                // 弹道打击核心公式：V0 = (位移 - 0.5 * g * t^2) / t
                if (timeToReach > 0)
                {
                    Vector2 displacement = predictedPos - (Vector2)transform.position;
                    // 注意：Constants.cGravity 通常是负数
                    Vector2 gravityComp = new Vector2(0, 0.5f * Constants.cGravity * timeToReach * timeToReach);
                    velocity = (displacement - gravityComp) / timeToReach;
                }
                break;

            case TrapBehaviorType.Sniper:
                // 狙击：直线速度 = 位移 / 时间
                if (timeToReach > 0)
                {
                    Vector2 dir = predictedPos - (Vector2)transform.position;
                    velocity = dir / timeToReach;
                    RotateToFaceVelocity();
                }
                break;

            case TrapBehaviorType.Homing:
                // 追踪：初始化朝向玩家的速度
                velocity = (player.mPosition - (Vector2)transform.position).normalized * (speed > 0 ? speed : 300f);
                break;
        }
    }

    void Update()
    {
        if (!isActive) return;

        // 1. 延迟/预警阶段
        if (timer < activeDelay)
        {
            timer += Time.deltaTime;

            // 攻击型陷阱在发射前剧烈颤抖，提示玩家
            if (behaviorType != TrapBehaviorType.FakeBlock && behaviorType != TrapBehaviorType.Static)
            {
                transform.position += (Vector3)(Random.insideUnitCircle * 2f);
            }
            return;
        }

        float dt = Time.deltaTime;

        // 2. 运动执行阶段
        switch (behaviorType)
        {
            case TrapBehaviorType.Static:
                break;

            case TrapBehaviorType.Falling:
            case TrapBehaviorType.Ballistic:
            case TrapBehaviorType.FakeBlock:
                // FakeBlock 延迟结束后变色并开始受重力下落
                if (behaviorType == TrapBehaviorType.FakeBlock && sr != null)
                    sr.color = new Color(0.8f, 0.8f, 0.8f, 0.8f);

                // 应用重力
                velocity.y += Constants.cGravity * dt;
                transform.position += velocity * dt;

                // 旋转增加动感（伪装块除外）
                if (behaviorType != TrapBehaviorType.FakeBlock)
                    transform.Rotate(0, 0, 360f * dt);
                break;

            case TrapBehaviorType.Rising:
            case TrapBehaviorType.Sniper:
                // 不受重力的直线运动
                transform.position += velocity * dt;
                break;

            case TrapBehaviorType.Homing:
                if (targetPlayer != null)
                {
                    Vector2 toPlayer = targetPlayer.mPosition - (Vector2)transform.position;
                    // 插值转向
                    Vector2 newVel = Vector3.RotateTowards(velocity, toPlayer, homingTurnRate * dt, 0f);
                    velocity = newVel.normalized * (speed > 0 ? speed : 300f);
                }
                transform.position += velocity * dt;
                RotateToFaceVelocity();
                break;
        }

        // 边界销毁，防止无限掉落占用资源
        if (transform.position.y < -2000f || transform.position.y > 5000f || Mathf.Abs(transform.position.x) > 5000f)
            Destroy(gameObject);
    }

    void RotateToFaceVelocity()
    {
        if (velocity != Vector3.zero)
        {
            float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}