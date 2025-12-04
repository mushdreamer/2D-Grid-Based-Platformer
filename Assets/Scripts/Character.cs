using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Algorithms;

public class Character : MovingObject
{
    [System.Serializable]
    public enum CharacterState
    {
        Stand,
        Run,
        Jump,
        GrabLedge,
        Die
    };

    public AudioClip mHitWallSfx;
    public AudioClip mJumpSfx;
    public AudioClip mWalkSfx;
    public AudioSource mAudioSource;

    public float mWalkSfxTimer = 0.0f;
    public const float cWalkSfxTime = 0.25f;
    /// <summary>
    /// The current state.
    /// </summary>
    [HideInInspector]
    public CharacterState mCurrentState = CharacterState.Stand;

    public Animator mAnimator;

    /// <summary>
    /// The number of frames passed from changing the state to jump.
    /// </summary>
    protected int mFramesFromJumpStart = 0;

    protected bool[] mInputs;
    protected bool[] mPrevInputs;

    /// <summary>
    /// The hero's vertical speed when he starts a jump
    /// </summary>
    public float mJumpSpeed;

    /// <summary>
    /// The walk speed constant in pixels/second.
    /// </summary>
    public float mWalkSpeed;

    public List<Vector2i> mPath = new List<Vector2i>();

    /// <summary>
    /// Raises the draw gizmos event.
    /// </summary>
    void OnDrawGizmos()
    {
        DrawMovingObjectGizmos();

        //draw the path

        if (mPath != null && mPath.Count > 0)
        {
            var start = mPath[0];

            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(mMap.transform.position + new Vector3(start.x * Map.cTileSize, start.y * Map.cTileSize, -5.0f), 5.0f);

            for (var i = 1; i < mPath.Count; ++i)
            {
                var end = mPath[i];
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(mMap.transform.position + new Vector3(end.x * Map.cTileSize, end.y * Map.cTileSize, -5.0f), 5.0f);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(mMap.transform.position + new Vector3(start.x * Map.cTileSize, start.y * Map.cTileSize, -5.0f),
                                mMap.transform.position + new Vector3(end.x * Map.cTileSize, end.y * Map.cTileSize, -5.0f));
                start = end;
            }
        }
    }

    public LineRenderer lineRenderer;

    protected void DrawPathLines()
    {
        if (mPath != null && mPath.Count > 0)
        {
            lineRenderer.enabled = true;
            // --- FIX STARTS HERE ---
            // Old: lineRenderer.SetVertexCount(mPath.Count);
            lineRenderer.positionCount = mPath.Count;

            // Old: lineRenderer.SetWidth(4.0f, 4.0f);
            lineRenderer.startWidth = 4.0f;
            lineRenderer.endWidth = 4.0f;
            // --- FIX ENDS HERE ---

            for (var i = 0; i < mPath.Count; ++i)
            {
                // --- FIX STARTS HERE ---
                // Old: lineRenderer.SetColors(Color.red, Color.red);
                // Note: It's more efficient to set colors outside the loop
                // but for this fix, we'll keep the structure.
                lineRenderer.startColor = Color.red;
                lineRenderer.endColor = Color.red;
                // --- FIX ENDS HERE ---
                lineRenderer.SetPosition(i, mMap.transform.position + new Vector3(mPath[i].x * Map.cTileSize, mPath[i].y * Map.cTileSize, -5.0f));
            }
        }
        else
            lineRenderer.enabled = false;
    }

    public void UpdatePrevInputs()
    {
        var count = (byte)KeyInput.Count;

        for (byte i = 0; i < count; ++i)
            mPrevInputs[i] = mInputs[i];
    }

    private void HandleJumping()
    {
        //increase the number of frames that we've been in the jump state
        ++mFramesFromJumpStart;

        //if we hit the ceiling, we don't want to compensate pro jumping, we can prevent by faking a huge mFramesFromJumpStart
        if (mAtCeiling)
            mFramesFromJumpStart = 100;

        //if we're jumping/falling then apply the gravity
        //this should be applied at the beginning of the jump routine
        //because this way we can assure that when we hit the ground 
        //the speed.y will not change after we zero it
        mSpeed.y += Constants.cGravity * Time.deltaTime;

        mSpeed.y = Mathf.Max(mSpeed.y, Constants.cMaxFallingSpeed);

        if (!mInputs[(int)KeyInput.Jump] && mSpeed.y > 0.0f)
        {
            mSpeed.y = Mathf.Min(mSpeed.y, 200.0f);
            mFramesFromJumpStart = 100;
        }

        //in air movement
        //if both or none horizontal movement keys are pressed
        if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
        {
            mSpeed.x = 0.0f;
        }
        else if (mInputs[(int)KeyInput.GoRight])	//if right key is pressed then accelerate right
        {
            transform.localScale = new Vector3(-mScale.x, mScale.y, 1.0f);
            mSpeed.x = mWalkSpeed;

            //..W
            //.H.     <- to not get stuck in these kind of situations we beed to advance
            //..W			the hero forward if he doesn't push a wall anymore
            if (mPushedRightWall && !mPushesRightWall)
                mPosition.x += 1.0f;
        }
        else if (mInputs[(int)KeyInput.GoLeft])	//if left key is pressed then accelerate left
        {
            transform.localScale = new Vector3(mScale.x, mScale.y, 1.0f);
            mSpeed.x = -mWalkSpeed;

            //W..
            //.H.     <- to not get stuck in these kind of situations we need to advance
            //W..			the hero forward if he doesn't push a wall anymore
            if (mPushedLeftWall && !mPushesLeftWall)
                mPosition.x -= 1.0f;
        }

        //if we just started falling and want to jump, then jump anyway
        if (mInputs[(int)KeyInput.Jump] && (mOnGround || (mSpeed.y < 0.0f && mFramesFromJumpStart < Constants.cJumpFramesThreshold)))
            mSpeed.y = mJumpSpeed;
    }

    protected override void CheckForDangerZone()
    {
        // 如果已经死了，就不要再检测了，防止重复触发
        if (mCurrentState == CharacterState.Die) return;

        // 获取脚下位置
        Vector2 feetPosition = mAABB.Center - new Vector2(0, mAABB.HalfSizeY);
        Vector2i tileCoords = mMap.GetMapTileAtPoint(feetPosition);
        TileType currentTileType = mMap.GetTile(tileCoords.x, tileCoords.y);

        // 如果踩到了 Danger (尖刺)，触发死亡
        if (currentTileType == TileType.Danger)
        {
            Die();
        }
    }

    public void Die()
    {
        if (mCurrentState == CharacterState.Die) return;

        Debug.Log("Player Died!");
        mCurrentState = CharacterState.Die;

        // 1. 给一个向上的“反弹”速度 (类似马里奥死亡)
        mSpeed.x = 0; // 停止横向移动
        mSpeed.y = 350.0f; // 向上的跳跃力度，你可以调整这个数值

        // 2. 播放跳跃音效或者死亡音效
        if (mJumpSfx != null) mAudioSource.PlayOneShot(mJumpSfx);

        // 3. 播放跳跃动画 (或者你有专门的死亡动画也可以换成 "Die")
        mAnimator.Play("Jump");
    }

    public void CharacterUpdate()
    {
        switch (mCurrentState)
        {
            // --- 新增：死亡状态的更新逻辑 ---
            case CharacterState.Die:
                // 1. 应用重力
                mSpeed.y += Constants.cGravity * Time.deltaTime;

                // 2. 手动更新位置 (不调用 UpdatePhysics，从而穿过墙壁和地板)
                mPosition += mSpeed * Time.deltaTime;
                transform.position = new Vector3(Mathf.Round(mPosition.x), Mathf.Round(mPosition.y), mSpriteDepth);

                // 3. 检查是否掉出了地图下边界，如果掉出去了，就通知地图重置
                if (mPosition.y < mMap.position.y - 100.0f)
                {
                    // 调用 Map 中的重置方法 (稍后在 Map.cs 中添加 GameOver)
                    mMap.GameOver();
                    gameObject.SetActive(false); // 暂时隐藏自己
                }

                // 死亡状态下直接返回，不执行后面的 UpdatePhysics
                return;
            // -----------------------------
            case CharacterState.Stand:

                mWalkSfxTimer = cWalkSfxTime;
                mAnimator.Play("Stand");

                mSpeed = Vector2.zero;

                if (!mOnGround)
                {
                    mCurrentState = CharacterState.Jump;
                    break;
                }

                //if left or right key is pressed, but not both
                if (mInputs[(int)KeyInput.GoRight] != mInputs[(int)KeyInput.GoLeft])
                {
                    mCurrentState = CharacterState.Run;
                }
                else if (mInputs[(int)KeyInput.Jump])
                {
                    mSpeed.y = mJumpSpeed;
                    mAudioSource.PlayOneShot(mJumpSfx);
                    mCurrentState = CharacterState.Jump;
                }

                if (mInputs[(int)KeyInput.GoDown] && mOnOneWayPlatform)
                    mPosition -= Vector2.up * cOneWayPlatformThreshold;

                break;
            case CharacterState.Run:

                mAnimator.Play("Walk");

                mWalkSfxTimer += Time.deltaTime;

                if (mWalkSfxTimer > cWalkSfxTime)
                {
                    mWalkSfxTimer = 0.0f;
                    mAudioSource.PlayOneShot(mWalkSfx);
                }

                //if both or neither left nor right keys are pressed then stop walking and stand

                if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
                {
                    mCurrentState = CharacterState.Stand;
                    mSpeed = Vector2.zero;
                }
                else if (mInputs[(int)KeyInput.GoRight])
                {
                    mSpeed.x = mWalkSpeed;
                    transform.localScale = new Vector3(-mScale.x, mScale.y, 1.0f);
                }
                else if (mInputs[(int)KeyInput.GoLeft])
                {
                    mSpeed.x = -mWalkSpeed;
                    transform.localScale = new Vector3(mScale.x, mScale.y, 1.0f);
                }

                //if there's no tile to walk on, fall
                if (mInputs[(int)KeyInput.Jump])
                {

                    mSpeed.y = mJumpSpeed;
                    mAudioSource.PlayOneShot(mJumpSfx, 1.0f);
                    mCurrentState = CharacterState.Jump;
                }
                else if (!mOnGround)
                {
                    mCurrentState = CharacterState.Jump;
                    break;
                }

                //don't move left when pushing left wall
                if (mPushesLeftWall)
                    mSpeed.x = Mathf.Max(mSpeed.x, 0.0f);
                //don't move right when pushing right wall
                else if (mPushesRightWall)
                    mSpeed.x = Mathf.Min(mSpeed.x, 0.0f);

                break;
            case CharacterState.Jump:

                mWalkSfxTimer = cWalkSfxTime;

                mAnimator.Play("Jump");

                HandleJumping();


                //if we hit the ground
                if (mOnGround)
                {
                    //if there's no movement change state to standing
                    if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
                    {
                        mCurrentState = CharacterState.Stand;
                        mSpeed = Vector2.zero;
                    }
                    else	//either go right or go left are pressed so we change the state to walk
                    {
                        mCurrentState = CharacterState.Run;
                        mSpeed.y = 0.0f;
                    }
                }
                break;
        }

        if ((!mWasOnGround && mOnGround)
            || (!mWasAtCeiling && mAtCeiling)
            || (!mPushedLeftWall && mPushesLeftWall)
            || (!mPushedRightWall && mPushesRightWall))
            mAudioSource.PlayOneShot(mHitWallSfx, 0.5f);

        UpdatePhysics();

        if (mWasOnGround && !mOnGround)
            mFramesFromJumpStart = 0;

        UpdatePrevInputs();
    }
}
