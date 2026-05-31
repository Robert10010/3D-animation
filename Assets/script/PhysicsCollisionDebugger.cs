using UnityEngine;

/// <summary>
/// 物理碰撞偵錯與強制忽略工具
/// 用於找出兩個階層結構中所有的碰撞器，並以程式碼強制將它們彼此之間的物理碰撞忽略。
/// </summary>
public class PhysicsCollisionDebugger : MonoBehaviour
{
    [Header("Target Objects")]
    [Tooltip("第一個物件 (例如 windmill_Final)")]
    public Transform objectA;
    
    [Tooltip("第二個物件 (例如 Q (3)_test_2)")]
    public Transform objectB;

    void Start()
    {
        if (objectA == null || objectB == null)
        {
            Debug.LogWarning("[CollisionDebugger] 請在 Inspector 中指定 Object A 與 Object B 物件！");
            return;
        }

        // 獲取兩者階層中所有的 Collider (包括隱藏的)
        Collider[] collidersA = objectA.GetComponentsInChildren<Collider>(true);
        Collider[] collidersB = objectB.GetComponentsInChildren<Collider>(true);

        Debug.Log($"[CollisionDebugger] 物件 A ({objectA.name}) 內共找到 {collidersA.Length} 個碰撞器：");
        foreach (var col in collidersA)
        {
            Debug.Log($"  - 碰撞器: {col.GetType().Name} 於 [ {col.gameObject.name} ]，目前啟用狀態: {col.enabled}");
        }

        Debug.Log($"[CollisionDebugger] 物件 B ({objectB.name}) 內共找到 {collidersB.Length} 個碰撞器：");
        foreach (var col in collidersB)
        {
            Debug.Log($"  - 碰撞器: {col.GetType().Name} 於 [ {col.gameObject.name} ]，目前啟用狀態: {col.enabled}");
        }

        // 雙重迴圈，強制忽略彼此的所有碰撞組合
        int ignoreCount = 0;
        foreach (var colA in collidersA)
        {
            foreach (var colB in collidersB)
            {
                if (colA != null && colB != null)
                {
                    Physics.IgnoreCollision(colA, colB, true);
                    ignoreCount++;
                }
            }
        }

        Debug.Log($"[CollisionDebugger] ➔ 成功強制設定忽略了 {ignoreCount} 組碰撞配對！這兩者目前在物理上絕對不會再產生碰撞阻擋。");

        // 偵測是否有多個 Rigidbody 造成物理系統混亂
        Rigidbody[] rbsA = objectA.GetComponentsInChildren<Rigidbody>(true);
        Rigidbody[] rbsB = objectB.GetComponentsInChildren<Rigidbody>(true);
        if (rbsA.Length > 1) Debug.LogWarning($"[CollisionDebugger] ⚠️ 警告：{objectA.name} 階層內包含多達 {rbsA.Length} 個 Rigidbody，這可能導致關節物理衝突！");
        if (rbsB.Length > 1) Debug.LogWarning($"[CollisionDebugger] ⚠️ 警告：{objectB.name} 階層內包含多達 {rbsB.Length} 個 Rigidbody，這可能導致關節物理衝突！");
    }
}
