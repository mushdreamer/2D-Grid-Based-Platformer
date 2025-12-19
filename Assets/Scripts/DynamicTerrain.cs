using UnityEngine;
using System.Collections.Generic;

public enum TerrainMotion
{
    SplitHorizontal, // 水平裂开
    Rotate180,       // 翻转
    DropToHell,      // 塌陷
    CrushVertical    // 压扁
}

public class DynamicTerrain : MonoBehaviour
{
    public TerrainMotion motionType;
    public float speed = 2.0f;

    private bool isMoving = false;

    // 初始化：接收一堆砖块的 Sprite
    public void Initialize(List<GameObject> blocks, TerrainMotion motion, float spd)
    {
        motionType = motion;
        speed = spd;

        foreach (var b in blocks)
        {
            b.transform.parent = this.transform;
        }
        isMoving = true;

        // 5秒后销毁，节省性能
        Destroy(gameObject, 5.0f);
    }

    void Update()
    {
        if (!isMoving) return;

        float dt = Time.deltaTime;

        switch (motionType)
        {
            case TerrainMotion.SplitHorizontal:
                // 沿着自身的右轴移动 (根据速度正负决定方向)
                transform.position += Vector3.right * speed * dt;
                break;

            case TerrainMotion.DropToHell:
                transform.position += Vector3.down * speed * 2f * dt;
                break;

            case TerrainMotion.Rotate180:
                transform.Rotate(0, 0, 180f * dt);
                break;
        }
    }
}