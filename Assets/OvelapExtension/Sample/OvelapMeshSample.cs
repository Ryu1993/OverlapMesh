using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class OvelapMeshSample : MonoBehaviour
{
    [SerializeField] private KeyCode TestKey = KeyCode.A;
    [SerializeField] private int CheckLimit = 30;
    [SerializeField] private LayerMask CheckLayer;
    [SerializeField] private QueryTriggerInteraction TriggerInteraction;
    private MeshCollider meshCollider;
    private Collider[] overlapBounds;
    private Collider[] overlapMeshes;
    
    private void Awake()
    {
        if (TryGetComponent(out meshCollider) == false) return;
        meshCollider.convex = true;
    }

    private void Update()
    {
        if (overlapBounds == null || overlapBounds.Length != CheckLimit)
        {
            overlapBounds = new Collider[CheckLimit];
            overlapMeshes = new Collider[CheckLimit];
        }
        if (Input.GetKeyDown(TestKey) == false) return;
        if (meshCollider == null || meshCollider.sharedMesh == null) return;
        int count = meshCollider.OverlapMesh(overlapBounds, overlapMeshes, CheckLayer, TriggerInteraction);
        if (count == 0)
        {
            Debug.Log("충돌한 콜라이더 없음");
        }
        else
        {
            foreach (var col in overlapMeshes.Take(count))
            {
                Debug.Log($"충돌한 게임오브젝트 : {col.gameObject}");
            }
        }
        
    }
}
