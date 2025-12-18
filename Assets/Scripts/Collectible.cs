using UnityEngine;
using System.Collections;

public class Collectible : MonoBehaviour
{
    public enum ItemType
    {
        Fruit,      // 分数物品
        Checkpoint, // 存档点 (起点)
        Box,        // 装饰
        Finish      // 新增：终点 (胜利触发器)
    }

    public ItemType type;
    public AudioClip collectSfx;

    // 简单的浮动动画
    private Vector3 startPos;
    private float floatSpeed = 2.0f;
    private float floatAmount = 0.1f;

    // 缓存玩家引用，用于手动检测
    private Character cachedPlayer;

    void Start()
    {
        startPos = transform.position;
        // 自动查找场景中的玩家
        cachedPlayer = FindObjectOfType<Character>();
    }

    void Update()
    {
        // 上下浮动效果
        float newY = startPos.y + Mathf.Sin(Time.time * floatSpeed) * floatAmount;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);

        // --- 新增：手动碰撞检测 ---
        // 这是一道保险，确保即使 BoxCollider2D 没有正确触发 (例如角色没有 Rigidbody2D)，
        // 也能通过距离判断触发收集。
        if (cachedPlayer != null && cachedPlayer.gameObject.activeInHierarchy)
        {
            CheckManualCollision(cachedPlayer);
        }
    }

    // 兼容 Unity 原生物理 (如果双方都有 Collider/Rigidbody)
    void OnTriggerEnter2D(Collider2D other)
    {
        Character player = other.GetComponent<Character>();
        if (player != null)
        {
            OnCollect(player);
        }
    }

    // 手动检测逻辑
    void CheckManualCollision(Character player)
    {
        // 获取物品的碰撞盒半径，如果没有则默认半个格子
        float radius = Map.cTileSize * 0.5f;
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) radius = col.bounds.extents.x;

        // 计算距离
        float dist = Vector2.Distance(transform.position, player.mAABB.Center);

        // 如果距离小于 (物品半径 + 玩家半宽)，认为接触
        if (dist < (radius + player.mAABB.HalfSizeX))
        {
            OnCollect(player);
        }
    }

    void OnCollect(Character player)
    {
        // 防止已收集的物品被重复触发
        if (!this.enabled) return;

        switch (type)
        {
            case ItemType.Fruit:
                Debug.Log("Get Fruit! Score +1");
                PlaySfx();
                Destroy(gameObject);
                break;

            case ItemType.Checkpoint:
                Debug.Log("Checkpoint Reached!");
                if (player.mMap != null)
                {
                    player.mMap.SetCheckpoint(player.mMap.GetMapTileAtPoint(transform.position));
                }
                PlaySfx();
                // 变色表示激活，并禁用脚本防止重复触发
                GetComponent<SpriteRenderer>().color = Color.green;
                DisableItem();
                break;

            case ItemType.Finish:
                Debug.Log(">>> LEVEL COMPLETE! <<<");
                PlaySfx();
                if (player.mMap != null)
                {
                    player.mMap.LevelComplete();
                }
                // 销毁或禁用
                Destroy(gameObject);
                break;

            case ItemType.Box:
                break;
        }
    }

    void PlaySfx()
    {
        if (collectSfx != null) AudioSource.PlayClipAtPoint(collectSfx, transform.position);
    }

    void DisableItem()
    {
        this.enabled = false;
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }
}