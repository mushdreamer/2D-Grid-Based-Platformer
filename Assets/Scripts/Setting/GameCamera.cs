using UnityEngine;
using System.Collections;

public class GameCamera : MonoBehaviour
{
    public Transform mPlayerTransform;
    public Character mPlayer;
    public Map mMap;

    // [新增] 摄像机直接持有背景的引用
    public SpriteRenderer backgroundRenderer;

    void Start()
    {
        // 自动查找玩家，防止 Inspector 丢失引用
        if (mPlayerTransform == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) mPlayerTransform = playerObj.transform;
        }

        UpdateCameraPosition();
    }

    public void LateUpdate()
    {
        // 1. 先计算摄像机要去哪里
        UpdateCameraPosition();

        // 2. 然后强行把背景按在摄像机脸上，并拉伸填满
        FitBackgroundToScreen();
    }

    void UpdateCameraPosition()
    {
        if (mPlayerTransform == null) return;
        if (mPlayer == null) mPlayer = mPlayerTransform.GetComponent<Character>();
        if (mMap == null) return;

        float camHeight = Camera.main.orthographicSize * 2f;
        float camWidth = camHeight * Camera.main.aspect;

        float playerRelX = mPlayer.mPosition.x - mMap.position.x;
        float playerRelY = mPlayer.mPosition.y - mMap.position.y;

        // 计算当前处于第几屏 (Screen Snapping)
        int screenIndexX = Mathf.FloorToInt(playerRelX / camWidth);
        int screenIndexY = Mathf.FloorToInt(playerRelY / camHeight);

        if (screenIndexX < 0) screenIndexX = 0;
        if (screenIndexY < 0) screenIndexY = 0;

        float targetX = mMap.position.x + (screenIndexX * camWidth) + (camWidth / 2f);
        float targetY = mMap.position.y + (screenIndexY * camHeight) + (camHeight / 2f);

        // 限制边界 (可选)
        float mapWorldWidth = mMap.mWidth * Map.cTileSize;
        float mapWorldHeight = mMap.mHeight * Map.cTileSize;

        // 只有当地图尺寸大于屏幕时才限制，防止小地图抖动
        if (mapWorldWidth > camWidth)
        {
            if (targetX - camWidth / 2f > mMap.position.x + mapWorldWidth)
                targetX = mMap.position.x + mapWorldWidth - camWidth / 2f;
        }

        if (mapWorldHeight > camHeight)
        {
            if (targetY - camHeight / 2f > mMap.position.y + mapWorldHeight)
                targetY = mMap.position.y + mapWorldHeight - camHeight / 2f;
        }

        transform.position = new Vector3(targetX, targetY, transform.position.z);
    }

    // [新增] 核心逻辑：直接填充背景
    void FitBackgroundToScreen()
    {
        if (backgroundRenderer == null || backgroundRenderer.sprite == null) return;

        // A. 强制复位：确保背景就在摄像机正中心
        // 修正：Map.cs 中 Tiles 生成在 Z=10。
        // 如果摄像机在 Z=-10，localPosition.z = 20 会导致 World Z = 10，产生 Z-Fighting。
        // 我们将其设为 40 (World Z = 30)，确保背景在 Tiles 后面。
        backgroundRenderer.transform.localPosition = new Vector3(0, 0, 40f);
        backgroundRenderer.transform.localRotation = Quaternion.identity;

        // B. 强制拉伸：计算屏幕长宽比，修改 Scale
        float camHeight = Camera.main.orthographicSize * 2f;
        float camWidth = camHeight * Camera.main.aspect;

        Vector2 spriteSize = backgroundRenderer.sprite.bounds.size;

        Vector3 newScale = new Vector3(1, 1, 1);
        // 避免除以0错误
        if (spriteSize.x > 0) newScale.x = camWidth / spriteSize.x;
        if (spriteSize.y > 0) newScale.y = camHeight / spriteSize.y;

        backgroundRenderer.transform.localScale = newScale;
    }
}