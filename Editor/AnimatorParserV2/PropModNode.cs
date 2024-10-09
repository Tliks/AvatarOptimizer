using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Debug = System.Diagnostics.Debug;
using Object = UnityEngine.Object;

namespace Anatawa12.AvatarOptimizer.AnimatorParsersV2
{
    interface IPropModNode
    {
        bool AppliedAlways { get; }
    }

    interface IValueInfo<TValueInfo>
        where TValueInfo : struct, IValueInfo<TValueInfo>
    {
        public bool IsConstant { get; }

        // following functions are intended to be called on default(TValueInfo) and "this" will not be affected
        // Those functions should be static abstract but Unity doesn't support static abstract functions.

        TValueInfo ConstantInfoForSideBySide(IEnumerable<PropModNode<TValueInfo>> nodes);
        TValueInfo ConstantInfoForBlendTree(IEnumerable<PropModNode<TValueInfo>> nodes, BlendTreeType blendTreeType);

        TValueInfo ConstantInfoForOverriding<TLayer>(IEnumerable<TLayer> layersReversed)
            where TLayer : ILayer<TValueInfo>;
    }
    
    /// <summary>
    /// This class represents a node in the property modification tree.
    ///
    /// In AnimatorParser V2, Modifications of each property are represented as a tree to make it possible to
    /// remove modifications of a property.
    ///
    /// This class is the abstract class for the nodes.
    ///
    /// Most nodes are immutable but some nodes are mutable.
    /// </summary>
    internal abstract class PropModNode<TValueInfo> : IErrorContext, IPropModNode
        where TValueInfo: struct, IValueInfo<TValueInfo>
    {
        /// <summary>
        /// Returns true if this node is always applied. For inactive nodes, this returns false.
        /// </summary>
        public abstract bool AppliedAlways { get; }

        public abstract TValueInfo Value { get; }
        public abstract IEnumerable<ObjectReference> ContextReferences { get; }
    }

    internal readonly struct FloatValueInfo : IValueInfo<FloatValueInfo>, IEquatable<FloatValueInfo>
    {
        public bool IsConstant => _possibleValues is { Length: 1 };
        private readonly float[]? _possibleValues;

        public FloatValueInfo(float value) => _possibleValues = new[] { value };
        public FloatValueInfo(float[] values) => _possibleValues = values;

        public float ConstantValue
        {
            get
            {
                if (!IsConstant) throw new InvalidOperationException("Not Constant");
                return _possibleValues![0]; // non constant => there is value
            }
        }

        public float[]? PossibleValues => _possibleValues;
        public static FloatValueInfo Variable => default;

        public bool TryGetConstantValue(out float o)
        {
            if (IsConstant)
            {
                o = ConstantValue;
                return true;
            }
            else
            {
                o = default;
                return false;
            }
        }

        public FloatValueInfo ConstantInfoForSideBySide(IEnumerable<PropModNode<FloatValueInfo>> nodes)
        {
            var allPossibleValues = new HashSet<float>();
            foreach (var propModNode in nodes)
            {
                if (propModNode.Value.PossibleValues is not { } values) return Variable;
                allPossibleValues.UnionWith(values);
            }
            return new FloatValueInfo(allPossibleValues.ToArray());
        }

        public FloatValueInfo ConstantInfoForBlendTree(IEnumerable<PropModNode<FloatValueInfo>> nodes,
            BlendTreeType blendTreeType) =>
            blendTreeType == BlendTreeType.Direct ? Variable : ConstantInfoForSideBySide(nodes);

        public FloatValueInfo ConstantInfoForOverriding<TLayer>(IEnumerable<TLayer> layersReversed)
            where TLayer : ILayer<FloatValueInfo>
        {
            var allPossibleValues = new HashSet<float>();

            foreach (var layer in layersReversed)
            {
                switch (layer.Weight)
                {
                    case AnimatorWeightState.AlwaysOne:
                    case AnimatorWeightState.EitherZeroOrOne:
                    {
                        if (layer.Node.Value.PossibleValues is not { } otherValues) return Variable;

                        switch (layer.BlendingMode)
                        {
                            case AnimatorLayerBlendingMode.Additive:
                                // having multiple possible value means animated, and this means variable.
                                // if only one value is exists with additive layer, noting is added so skip this layer.
                                // for additive reference pose, length of otherValues will be two or more with 
                                // reference post value.
                                // see implementation of FloatAnimationCurveNode.ParseProperty
                                if (otherValues.Length != 1) return Variable;
                                break;
                            case AnimatorLayerBlendingMode.Override:
                                allPossibleValues.UnionWith(otherValues);

                                if (layer.IsAlwaysOverride())
                                {
                                    // the layer is always applied at the highest property.
                                    return new FloatValueInfo(allPossibleValues.ToArray());
                                }

                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                        break;
                    case AnimatorWeightState.Variable:
                        return Variable;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return new FloatValueInfo(allPossibleValues.ToArray());
        }

        public bool Equals(FloatValueInfo other) => NodeImplUtils.SetEquals(_possibleValues, other._possibleValues);
        public override bool Equals(object? obj) => obj is FloatValueInfo other && Equals(other);
        public override int GetHashCode() => _possibleValues != null ? _possibleValues.GetSetHashCode() : 0;
        public static bool operator ==(FloatValueInfo left, FloatValueInfo right) => left.Equals(right);
        public static bool operator !=(FloatValueInfo left, FloatValueInfo right) => !left.Equals(right);
    }

    // note: no default is allowed
    internal readonly struct ObjectValueInfo : IValueInfo<ObjectValueInfo>, IEquatable<ObjectValueInfo>
    {
        private readonly Object[] _possibleValues;

        public ObjectValueInfo(Object value) => _possibleValues = new[] { value };
        public ObjectValueInfo(Object[] values) => _possibleValues = values;

        public bool IsConstant => _possibleValues is { Length: 1 };

        public Object[] PossibleValues => _possibleValues ?? Array.Empty<Object>();

        public ObjectValueInfo ConstantInfoForSideBySide(IEnumerable<PropModNode<ObjectValueInfo>> nodes) =>
            new(nodes.SelectMany(node => node.Value.PossibleValues).Distinct().ToArray());

        public ObjectValueInfo ConstantInfoForBlendTree(IEnumerable<PropModNode<ObjectValueInfo>> nodes,
            BlendTreeType blendTreeType) => ConstantInfoForSideBySide(nodes);

        public ObjectValueInfo ConstantInfoForOverriding<TLayer>(IEnumerable<TLayer> layersReversed)
            where TLayer : ILayer<ObjectValueInfo>
        {
            return new ObjectValueInfo(layersReversed.WhileApplied().SelectMany(layer => layer.Node.Value.PossibleValues).Distinct().ToArray());
        }

        public bool Equals(ObjectValueInfo other) => NodeImplUtils.SetEquals(PossibleValues, PossibleValues);
        public override bool Equals(object? obj) => obj is ObjectValueInfo other && Equals(other);
        public override int GetHashCode() => _possibleValues.GetHashCode();
        public static bool operator ==(ObjectValueInfo left, ObjectValueInfo right) => left.Equals(right);
        public static bool operator !=(ObjectValueInfo left, ObjectValueInfo right) => !left.Equals(right);
    }

    internal static class NodeImplUtils
    {
        public static bool SetEquals<T>(T[]? a, T[]? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            return new HashSet<T>(a).SetEquals(b);
        }

        public static bool AlwaysAppliedForOverriding<TLayer>(IEnumerable<TLayer> layersReversed)
            where TLayer : ILayer
        {
            return layersReversed.Any(x =>
                x.Weight == AnimatorWeightState.AlwaysOne && x.BlendingMode == AnimatorLayerBlendingMode.Override &&
                x.Node.AppliedAlways);
        }

        public static bool IsAlwaysOverride<TLayer>(this TLayer layer)
            where TLayer : ILayer
        {
            return layer.Node.AppliedAlways && layer.Weight == AnimatorWeightState.AlwaysOne &&
                   layer.BlendingMode == AnimatorLayerBlendingMode.Override;
        }

        public static IEnumerable<TLayer> WhileApplied<TLayer>(this IEnumerable<TLayer> layer)
            where TLayer : ILayer
        {
            foreach (var layerInfo in layer)
            {
                yield return layerInfo;
                if (layerInfo.IsAlwaysOverride()) yield break;
            }
        }
    }

    interface ILayer
    {
        AnimatorWeightState Weight { get; }
        AnimatorLayerBlendingMode BlendingMode { get; }
        IPropModNode Node { get; }
    }

    internal interface ILayer<TValueInfo> : ILayer
        where TValueInfo : struct, IValueInfo<TValueInfo>
    {
        new AnimatorWeightState Weight { get; }
        new AnimatorLayerBlendingMode BlendingMode { get; }
        new PropModNode<TValueInfo> Node { get; }
    }

    internal sealed class RootPropModNode<TValueInfo> : PropModNode<TValueInfo>, IErrorContext
        where TValueInfo : struct, IValueInfo<TValueInfo>
    {
        internal readonly struct ComponentInfo
        {
            public readonly ComponentPropModNodeBase<TValueInfo> Node;
            public readonly bool AlwaysApplied;

            public bool AppliedAlways => AlwaysApplied && Node.AppliedAlways;
            public IEnumerable<ObjectReference> ContextReferences => Node.ContextReferences;
            public Component Component => Node.Component;

            public ComponentInfo(ComponentPropModNodeBase<TValueInfo> node, bool alwaysApplied)
            {
                Node = node;
                AlwaysApplied = alwaysApplied;
            }
        }

        private readonly List<ComponentInfo> _children = new List<ComponentInfo>();

        public IEnumerable<ComponentInfo> Children => _children;

        public override bool AppliedAlways => _children.All(x => x.AppliedAlways);

        public override IEnumerable<ObjectReference> ContextReferences =>
            _children.SelectMany(x => x.ContextReferences);

        public override TValueInfo Value => default(TValueInfo).ConstantInfoForSideBySide(_children.Select(x => x.Node));

        public bool IsEmpty => _children.Count == 0;

        public IEnumerable<Component> SourceComponents => _children.Select(x => x.Component);
        public IEnumerable<ComponentPropModNodeBase<TValueInfo>> ComponentNodes => _children.Select(x => x.Node);

        public void Add(ComponentPropModNodeBase<TValueInfo> node, bool alwaysApplied)
        {
            _children.Add(new ComponentInfo(node, alwaysApplied));
            DestroyTracker.Track(node.Component, OnDestroy);
        }

        public void Add(RootPropModNode<TValueInfo> toAdd)
        {
            if (toAdd == null) throw new ArgumentNullException(nameof(toAdd));
            foreach (var child in toAdd._children)
                Add(child.Node, child.AppliedAlways);
        }

        private void OnDestroy(int objectId)
        {
            _children.RemoveAll(x => x.Component.GetInstanceID() == objectId);
        }

        public void Invalidate()
        {
            foreach (var componentInfo in _children)
                DestroyTracker.Untrack(componentInfo.Component, OnDestroy);
            _children.Clear();
        }

        public RootPropModNode<TValueInfo>? Normalize() => IsEmpty ? null : this;
    }

    internal abstract class ImmutablePropModNode<TValueInfo> : PropModNode<TValueInfo>
        where TValueInfo: struct, IValueInfo<TValueInfo>
    {
    }

    internal class FloatAnimationCurveNode : ImmutablePropModNode<FloatValueInfo>
    {
        public AnimationCurve Curve { get; }
        public AnimationClip Clip { get; }

        public static FloatAnimationCurveNode? Create(AnimationClip clip, EditorCurveBinding binding,
            AnimationClip? additiveReferenceClip, float additiveReferenceFrame)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) return null;
            if (curve.keys.Length == 0) return null;
            
            float referenceValue = 0;
            if (additiveReferenceClip != null 
                && AnimationUtility.GetEditorCurve(additiveReferenceClip, binding) is { } referenceCurve)
                referenceValue = referenceCurve.Evaluate(additiveReferenceFrame);
            else
                referenceValue = curve.Evaluate(0);

            return new FloatAnimationCurveNode(clip, curve, referenceValue);
        }

        private FloatAnimationCurveNode(AnimationClip clip, AnimationCurve curve, float referenceValue)
        {
            if (!clip) throw new ArgumentNullException(nameof(clip));
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            Debug.Assert(curve.keys.Length > 0);
            Clip = clip;
            Curve = curve;
            _constantInfo = new Lazy<FloatValueInfo>(() => ParseProperty(curve, referenceValue), isThreadSafe: false);
        }

        private readonly Lazy<FloatValueInfo> _constantInfo;

        public override bool AppliedAlways => true;
        public override FloatValueInfo Value => _constantInfo.Value;
        public override IEnumerable<ObjectReference> ContextReferences => new[] { ObjectRegistry.GetReference(Clip) };

        private static FloatValueInfo ParseProperty(AnimationCurve curve, float referenceValue)
        {
            var curveValue = ParseCurve(curve);
            if (curveValue.PossibleValues == null) return FloatValueInfo.Variable;
            return new FloatValueInfo(curveValue.PossibleValues.Concat(new[] { referenceValue }).Distinct()
                .ToArray());
        }

        private static FloatValueInfo ParseCurve(AnimationCurve curve)
        {
            if (curve.keys.Length == 1) return new FloatValueInfo(curve.keys[0].value);

            float constValue = 0;
            foreach (var (preKey, postKey) in curve.keys.ZipWithNext())
            {
                var preWeighted = preKey.weightedMode == WeightedMode.Out || preKey.weightedMode == WeightedMode.Both;
                var postWeighted = postKey.weightedMode == WeightedMode.In || postKey.weightedMode == WeightedMode.Both;

                if (preKey.value.CompareTo(postKey.value) != 0) return FloatValueInfo.Variable;
                constValue = preKey.value;
                // it's constant
                if (float.IsInfinity(preKey.outWeight) || float.IsInfinity(postKey.inTangent)) continue;
                if (preKey.outTangent == 0 && postKey.inTangent == 0) continue;
                if (preWeighted && postWeighted && preKey.outWeight == 0 && postKey.inWeight == 0) continue;
                return FloatValueInfo.Variable;
            }

            return new FloatValueInfo(constValue);
        }
    }

    internal class ObjectAnimationCurveNode : ImmutablePropModNode<ObjectValueInfo>
    {
        public ObjectReferenceKeyframe[] Frames { get; set; }
        public AnimationClip Clip { get; }

        public static ObjectAnimationCurveNode? Create(AnimationClip clip, EditorCurveBinding binding)
        {
            var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (curve == null) return null;
            if (curve.Length == 0) return null;
            return new ObjectAnimationCurveNode(clip, curve);
        }

        private ObjectAnimationCurveNode(AnimationClip clip, ObjectReferenceKeyframe[] frames)
        {
            Debug.Assert(frames.Length > 0);
            Clip = clip;
            Frames = frames;
            _constantInfo = new Lazy<ObjectValueInfo>(() => ParseProperty(frames), isThreadSafe: false);
        }


        private readonly Lazy<ObjectValueInfo> _constantInfo;

        public override bool AppliedAlways => true;
        public override ObjectValueInfo Value => _constantInfo.Value;
        public override IEnumerable<ObjectReference> ContextReferences => new[] { ObjectRegistry.GetReference(Clip) };

        private static ObjectValueInfo ParseProperty(ObjectReferenceKeyframe[] frames) =>
            new(frames.Select(x => x.value).Distinct().ToArray());
    }

    internal struct BlendTreeElement<TValueInfo>
        where TValueInfo : struct, IValueInfo<TValueInfo>
    {
        public int Index;
        public ImmutablePropModNode<TValueInfo> Node;

        public BlendTreeElement(int index, ImmutablePropModNode<TValueInfo> node)
        {
            Index = index;
            Node = node ?? throw new ArgumentNullException(nameof(node));
        }
    }

    internal class BlendTreeNode<TValueInfo> : ImmutablePropModNode<TValueInfo>
        where TValueInfo : struct, IValueInfo<TValueInfo>
    {
        private readonly List<BlendTreeElement<TValueInfo>> _children;
        private readonly BlendTreeType _blendTreeType;
        private readonly bool _partial;

        public BlendTreeNode(List<BlendTreeElement<TValueInfo>> children,
            BlendTreeType blendTreeType, bool partial)
        {
            // expected to pass list or array
            // ReSharper disable once PossibleMultipleEnumeration
            Debug.Assert(children.Any());
            // ReSharper disable once PossibleMultipleEnumeration
            _children = children;
            _blendTreeType = blendTreeType;
            _partial = partial;
        }


        private bool WeightSumIsOne => _blendTreeType != BlendTreeType.Direct;
        public IReadOnlyList<BlendTreeElement<TValueInfo>> Children => _children;
        public override bool AppliedAlways => WeightSumIsOne && !_partial && _children.All(x => x.Node.AppliedAlways);

        public override TValueInfo Value
        {
            get => default(TValueInfo).ConstantInfoForBlendTree(_children.Select(x => x.Node), _blendTreeType);
        }

        public override IEnumerable<ObjectReference> ContextReferences =>
            _children.SelectMany(x => x.Node.ContextReferences);
    }

    abstract class ComponentPropModNodeBase<TValueInfo> : PropModNode<TValueInfo>
        where TValueInfo : struct, IValueInfo<TValueInfo>
    {
        protected ComponentPropModNodeBase(Component component)
        {
            if (!component) throw new ArgumentNullException(nameof(component));
            Component = component;
        }

        public Component Component { get; }

        public override IEnumerable<ObjectReference> ContextReferences =>
            new[] { ObjectRegistry.GetReference(Component) };
    }

    abstract class ComponentPropModNode<TValueInfo, TComponent> : ComponentPropModNodeBase<TValueInfo>
        where TValueInfo : struct, IValueInfo<TValueInfo>
        where TComponent : Component
    {
        protected ComponentPropModNode(TComponent component) : base(component)
        {
            if (!component) throw new ArgumentNullException(nameof(component));
            Component = component;
        }

        public new TComponent Component { get; }

        public override IEnumerable<ObjectReference> ContextReferences =>
            new[] { ObjectRegistry.GetReference(Component) };
    }

    class VariableComponentPropModNode : ComponentPropModNode<FloatValueInfo, Component>
    {
        public VariableComponentPropModNode(Component component) : base(component)
        {
        }

        public override bool AppliedAlways => false;
        public override FloatValueInfo Value => FloatValueInfo.Variable;
    }

    class AnimationComponentPropModNode<TValueInfo> : ComponentPropModNode<TValueInfo, Animation>
        where TValueInfo : struct, IValueInfo<TValueInfo>
    {
        public ImmutablePropModNode<TValueInfo> Animation { get; }

        public AnimationComponentPropModNode(Animation component, ImmutablePropModNode<TValueInfo> animation) : base(component)
        {
            Animation = animation;
            _constantInfo = new Lazy<TValueInfo>(() => animation.Value, isThreadSafe: false);
        }

        private readonly Lazy<TValueInfo> _constantInfo;

        public override bool AppliedAlways => true;
        public override TValueInfo Value => _constantInfo.Value;

        public override IEnumerable<ObjectReference> ContextReferences =>
            base.ContextReferences.Concat(Animation.ContextReferences);
    }
}
