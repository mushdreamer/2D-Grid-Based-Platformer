using UnityEngine;
using System.Collections;

public class Collectible : MonoBehaviour
{
    public enum ItemType
    {
        Fruit,      // 分数/成就物品 (IWBTG 的樱桃)
        Checkpoint, // 存档点
        Box         // 装饰或物理道具
    }

    public ItemType type;
    public AudioClip collectSfx;

    // 简单的浮动动画
    private Vector3 startPos;
    private float floatSpeed = 2.0f;
    private float floatAmount = 0.1f;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        // 上下浮动效果
        float newY = startPos.y + Mathf.Sin(Time.time * floatSpeed) * floatAmount;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // 假设角色身上有 Character 或 Bot 组件
        Character player = other.GetComponent<Character>();

        if (player != null)
        {
            OnCollect(player);
        }
    }

    void OnCollect(Character player)
    {
        switch (type)
        {
            case ItemType.Fruit:
                Debug.Log("Get Fruit! Score +1");
                // 这里可以增加分数逻辑
                if (collectSfx != null) AudioSource.PlayClipAtPoint(collectSfx, transform.position);
                Destroy(gameObject);
                break;

            case ItemType.Checkpoint:
                Debug.Log("Checkpoint Reached!");
                // 更新地图的起始点为当前位置
                if (player.mMap != null)
                {
                    player.mMap.SetCheckpoint(player.mMap.GetMapTileAtPoint(transform.position));
                }
                if (collectSfx != null) AudioSource.PlayClipAtPoint(collectSfx, transform.position);
                // 存档点通常不会消失，或者变色，这里简单处理为变色
                GetComponent<SpriteRenderer>().color = Color.green;
                GetComponent<Collider2D>().enabled = false; // 触发一次后禁用
                break;

            case ItemType.Box:
                // 盒子可能只是物理碰撞，或者推着走，这里暂时不做处理
                break;
        }
    }
}