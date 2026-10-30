using System;
using NUnit.Framework;

namespace Game.Tests
{
    public class ScoreCounterTests
    {
        [Test]
        public void StartsAtZero()
        {
            var score = new ScoreCounter();
            Assert.AreEqual(0, score.Value);
            Assert.AreEqual(0, score.Best);
        }

        [Test]
        public void Add_IncreasesValueAndBest()
        {
            var score = new ScoreCounter();
            score.Add(3);
            score.Add(2);
            Assert.AreEqual(5, score.Value);
            Assert.AreEqual(5, score.Best);
        }

        [Test]
        public void Reset_KeepsBest()
        {
            var score = new ScoreCounter();
            score.Add(7);
            score.Reset();
            Assert.AreEqual(0, score.Value);
            Assert.AreEqual(7, score.Best);
        }

        [Test]
        public void Best_OnlyChangesWhenBeaten()
        {
            var score = new ScoreCounter(best: 10);
            score.Add(4);
            Assert.AreEqual(10, score.Best);
            score.Add(8);
            Assert.AreEqual(12, score.Best);
        }

        [Test]
        public void Add_SaturatesInsteadOfOverflowing()
        {
            var score = new ScoreCounter();
            score.Add(int.MaxValue);
            score.Add(1);
            Assert.AreEqual(int.MaxValue, score.Value);
        }

        [Test]
        public void Changed_RaisedOnlyOnRealChanges()
        {
            var score = new ScoreCounter();
            int calls = 0, last = -1;
            score.Changed += v => { calls++; last = v; };

            score.Add(0);
            score.Reset();
            Assert.AreEqual(0, calls);

            score.Add(2);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(2, last);

            score.Reset();
            Assert.AreEqual(2, calls);
            Assert.AreEqual(0, last);
        }

        [Test]
        public void NegativeInputs_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreCounter(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreCounter().Add(-1));
        }
    }
}
