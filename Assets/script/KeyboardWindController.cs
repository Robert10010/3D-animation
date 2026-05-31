using UnityEngine;

/// <summary>
/// 鍵盤測試控制 Wind 物件左右移動腳本
/// 便於在沒有連接 Joy-Con 的情況下，透過 A/D 鍵或方向鍵測試物理關節跟隨效果。
/// </summary>
public class KeyboardWindController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("鍵盤控制的左右移動速度")]
    public float moveSpeed = 10f;
    
    [Tooltip("左右移動的範圍限制 (相對於起點)")]
    public float moveLimitX = 5f;

    private Vector3 m_startPosition;

    void OnEnable()
    {
        // 記錄初始 localPosition
        m_startPosition = transform.localPosition;
    }

    void Update()
    {
        // 讀取鍵盤左右輸入 (A/D 鍵 或 左右方向鍵)
        float horizontalInput = Input.GetAxis("Horizontal");

        if (Mathf.Abs(horizontalInput) > 0.01f)
        {
            Vector3 pos = transform.localPosition;
            pos.x += horizontalInput * moveSpeed * Time.deltaTime;

            // 限制左右位移範圍
            pos.x = Mathf.Clamp(pos.x, m_startPosition.x - moveLimitX, m_startPosition.x + moveLimitX);

            transform.localPosition = pos;
        }
    }
}
