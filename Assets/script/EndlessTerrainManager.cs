using System.Collections.Generic;
using UnityEngine;

public class EndlessTerrainManager : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("要向前移動的物體 (例如 Player)")]
    public Transform targetObject;
    public Transform camera;
    [Tooltip("移動速度")]
    public float moveSpeed = 15f;

    [Header("Terrain Settings")]
    [Tooltip("請放入你要複製的地形 (場景中的物件或 Prefab 都可以)")]
    public Transform terrainPrefab; 
    
    [Tooltip("畫面上要同時保持幾個地形？(建議至少 3 個，才能提前在前方生成，避免破圖)")]
    public int visibleTerrainCount = 3;

    [Tooltip("Terrain 的長度 (Z軸)")]
    public float terrainLength = 200f;
    // Terrain 寬度 (X軸) 為 150，這裡我們保留物件原本的 X 座標即可
    
    // 下一個地形要放置的 Z 軸位置
    private float nextSpawnZ;

    // 儲存目前已經生成的 Terrain 列表
    private List<Transform> activeTerrains = new List<Transform>();

    [Header("Background Settings (Optional)")]
    [Tooltip("這些物件會跟著玩家一起等速向前移動，永遠保持在前方/相對位置 (例如遠處的山脈 Terrain)")]
    public Transform[] backgroundObjects;

    void Start()
    {
        // 如果沒有設定 targetObject，預設為掛載此腳本的物件 (也就是 PlayerContainer)
        if (targetObject == null)
        {
            targetObject = this.transform;
            Debug.Log("未設定 Target Object，自動使用當前物件作為移動目標。");
        }

        // 確保有設定 Terrain 來源
        if (terrainPrefab == null)
        {
            Debug.LogError("【錯誤】請在 EndlessTerrainManager 中設定 Terrain Prefab！否則將不會移動。");
            return;
        }

        // 假設 Terrain 的起點從 targetObject 的 Z 軸開始，或是從 0 開始
        nextSpawnZ = targetObject.position.z;

        // 一開始就「提前複製」設定數量的地形放在前方
        for (int i = 0; i < visibleTerrainCount; i++)
        {
            SpawnTerrain();
        }

        // 如果 terrainPrefab 是場景中的物件（非 Prefab）且沒有被我們加入 activeTerrains 中，可以選擇將它隱藏
        // (避免它停留在原地造成畫面重疊)
        if (terrainPrefab.gameObject.scene.IsValid() && !activeTerrains.Contains(terrainPrefab))
        {
            terrainPrefab.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (targetObject != null)
        {
            // 1. 讓物體一直向前移動 (Z軸方向)
            Vector3 movement = Vector3.forward * moveSpeed * Time.deltaTime;
            targetObject.Translate(movement, Space.World);
            if (camera != null) camera.Translate(movement, Space.World);
        }

        // 讓背景物件也一起等速向前移動 (保持相對距離不變)
        if (backgroundObjects != null)
        {
            foreach (Transform bg in backgroundObjects)
            {
                if (bg != null)
                {
                    Vector3 movement = Vector3.forward * moveSpeed * Time.deltaTime;
                    bg.Translate(movement, Space.World);
                }
            }
        }

        // 2. 檢查玩家是否已經完全走過了第一塊地形
        if (activeTerrains.Count > 0 && targetObject != null)
        {
            Transform firstTerrain = activeTerrains[0];
            // 加上一個小偏移(例如多留一點距離)或者直接判斷
            if (targetObject.position.z > firstTerrain.position.z + terrainLength)
            {
                // 先將它從檢查列表中移除，避免重複觸發
                activeTerrains.RemoveAt(0);
                
                // 開啟協程：延遲 5 秒後再將地形搬到最前方
                StartCoroutine(DelayMoveTerrainToFront(firstTerrain, 5f));
            }
        }
    }

    private void SpawnTerrain()
    {
        // 複製 (Instantiate) 一個新的 Terrain
        Transform newTerrain = Instantiate(terrainPrefab);
        newTerrain.gameObject.SetActive(true); // 確保它是顯示的

        // 設定位置：保留原本的 X 和 Y，只改變 Z 軸
        Vector3 newPos = newTerrain.position;
        newPos.z = nextSpawnZ;
        newTerrain.position = newPos;

        // 記錄到列表中
        activeTerrains.Add(newTerrain);

        // 增加下一個的 Z 軸位置
        nextSpawnZ += terrainLength;
    }

    private System.Collections.IEnumerator DelayMoveTerrainToFront(Transform terrainToMove, float delay)
    {
        // 讓這塊地形在玩家背後多停留指定的秒數 (例如 5 秒)，避免玩家回頭或視角看到破圖
        yield return new WaitForSeconds(delay);

        // 將位置移到最前方的 nextSpawnZ
        Vector3 newPos = terrainToMove.position;
        newPos.z = nextSpawnZ;
        terrainToMove.position = newPos;

        // 更新下一個要生成的 Z 軸位置
        nextSpawnZ += terrainLength;

        // 將搬運好的地形重新加入列表最後面，繼續循環
        activeTerrains.Add(terrainToMove);
    }
}
