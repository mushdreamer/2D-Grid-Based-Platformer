using UnityEngine;

public enum TrapBehaviorType
{
    Static,
    Falling,
    Rising,
    FakeBlock,
    Ballistic,
    Sniper,
    Homing
}

public class SmartTrap : MonoBehaviour
{
    [Header("Behavior Settings")]
    public TrapBehaviorType behaviorType = TrapBehaviorType.Static;
    public float activeDelay = 0.2f;

    [Header("Motion Settings")]
    public float speed = 0f;
    public float homingTurnRate = 5f;

    // [新增] 记录出生地，用于重生
    [HideInInspector] public Vector2 initialSpawnPosition;
    // [新增] 记录所属的配置名称，用于重生时查找 Prefab
    [HideInInspector] public string configName;

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
        initialSpawnPosition = transform.position; // 记录初始位置

        // 只有非伪装块才加杀伤判定，伪装块靠坑人
        if (behaviorType != TrapBehaviorType.FakeBlock)
        {
            if (GetComponent<KillerObject>() == null)
                gameObject.AddComponent<KillerObject>();
        }
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

    void InitializeFakeBlock()
    {
        if (map == null) return;
        myTilePos = map.GetMapTileAtPoint(transform.position);
        map.SetTile(myTilePos.x, myTilePos.y, TileType.Block);
        if (sr != null) sr.enabled = false;
        transform.position = map.GetMapTilePosition(myTilePos);
    }

    void Update()
    {
        if (!isActive) return;

        if (behaviorType == TrapBehaviorType.FakeBlock)
        {
            CheckFakeBlockTrigger();
            if (timer > 0)
            {
                timer += Time.deltaTime;
                if (timer > activeDelay) CollapseBlock();
            }
            return;
        }

        if (timer < activeDelay)
        {
            timer += Time.deltaTime;
            if (behaviorType != TrapBehaviorType.Static)
                transform.position += (Vector3)(Random.insideUnitCircle * 2f);
            return;
        }

        PerformMovement(Time.deltaTime);
    }

    void CheckFakeBlockTrigger()
    {
        if (targetPlayer == null || map == null) return;
        if (timer > 0) return;

        if (targetPlayer.mOnGround)
        {
            Vector2 playerFootPos = targetPlayer.mPosition - new Vector2(0, targetPlayer.mAABB.HalfSizeY + 2.0f);
            Vector2i playerStandingTile = map.GetMapTileAtPoint(playerFootPos);

            if (playerStandingTile == myTilePos)
            {
                timer = 0.001f;
            }
        }
    }

    void CollapseBlock()
    {
        if (map != null) map.SetTile(myTilePos.x, myTilePos.y, TileType.Empty);

        // 伪装块虽然不直接杀人，但导致坠落。
        // 我们可以在这里通知导演记录事件，或者简单销毁。
        // 对于 FakeBlock，通常作为地形事件处理更合适。

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

        if (transform.position.y < map.position.y - 500f || transform.position.y > map.position.y + map.mHeight * Map.cTileSize + 500f)
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

    // [核心] 碰撞检测：如果碰到玩家，我就是凶手
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<Bot>() != null)
        {
            // 向导演自首
            var director = FindObjectOfType<AdversarialDirector>();
            if (director != null)
            {
                director.RecordKillerTrap(this);
            }
        }
    }
}