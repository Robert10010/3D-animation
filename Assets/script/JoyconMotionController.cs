using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Switch;

/// <summary>
/// JoyCon 陀螺儀 → 物件旋轉/移動控制器
/// 修正重點：
///   1. filterNoiseOnCurrent = false（讓 noisy IMU controls 可更新）
///   2. 用 Coroutine 延遲發送 IMU 啟用指令，避免初始化競態條件
///   3. 同時嘗試對 SwitchJoyConLHID / SwitchJoyConRHID 分別初始化
/// </summary>
public class JoyconMotionController : MonoBehaviour
{
    [Header("Rotation Settings")]
    public bool applyRotation = true;
    public float rotationSmoothness = 15f;

    [Header("Axis Remap（若旋轉方向不對在這裡調整）")]
    [Tooltip("交換 X 軸與 Y 軸（往右轉=前進、往前轉=往右 → 勾選此項修正）")]
    public bool swapXY = true;
    [Tooltip("反轉 X 軸旋轉方向（往前旋轉=往後 → 勾選此項修正）")]
    public bool invertX = true;
    [Tooltip("反轉 Y 軸旋轉方向（往右旋轉=往左 → 勾選此項修正）")]
    public bool invertY = true;
    [Tooltip("反轉 Z 軸旋轉方向")]
    public bool invertZ = false;

    [Header("Gyro Bias Calibration")]
    [Tooltip("啟動時自動量測並扣除陀螺儀靜止偏移（需保持控制器靜止 1~2 秒）")]
    public bool autoCorrectGyroBias = true;
    [Tooltip("量測 bias 的採樣秒數")]
    public float biasSampleDuration = 1.5f;

    [Header("Movement Settings")]
    public bool applyMovement = true;
    [Tooltip("移動靈敏度")]
    public float moveSensitivity = 3f;
    [Tooltip("移動阻尼")]
    public float movementDamping = 5f;
    [Tooltip("移動範圍限制（相對初始位置的最大偏移）")]
    public float moveLimit = 4f;

    [Header("Mic Driven Movement (麥克風模式)")]
    [Tooltip("勾選以啟用麥克風相對速度與角度觸發的移動模式")]
    public bool enableMicMovement = false;
    
    [Tooltip("往左右轉的速度(角速度) 轉換為 X軸移動速度的比例 (若方向反了可改為負數)")]
    public float turnSpeedMultiplier = 0.05f;

    [Tooltip("舉起角度超過此值時，物件往上移動 (例如 -30。正負取決於您的反轉設定)")]
    public float liftAngleThreshold = -30f;
    
    [Tooltip("舉起速度超過此值時，物件往上移動 (例如 -50)")]
    public float liftSpeedThreshold = -50f;

    [Tooltip("觸發向上時的移動速度")]
    public float upwardMoveSpeed = 5f;
    
    [Tooltip("放下速度超過此值時，物件往下移動 (例如 50)")]
    public float dropSpeedThreshold = 50f;
    [Tooltip("觸發向下時的移動速度")]
    public float downwardMoveSpeed = 5f;

    [Tooltip("X軸(左右)的移動範圍限制 (相對於起點)")]
    public Vector2 micMoveLimitX = new Vector2(-10f, 10f);
    [Tooltip("Y軸(上下)的移動範圍限制 (相對於起點)")]
    public Vector2 micMoveLimitY = new Vector2(0f, 8f);

    [Tooltip("歌手靜止不動時，物件自動回歸中心點的速度 (0=不歸位)")]
    public float micReturnSpeed = 2f;

    [Tooltip("靜止不動多久 (秒) 後，物件才開始自動歸位")]
    public float micReturnDelay = 1.0f;
    private float m_micIdleTimer = 0f;

    [Tooltip("【防飄移技術】麥克風角度自動校正速度。能解決用久了必須越舉越高的問題。")]
    public float angleAutoCenterSpeed = 0.5f;

    [Header("Pose Driven Position (姿態觸發位置變換)")]
    public bool enablePosePosition = true;
    public enum TriggerAxis { X, Y, Z }
    [Tooltip("作為判斷的旋轉軸向（通常 Y是左右轉，Z是翻轉，X是前後傾）")]
    public TriggerAxis triggerAxis = TriggerAxis.Z; 
    
    [Tooltip("往『負方向(如左方)』旋轉達到此角度時觸發 (例如 -50)")]
    public float negativeThreshold = -50f;
    public Vector3 negativePosition = new Vector3(5f, 5f, 0f);

    [Tooltip("往『正方向(如右方)』旋轉達到此角度時觸發 (例如 50)")]
    public float positiveThreshold = 50f;
    public Vector3 positivePosition = new Vector3(5f, 5f, 0f); 

    [Tooltip("都沒達到50度閾值時，預設回歸的中心坐標")]
    public Vector3 defaultPosition = new Vector3(0f, 0f, 0f);

    [Tooltip("位置切換時的過渡平滑度")]
    public float posePositionSmoothness = 8f;

    [Header("Drift Filter (防漂移過濾)")]
    public bool enableDriftFilter = true;
    [Tooltip("當角速度低於此數值時視為靜止，停止更新旋轉以濾除緩慢漂移")]
    public float stationaryThreshold = 0.05f;

    [Header("Debug")]
    public bool showDebugGUI = true;

    // ---- 移動內部變數 ----
    private Vector3 m_startPosition;     // 記錄物件的起始位置，移動以此為錢點
    private Vector3 m_currentVelocity;
    private Vector3 m_gravityAccel;
    private const float kGravityAlpha = 0.8f;

    // ---- 防漂移記憶變數 ----
    private Quaternion m_lastRawRot = Quaternion.identity;
    private Quaternion m_filteredRot = Quaternion.identity;
    private bool       m_rotInitialized = false;

    // ---- Debug 快照 ----
    private bool       m_isConnected;
    private bool       m_imuInitDone;
    private string     m_initStatus   = "尚未初始化";
    private string     m_deviceName   = "---";
    private string     m_deviceType   = "---";
    private string     m_battLevel    = "---";
    private string     m_firmware     = "---";
    private string     m_mac          = "---";

    private Vector3    m_rawAccel;
    private Vector3    m_linearAccel;
    private Vector3    m_angVel;
    private Vector3    m_uncalAccel;
    private Vector3    m_uncalGyro;
    private Vector3    m_orientation;
    private Quaternion m_deviceRot;

    private Vector3    m_accelBase;
    private Vector3    m_gyroBase;

    // ---- Gyro Bias（靜止偏移，量測後扣除）----
    private Vector3    m_gyroBias;          // 量測到的靜止 bias（已扣除後這裡存的是修正量）
    private bool       m_biasMeasured;      // 是否已量測完成
    private string     m_biasStatus = "尚未量測";

    private GUIStyle   m_header;
    private GUIStyle   m_label;

    // ────────────────────────────────────────────────────────────────
    void OnEnable()
    {
        // ★ 關鍵：關閉 noisy controls 的過濾，否則 IMU 數値永遠不更新
        InputSystem.settings.filterNoiseOnCurrent = false;

        InputSystem.onDeviceChange += OnDeviceChange;

        // 記錄初始位置 (改為 localPosition 避免與外層的向前移動衝突)
        m_startPosition = transform.localPosition;
        m_rotInitialized = false;

        // 若已連接，立刻嘗試初始化
        if (SwitchControllerHID.current != null)
            StartCoroutine(InitJoyconDelayed(SwitchControllerHID.current));
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is SwitchControllerHID joycon && change == InputDeviceChange.Added)
        {
            m_imuInitDone = false;
            m_rotInitialized = false;
            StartCoroutine(InitJoyconDelayed(joycon));
        }
    }

    IEnumerator InitJoyconDelayed(SwitchControllerHID joycon)
    {
        m_initStatus = "等待裝置穩定...";
        // 等 1 秒讓裝置完成初次握手（藍牙連線後 Joy-Con 需要一點時間）
        yield return new WaitForSecondsRealtime(1.0f);

        m_initStatus = "發送 Standard Mode 指令...";
        joycon.SetInputReportMode(InputModeEnum.Standard);
        yield return new WaitForSecondsRealtime(0.3f);

        m_initStatus = "發送 Enable IMU 指令...";
        joycon.SetIMUEnabled(true);
        yield return new WaitForSecondsRealtime(0.3f);

        m_initStatus = "讀取 Factory IMU 校準資料...";
        joycon.ReadFactoryIMUCalibrationData();
        yield return new WaitForSecondsRealtime(0.5f);

        // ★ 量測陀螺儀靜止 Bias（請保持控制器靜止）
        if (autoCorrectGyroBias)
        {
            m_biasStatus = $"量測 Bias 中...（請保持靜止 {biasSampleDuration}s）";
            m_initStatus = m_biasStatus;
            yield return StartCoroutine(MeasureGyroBias(joycon));
        }

        m_imuInitDone = true;
        m_initStatus = "✓ 初始化完成";
        Debug.Log("[JoyConMotion] IMU 初始化完成");
    }

    IEnumerator MeasureGyroBias(SwitchControllerHID joycon)
    {
        Vector3 accumulator = Vector3.zero;
        int sampleCount = 0;
        float elapsed = 0f;

        while (elapsed < biasSampleDuration)
        {
            // 讀取未校準陀螺儀（最原始的值，確保沒有其他處理干擾）
            accumulator += joycon.angularVelocity.ReadValue();
            sampleCount++;
            elapsed += Time.unscaledDeltaTime;
            yield return null; // 等下一幀
        }

        if (sampleCount > 0)
            m_gyroBias = accumulator / sampleCount;
        else
            m_gyroBias = Vector3.zero;

        m_biasMeasured = true;
        m_biasStatus = $"✓ Bias 已量測: {V3(m_gyroBias)}";
        Debug.Log($"[JoyConMotion] Gyro Bias 量測完成: {m_gyroBias}");
    }

    // ────────────────────────────────────────────────────────────────
    void Update()
    {
        var joycon = SwitchControllerHID.current;
        m_isConnected = (joycon != null);
        if (!m_isConnected) return;

        // ── 快照裝置資訊 ──
        m_deviceName = joycon.displayName;
        m_deviceType = joycon.SpecificControllerType.ToString();
        m_battLevel  = joycon.BatteryLevel.ToString();
        m_firmware   = joycon.FirmwareVersion;
        m_mac        = joycon.MACAddress;

        var imuCalib = joycon.calibrationData.imuCalibData;
        m_accelBase = new Vector3((short)imuCalib.accelBase.x, (short)imuCalib.accelBase.y, (short)imuCalib.accelBase.z);
        m_gyroBase  = new Vector3((short)imuCalib.gyroBase.x,  (short)imuCalib.gyroBase.y,  (short)imuCalib.gyroBase.z);

        // ── 讀取 IMU ──
        m_rawAccel    = joycon.acceleration.ReadValue();
        m_angVel      = joycon.angularVelocity.ReadValue();
        m_uncalAccel  = joycon.uncalibratedAcceleration.ReadValue();
        m_uncalGyro   = joycon.uncalibratedAngularVelocity.ReadValue();
        m_orientation = joycon.orientation.ReadValue();
        m_deviceRot   = joycon.deviceRotation.ReadValue();

        // ── 重力濾波 ──
        m_gravityAccel = m_gravityAccel * kGravityAlpha + m_rawAccel * (1f - kGravityAlpha);
        m_linearAccel  = m_rawAccel - m_gravityAccel;

        if (!m_imuInitDone) return; // 初始化還沒完成先不動物件

        // ── 防漂移過濾處理 (Delta 累加法) ──
        if (!m_rotInitialized)
        {
            m_lastRawRot = m_deviceRot;
            m_filteredRot = m_deviceRot;
            m_rotInitialized = true;
        }

        Vector3 currentAngVel = m_angVel;
        if (m_biasMeasured && autoCorrectGyroBias)
        {
            currentAngVel -= m_gyroBias; // 扣除測量到的靜止偏移量以獲得更精準的角速度
        }

        // 取得本幀的「本地(Local)」旋轉變化量 (Delta)
        // 【關鍵修正】：使用 Local Delta 而非 Global Delta。
        // 當 m_filteredRot 和 m_deviceRot 因為過濾產生了 Yaw (Y軸) 的角度差時，
        // 如果使用 Global Delta，會導致旋轉軸向錯亂 (例如你往前傾 Pitch，卻因為角度差變成了 Roll，導致 X 跟 Z 逐漸變大)。
        Quaternion localDelta = Quaternion.Inverse(m_lastRawRot) * m_deviceRot;
        m_lastRawRot = m_deviceRot; // 永遠將硬體 raw 值存入，將未過濾的漂移背景「消耗」掉

        if (enableDriftFilter && currentAngVel.magnitude < stationaryThreshold)
        {
            // 靜止狀態：忽略此幀的變動以避免緩慢漂移
        }
        else
        {
            // 活動狀態：累積有效變化量 (將 localDelta 乘在右邊)
            m_filteredRot = m_filteredRot * localDelta;
            m_filteredRot.Normalize(); // 防止浮點數累積誤差
        }

        // 【自動回正 (Leaky Integrator) 防飄移技術】
        // 當開啟麥克風模式時，隨時以極緩慢的速度將角度拉回 0 度。
        // 這能徹底消除 JoyCon 長時間使用累積的「永久角度飄移」，避免歌手必須越舉越高。
        if (enableMicMovement && angleAutoCenterSpeed > 0f)
        {
            // 改良：只有在麥克風接近平舉 (傾角小於 25 度) 時才進行背景回正。
            // 這樣歌手如果故意高舉著麥克風，就不會因為被拉回而導致角度判定失效！
            float currentPitch = Mathf.Abs(NormalizeAngle(m_filteredRot.eulerAngles.x));
            if (currentPitch < 25f)
            {
                m_filteredRot = Quaternion.Slerp(m_filteredRot, Quaternion.identity, Time.deltaTime * angleAutoCenterSpeed);
            }
        }

        // 計算解算後的最新目標四元數
        Quaternion targetRot = RemapRotation(m_filteredRot);

        // ── 1. 旋轉 ──
        // 依照需求：當開啟「麥克風模式」時，強制關閉物體的旋轉
        if (applyRotation && !enableMicMovement)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                Time.deltaTime * rotationSmoothness);
        }

        // ── 2. 移動與位置觸發 ──
        if (enableMicMovement)
        {
            // 將角速度轉換到與旋轉相同的座標系
            float avX = swapXY ? currentAngVel.y : currentAngVel.x;
            float avY = swapXY ? currentAngVel.x : currentAngVel.y;
            float avZ = currentAngVel.z;
            if (invertX) avX = -avX;
            if (invertY) avY = -avY;
            if (invertZ) avZ = -avZ;

            // 1. 處理左右移動 (使用角速度 Y，相對速度連續計算)
            // rad/s 轉成適當數值，因為 Unity 的角速度是度/秒還是 rad/s 視底層而定，直接乘上 Multiplier 微調即可
            float moveDeltaX = avY * turnSpeedMultiplier * Time.deltaTime;

            // 2. 處理上下移動 (角度 或 角速度 觸發)
            float moveDeltaY = 0f;
            Vector3 euler = targetRot.eulerAngles;
            float pitchAngle = NormalizeAngle(euler.x);

            // 檢查角度是否達標 (支援正向或負向設定)
            bool angleUp = (liftAngleThreshold > 0 && pitchAngle >= liftAngleThreshold) || 
                           (liftAngleThreshold < 0 && pitchAngle <= liftAngleThreshold);
                           
            // 檢查角速度是否達標 (舉起速度)
            bool speedUp = (liftSpeedThreshold > 0 && avX >= liftSpeedThreshold) || 
                           (liftSpeedThreshold < 0 && avX <= liftSpeedThreshold);
                           
            // 檢查放下角速度
            bool speedDown = (dropSpeedThreshold > 0 && avX >= dropSpeedThreshold) || 
                             (dropSpeedThreshold < 0 && avX <= dropSpeedThreshold);

            if (angleUp || speedUp)
            {
                moveDeltaY = upwardMoveSpeed * Time.deltaTime;
            }
            else if (speedDown)
            {
                moveDeltaY = -downwardMoveSpeed * Time.deltaTime;
            }

            // 若沒有明顯動作，自動回歸中心
            bool isIdle = Mathf.Abs(avY) < 10f && !angleUp && !speedUp && !speedDown;
            
            if (isIdle) m_micIdleTimer += Time.deltaTime;
            else m_micIdleTimer = 0f;

            Vector3 pos = transform.localPosition;

            if (isIdle && m_micIdleTimer >= micReturnDelay && micReturnSpeed > 0f)
            {
                pos.x = Mathf.MoveTowards(pos.x, m_startPosition.x, micReturnSpeed * Time.deltaTime);
                pos.y = Mathf.MoveTowards(pos.y, m_startPosition.y, micReturnSpeed * Time.deltaTime);
            }
            else
            {
                pos.x += moveDeltaX;
                pos.y += moveDeltaY;
            }

            // 限制移動範圍
            pos.x = Mathf.Clamp(pos.x, m_startPosition.x + micMoveLimitX.x, m_startPosition.x + micMoveLimitX.y);
            pos.y = Mathf.Clamp(pos.y, m_startPosition.y + micMoveLimitY.x, m_startPosition.y + micMoveLimitY.y);

            // 保持原本 Z 軸的向前移動
            pos.z = transform.localPosition.z;
            transform.localPosition = pos;
        }
        else if (enablePosePosition)
        {
            // 將目標轉換成易懂的 -180 ~ 180 度視角
            Vector3 euler = targetRot.eulerAngles;
            float cx = NormalizeAngle(euler.x);
            float cy = NormalizeAngle(euler.y);
            float cz = NormalizeAngle(euler.z);

            // 挑出要比對角度的軸向
            float checkAngle = cx;
            if (triggerAxis == TriggerAxis.Y) checkAngle = cy;
            else if (triggerAxis == TriggerAxis.Z) checkAngle = cz;

            // 預設為相對於初始位置的偏移
            Vector3 targetPos = m_startPosition + defaultPosition;

            if (checkAngle <= negativeThreshold)
                targetPos = m_startPosition + negativePosition; // 例如到達左側 -50度 時
            else if (checkAngle >= positiveThreshold)
                targetPos = m_startPosition + positivePosition; // 例如到達右側 50度 時
            
            // [關鍵修正] 保留目前的 Z 軸位置，避免覆蓋 EndlessTerrainManager 的持續前進
            targetPos.z = transform.localPosition.z;

            // 平滑切換至目標位置 (使用 localPosition)
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * posePositionSmoothness);
        }
        else if (applyMovement)
        {
            // 將線性加速度轉為目標速度
            Vector3 target    = m_linearAccel * moveSensitivity;
            m_currentVelocity = Vector3.Lerp(m_currentVelocity, target, Time.deltaTime * movementDamping);

            // 更新位置，並限制在起始位置的 ±moveLimit 範圍內 (使用 localPosition)
            Vector3 newPos = transform.localPosition + m_currentVelocity;
            Vector3 offset = newPos - m_startPosition;
            offset.x = Mathf.Clamp(offset.x, -moveLimit, moveLimit);
            offset.y = Mathf.Clamp(offset.y, -moveLimit, moveLimit);
            // 放開對 Z 軸的 offset 限制，讓外部程式(EndlessTerrainManager) 可以自由往前推動
            
            transform.localPosition = new Vector3(
                m_startPosition.x + offset.x,
                m_startPosition.y + offset.y,
                transform.localPosition.z + m_currentVelocity.z
            );
        }
    }

    //────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!showDebugGUI) return;

        if (m_header == null)
        {
            m_header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            m_header.normal.textColor = Color.yellow;
            m_label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            m_label.normal.textColor = Color.white;
        }

        GUI.color = new Color(0f, 0f, 0f, 0.80f);
        GUI.DrawTexture(new Rect(10, 10, 470, 440), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = 18f, y = 15f, lh = 22f;
        GUI.Label(new Rect(x, y, 450, lh), "══ JoyCon Debug Panel ══", m_header); y += lh;

        // 連線狀態
        var cs = new GUIStyle(m_label);
        cs.normal.textColor = m_isConnected ? Color.green : Color.red;
        GUI.Label(new Rect(x, y, 450, lh),
            m_isConnected ? "● CONNECTED" : "● NOT CONNECTED  (SwitchControllerHID.current == null)", cs);
        y += lh;

        // filterNoise 狀態
        bool filterOff = !InputSystem.settings.filterNoiseOnCurrent;
        var fs = new GUIStyle(m_label);
        fs.normal.textColor = filterOff ? Color.green : Color.red;
        GUI.Label(new Rect(x, y, 450, lh),
            filterOff ? "● filterNoiseOnCurrent = false  ✓ (IMU 可更新)"
                      : "● filterNoiseOnCurrent = true   ✗ (IMU 被過濾！)", fs);
        y += lh;

        // Drift Filter 狀態
        var dfs = new GUIStyle(m_label);
        dfs.normal.textColor = enableDriftFilter ? Color.yellow : Color.gray;
        GUI.Label(new Rect(x, y, 450, lh),
            enableDriftFilter ? $"● Drift Filter: ON (Threshold: {stationaryThreshold})" 
                              : "● Drift Filter: OFF", dfs);
        y += lh;

        // 初始化狀態
        var is2 = new GUIStyle(m_label);
        is2.normal.textColor = m_imuInitDone ? Color.green : Color.cyan;
        GUI.Label(new Rect(x, y, 450, lh), $"● Init: {m_initStatus}", is2); y += lh + 4f;

        if (!m_isConnected) return;

        // ── 裝置資訊 ──
        GUI.Label(new Rect(x, y, 450, lh), "▶ Device Info", m_header); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  Name    : {m_deviceName}  (Type: {m_deviceType})", m_label); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  Battery : {m_battLevel}   FW: {m_firmware}", m_label); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  MAC     : {m_mac}", m_label); y += lh + 4f;

        // ── IMU 校準基底（全 0 代表校準資料未讀到）──
        GUI.Label(new Rect(x, y, 450, lh), "▶ IMU Calibration Base (全0=未讀到校準資料!)", m_header); y += lh;
        bool calibOK = m_accelBase != Vector3.zero || m_gyroBase != Vector3.zero;
        var calStyle = new GUIStyle(m_label);
        calStyle.normal.textColor = calibOK ? Color.white : Color.red;
        GUI.Label(new Rect(x, y, 450, lh), $"  AccelBase : {V3(m_accelBase)}", calStyle); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  GyroBase  : {V3(m_gyroBase)}",  calStyle); y += lh + 4f;

        // ── 校準後 IMU ──
        GUI.Label(new Rect(x, y, 450, lh), "▶ Calibrated IMU", m_header); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  Accel (raw) : {V3(m_rawAccel)}",    m_label); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  Accel (lin) : {V3(m_linearAccel)}", m_label); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  AngVelocity : {V3(m_angVel)}",      m_label); y += lh + 4f;

        // ── 未校準 IMU（最原始，這個如果也是 0 代表硬體層問題）──
        GUI.Label(new Rect(x, y, 450, lh), "▶ UNCalibrated IMU  ← 若此也是0則是硬體/藍牙問題", m_header); y += lh;
        bool uncalMoving = m_uncalAccel.magnitude > 0.01f || m_uncalGyro.magnitude > 0.01f;
        var uStyle = new GUIStyle(m_label);
        uStyle.normal.textColor = uncalMoving ? Color.white : Color.red;
        GUI.Label(new Rect(x, y, 450, lh), $"  Uncal Accel : {V3(m_uncalAccel)}", uStyle); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  Uncal Gyro  : {V3(m_uncalGyro)}",  uStyle); y += lh + 4f;

        // ── Gyro Bias ──
        GUI.Label(new Rect(x, y, 450, lh), "▶ Gyro Bias", m_header); y += lh;
        var bStyle = new GUIStyle(m_label);
        bStyle.normal.textColor = m_biasMeasured ? Color.green : Color.cyan;
        GUI.Label(new Rect(x, y, 450, lh), $"  {m_biasStatus}", bStyle); y += lh;
        if (m_biasMeasured)
            GUI.Label(new Rect(x, y, 450, lh), $"  Bias Value  : {V3(m_gyroBias)}", m_label);
        y += lh + 4f;

        // ── 方向 ──
        GUI.Label(new Rect(x, y, 450, lh), "▶ Orientation", m_header); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  Euler       : {V3(m_orientation)}", m_label); y += lh;
        GUI.Label(new Rect(x, y, 450, lh), $"  Quaternion  : {Q4(m_deviceRot)}",   m_label); y += lh + 4f;

        // ── Pose Trigger (解算後的指定視角) ──
        GUI.Label(new Rect(x, y, 450, lh), "▶ Pose Target Angles (-180~180)", m_header); y += lh;
        Quaternion curTargetRot = RemapRotation(m_filteredRot);
        Vector3 curEuler = curTargetRot.eulerAngles;
        float nx = NormalizeAngle(curEuler.x);
        float ny = NormalizeAngle(curEuler.y);
        float nz = NormalizeAngle(curEuler.z);
        GUI.Label(new Rect(x, y, 450, lh), $"  X(傾角): {nx,6:F1} | Y(轉頭): {ny,6:F1} | Z(翻捲): {nz,6:F1}", m_label); y += lh;
    }

    float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// 將 Joy-Con 的四元數轉換到 Unity 期望的軸向。
    ///
    /// ★ 不經過 Euler 角轉換，直接操作四元數分量，從根本消除萬向鎖問題。
    ///
    /// 四元數 (x, y, z, w) 表示繞軸 n * sin(θ/2)，因此
    /// 直接對調换分量就相當於調换旋轉軸向。
    /// </summary>
    Quaternion RemapRotation(Quaternion raw)
    {
        // 偏移量已透過 Delta Filter 在 Update 中完美過濾處理
        Quaternion corrected = raw;

        // 步驔：直接對調四元數分量（不經過 Euler，無萬向鎖）
        //   swapXY = true  →  Joy-Con 的 X 軸轉名到 Unity Y，Joy-Con Y 軸轉名到 Unity X
        //   invertX/Y/Z    →  對對應分量取負號
        float qx = swapXY ? corrected.y : corrected.x;
        float qy = swapXY ? corrected.x : corrected.y;
        float qz = corrected.z;
        float qw = corrected.w;

        if (invertX) qx = -qx;
        if (invertY) qy = -qy;
        if (invertZ) qz = -qz;

        return new Quaternion(qx, qy, qz, qw);
    }

    static string V3(Vector3 v)    => $"({v.x,8:F3}, {v.y,8:F3}, {v.z,8:F3})";
    static string Q4(Quaternion q) => $"({q.x,6:F3}, {q.y,6:F3}, {q.z,6:F3}, {q.w,6:F3})";
}
