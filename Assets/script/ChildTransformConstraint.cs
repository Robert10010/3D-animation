using UnityEngine;

/// <summary>
/// 子物件變形限制工具
/// 當物件作為其他物件的子物件時，可用此腳本鎖定其特定的位置或旋轉軸向，使其不受父物件該軸向的影響。
/// </summary>
public class ChildTransformConstraint : MonoBehaviour
{
    [Header("Position Constraints (位置限制)")]
    [Tooltip("是否鎖定世界座標的 Y 軸位置 (例如固定高度，不隨父物件上下起伏)")]
    public bool lockWorldYPosition = true;
    
    [Tooltip("是否在啟動時自動使用當前世界高度作為鎖定值")]
    public bool autoGetStartWorldY = true;
    
    [Tooltip("手動設定鎖定的世界 Y 座標值")]
    public float lockedWorldY = 0f;

    [Header("Rotation Constraints (旋轉限制)")]
    [Tooltip("是否鎖定 Y 軸旋轉 (例如防止角色隨著父物件繞 Y 軸自轉)")]
    public bool lockYRotation = false;
    
    [Tooltip("是否在啟動時自動使用當前 Y 軸旋轉角度作為鎖定值")]
    public bool autoGetStartRotationY = true;
    
    [Tooltip("手動設定鎖定的 Y 軸旋轉角度 (度)")]
    public float lockedYRotationValue = 0f;

    void Start()
    {
        // 自動記錄啟動時的初始世界 Y 座標
        if (autoGetStartWorldY)
        {
            lockedWorldY = transform.position.y;
        }

        // 自動記錄啟動時的初始 Y 軸旋轉角度
        if (autoGetStartRotationY)
        {
            lockedYRotationValue = transform.eulerAngles.y;
        }
    }

    // 在 LateUpdate 中執行，確保在父物件移動與 Animator 動態更新之後才強行修正座標
    void LateUpdate()
    {
        // 1. 鎖定世界 Y 軸位置 (不隨父物件在世界座標中的上下位移影響)
        if (lockWorldYPosition)
        {
            Vector3 pos = transform.position;
            pos.y = lockedWorldY;
            transform.position = pos;
        }

        // 2. 鎖定世界 Y 軸旋轉角度 (不隨父物件在世界座標中的轉動影響)
        if (lockYRotation)
        {
            Vector3 euler = transform.eulerAngles;
            euler.y = lockedYRotationValue;
            transform.eulerAngles = euler;
        }
    }
}
