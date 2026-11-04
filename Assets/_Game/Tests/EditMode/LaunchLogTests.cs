using NUnit.Framework;
using OpenSkies.Platform;

namespace OpenSkies.Tests
{
    /// <summary>The launch log kept by the development diagnostics overlay.</summary>
    public class LaunchLogTests
    {
        [Test]
        public void KeepsTheLastLines_OldestFirst()
        {
            var log = new LaunchLog(3);
            log.Add("a");
            log.Add("b");
            log.Add("c");
            Assert.AreEqual("a\nb\nc", log.Text);
            log.Add("d");
            Assert.AreEqual(3, log.Count);
            Assert.AreEqual("b\nc\nd", log.Text, "the oldest line goes first");
        }

        [Test]
        public void ErrorsAreRemembered_EmptyLinesIgnored()
        {
            var log = new LaunchLog(4);
            log.Add("bootstrap");
            Assert.IsFalse(log.HasErrors);
            log.Add(null);
            log.Add(string.Empty);
            Assert.AreEqual(1, log.Count);
            log.Add("Exception: NullReferenceException", isError: true);
            Assert.IsTrue(log.HasErrors);
            log.Add("frame 60");
            Assert.IsTrue(log.HasErrors, "an error stays flagged after later breadcrumbs");
            Assert.AreEqual("bootstrap\nException: NullReferenceException\nframe 60", log.Text);
        }

        [Test]
        public void TextIsRebuiltOnlyWhenTheLogChanges()
        {
            var log = new LaunchLog(2);
            log.Add("one");
            string first = log.Text;
            Assert.AreSame(first, log.Text, "unchanged log: the same string");
            log.Add("two");
            Assert.AreNotSame(first, log.Text);
            Assert.AreEqual("one\ntwo", log.Text);
        }
    }
}
