using UnityEngine;

public class KillerObject : MonoBehaviour
{
    // 如果碰到了角色，直接杀死
    void OnTriggerEnter2D(Collider2D other)
    {
        // 尝试获取 Bot 或 Character 组件
        // 注意：因为你的 Character 没用 Rigidbody2D，这个 OnTrigger 可能需要 Character 也有 Collider2D
        // 或者，我们可以用 Character.cs 里的自定义碰撞来检测这个物体

        // 简便方案：假设 Character 也有一个 Trigger Collider 用于检测这种事件
        Bot player = other.GetComponent<Bot>();
        if (player != null)
        {
            Debug.Log("HA! Trap triggered!");
            player.Die(); // 需要确保 Character.cs 里有公开的 Die() 方法
            player.mMap.GameOver();
        }
    }
}