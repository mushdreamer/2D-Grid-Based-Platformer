using UnityEngine;
using System.Collections;

public class GameCamera : MonoBehaviour
{
    public Transform mPlayerTransform;
    public Character mPlayer;
    public Map mMap;

    // 不需要 dampTime 了，因为是瞬间切屏
    // public float dampTime = 0.15f; 

    void Start()
    {
        // 初始化时更新一次
        UpdateCameraPosition();
    }

    // 使用 LateUpdate 确保在主角移动后计算
    public void LateUpdate()
    {
        UpdateCameraPosition();
    }

    void UpdateCameraPosition()
    {
        if (mPlayerTransform == null) return;
        if (mPlayer == null) mPlayer = mPlayerTransform.GetComponent<Character>();
        if (mMap == null) return;

        // 1. 获取摄像机的视口大小 (世界单位)
        float camHeight = Camera.main.orthographicSize * 2f;
        float camWidth = camHeight * Camera.main.aspect;

        // 2. 计算玩家相对于地图起点的偏移量
        // (假设地图是从左下角开始生成的)
        float playerRelX = mPlayer.mPosition.x - mMap.position.x;
        float playerRelY = mPlayer.mPosition.y - mMap.position.y;

        // 3. 计算玩家当前处于第几个“屏幕” (Grid Index)
        // 例如：如果在 0-Width 范围内，screenIndexX 就是 0
        int screenIndexX = Mathf.FloorToInt(playerRelX / camWidth);
        int screenIndexY = Mathf.FloorToInt(playerRelY / camHeight);

        // 防止负数索引 (比如玩家稍微跑出左边界)
        if (screenIndexX < 0) screenIndexX = 0;
        if (screenIndexY < 0) screenIndexY = 0;

        // 4. 计算该屏幕中心的绝对坐标
        float targetX = mMap.position.x + (screenIndexX * camWidth) + (camWidth / 2f);
        float targetY = mMap.position.y + (screenIndexY * camHeight) + (camHeight / 2f);

        // 5. 限制摄像机不要超出地图的最大边界 (可选，防止看到虚空)
        // 计算地图总宽高的世界单位
        float mapWorldWidth = mMap.mWidth * Map.cTileSize;
        float mapWorldHeight = mMap.mHeight * Map.cTileSize;

        // 如果计算出的中心点超出了地图范围，就卡在边界上
        // (注意：这只在地图尺寸不是屏幕尺寸整数倍时有用)
        if (targetX - camWidth / 2f > mMap.position.x + mapWorldWidth) targetX = mMap.position.x + mapWorldWidth - camWidth / 2f;
        if (targetY - camHeight / 2f > mMap.position.y + mapWorldHeight) targetY = mMap.position.y + mapWorldHeight - camHeight / 2f;

        // 6. 瞬间设置位置 (Snap)
        // 这种模式下不需要 SmoothDamp，直接赋值最干脆，完全不抖
        transform.position = new Vector3(targetX, targetY, transform.position.z);
    }
}