using UnityEngine;

public enum TrapBehaviorType
{
    Static,         // 静态刺（不动）
    Falling,        // 类似苹果，触发后受重力下落
    Rising,         // 地刺，从下往上突刺
    FakeBlock,      // 伪装砖块，玩家踩上去后会碎裂或掉落
    Homing          // 追踪（高级）
}

public class SmartTrap : MonoBehaviour
{
    [Header("Behavior Settings")]
    public TrapBehaviorType behaviorType = TrapBehaviorType.Static;
    public float speed = 0f;
    public float acceleration = 0f;
    public float activeDelay = 0f; // 延迟多少秒启动（给玩家反应时间，或者故意慢半拍吓人）

    private bool isActive = false;
    private float timer = 0f;
    private Vector3 velocity = Vector3.zero;

    // 缓存引用
    private SpriteRenderer sr;
    private BoxCollider2D col; // 如果你有用到 Unity 物理
    // 你的项目用的是手动物理，所以这里主要用于简单的重叠检测或 KillerObject 脚本

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        // 自动添加 KillerObject 以便能杀死玩家 (复用你之前的脚本)
        if (GetComponent<KillerObject>() == null)
            gameObject.AddComponent<KillerObject>();
    }

    void Start()
    {
        // 伪装块一开始看起来像普通砖块
        if (behaviorType == TrapBehaviorType.FakeBlock)
        {
            if (sr != null) sr.color = Color.white;
        }
    }

    public void ActivateTrap()
    {
        isActive = true;

        // 特殊初始化
        if (behaviorType == TrapBehaviorType.Falling)
        {
            // 给一点初速度或仅仅受重力
            velocity = Vector3.down * speed;
        }
        else if (behaviorType == TrapBehaviorType.Rising)
        {
            velocity = Vector3.up * speed;
        }
    }

    void Update()
    {
        if (!isActive) return;

        // 延迟逻辑
        if (timer < activeDelay)
        {
            timer += Time.deltaTime;
            // 伪装块在延迟期间可能会颤抖一下提示玩家
            if (behaviorType == TrapBehaviorType.FakeBlock)
            {
                float shake = Mathf.Sin(Time.time * 50f) * 0.05f;
                transform.position += new Vector3(shake, 0, 0) * Time.deltaTime;
            }
            return;
        }

        // 行为逻辑
        switch (behaviorType)
        {
            case TrapBehaviorType.Falling:
                // 模拟简单的重力加速度
                velocity.y -= (acceleration > 0 ? acceleration : 9.8f) * Time.deltaTime;
                transform.position += velocity * Time.deltaTime;

                // 掉出地图销毁
                if (transform.position.y < -500f) Destroy(gameObject);
                break;

            case TrapBehaviorType.Rising:
                // 匀速上升
                transform.position += Vector3.up * speed * Time.deltaTime;
                // 上升一定高度后可以停下或销毁，这里简单处理为一直上升
                break;

            case TrapBehaviorType.FakeBlock:
                // 伪装块触发后，变色并掉落
                if (sr != null) sr.color = new Color(0.8f, 0.8f, 0.8f, 0.5f); // 变暗变透明
                velocity.y -= 20.0f * Time.deltaTime; // 快速掉落
                transform.position += velocity * Time.deltaTime;
                break;

            case TrapBehaviorType.Static:
                // 静态的不动
                break;
        }
    }
}