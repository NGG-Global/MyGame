using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Unity saves a component in a scene, or a ScriptableObject as an asset, only when its class has a
    /// script file of the same name. A class declared inside another file works in code and in every
    /// test that builds objects at runtime, but the generated scene stores it as a missing script and the
    /// player reads the scene as corrupted. This catches it before a build does.
    /// </summary>
    public class ScriptFileTests
    {
        [Test]
        public void EveryComponentAndScriptableObject_HasAScriptFileOfItsOwnName()
        {
            var scripted = new HashSet<Type>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/_Game/Scripts" }))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                var type = script != null ? script.GetClass() : null;
                if (type != null) scripted.Add(type);
            }

            var missing = new List<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (!name.StartsWith("OpenSkies.") || name == "OpenSkies.Editor" || name.StartsWith("OpenSkies.Tests")) continue;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
                    if (!type.IsSubclassOf(typeof(MonoBehaviour)) && !type.IsSubclassOf(typeof(ScriptableObject))) continue;
                    if (!scripted.Contains(type)) missing.Add(type.FullName + " (" + name + ")");
                }
            }
            Assert.IsEmpty(missing, "Move each class into a file named after it: " + string.Join(", ", missing));
        }
    }
}
