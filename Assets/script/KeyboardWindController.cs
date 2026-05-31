using UnityEngine;

/// <summary>
/// 鍵盤測試控制 Wind 物件移動腳本
/// 支援左右（A/D 或方向鍵）與上下（W/S 或自訂鍵，例如 W/D）的位移測試。
/// </summary>
public class KeyboardWindController : MonoBehaviour
{
    [Header("Horizontal Movement (左右移動)")]
    [Tooltip("左右移動的速度")]
    public float moveSpeedX = 10f;
    [Tooltip("左右位移的範圍限制 (相對於起點)")]
    public float moveLimitX = 5f;

    [Header("Vertical Movement (上下移動)")]
    [Tooltip("上下移動的速度")]
    public float moveSpeedY = 10f;
    [Tooltip("上下位移的範圍限制 (相對於起點)")]
    public float moveLimitY = 5f;

    [Header("Vertical Keys (上下按鍵)")]
    [Tooltip("向上移動按鍵 (預設為 W)")]
    public KeyCode upKey = KeyCode.W;
    
    [Tooltip("向下移動按鍵 (預設為 S，可依您的需求在 Inspector 中改為 D)")]
    public KeyCode downKey = KeyCode.S;

    private Vector3 m_startPosition;

    void OnEnable()
    {
        // 記錄初始 localPosition
        m_startPosition = transform.localPosition;
    }

    void Update()
    {
        // ── 1. 左右移動處理 ──
        float horizontalInput = Input.GetAxis("Horizontal"); // 讀取 A/D 鍵 或 左右方向鍵
        
        Vector3 pos = transform.localPosition;

        if (Mathf.Abs(horizontalInput) > 0.01f)
        {
            pos.x += horizontalInput * moveSpeedX * Time.deltaTime;
            pos.x = Mathf.Clamp(pos.x, m_startPosition.x - moveLimitX, m_startPosition.x + moveLimitX);
        }

        // ── 2. 上下移動處理 ──
        float verticalInput = 0f;
        if (Input.GetKey(upKey))
        {
            verticalInput += 1f;
        }
        if (Input.GetKey(downKey))
        {
            verticalInput -= 1f;
        }

        if (Mathf.Abs(verticalInput) > 0.01f)
        {
            pos.y += verticalInput * moveSpeedY * Time.deltaTime;
            pos.y = Mathf.Clamp(pos.y, m_startPosition.y - moveLimitY, m_startPosition.y + moveLimitY);
        }

        transform.localPosition = pos;
    }
}
