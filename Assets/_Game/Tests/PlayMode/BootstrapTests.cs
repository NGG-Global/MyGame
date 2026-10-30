using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public class BootstrapTests
    {
        [TearDown]
        public void TearDown()
        {
            if (GameRoot.Instance != null) Object.Destroy(GameRoot.Instance.gameObject);
        }

        [UnityTest]
        public IEnumerator EnsureGameRoot_CreatesOneRootWithCameraAndZeroScore()
        {
            var root = Bootstrap.EnsureGameRoot();
            yield return null;

            Assert.IsNotNull(root);
            Assert.AreSame(root, Bootstrap.EnsureGameRoot(), "EnsureGameRoot must be idempotent");
            Assert.AreEqual(0, root.Score.Value);
            Assert.IsNotNull(Camera.main, "A main camera should exist after bootstrap");
            Assert.IsNotNull(root.GetComponentInChildren<SafeAreaFitter>(), "HUD should sit under a SafeAreaFitter");
        }
    }
}
