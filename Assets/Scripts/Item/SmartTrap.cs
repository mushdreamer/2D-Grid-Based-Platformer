using System;
using UnityEngine;

public enum TrapBehaviorType
{
    Static,
    Falling,
    Rising,

    FakeBlock,      // 坑爹砖：踩上去消失
    FakeSpike,      // [新增] 伪装刺：踩上去变刺
    Ballistic,
    Sniper,
    Homing
}

public class SmartTrap : MonoBehaviour
{
    public static event Action<SmartTrap> KillerTrapTriggered;
    [Header("Behavior Settings")]
    public TrapBehaviorType behaviorType = TrapBehaviorType.Static;
    public float activeDelay = 0.2f;

    [Header("Motion Settings")]
    public float speed = 0f;
    public float homingTurnRate = 5f;

    // 重生系统需要的数据
    [HideInInspector] public Vector2 initialSpawnPosition;
    [HideInInspector] public string configName;

    // 伪装系统需要的数据
    private Sprite spikeSprite; // 原始刺图片
    private Sprite blockSprite; // 伪装砖图片

    private bool isActive = false;
    private float timer = 0f;
    private Vector3 velocity = Vector3.zero;

    private Bot targetPlayer;
    private SpriteRenderer sr;
    private Map map;
    private Vector2i myTilePos;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        initialSpawnPosition = transform.position;

        // 自动添加杀人组件（如果是伪装块，稍后会禁用）
        if (GetComponent<KillerObject>() == null)
            gameObject.AddComponent<KillerObject>();

        // 保存原始长相（刺的图片）
        if (sr != null) spikeSprite = sr.sprite;
    }

    public void ActivateTrap(Vector2 predictedPos, float timeToReach, Bot player)
    {
        targetPlayer = player;
        isActive = true;
        map = FindObjectOfType<Map>();

        switch (behaviorType)
        {
            case TrapBehaviorType.FakeBlock:
                InitializeFakeBlock();
                break;
            case TrapBehaviorType.FakeSpike: // [新增]
                InitializeFakeSpike();
                break;
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

    // 初始化：伪装成路
    void InitializeFakeBlock()
    {
        if (map == null) return;
        myTilePos = map.GetMapTileAtPoint(transform.position);

        // 物理上设为实心 Block，玩家能站住
        map.SetTile(myTilePos.x, myTilePos.y, TileType.Block);

        // 视觉上隐藏自己（显示地图原本的砖块）
        if (sr != null) sr.enabled = false;

        // 对齐网格
        transform.position = map.GetMapTilePosition(myTilePos);

        // 暂时禁用杀人能力
        var killer = GetComponent<KillerObject>();
        if (killer) killer.enabled = false;
    }

    // 初始化：伪装成路，但是是假的
    void InitializeFakeSpike()
    {
        if (map == null) return;
        myTilePos = map.GetMapTileAtPoint(transform.position);

        // 获取当前关卡的砖块贴图
        if (map.terrainSprites != null && map.terrainSprites.Count > 0)
            blockSprite = map.terrainSprites[0];
        else if (map.mDirtSprites != null && map.mDirtSprites.Count > 0)
            blockSprite = map.mDirtSprites[0];

        // 换皮：变成砖块的样子
        if (sr != null && blockSprite != null) sr.sprite = blockSprite;

        // 对齐网格
        transform.position = map.GetMapTilePosition(myTilePos);

        // 注意：我们不调用 map.SetTile(Block)。
        // 这样它在 Map 数据层面上依然是 Empty 或 Danger，玩家站上去没有物理支撑。
        // 这符合“陷阱”的定位，且如果不小心站上去会穿过它触发内部判定。
    }

    void Update()
    {
        if (!isActive) return;

        // 伪装系逻辑检查
        if (behaviorType == TrapBehaviorType.FakeBlock || behaviorType == TrapBehaviorType.FakeSpike)
        {
            CheckTrigger(); // 检测玩家距离

            // 伪装块倒计时塌陷
            if (behaviorType == TrapBehaviorType.FakeBlock && timer > 0)
            {
                timer += Time.deltaTime;
                if (timer > activeDelay) CollapseBlock();
            }
            return;
        }

        // 延迟启动逻辑（针对移动陷阱）
        if (timer < activeDelay)
        {
            timer += Time.deltaTime;
            // 启动前稍微抖动一下提示危险（可选）
            if (behaviorType != TrapBehaviorType.Static)
                transform.position += (Vector3)(UnityEngine.Random.insideUnitCircle * 2f);
            return;
        }

        PerformMovement(Time.deltaTime);
    }

    void CheckTrigger()
    {
        if (targetPlayer == null || map == null) return;
        if (timer > 0) return; // 已经触发过了

        // 简单的距离触发：当玩家靠近中心点时
        if (Vector2.Distance(targetPlayer.mPosition, transform.position) < Map.cTileSize * 1.2f)
        {
            // 伪装刺：靠近即死，现原形
            if (behaviorType == TrapBehaviorType.FakeSpike)
            {
                RevealSpike();
            }
            // 伪装块：踩在头上才触发
            else if (behaviorType == TrapBehaviorType.FakeBlock && targetPlayer.mOnGround)
            {
                Vector2 playerFootPos = targetPlayer.mPosition - new Vector2(0, targetPlayer.mAABB.HalfSizeY + 2.0f);
                Vector2i playerStandingTile = map.GetMapTileAtPoint(playerFootPos);
                if (playerStandingTile == myTilePos) timer = 0.001f; // 开始倒计时
            }
        }
    }

    // [新增] 伪装刺现原形
    void RevealSpike()
    {
        timer = 1.0f; // 标记为已触发

        // 变回刺的样子
        if (sr != null && spikeSprite != null) sr.sprite = spikeSprite;

        // 确保杀人判定开启
        var killer = GetComponent<KillerObject>();
        if (killer) killer.enabled = true;
    }

    void CollapseBlock()
    {
        if (map != null) map.SetTile(myTilePos.x, myTilePos.y, TileType.Empty);
        Destroy(gameObject);
    }

    void PerformMovement(float dt)
    {
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
        if (map != null)
        {
            if (transform.position.y < map.position.y - 500f || transform.position.y > map.position.y + map.mHeight * Map.cTileSize + 500f)
                Destroy(gameObject);
        }
    }

    void RotateToFaceVelocity()
    {
        if (velocity != Vector3.zero)
        {
            float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (behaviorType == TrapBehaviorType.FakeBlock) return;

        if (other.GetComponent<Bot>() != null)
        {
            KillerTrapTriggered?.Invoke(this);
        }
    }
}