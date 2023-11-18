using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Serialization;

public static class OverlapExtension
{
    private static readonly Vector3[] sBoxVertices = new Vector3[8];
    private static readonly Vector3[] sBoxNormals = 
    {
        Vector3.left,
        Vector3.right,
        Vector3.down,
        Vector3.up,
        Vector3.back,
        Vector3.forward
    };

    public static Vector3 LocalToWorldPoint(this Vector3 originWorldPosition, Quaternion originWorldRotation, Vector3 point)
        => LocalToWorldPoint(originWorldPosition, originWorldRotation, Vector3.one, point);
    public static Vector3 LocalToWorldPoint(this Vector3 originWorldPosition, Vector3 originLossyScale, Vector3 point)
        => LocalToWorldPoint(originWorldPosition, Quaternion.identity, originLossyScale, point);
    public static Vector3 LocalToWorldPoint(this Vector3 originWorldPosition, Vector3 point)
        => LocalToWorldPoint(originWorldPosition, Quaternion.identity, Vector3.one, point);
    
    [BurstCompile]
    private static Vector3 LocalToWorldPoint(this Vector3 originWorldPosition, Quaternion originWorldRotation, Vector3 originLossyScale,Vector3 point )
    {
        Vector3 scaledPoint = Vector3.Scale(point, originLossyScale);
        Vector3 rotatePoint = originWorldRotation * scaledPoint;
        Vector3 worldPoint = rotatePoint + originWorldPosition;
        return worldPoint;
    }

    [BurstCompile]
    private static Vector3 LocalToWorldDirection(this Quaternion originWorldRotation, Vector3 point)
    {
        return originWorldRotation * point;
    }


    /// <summary>
    /// Mesh를 직접적으로 계산할 때 convex한 메쉬가 아니라면 제대로 작동하지 않음
    /// </summary>
    /// <param name="mesh"></param>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    /// <param name="scale"></param>
    /// <param name="overlapBounds"></param>
    /// <param name="overlapMeshes"></param>
    /// <param name="layerMask"></param>
    /// <param name="queryTriggerInteraction"></param>
    /// <returns></returns>
    public static int OverlapMesh(this Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Collider[] overlapBounds, Collider[] overlapMeshes, LayerMask layerMask, QueryTriggerInteraction queryTriggerInteraction)
    {
        Bounds bounds = new (position.LocalToWorldPoint(rotation,scale,mesh.bounds.center), Vector3.Scale(mesh.bounds.size, scale));
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        int count = Physics.OverlapBoxNonAlloc(center, extents, overlapBounds, rotation, layerMask,queryTriggerInteraction);
        int checkCount = Mathf.Min(count, overlapMeshes.Length);
        int resultCount = 0;
        for (int i = 0; i < checkCount; i++)
        {
            switch (overlapBounds[i])
            {
                case MeshCollider meshCollider :
                    if (OverlapCheckMeshCollider(mesh,center,rotation,scale, meshCollider))
                    {
                        overlapMeshes[resultCount] = overlapBounds[i];
                        resultCount++;
                    }
                    break;
                case BoxCollider boxCollider :
                    if (OverlapCheckBoxCollider(mesh,center,rotation,scale, boxCollider))
                    {
                        overlapMeshes[resultCount] = overlapBounds[i];
                        resultCount++;
                    }
                    break;
                case SphereCollider sphereCollider :
                    if (OverlapCheckSphereCollider(mesh,center,rotation,scale,sphereCollider))
                    {
                        overlapMeshes[resultCount] = overlapBounds[i];
                        resultCount++;
                    }
                    break;
                case CapsuleCollider capsuleCollider :
                    if (OverlapCheckCapsuleCollider(mesh,center,rotation,scale,capsuleCollider))
                    {
                        overlapMeshes[resultCount] = overlapBounds[i];
                        resultCount++;
                    }
                    break;
            }
        }
        return resultCount;
    }
    /// <summary>
    /// 충돌 체크 매체가 되는 콜라이더도 계산되는 점 주의
    /// </summary>
    /// <param name="originMeshCollider"></param>
    /// <param name="overlapBounds"></param>
    /// <param name="overlapMeshes"></param>
    /// <param name="layerMask"></param>
    /// <param name="queryTriggerInteraction"></param>
    /// <returns></returns>
    public static int OverlapMesh(this MeshCollider originMeshCollider,Collider[] overlapBounds,Collider[] overlapMeshes,LayerMask layerMask,QueryTriggerInteraction queryTriggerInteraction)
    {
        if (originMeshCollider.convex == false) return 0;
        
        Mesh mesh = originMeshCollider.sharedMesh;
        Transform transform = originMeshCollider.transform;
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        Vector3 scale = transform.lossyScale;
        return OverlapMesh(mesh, position, rotation, scale, overlapBounds, overlapMeshes, layerMask, queryTriggerInteraction);
    }


    private static bool OverlapCheckCapsuleCollider(Mesh mesh,Vector3 position,Quaternion rotation,Vector3 scale, CapsuleCollider capsule)
    {
        Transform capsuleTransform = capsule.transform;
        Vector3 capsuleLossyScale = capsuleTransform.lossyScale;
        Vector3 capsuleWorldCenter = capsuleTransform.TransformPoint(capsule.center);
        float capsuleWorldRadius = Mathf.Max(capsuleLossyScale.x, capsuleLossyScale.z) * capsule.radius;
        float capsuleWorldHeight = capsule.height * capsule.transform.lossyScale.y;
        Vector3 capsuleDirection = capsule.direction switch
        {
            0 => Vector3.right,
            1 => Vector3.up,
            _ => Vector3.forward
        };
        Vector3 capsuleWorldOffset = capsuleDirection * (capsuleWorldHeight * 0.5f - capsuleWorldRadius);
        Vector3 capsuleWorldStart = capsuleWorldCenter - capsuleWorldOffset;
        Vector3 capsuleWorldEnd = capsuleWorldCenter + capsuleWorldOffset;
        NativeArray<Vector3> nativeVertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
        NativeArray<bool> result = new NativeArray<bool>(nativeVertices.Length, Allocator.TempJob);


        new SphereConflictCheckJob()
        {
            Vertices = nativeVertices,
            Result = result,
            SphereOrigin = capsuleWorldStart,
            SphereRadius = capsuleWorldRadius,
            OriginPosition = position,
            OriginRotation = rotation,
            OriginScale = scale
        }.Schedule(result.Length,1).Complete();
        bool isConflict = result.Contains(true);
        
        if (isConflict)
        {
            nativeVertices.Dispose();
            result.Dispose();
            return true;
        }
        
        new SphereConflictCheckJob()
        {
            Vertices = nativeVertices,
            Result = result,
            SphereOrigin = capsuleWorldEnd,
            SphereRadius = capsuleWorldRadius,
            OriginPosition = position,
            OriginRotation = rotation,
            OriginScale = scale
        }.Schedule(result.Length,1).Complete();
        isConflict = result.Contains(true);
        if (isConflict)
        {
            nativeVertices.Dispose();
            result.Dispose();
            return true;
        }
        
        new CapsuleConflictCheckJob
        {
            Vertices = nativeVertices,
            Result = result,
            CapsuleStart = capsuleWorldStart,
            CapsuleEnd = capsuleWorldEnd,
            CapsuleRadius = capsuleWorldRadius,
            OriginPosition = position,
            OriginRotation = rotation,
            OriginScale = scale
        }.Schedule(result.Length,1).Complete();
        isConflict = result.Contains(true);
        nativeVertices.Dispose();
        result.Dispose();
        return isConflict;
    }
    
    private static bool OverlapCheckBoxCollider(Mesh originMesh,Vector3 position,Quaternion rotation,Vector3 scale, BoxCollider box)
    {
        Transform compareTransform = box.transform;
        Vector3 half = box.size * 0.5f;
        sBoxVertices[0] = new Vector3(-half.x, -half.y, -half.z);
        sBoxVertices[1] = new Vector3(-half.x, -half.y, half.z);
        sBoxVertices[2] = new Vector3(-half.x, half.y, -half.z);
        sBoxVertices[3] = new Vector3(-half.x, half.y, half.z);
        sBoxVertices[4] = new Vector3(half.x, -half.y, -half.z);
        sBoxVertices[5] = new Vector3(half.x, -half.y, half.z);
        sBoxVertices[6] = new Vector3(half.x, half.y, -half.z);
        sBoxVertices[7] = new Vector3(half.x, half.y, half.z);
        Vector3[] originNormals = originMesh.normals;
        NativeArray<Vector3> nativeOriginVertices = new NativeArray<Vector3>(originMesh.vertices, Allocator.TempJob);
        NativeArray<Vector3> nativeCompareVertices = new NativeArray<Vector3>(sBoxVertices, Allocator.TempJob);
        NativeArray<Vector3> nativeAxes = new NativeArray<Vector3>(originNormals.Length + sBoxNormals.Length, Allocator.TempJob);
        NativeArray<bool> result = new NativeArray<bool>(nativeAxes.Length, Allocator.TempJob);
        for (int i = 0; i < originNormals.Length; i++)
        {
            nativeAxes[i] = rotation.LocalToWorldDirection(originNormals[i]);
        }
        for (int i = originNormals.Length; i < originNormals.Length + sBoxNormals.Length; i++)
        {
            nativeAxes[i] = compareTransform.TransformDirection(sBoxNormals[i - originNormals.Length]);
        }
        new MeshConflictCheckJob
        {
            OriginVertices = nativeOriginVertices,
            CompareVertices = nativeCompareVertices,
            Axes = nativeAxes,
            Result = result,
            OriginPosition = position,
            OriginRotation = rotation,
            OriginScale = scale,
            ComparePosition = compareTransform.position,
            CompareRotation = compareTransform.rotation,
            CompareScale = compareTransform.lossyScale
        }.Schedule(nativeAxes.Length,1).Complete();
        
        bool isConflict = result.Contains(true);

        nativeOriginVertices.Dispose();
        nativeCompareVertices.Dispose();
        nativeAxes.Dispose();
        result.Dispose();
        return isConflict;
    }

    private static bool OverlapCheckSphereCollider(Mesh mesh,Vector3 originPosition,Quaternion originRotation,Vector3 originScale, SphereCollider sphereCollider)
    {
        Transform sphereTransform = sphereCollider.transform;
        Vector3 sphereLossyScale = sphereTransform.lossyScale;
        float sphereScale = Mathf.Max(sphereLossyScale.x, sphereLossyScale.y, sphereLossyScale.z);
        float sphereRadius = sphereCollider.radius * sphereScale;
        return OverlapCheckSphere(mesh, originPosition,originRotation,originScale, sphereRadius, sphereTransform.TransformPoint(sphereCollider.center));
    }
    
    
    private static bool OverlapCheckSphere(Mesh mesh,Vector3 position,Quaternion rotation,Vector3 scale, float sphereRadius, Vector3 sphereOrigin)
    {
        NativeArray<Vector3> nativeVertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
        NativeArray<bool> result = new NativeArray<bool>(nativeVertices.Length, Allocator.TempJob);

        new SphereConflictCheckJob
        {
            Vertices = nativeVertices,
            Result = result,
            SphereRadius = sphereRadius,
            SphereOrigin = sphereOrigin,
            OriginPosition = position,
            OriginRotation = rotation,
            OriginScale = scale
        }.Schedule(nativeVertices.Length, 1).Complete();
        
        bool isConflict = result.Contains(true);
        nativeVertices.Dispose();
        result.Dispose();
        return isConflict;
    }
    

    

    private static bool OverlapCheckMeshCollider(Mesh originMesh,Vector3 position,Quaternion rotation,Vector3 scale, MeshCollider compareTarget)
    {
        Mesh compareMesh = compareTarget.sharedMesh;
        Transform compareTransform = compareTarget.transform;
        Vector3[] originNormals = originMesh.normals;
        Vector3[] compareNormals = compareMesh.normals;
        NativeArray<Vector3> nativeOriginVertices = new NativeArray<Vector3>(originMesh.vertices, Allocator.TempJob);
        NativeArray<Vector3> nativeCompareVertices = new NativeArray<Vector3>(compareMesh.vertices, Allocator.TempJob);
        NativeArray<Vector3> nativeAxes = new NativeArray<Vector3>(originNormals.Length + compareNormals.Length, Allocator.TempJob);
        NativeArray<bool> result = new NativeArray<bool>(nativeAxes.Length, Allocator.TempJob);
        for (int i = 0; i < originNormals.Length; i++)
        {
            nativeAxes[i] = rotation.LocalToWorldDirection(originNormals[i]);
        }
        for (int i = originNormals.Length; i < originNormals.Length + compareNormals.Length; i++)
        {
            nativeAxes[i] = compareTransform.TransformDirection(compareNormals[i - originNormals.Length]);
        }
        new MeshConflictCheckJob
        {
            OriginVertices = nativeOriginVertices,
            CompareVertices = nativeCompareVertices,
            Axes = nativeAxes,
            Result = result,
            OriginPosition = position,
            OriginRotation = rotation,
            OriginScale = scale,
            ComparePosition = compareTransform.position,
            CompareRotation = compareTransform.rotation,
            CompareScale = compareTransform.lossyScale
        }.Schedule(nativeAxes.Length,1).Complete();
        
        bool isConflict = result.Contains(true);

        nativeOriginVertices.Dispose();
        nativeCompareVertices.Dispose();
        nativeAxes.Dispose();
        result.Dispose();
        return isConflict;
    }

    [BurstCompile]
    private struct MeshConflictCheckJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> OriginVertices;
        [ReadOnly] public NativeArray<Vector3> CompareVertices;
        [ReadOnly] public NativeArray<Vector3> Axes;
        public Vector3 OriginPosition;
        public Vector3 OriginScale;
        public Quaternion OriginRotation;
        public Vector3 ComparePosition;
        public Vector3 CompareScale;
        public Quaternion CompareRotation;
        public NativeArray<bool> Result;

        public void Execute(int index)
        {
            Result[index] = IsAxisIntersecting(Axes[index], OriginVertices, CompareVertices);
        }

        private bool IsAxisIntersecting(Vector3 axis, NativeArray<Vector3> vertices1, NativeArray<Vector3> vertices2)
        {
            var min1 = float.MaxValue;
            var min2 = float.MaxValue;
            var max1 = float.MinValue;
            var max2 = float.MinValue;

            for (int i = 0; i < vertices1.Length; i++)
            {
                var vertex = OriginPosition.LocalToWorldPoint(OriginRotation, OriginScale, vertices1[i]);
                var projection = Vector3.Dot(vertex, axis);
                min1 = Mathf.Min(min1, projection);
                max1 = Mathf.Max(max1, projection);
            }

            for (int i = 0; i < vertices2.Length; i++)
            {
                var vertex = ComparePosition.LocalToWorldPoint(CompareRotation, CompareScale, vertices2[i]);
                var projection = Vector3.Dot(vertex, axis);
                min2 = Mathf.Min(min2, projection);
                max2 = Mathf.Max(max2, projection);
            }
            
            return (min1 <= max2 && max1 >= min2) || (min2 <= max1 && max2 >= min1);
        }
    }
    
    [BurstCompile]
    private struct SphereConflictCheckJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> Vertices;
        public Vector3 OriginPosition;
        public Quaternion OriginRotation;
        public Vector3 OriginScale;
        public Vector3 SphereOrigin;
        public float SphereRadius;
        public NativeArray<bool> Result;

        public void Execute(int index)
        {
            var vertex = OriginPosition.LocalToWorldPoint(OriginRotation, OriginScale, Vertices[index]);
            var distance = Vector3.Distance(SphereOrigin, vertex);
            Result[index] = distance <= SphereRadius;
        }
    }
    
    //정확히는 원통형 충돌 체크 캡슐 충돌은 원 충돌 체크 2 + 해당 충돌체크 1번으로 이루어짐
    [BurstCompile]
    private struct CapsuleConflictCheckJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> Vertices;
        public Vector3 CapsuleStart;
        public Vector3 CapsuleEnd;
        public float CapsuleRadius;
        public Vector3 OriginPosition;
        public Quaternion OriginRotation;
        public Vector3 OriginScale;
        public NativeArray<bool> Result;
        
        public void Execute(int index)
        {
            Vector3 vertex = OriginPosition.LocalToWorldPoint(OriginRotation, OriginScale, Vertices[index]);
            float t = Vector3.Dot(vertex - CapsuleStart, CapsuleEnd - CapsuleStart) / Vector3.Dot(CapsuleEnd - CapsuleStart, CapsuleEnd - CapsuleStart);
            Vector3 closetPoint = CapsuleStart + Mathf.Clamp01(t) * (CapsuleEnd - CapsuleStart);
            float distance = Vector3.Distance(vertex,closetPoint);
            Result[index] = distance <= CapsuleRadius;
        }
    }
}
