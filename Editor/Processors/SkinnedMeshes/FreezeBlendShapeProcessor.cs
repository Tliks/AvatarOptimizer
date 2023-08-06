using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Anatawa12.AvatarOptimizer.Processors.SkinnedMeshes
{
    internal class FreezeBlendShapeProcessor : EditSkinnedMeshProcessor<FreezeBlendShape>
    {
        public FreezeBlendShapeProcessor(FreezeBlendShape component) : base(component)
        {
        }

        public override int ProcessOrder => -10000;

        public override void Process(OptimizerSession session, MeshInfo2 target, MeshInfo2Holder meshInfo2Holder)
        {
            FreezeBlendShapes(this, session, target, Component.FreezingShapeKeys);
        }

        public static void FreezeBlendShapes<TComponent>(
            EditSkinnedMeshProcessor<TComponent> processor,
            OptimizerSession session,
            MeshInfo2 target,
            HashSet<string> freezeNames
        ) where TComponent : EditSkinnedMeshComponent
        {
            var freezes = new BitArray(target.BlendShapes.Count);
            for (var i = 0; i < target.BlendShapes.Count; i++)
                freezes[i] = freezeNames.Contains(target.BlendShapes[i].name);

            foreach (var vertex in target.Vertices)
            {
                for (var i = 0; i < target.BlendShapes.Count; i++)
                {
                    if (!freezes[i]) continue;
                    var (name, weight) = target.BlendShapes[i];
                    if (!vertex.BlendShapes.TryGetValue(name, out var value)) continue;
                    weight /= 100;

                    vertex.Position += value.position * weight;
                    vertex.Normal += value.normal * weight;
                    var tangent = (Vector3)vertex.Tangent + value.tangent * weight;
                    vertex.Tangent = new Vector4(tangent.x, tangent.y, tangent.z, vertex.Tangent.w);
                    vertex.BlendShapes.Remove(name);
                }
            }

            {
                int srcI = 0, dstI = 0;
                for (; srcI < target.BlendShapes.Count; srcI++)
                {
                    if (!freezes[srcI])
                    {
                        // for keep prop: move the BlendShape index. name is not changed.
                        session.MappingBuilder.RecordMoveProperty(processor.Target, 
                            VProp.BlendShapeIndex(srcI), 
                            VProp.BlendShapeIndex(dstI));
                        target.BlendShapes[dstI++] = target.BlendShapes[srcI];
                    }
                    else
                    {
                        // for frozen prop: remove that BlendShape
                        session.MappingBuilder.RecordRemoveProperty(processor.Target, VProp.BlendShapeIndex(srcI));
                        session.MappingBuilder.RecordRemoveProperty(processor.Target, $"blendShape.{target.BlendShapes[srcI].name}");
                    }
                }

                target.BlendShapes.RemoveRange(dstI, target.BlendShapes.Count - dstI);
            }
        }

        public override IMeshInfoComputer GetComputer(IMeshInfoComputer upstream) => new MeshInfoComputer(this, upstream);

        class MeshInfoComputer : AbstractMeshInfoComputer
        {
            private readonly FreezeBlendShapeProcessor _processor;

            public MeshInfoComputer(FreezeBlendShapeProcessor processor, IMeshInfoComputer upstream) : base(upstream)
                => _processor = processor;

            public override string[] BlendShapes()
            {
                var set = _processor.Component.FreezingShapeKeys;
                return base.BlendShapes().Where(x => !set.Contains(x)).ToArray();
            }
        }
    }
}
