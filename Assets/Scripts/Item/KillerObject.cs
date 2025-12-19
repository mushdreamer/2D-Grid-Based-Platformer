using UnityEngine;

public class KillerObject : MonoBehaviour
{
    private Bot cachedPlayer;

    void Start()
    {
        // 尝试自动找到玩家
        cachedPlayer = FindObjectOfType<Bot>();
    }

    void Update()
    {
        // 如果 Unity 物理系统没触发，我们在 Update 里手动检查
        if (cachedPlayer != null && cachedPlayer.gameObject.activeInHierarchy)
        {
            ManualCollisionCheck(cachedPlayer);
        }
    }

    // 兼容 Unity 原生物理系统 (如果玩家加了 Rigidbody2D)
    void OnTriggerEnter2D(Collider2D other)
    {
        Bot player = other.GetComponent<Bot>();
        if (player != null)
        {
            Kill(player);
        }
    }

    // 手动 AABB 或 距离检测
    void ManualCollisionCheck(Bot player)
    {
        // 1. 简单距离检测 (性能最好)
        // 假设陷阱大约是 1 格大小 (Map.cTileSize)
        float dist = Vector2.Distance(transform.position, player.mPosition);
        if (dist < Map.cTileSize * 0.7f) // 0.7f 是个比较宽松的判定范围
        {
            Kill(player);
            return;
        }

        // 2. 如果需要更精确的 AABB 检测，可以使用 player.mAABB.Overlaps(...)
        // 但对于静态刺儿，距离检测通常够用了。
    }

    void Kill(Bot player)
    {
        // 防止重复触发
        if (player.mCurrentState == Character.CharacterState.Die) return;

        Debug.Log("HA! Trap triggered!");
        player.Die();
        if (player.mMap != null)
        {
            player.mMap.GameOver();
        }
    }
}