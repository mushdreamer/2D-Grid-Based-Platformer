using UnityEngine;

public enum TrapBehaviorType
{
    Static,         // 静态障碍
    Falling,        // 物理下落 (苹果)
    Rising,         // 向上突刺 (地刺)
    FakeBlock,      // [核心功能] 虚假砖块：踩上去消失
    Ballistic,      // 抛物线投掷
    Sniper,         // 直线狙击
    Homing          // 追踪导弹
}

public class SmartTrap : MonoBehaviour
{
    [Header("Behavior Settings")]
    public TrapBehaviorType behaviorType = TrapBehaviorType.Static;
    public float activeDelay = 0.2f; // 延迟销毁时间 (给玩家反应，也模拟"塌陷"感)

    [Header("Motion Settings")]
    public float speed = 0f;
    public float homingTurnRate = 5f;

    private bool isActive = false;
    private float timer = 0f;
    private Vector3 velocity = Vector3.zero;

    private Bot targetPlayer;
    private SpriteRenderer sr;
    private Map map; // 需要引用 Map 来修改地形
    private Vector2i myTilePos; // 记录自己在网格中的位置

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        // [修改] 只有杀伤性陷阱才需要 KillerObject
        // FakeBlock 只是让人掉下去，本身不杀人，杀人的是下面的刺
        if (behaviorType != TrapBehaviorType.FakeBlock)
        {
            if (GetComponent<KillerObject>() == null)
                gameObject.AddComponent<KillerObject>();
        }
    }

    /// <summary>
    /// 激活陷阱 (Director 调用此方法)
    /// </summary>
    public void ActivateTrap(Vector2 predictedPos, float timeToReach, Bot player)
    {
        targetPlayer = player;
        isActive = true;
        map = FindObjectOfType<Map>(); // 获取地图引用

        switch (behaviorType)
        {
            case TrapBehaviorType.FakeBlock:
                InitializeFakeBlock();
                break;

            // ... 其他类型的逻辑保持不变 ...
            case TrapBehaviorType.Falling:
                velocity = Vector3.down * (speed > 0 ? speed : 100f);
                break;
            case TrapBehaviorType.Rising:
                velocity = Vector3.up * (speed > 0 ? speed : 400f);
                break;
            case TrapBehaviorType.Ballistic:
                if (timeToReach > 0)
                {
                    Vector2 displacement = predictedPos - (Vector2)transform.position;
                    Vector2 gravityComp = new Vector2(0, 0.5f * Constants.cGravity * timeToReach * timeToReach);
                    velocity = (displacement - gravityComp) / timeToReach;
                }
                break;
            case TrapBehaviorType.Sniper:
                if (timeToReach > 0)
                {
                    Vector2 dir = predictedPos - (Vector2)transform.position;
                    velocity = dir / timeToReach;
                    RotateToFaceVelocity();
                }
                break;
            case TrapBehaviorType.Homing:
                velocity = (player.mPosition - (Vector2)transform.position).normalized * (speed > 0 ? speed : 300f);
                break;
        }
    }

    // [核心逻辑] 初始化伪装
    void InitializeFakeBlock()
    {
        if (map == null) return;

        // 1. 计算自己在哪个格子上
        myTilePos = map.GetMapTileAtPoint(transform.position);

        // 2. 强制把地图的这个格子变成实心块 (Block)
        // 这样玩家就能真的站上去了，而且视觉上和普通砖块一模一样
        map.SetTile(myTilePos.x, myTilePos.y, TileType.Block);

        // 3. 隐藏自己的 Sprite (因为地图已经渲染了砖块，我们不想重叠显示)
        if (sr != null) sr.enabled = false;

        // 4. 对齐位置到网格中心 (为了逻辑严谨)
        transform.position = map.GetMapTilePosition(myTilePos);
    }

    void Update()
    {
        if (!isActive) return;

        // --- FakeBlock 专用逻辑: 检测玩家踩踏 ---
        if (behaviorType == TrapBehaviorType.FakeBlock)
        {
            CheckFakeBlockTrigger();

            // 如果触发了，计时销毁
            if (timer > 0)
            {
                timer += Time.deltaTime;
                if (timer > activeDelay)
                {
                    CollapseBlock();
                }
            }
            return; // 伪装块不执行下面的运动逻辑
        }

        // --- 其他陷阱的运动逻辑 (延迟启动) ---
        if (timer < activeDelay)
        {
            timer += Time.deltaTime;
            // 攻击型陷阱颤抖
            if (behaviorType != TrapBehaviorType.Static)
                transform.position += (Vector3)(Random.insideUnitCircle * 2f);
            return;
        }

        // ... (运动代码保持不变) ...
        PerformMovement(Time.deltaTime);
    }

    // 检测玩家是否站在我头上
    void CheckFakeBlockTrigger()
    {
        if (targetPlayer == null || map == null) return;
        if (timer > 0) return; // 已经触发过了

        // 条件1: 玩家必须在地上
        // 条件2: 玩家脚下的格子坐标必须等于我的坐标
        if (targetPlayer.mOnGround)
        {
            // 获取玩家脚底稍微往下一点点的坐标
            Vector2 playerFootPos = targetPlayer.mPosition - new Vector2(0, targetPlayer.mAABB.HalfSizeY + 2.0f);
            Vector2i playerStandingTile = map.GetMapTileAtPoint(playerFootPos);

            if (playerStandingTile == myTilePos)
            {
                // 触发！
                Debug.Log("踩到陷阱了！");
                timer = 0.001f; // 启动计时器

                // 可选：播放一个细微的音效或让地图块变色提示
                // map.HighlightTile(myTilePos.x, myTilePos.y, Color.grey); 
            }
        }
    }

    // 塌陷！
    void CollapseBlock()
    {
        if (map != null)
        {
            // 核心：把地图块挖空
            map.SetTile(myTilePos.x, myTilePos.y, TileType.Empty);

            // 可选：在这里生成一个碎块特效 (ParticleSystem)
        }
        Destroy(gameObject); // 销毁陷阱对象
    }

    void PerformMovement(float dt)
    {
        // 复制之前的运动逻辑...
        switch (behaviorType)
        {
            case TrapBehaviorType.Falling:
            case TrapBehaviorType.Ballistic:
                velocity.y += Constants.cGravity * dt;
                transform.position += velocity * dt;
                transform.Rotate(0, 0, 360f * dt);
                break;
            case TrapBehaviorType.Rising:
            case TrapBehaviorType.Sniper:
                transform.position += velocity * dt;
                break;
            case TrapBehaviorType.Homing:
                if (targetPlayer != null)
                {
                    Vector2 toPlayer = targetPlayer.mPosition - (Vector2)transform.position;
                    Vector2 newVel = Vector3.RotateTowards(velocity, toPlayer, homingTurnRate * dt, 0f);
                    velocity = newVel.normalized * (speed > 0 ? speed : 300f);
                }
                transform.position += velocity * dt;
                RotateToFaceVelocity();
                break;
        }

        // 边界销毁
        if (transform.position.y < -2000f || transform.position.y > 5000f) Destroy(gameObject);
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