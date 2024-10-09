using UnityEngine;

namespace Anatawa12.AvatarOptimizer.Test.Runtime
{
    public class PrefabSafeSetComponent : MonoBehaviour
    {
        public PrefabSafeSet.PrefabSafeSet<string> stringSet;

        public PrefabSafeSetComponent()
        {
            stringSet = new PrefabSafeSet.PrefabSafeSet<string>(this);
        }

        private void OnValidate()
        {
            PrefabSafeSet.PrefabSafeSet.OnValidate(this, x => x.stringSet);
        }
    }
}
