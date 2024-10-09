using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace Anatawa12.AvatarOptimizer
{
    // Since AAO 1.8.0, this component can be added multiple times.
    // In AAO 1.7.0 or earlier, this component was marked as [DisallowMultipleComponent].
    [AddComponentMenu("Avatar Optimizer/AAO Remove Mesh By BlendShape")]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    [AllowMultipleComponent]
    [HelpURL("https://vpm.anatawa12.com/avatar-optimizer/ja/docs/reference/remove-mesh-by-blendshape/")]
    [PublicAPI]
    public sealed class RemoveMeshByBlendShape : EditSkinnedMeshComponent
    {
        [SerializeField]
        internal PrefabSafeSet.PrefabSafeSet<string> shapeKeysSet;
        [AAOLocalized("RemoveMeshByBlendShape:prop:Tolerance",
            "RemoveMeshByBlendShape:tooltip:Tolerance")]
        [SerializeField]
        internal double tolerance = 0.001;
        internal RemoveMeshByBlendShape()
        {
            shapeKeysSet = new PrefabSafeSet.PrefabSafeSet<string>(this);
        }

        internal HashSet<string> RemovingShapeKeys => shapeKeysSet.GetAsSet();

        private void OnValidate()
        {
            PrefabSafeSet.PrefabSafeSet.OnValidate(this, x => x.shapeKeysSet);
        }

        APIChecker _checker;
        
        /// <summary>
        /// Initializes the RemoveMEshByBlendShape with the specified default behavior version.
        ///
        /// As Described in the documentation, you have to call this method after `AddComponent` to make sure
        /// the default configuration is what you want.
        /// Without calling this method, the default configuration might be changed in the future.
        /// </summary>
        /// <param name="version">
        /// The default configuration version.
        /// Since 1.7.0, version 1 is supported.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Unsupported configuration version</exception>
        [PublicAPI]
        public void Initialize(int version)
        {
            switch (version)
            {
                case 1:
                    // nothing to do
                    break; 
                default:
                    throw new ArgumentOutOfRangeException(nameof(version), $"unsupported version: {version}");
            }
            _checker.OnInitialize(version, this);
        }

        /// <summary>
        /// Gets or sets the tolerance for the blend shape delta
        ///
        /// If the delta is less than this value, the vertex is considered to be not moved.
        /// </summary>
        [PublicAPI]
        public double Tolerance
        {
            get => _checker.OnAPIUsage(this, tolerance);
            set => _checker.OnAPIUsage(this, tolerance = value);
        }

        /// <summary>
        /// Gets the set of shape keys to remove meshes.
        /// </summary>
        [PublicAPI]
        public API.PrefabSafeSetAccessor<string> ShapeKeys =>
            _checker.OnAPIUsage(this, new API.PrefabSafeSetAccessor<string>(shapeKeysSet));
    }
}
