using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Animator Optimizer AnimatorController Wrapper classes

namespace Anatawa12.AvatarOptimizer.Processors.AnimatorOptimizer
{
    public class AOAnimatorController
    {
        private readonly AnimatorController _animatorController;

        public AOAnimatorController(AnimatorController animatorController)
        {
            if (!animatorController) throw new ArgumentNullException(nameof(animatorController));
            _animatorController = animatorController;
            layers = _animatorController.layers.Select(x => new AOAnimatorControllerLayer(this, x)).ToArray();
            if (layers.Length != 0)
                layers[0].IsBaseLayer = true;
            foreach (var layer in layers)
            {
                var syncedLayerIndex = layer.syncedLayerIndex;
                if (syncedLayerIndex != -1)
                {
                    var syncedLayer = layers[syncedLayerIndex];
                    layer.SyncedLayer = syncedLayer;
                    syncedLayer.IsSyncedToOtherLayer = true;
                }
            }
        }

        // ReSharper disable InconsistentNaming
        public AnimatorControllerParameter[] parameters
        {
            get => _animatorController.parameters;
            set => _animatorController.parameters = value;
        }

        // do not assign to this field
        public AOAnimatorControllerLayer[] layers { get; private set; }
        // ReSharper restore InconsistentNaming

        public void SetLayersUnsafe(AOAnimatorControllerLayer[] layers)
        {
            this.layers = layers;
            UpdateLayers();
        }

        public AOAnimatorControllerLayer AddLayerFirst(string layerName)
        {
            var layer = new AnimatorControllerLayer
            {
                name = layerName,
                stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                }
            };
            var wrappedLayer = new AOAnimatorControllerLayer(this, layer);
            wrappedLayer.IsBaseLayer = true;

            // update our layers
            var wrappedLayers = layers;
            for (var i = 0; i < wrappedLayers.Length; i++)
                wrappedLayers[i].OnLayerIndexUpdated(i + 1);
            ArrayUtility.Insert(ref wrappedLayers, 0, wrappedLayer);
            layers = wrappedLayers;

            UpdateLayers();
            layers[1].IsBaseLayer = false;
            layers[1].defaultWeight = 1; // previous default layer

            return wrappedLayer;
        }

        public AOAnimatorControllerLayer AddLayer(string layerName)
        {
            var layer = new AnimatorControllerLayer
            {
                name = layerName,
                stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                }
            };
            var wrappedLayer = new AOAnimatorControllerLayer(this, layer);

            // update our layers
            var wrappedLayers = layers;
            ArrayUtility.Add(ref wrappedLayers, wrappedLayer);
            layers = wrappedLayers;

            UpdateLayers();

            return wrappedLayer;
        }

        public void UpdateLayers()
        {
            _animatorController.layers = layers.Select(x => x.Layer).ToArray();
        }
    }

    public class AOAnimatorControllerLayer
    {
        public readonly AnimatorControllerLayer Layer;
        private readonly AOAnimatorController _parent;

        public AOAnimatorControllerLayer(AOAnimatorController parent, AnimatorControllerLayer layer)
        {
            _parent = parent;
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        }

        public bool IsSynced => Layer.syncedLayerIndex != -1;
        public bool IsSyncedToOtherLayer = false;
        public AOAnimatorControllerLayer? SyncedLayer { get; internal set; }

        public AnimatorWeightChange WeightChange;

        // ReSharper disable InconsistentNaming
        public float defaultWeight
        {
            get => IsBaseLayer ? 1 : Layer.defaultWeight;
            set
            {
                Layer.defaultWeight = value;
                _parent.UpdateLayers();
            }
        }

        public int syncedLayerIndex => Layer.syncedLayerIndex;
        public AnimatorStateMachine? stateMachine => Layer.stateMachine ? Layer.stateMachine : null;
        public string name => Layer.name;
        public AvatarMask? avatarMask => Layer.avatarMask;
        // ReSharper restore InconsistentNaming

        private bool _removable = true;
        public bool IsRemovable => !IsBaseLayer && _removable;
        public void MarkUnRemovable() => _removable = false;
        public event Action<int>? LayerIndexUpdated;
        public virtual void OnLayerIndexUpdated(int obj) => LayerIndexUpdated?.Invoke(obj);

        public bool IsBaseLayer { get; set; }
        public bool IsOverride => Layer.blendingMode == AnimatorLayerBlendingMode.Override;

        public Motion? GetOverrideMotion(AnimatorState state) => Layer.GetOverrideMotion(state);

        public IEnumerable<Motion?> GetMotions() => SyncedLayer == null
            ? ACUtils.AllStates(stateMachine).Select(x => x.motion)
            : ACUtils.AllStates(SyncedLayer.stateMachine).Select(GetOverrideMotion);
    }
}
