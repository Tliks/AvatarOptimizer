using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Anatawa12.AvatarOptimizer.EditModePreview
{
    internal class RemoveMeshInBoxRenderFilter : AAORenderFilterBase<RemoveMeshInBox>
    {
        private RemoveMeshInBoxRenderFilter() : base("Remove Mesh in Box", "remove-mesh-in-box")
        {
        }

        public static RemoveMeshInBoxRenderFilter Instance { get; } = new();
        protected override AAORenderFilterNodeBase<RemoveMeshInBox> CreateNode() => new RemoveMeshInBoxRendererNode();
        protected override bool SupportsMultiple() => true;
    }

    internal class RemoveMeshInBoxRendererNode : AAORenderFilterNodeBase<RemoveMeshInBox>
    {
        public static NativeArray<bool> ComputeShouldRemoveVertex(
            SkinnedMeshRenderer renderer,
            RemoveMeshInBox[] components
        )
        {
            UnityEngine.Profiling.Profiler.BeginSample("BakeMesh");
            var tempMesh = new Mesh();
            renderer.BakeMesh(tempMesh);
            UnityEngine.Profiling.Profiler.EndSample();

            var mesh = renderer.sharedMesh;

            var vertexIsInBox = new NativeArray<bool>(mesh.vertexCount, Allocator.TempJob);

            try
            {

                using var realPosition = new NativeArray<Vector3>(tempMesh.vertices, Allocator.TempJob);

                UnityEngine.Profiling.Profiler.BeginSample("CollectVertexData");
                foreach (var component in components)
                {
                    var boxesArray = component.boxes.ToArray();
                    var componentWorldToLocalMatrix = component.transform.worldToLocalMatrix;
                    using var boxes = new NativeArray<RemoveMeshInBox.BoundingBox>(boxesArray, Allocator.TempJob);

                    new CheckRemoveVertexJob
                    {
                        boxes = boxes,
                        vertexPosition = realPosition,
                        vertexIsInBox = vertexIsInBox,
                        meshToBoxTransform = renderer.transform.localToWorldMatrix * componentWorldToLocalMatrix,
                    }.Schedule(mesh.vertexCount, 32).Complete();
                }

                UnityEngine.Profiling.Profiler.EndSample();
            }
            catch
            {
                vertexIsInBox.Dispose();
                throw;
            }

            return vertexIsInBox;
        }

        protected override ValueTask Process(SkinnedMeshRenderer original, SkinnedMeshRenderer proxy,
            RemoveMeshInBox[] components,
            Mesh duplicated, ComputeContext context)
        {
            // Observe transform since the BakeMesh depends on the transform
            context.Observe(proxy.transform, t => t.localToWorldMatrix);

            UnityEngine.Profiling.Profiler.BeginSample("BakeMesh");
            var tempMesh = new Mesh();
            proxy.BakeMesh(tempMesh);
            UnityEngine.Profiling.Profiler.EndSample();

            foreach (var component in components)
            {
                context.Observe(component, c => c.boxes.ToArray(), Enumerable.SequenceEqual);
                context.Observe(component.transform, c => c.worldToLocalMatrix);
            }

            using var vertexIsInBox = ComputeShouldRemoveVertex(proxy, components);

            var uv = duplicated.uv;
            using var uvJob = new NativeArray<Vector2>(uv, Allocator.TempJob);

            for (var subMeshI = 0; subMeshI < duplicated.subMeshCount; subMeshI++)
            {
                var subMesh = duplicated.GetSubMesh(subMeshI);
                int vertexPerPrimitive;
                switch (subMesh.topology)
                {
                    case MeshTopology.Triangles:
                        vertexPerPrimitive = 3;
                        break;
                    case MeshTopology.Quads:
                        vertexPerPrimitive = 4;
                        break;
                    case MeshTopology.Lines:
                        vertexPerPrimitive = 2;
                        break;
                    case MeshTopology.Points:
                        vertexPerPrimitive = 1;
                        break;
                    case MeshTopology.LineStrip:
                    default:
                        // unsupported topology
                        continue;
                }

                var triangles = duplicated.GetTriangles(subMeshI);
                var primitiveCount = triangles.Length / vertexPerPrimitive;

                using var trianglesJob = new NativeArray<int>(triangles, Allocator.TempJob);
                using var shouldRemove = new NativeArray<bool>(primitiveCount, Allocator.TempJob);
                UnityEngine.Profiling.Profiler.BeginSample("JobLoop");
                var job = new ShouldRemovePrimitiveJob
                {
                    vertexPerPrimitive = vertexPerPrimitive,
                    triangles = trianglesJob,
                    vertexIsInBox = vertexIsInBox,
                    shouldRemove = shouldRemove,
                };
                job.Schedule(primitiveCount, 32).Complete();
                UnityEngine.Profiling.Profiler.EndSample();

                var modifiedTriangles = new List<int>(triangles.Length);

                UnityEngine.Profiling.Profiler.BeginSample("Inner Main Loop");
                for (var primitiveI = 0; primitiveI < primitiveCount; primitiveI++)
                    if (!shouldRemove[primitiveI])
                        for (var vertexI = 0; vertexI < vertexPerPrimitive; vertexI++)
                            modifiedTriangles.Add(triangles[primitiveI * vertexPerPrimitive + vertexI]);
                UnityEngine.Profiling.Profiler.EndSample();

                duplicated.SetTriangles(modifiedTriangles, subMeshI);
            }

            return default;
        }

        [BurstCompile]
        struct CheckRemoveVertexJob : IJobParallelFor
        {
            // ReSharper disable InconsistentNaming
            [ReadOnly] public NativeArray<RemoveMeshInBox.BoundingBox> boxes;
            [ReadOnly] public NativeArray<Vector3> vertexPosition;
            public NativeArray<bool> vertexIsInBox;

            public Matrix4x4 meshToBoxTransform;
            // ReSharper restore InconsistentNaming

            public void Execute(int vertexIndex)
            {
                var inBox = false;

                var position = meshToBoxTransform.MultiplyPoint3x4(vertexPosition[vertexIndex]);
                foreach (var box in boxes)
                {
                    if (box.ContainsVertex(position))
                    {
                        inBox = true;
                        break;
                    }
                }

                vertexIsInBox[vertexIndex] = inBox;
            }
        }

        [BurstCompile]
        struct ShouldRemovePrimitiveJob : IJobParallelFor
        {
            // ReSharper disable InconsistentNaming
            public int vertexPerPrimitive;
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<bool> vertexIsInBox;
            [WriteOnly] public NativeArray<bool> shouldRemove;
            // ReSharper restore InconsistentNaming

            public void Execute(int primitiveIndex)
            {
                var baseIndex = primitiveIndex * vertexPerPrimitive;
                var indices = triangles.Slice(baseIndex, vertexPerPrimitive);

                var result = true;
                foreach (var index in indices)
                {
                    if (!vertexIsInBox[index])
                    {
                        result = false;
                        break;
                    }
                }

                shouldRemove[primitiveIndex] = result;
            }
        }
    }
}
