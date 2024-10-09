using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace Anatawa12.AvatarOptimizer
{
    public static partial class Utils
    {
        public static void HorizontalLine(bool marginTop = true, bool marginBottom = true)
        {
            const float margin = 17f / 2;
            var maxHeight = 1f;
            if (marginTop) maxHeight += margin;
            if (marginBottom) maxHeight += margin;

            var rect = GUILayoutUtility.GetRect(
                EditorGUIUtility.fieldWidth, float.MaxValue, 
                1, maxHeight, GUIStyle.none);
            if (marginTop && marginBottom)
                rect.y += rect.height / 2 - 0.5f;
            else if (marginTop)
                rect.y += rect.height - 1f;
            else if (marginBottom)
                rect.y += 0;
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        }

        public static void FlattenMapping<T>(this Dictionary<T, T> self)
        {
            foreach (var key in self.Keys.ToArray())
            {
                var value = self[key];
                while (value != null && self.TryGetValue(value, out var mapped))
                    value = mapped;
                self[key] = value;
            }
        }

        [return:NotNullIfNotNull("root")]
        public static string? RelativePath(Transform? root, Transform child)
        {
            if (root == child) return "";

            var pathSegments = new List<string>();
            while (child != root)
            {
                if (child == null) return null;
                pathSegments.Add(child.name);
                child = child.transform.parent;
            }

            pathSegments.Reverse();
            return string.Join("/", pathSegments);
        }

        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            if (!component) component = go.AddComponent<T>();
            return component;
        }

        public static GameObject NewGameObject(string name, Transform parent)
        {
            var rootObject = new GameObject(name);
            rootObject.transform.parent = parent;
            rootObject.transform.localPosition = Vector3.zero;
            rootObject.transform.localRotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;
            return rootObject;
        }

        public static GameObject NewGameObject(string name, GameObject[] children)
        {
            var rootObject = new GameObject(name);
            rootObject.transform.localPosition = Vector3.zero;
            rootObject.transform.localRotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;
            foreach (var child in children)
                child.transform.parent = rootObject.transform;
            return rootObject;
        }

        public static GameObject NewGameObject(string name)
        {
            var rootObject = new GameObject(name);
            rootObject.transform.localPosition = Vector3.zero;
            rootObject.transform.localRotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;
            return rootObject;
        }

        public static IEnumerable<(T, T)> ZipWithNext<T>(this IEnumerable<T> enumerable)
        {
            using (var enumerator = enumerable.GetEnumerator())
            {
                if (!enumerator.MoveNext()) yield break;
                var prev = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    var current = enumerator.Current;
                    yield return (prev, current);
                    prev = current;
                }
            }
        }

        // Properties detailed first and nothing last
        public static IEnumerable<(string prop, string rest)> FindSubPaths(string prop, char sep)
        {
            var rest = "";
            for (;;)
            {
                yield return (prop, rest);

                var index = prop.LastIndexOf(sep);
                if (index == -1) yield break;

                rest = prop.Substring(index) + rest;
                prop = prop.Substring(0, index);
            }
        }

        private class EmptyDictionaryHolder<TKey, TValue>
        {
            public static readonly IReadOnlyDictionary<TKey, TValue> Empty =
                new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>());
        }

        public static IReadOnlyDictionary<TKey, TValue> EmptyDictionary<TKey, TValue>() =>
            EmptyDictionaryHolder<TKey, TValue>.Empty;

        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> keyValuePair, out TKey key,
            out TValue value)
        {
            key = keyValuePair.Key;
            value = keyValuePair.Value;
        }

        public static IEnumerable<KeyValuePair<TKey, (TValue1?, TValue2?)>> ZipByKey<TKey, TValue1, TValue2>(
            this IReadOnlyDictionary<TKey, TValue1> first, IReadOnlyDictionary<TKey, TValue2> second)
        {
            foreach (var key in first.Keys.ToArray())
            {
                if (!second.TryGetValue(key, out var secondValue)) secondValue = default;

                yield return new KeyValuePair<TKey, (TValue1?, TValue2?)>(key, (first[key], secondValue));
            }

            foreach (var key in second.Keys.ToArray())
                if (!first.ContainsKey(key))
                    yield return new KeyValuePair<TKey, (TValue1?, TValue2?)>(key, (default, second[key]));
        }

        public static Type? GetTypeFromName(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name))
                .FirstOrDefault(type => !(type == null));

        public static T? DistinctSingleOrDefaultIfNoneOrMultiple<T>(this IEnumerable<T> enumerable)
        {
            using (var enumerator = enumerable.GetEnumerator())
            {
                if (!enumerator.MoveNext()) return default;
                var found = enumerator.Current;
                var eqOperator = EqualityComparer<T>.Default;

                while (enumerator.MoveNext())
                {
                    var nextValue = enumerator.Current;
                    if (!eqOperator.Equals(found, nextValue))
                        return default;
                }

                return found;
            }
        }

        public static T? SingleOrDefaultIfNoneOrMultiple<T>(this IEnumerable<T> enumerable)
        {
            using var enumerator = enumerable.GetEnumerator();
            if (!enumerator.MoveNext()) return default;
            var found = enumerator.Current;
            if (enumerator.MoveNext()) return default;
            return found;
        }

        public static T RemoveLast<T>(this IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            var lastIndex = list.Count - 1;
            var last = list[lastIndex];
            list.RemoveAt(lastIndex);
            return last;
        }

        public static EqualsHashSet<T> ToEqualsHashSet<T>(this IEnumerable<T> enumerable) =>
            new EqualsHashSet<T>(new HashSet<T>(enumerable));

        public static EqualsHashSet<T> ToEqualsHashSet<T>(this HashSet<T> hashSet) =>
            new EqualsHashSet<T>(hashSet);

        public static Transform? CommonRoot(IEnumerable<Transform> transforms)
        {
            using var enumerator = transforms.GetEnumerator();

            if (!enumerator.MoveNext()) return null;
            var commonRoot = enumerator.Current!;
            if (!enumerator.MoveNext()) return commonRoot;

            // child => parent
            var commonParents = Ancestors(commonRoot).ToArray();

            while (enumerator.MoveNext())
            {
                var gameObject = enumerator.Current!;

                var span = Ancestors(gameObject);

                var minLength = Math.Min(commonParents.Length, span.Length);

                for (var i = 0; i < minLength; i++)
                {
                    if (commonParents[i] != span[i])
                    {
                        commonParents = commonParents[..i];
                        break;
                    }
                }
            }

            if (commonParents.Length == 0) return null;
            return commonParents[^1];
            
            ReadOnlySpan<Transform> Ancestors(Transform transform)
            {
                var ancestors = transform.ParentEnumerable(includeMe: true).ToArray();
                Array.Reverse(ancestors);
                return ancestors.AsSpan();
            }
        }
  
        public static bool IsPowerOfTwo(this int x) => x != 0 && (x & (x - 1)) == 0;

        public static float MinPowerOfTwoGreaterThan(float x)
        {
            if (x <= 0) throw new ArgumentOutOfRangeException(nameof(x), x, "x must be positive");

            if (x < 1)
            {
                var r = 1f;
                while (r / 2 > x) r /= 2;
                return r;
            }
            else
            {
                var r = 1f;
                while (r < x) r *= 2;
                return r;
            }
        }

        public static int LeastCommonMultiple(params int[] numbers) => numbers.Length == 0 ? 0 : numbers.Aggregate(LeastCommonMultiple);

        public static int LeastCommonMultiple(int a, int b)
        {
            if (a == 0 || b == 0) return 0;
            return Math.Abs(a * b) / GreatestCommonDivisor(a, b);
        }

        public static int GreatestCommonDivisor(int a, int b)
        {
            if (a == 0) return b;
            if (b == 0) return a;
            while (b != 0)
            {
                var t = b;
                b = a % b;
                a = t;
            }

            return a;
        }

        public static int MostSignificantBit(int x) => MostSignificantBit((uint)x);

        public static int MostSignificantBit(uint x)
        {
            // https://github.com/microsoft/mimalloc/blob/fab7329c7a3eee64e455e0a7aea7566eb2038cf3/src/page-queue.c#L67-L80
            
            // de Bruijn multiplication, see <http://supertech.csail.mit.edu/papers/debruijn.pdf>
            ReadOnlySpan<byte> debruijn = new byte[]{
                31,  0, 22,  1, 28, 23, 18,  2, 29, 26, 24, 10, 19,  7,  3, 12,
                30, 21, 27, 17, 25,  9,  6, 11, 20, 16,  8,  5, 15,  4, 14, 13,
            };
            
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            x++;
            return debruijn[(int)((x * 0x076be629) >> 27)];
        }

        /// <summary>
        /// Fast compare of <see cref="Color32"/> array.
        ///
        /// This is semantically equivalent to <c>a.SequenceEqual(b)</c> but much faster (about 80x faster).
        ///
        /// Compared to <c>a.SequenceEqual(b, new Color32Comparator())</c>, this is about 10x faster.
        /// </summary>
        /// <param name="a">first array</param>
        /// <param name="b">second array</param>
        /// <returns>whether two arrays are equal</returns>
        public static bool Color32ArrayEquals(ReadOnlySpan<Color32> a, ReadOnlySpan<Color32> b)
        {
            if (Color32ArrayEqualsDataHolder.SafeToUseFastImplementation)
            {
                var aSlice = MemoryMarshal.Cast<Color32, int>(a);
                var bSlice = MemoryMarshal.Cast<Color32, int>(b);
                if (aSlice.Length != bSlice.Length) return false;
                for (var i = 0; i < aSlice.Length; i++)
                    if (aSlice[i] != bSlice[i])
                        return false;
                return true;
            }
            else
            {
                for (var i = 0; i < a.Length; i++)
                    if (!Color32Equals(a[i], b[i]))
                        return false;
                return true;

                bool Color32Equals(Color32 x, Color32 y) => x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
            }
        }

        private static class Color32ArrayEqualsDataHolder
        {
            public static readonly bool SafeToUseFastImplementation;

            static Color32ArrayEqualsDataHolder()
            {
                SafeToUseFastImplementation = ComputeSafeToUseFastImplementation();

                bool ComputeSafeToUseFastImplementation()
                {
                    if (UnsafeUtility.SizeOf<Color32>() != sizeof(int)) return false;
                    if ((typeof(Color32).Attributes & TypeAttributes.ExplicitLayout) == 0) return false;
                    var field = typeof(Color32).GetField("rgba", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field == null) return false;
                    if (field.GetCustomAttribute<FieldOffsetAttribute>()?.Value != 0) return false;
                    return true;
                }
            }
        }

        /// <summary>
        /// Fast compare of <see cref="Color32"/> array.
        ///
        /// This is semantically equivalent to <c>a.SequenceEqual(b)</c> but much faster (about 80x faster).
        ///
        /// Compared to <c>a.SequenceEqual(b, new Color32Comparator())</c>, this is about 10x faster.
        /// </summary>
        /// <param name="a">first array</param>
        /// <param name="b">second array</param>
        /// <returns>whether two arrays are equal</returns>
        public static bool Color32ArrayEquals(Color32[] a, Color32[] b) => Color32ArrayEquals(a.AsSpan(), b.AsSpan());
    }
}
